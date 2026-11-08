using UnityEngine;

/// <summary>
/// Sonidos del jugador: pasos (mas rapidos y fuertes al correr) y puñetazos
/// (silbido del golpe al aire + impacto si conecta con un enemigo).
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerSFX : MonoBehaviour
{
    [Header("Pasos")]
    public AudioClip[] pasos;
    public float intervaloCaminar = 0.5f;
    public float intervaloCorrer = 0.3f;
    [Range(0f, 1f)] public float volumenCaminar = 0.35f;
    [Range(0f, 1f)] public float volumenCorrer = 0.6f;

    [Header("Puñetazos")]
    public AudioClip[] golpeAire;
    public AudioClip[] golpeImpacto;
    [Range(0f, 1f)] public float volumenGolpe = 0.8f;

    PlayerController pc;
    AudioSource fuentePasos, fuenteGolpes;
    Vector3 ultimaPos;
    float timerPaso;
    int ultimoPaso = -1;

    void Awake()
    {
        pc = GetComponent<PlayerController>();
        fuentePasos = CrearFuente();
        fuenteGolpes = CrearFuente();
    }

    AudioSource CrearFuente()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;   // el jugador siempre se oye igual
        return s;
    }

    void OnEnable() { if (pc != null) pc.OnPunch += Golpe; }
    void OnDisable() { if (pc != null) pc.OnPunch -= Golpe; }
    void Start() { ultimaPos = transform.position; }

    void Update()
    {
        Vector3 d = transform.position - ultimaPos; d.y = 0f;
        float vel = Time.deltaTime > 0f ? d.magnitude / Time.deltaTime : 0f;
        ultimaPos = transform.position;

        if (!pc.IsGrounded || vel < 0.5f) { timerPaso = Mathf.Min(timerPaso, 0.08f); return; }

        timerPaso -= Time.deltaTime;
        if (timerPaso > 0f) return;
        bool corre = pc.IsRunning;
        timerPaso = corre ? intervaloCorrer : intervaloCaminar;
        Paso(corre ? volumenCorrer : volumenCaminar, corre ? 1.1f : 1f);
    }

    void Paso(float vol, float tono)
    {
        if (pasos == null || pasos.Length == 0) return;
        int i = Random.Range(0, pasos.Length);
        if (pasos.Length > 1 && i == ultimoPaso) i = (i + 1) % pasos.Length;   // no repetir el mismo paso
        ultimoPaso = i;
        fuentePasos.pitch = tono * Random.Range(0.92f, 1.08f);
        fuentePasos.PlayOneShot(pasos[i], vol * Ajustes.VolEfectos);
    }

    void Golpe(bool conecto)
    {
        fuenteGolpes.pitch = Random.Range(0.93f, 1.07f);
        if (golpeAire != null && golpeAire.Length > 0)
            fuenteGolpes.PlayOneShot(golpeAire[Random.Range(0, golpeAire.Length)], volumenGolpe * 0.6f * Ajustes.VolEfectos);
        if (conecto && golpeImpacto != null && golpeImpacto.Length > 0)
            fuenteGolpes.PlayOneShot(golpeImpacto[Random.Range(0, golpeImpacto.Length)], volumenGolpe * Ajustes.VolEfectos);
    }
}
