using UnityEngine;

/// <summary>
/// Sonido 3D de un animal. Cuando el jugador entra en su radio (porque el se acerca
/// o porque el animal lo persigue) suena de inmediato y despues repite cada pocos segundos.
/// Con "bucle" activado (abeja, mariposa) el sonido es continuo y sube de volumen al acercarse.
/// </summary>
public class AnimalSound : MonoBehaviour
{
    public AudioClip[] sonidos;
    [Tooltip("Sonido continuo (zumbido, aleteo) en lugar de sonidos sueltos")]
    public bool bucle;
    public float distanciaOir = 12f;
    [Range(0f, 1f)] public float volumen = 0.8f;
    public Vector2 intervalo = new Vector2(2.5f, 5f);
    public Vector2 tono = new Vector2(0.9f, 1.1f);

    AudioSource fuente;
    RatHealth vida;
    Transform jugador;
    bool cerca;
    float espera;

    void Awake()
    {
        vida = GetComponent<RatHealth>();
        fuente = gameObject.AddComponent<AudioSource>();
        fuente.playOnAwake = false;
        fuente.spatialBlend = 1f;
        fuente.rolloffMode = AudioRolloffMode.Linear;
        fuente.minDistance = 1.5f;
        fuente.maxDistance = distanciaOir * 1.4f;
        fuente.dopplerLevel = 0f;
        fuente.volume = volumen;
        if (vida != null) vida.OnHealthChanged += (c, m) => { if (c > 0f) Sonar(1.25f); };
    }

    void Start()
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        if (bucle && sonidos != null && sonidos.Length > 0)
        {
            fuente.clip = sonidos[0];
            fuente.loop = true;
            fuente.time = Random.Range(0f, sonidos[0].length);
            fuente.pitch = Random.Range(tono.x, tono.y);
        }
    }

    void Update()
    {
        if (jugador == null || sonidos == null || sonidos.Length == 0) return;
        if (vida != null && vida.IsDead) { if (fuente.isPlaying && bucle) fuente.Stop(); return; }

        fuente.volume = volumen * Ajustes.VolEfectos;
        bool ahora = (jugador.position - transform.position).sqrMagnitude <= distanciaOir * distanciaOir;

        if (bucle)
        {
            if (ahora && !fuente.isPlaying) fuente.Play();
            else if (!ahora && fuente.isPlaying && (jugador.position - transform.position).sqrMagnitude > fuente.maxDistance * fuente.maxDistance) fuente.Stop();
            return;
        }

        if (ahora && !cerca) Sonar(1f);   // acaba de entrar en el radio
        cerca = ahora;
        if (!cerca) return;
        espera -= Time.deltaTime;
        if (espera <= 0f) Sonar(1f);
    }

    void Sonar(float tonoExtra)
    {
        if (sonidos == null || sonidos.Length == 0 || bucle) return;
        fuente.pitch = Random.Range(tono.x, tono.y) * tonoExtra;
        fuente.PlayOneShot(sonidos[Random.Range(0, sonidos.Length)]);
        espera = Random.Range(intervalo.x, intervalo.y);
    }
}
