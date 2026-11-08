using System.Collections;
using UnityEngine;

/// <summary>
/// Jefa del hormiguero: la reina, en la camara mayor junto al hongo blanco que protege.
/// Despierta cuando el jugador entra a su camara. Ataques:
///   Mordida   : cuerpo a cuerpo con aviso (se echa hacia atras y tiembla).
///   Acido     : rafaga de proyectiles en abanico.
///   Llamado   : chilla y llegan soldados desde los tuneles.
///   Embestida : (con menos de la mitad de vida) carga en linea recta.
/// Nunca se aleja mucho del hongo. Al morir desata la furia de toda la colonia.
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class ReinaHormiga : MonoBehaviour
{
    enum Estado { Dormida, Pelea, Accion, Muerta }

    public Transform visual;
    public Renderer cuerpo;
    public Material matQuieta, matCamina, matFuriosa;
    public Vector3 hogar;
    public float radioCorrea = 24f;
    public float radioDespertar = 55f;
    public float velocidad = 2.6f;

    [Header("Ataques")]
    public float danioMordida = 24f;
    public float alcanceMordida = 9f;
    public float danioAcido = 11f;
    public float danioEmbestida = 32f;
    public Color colorAcido = new Color(0.75f, 0.95f, 0.2f);
    public HormigaNido plantillaSoldado;
    public Transform[] puntosLlamado;

    [Header("Sonido")]
    public AudioClip rugido, chillido, grunido;
    public GameObject particulasMuerte;

    Estado estado = Estado.Dormida;
    RatHealth vida;
    Transform jugador;
    PlayerHealth vidaJugador;
    AudioSource audioSrc;
    float cdAtaque = 2f, cdAcido = 4f, cdLlamado = 10f, cdEmbestida = 8f, timerGrunido = 5f;
    float pitch, temblor;
    Vector3 visualBase;

    bool Fase2 => vida.HealthPercent < 0.5f;

    void Awake()
    {
        vida = GetComponent<RatHealth>();
        if (hogar == Vector3.zero) hogar = transform.position;
        audioSrc = GetComponent<AudioSource>();
        if (visual != null) visualBase = visual.localPosition;
        vida.DamageFilter = a => estado == Estado.Dormida ? a * 1.5f : a;   // golpe a traicion: mas daño
    }

    void OnEnable() { vida.OnDeath += Morir; vida.OnHealthChanged += AlDanio; }
    void OnDisable() { vida.OnDeath -= Morir; vida.OnHealthChanged -= AlDanio; }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) { jugador = pc.transform; vidaJugador = pc.GetComponent<PlayerHealth>(); }
    }

    void AlDanio(float actual, float max) { if (estado == Estado.Dormida && actual > 0f) Despertar(); }

    void Despertar()
    {
        if (estado != Estado.Dormida) return;
        estado = Estado.Pelea;
        Sonar(rugido, 1f);
        if (Hormiguero.Instancia != null) Hormiguero.Instancia.Alarma(hogar, radioCorrea + 30f);
        if (cuerpo != null) cuerpo.sharedMaterial = matCamina;
    }

    void Sonar(AudioClip c, float vol) { if (c != null && audioSrc != null) audioSrc.PlayOneShot(c, vol * Ajustes.VolEfectos); }

    // ------------------------------------------------------------------ bucle
    void Update()
    {
        if (estado == Estado.Muerta || jugador == null) return;
        float dt = Time.deltaTime;
        if ((jugador.position - hogar).sqrMagnitude < 120f * 120f) RatHealthBarUI.jefe = vida;
        Vector3 hacia = jugador.position - transform.position; hacia.y = 0f;
        float dist = hacia.magnitude;

        AnimarVisual(dt);
        if (estado == Estado.Dormida)
        {
            if ((jugador.position - hogar).magnitude < radioDespertar) Despertar();
            return;
        }
        if (estado == Estado.Accion) return;

        Girar(hacia, dt, 2.2f);
        timerGrunido -= dt;
        if (timerGrunido <= 0f) { timerGrunido = Random.Range(5f, 9f); Sonar(grunido, 0.7f); }

        // se acerca al jugador sin alejarse del hongo
        float vel = velocidad * (Fase2 ? 1.4f : 1f);
        if (dist > alcanceMordida * 0.8f)
        {
            Vector3 p = transform.position + hacia.normalized * vel * dt;
            Vector3 off = p - hogar; off.y = 0f;
            if (off.magnitude > radioCorrea) p = hogar + off.normalized * radioCorrea + Vector3.up * (p.y - hogar.y);
            transform.position = p;
            if (cuerpo != null) cuerpo.sharedMaterial = Fase2 ? matFuriosa : matCamina;
        }
        else if (cuerpo != null) cuerpo.sharedMaterial = matQuieta;

        float k = Fase2 ? 0.65f : 1f;
        cdAtaque -= dt; cdAcido -= dt; cdLlamado -= dt; cdEmbestida -= dt;
        if (cdAtaque <= 0f && dist < alcanceMordida) { cdAtaque = 1.8f * k; StartCoroutine(Mordida()); }
        else if (Fase2 && cdEmbestida <= 0f && dist > 14f) { cdEmbestida = 9f; StartCoroutine(Embestida()); }
        else if (cdAcido <= 0f && dist > 8f) { cdAcido = 5.5f * k; StartCoroutine(Acido()); }
        else if (cdLlamado <= 0f) { cdLlamado = 17f * k; StartCoroutine(Llamado()); }
    }

    void Girar(Vector3 dir, float dt, float vel)
    {
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), vel * dt);
    }

    void AnimarVisual(float dt)
    {
        if (visual == null) return;
        Vector3 shk = temblor > 0f ? new Vector3(Mathf.Sin(Time.time * 60f), 0f, Mathf.Cos(Time.time * 53f)) * 0.25f * temblor : Vector3.zero;
        visual.localPosition = visualBase + shk;
        visual.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
    }

    // ------------------------------------------------------------------ ataques
    IEnumerator Mordida()
    {
        estado = Estado.Accion;
        for (float t = 0f; t < 0.55f; t += Time.deltaTime) { pitch = Mathf.Lerp(0f, 16f, t / 0.55f); temblor = t / 0.55f; yield return null; }
        temblor = 0f;
        Sonar(chillido, 0.5f);
        Vector3 f = transform.forward;
        for (float t = 0f; t < 0.18f; t += Time.deltaTime) { pitch = Mathf.Lerp(16f, -14f, t / 0.18f); transform.position += f * 9f * Time.deltaTime; yield return null; }
        Vector3 hacia = jugador.position - transform.position; hacia.y = 0f;
        if (hacia.magnitude < alcanceMordida + 1.5f && Vector3.Angle(transform.forward, hacia) < 70f && vidaJugador != null)
            vidaJugador.TakeDamage(danioMordida);
        for (float t = 0f; t < 0.35f; t += Time.deltaTime) { pitch = Mathf.Lerp(-14f, 0f, t / 0.35f); yield return null; }
        estado = Estado.Pelea;
    }

    IEnumerator Acido()
    {
        estado = Estado.Accion;
        for (float t = 0f; t < 0.8f; t += Time.deltaTime) { pitch = Mathf.Lerp(0f, 22f, t / 0.8f); temblor = 0.6f; Girar(jugador.position - transform.position, Time.deltaTime, 5f); yield return null; }
        temblor = 0f;
        Vector3 boca = transform.position + transform.forward * 5.5f + Vector3.up * 3.2f;
        int n = Fase2 ? 7 : 5;
        for (int i = 0; i < n; i++)
        {
            float ang = (i - (n - 1) * 0.5f) * 9f;
            Vector3 obj = jugador.position + Vector3.up;
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * (obj - boca);
            EnemyProjectile.Spawn(boca, boca + dir, 14f, danioAcido, colorAcido, 0.6f, 1.5f, 1.1f);
        }
        Sonar(chillido, 0.4f);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { pitch = Mathf.Lerp(22f, 0f, t / 0.4f); yield return null; }
        estado = Estado.Pelea;
    }

    IEnumerator Llamado()
    {
        estado = Estado.Accion;
        Sonar(rugido, 1f);
        for (float t = 0f; t < 1.2f; t += Time.deltaTime) { pitch = 28f * Mathf.Sin(t / 1.2f * Mathf.PI); temblor = 1f; yield return null; }
        temblor = 0f; pitch = 0f;
        if (plantillaSoldado != null && puntosLlamado != null && puntosLlamado.Length > 0)
        {
            int n = Fase2 ? 5 : 3;
            for (int i = 0; i < n; i++)
            {
                var p = puntosLlamado[Random.Range(0, puntosLlamado.Length)];
                if (p == null) continue;
                var h = Instantiate(plantillaSoldado, p.position + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)), p.rotation, transform.parent);
                h.gameObject.SetActive(true);
                h.Enfurecer();
            }
        }
        estado = Estado.Pelea;
    }

    IEnumerator Embestida()
    {
        estado = Estado.Accion;
        for (float t = 0f; t < 0.9f; t += Time.deltaTime) { pitch = -10f; temblor = t / 0.9f; Girar(jugador.position - transform.position, Time.deltaTime, 6f); yield return null; }
        temblor = 0f;
        Sonar(rugido, 0.8f);
        Vector3 dir = jugador.position - transform.position; dir.y = 0f; dir.Normalize();
        bool golpeo = false;
        for (float t = 0f; t < 1.1f; t += Time.deltaTime)
        {
            Vector3 p = transform.position + dir * 16f * Time.deltaTime;
            Vector3 off = p - hogar; off.y = 0f;
            if (off.magnitude > radioCorrea + 6f) break;     // no sale de su camara
            transform.position = p;
            if (!golpeo && (jugador.position - transform.position).magnitude < 6.5f && vidaJugador != null) { golpeo = true; vidaJugador.TakeDamage(danioEmbestida); }
            yield return null;
        }
        pitch = 0f;
        yield return new WaitForSeconds(0.6f);
        estado = Estado.Pelea;
    }

    // ------------------------------------------------------------------ muerte
    void Morir()
    {
        if (estado == Estado.Muerta) return;
        StopAllCoroutines();
        estado = Estado.Muerta;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (cuerpo != null) cuerpo.sharedMaterial = matQuieta;
        Sonar(chillido, 1f);
        StartCoroutine(RutinaMuerte());
    }

    IEnumerator RutinaMuerte()
    {
        Quaternion r0 = visual != null ? visual.localRotation : Quaternion.identity;
        for (float t = 0f; t < 1.5f; t += Time.deltaTime)
        {
            temblor = 1f - t / 1.5f;
            if (visual != null) { AnimarVisual(Time.deltaTime); visual.localRotation = Quaternion.Slerp(r0, Quaternion.Euler(0f, 0f, 170f), t / 1.5f); }
            yield return null;
        }
        if (particulasMuerte != null)
            for (int i = 0; i < 6; i++)
                Instantiate(particulasMuerte, transform.position + Random.insideUnitSphere * 4f + Vector3.up * 2f, Quaternion.identity);
        var drops = GetComponent<DropTable>();
        if (drops != null) drops.Drop(transform.position + transform.forward * 3f);
        if (RatHealthBarUI.jefe == vida) RatHealthBarUI.jefe = null;
        Aviso.Mostrar("¡La colonia esta furiosa!", new Color(1f, 0.35f, 0.25f));
        yield return new WaitForSeconds(1.2f);
        if (Hormiguero.Instancia != null) Hormiguero.Instancia.DesatarFuria();
        // el cuerpo queda como trofeo junto al hongo
        enabled = false;
    }
}
