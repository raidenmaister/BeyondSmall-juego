using System.Collections;
using UnityEngine;

/// <summary>
/// Jefa de la colmena: la reina, en la camara real en lo alto del arbol. Vuela alrededor del centro
/// de su camara; despierta cuando entras o la golpeas. Ataques:
///   Picada     : se lanza contra el jugador y queda aturdida en el suelo un momento (momento de golpearla).
///   Aguijones  : rafaga de aguijones en abanico que ralentizan.
///   Llamado    : zumbido fuerte y llegan soldados de la colmena.
///   Vendaval   : (bajo la mitad de vida) aleteo que daña y ralentiza si estas cerca.
/// El humo de la antorcha no la detiene, pero si a sus soldados. Al morir toda la colmena ataca.
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class ReinaAbeja : MonoBehaviour
{
    enum Estado { Dormida, Pelea, Accion, Muerta }

    public Animator animator;
    public Transform visual;
    public Vector3 centroCamara;
    public float radioCamara = 15f;
    public float alturaVuelo = 10f;
    public AbejaColmena plantillaSoldado;
    public AudioClip chillido, zumbido;
    public GameObject particulasMuerte;
    public float danioPicada = 22f, danioAguijon = 8f, danioVendaval = 10f;
    public Color colorAguijon = new Color(1f, 0.82f, 0.2f);

    Estado estado = Estado.Dormida;
    RatHealth vida;
    Transform jugador;
    PlayerHealth vidaJugador;
    AudioSource audioSrc;
    float angulo, cdPicada = 4f, cdAguijon = 2.5f, cdLlamado = 9f, cdVendaval = 6f;
    bool aturdida;
    int soldadosVivos;
    string animActual = "";

    bool Fase2 => vida.HealthPercent < 0.5f;

    void Awake()
    {
        vida = GetComponent<RatHealth>();
        audioSrc = GetComponent<AudioSource>();
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.isTrigger = true;
        // aturdida en el suelo recibe mas daño; volando muy alto no se le puede golpear de todas formas
        vida.DamageFilter = a => aturdida ? a * 1.6f : a;
    }

    void OnEnable() { vida.OnDeath += Morir; vida.OnHealthChanged += AlDanio; }
    void OnDisable() { vida.OnDeath -= Morir; vida.OnHealthChanged -= AlDanio; }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) { jugador = pc.transform; vidaJugador = pc.GetComponent<PlayerHealth>(); }
        Anim("Run");
    }

    void Anim(string s) { if (animator != null && animActual != s) { animActual = s; animator.CrossFadeInFixedTime(s, 0.12f); } }
    void Sonar(AudioClip c, float v) { if (c != null && audioSrc != null) audioSrc.PlayOneShot(c, v * Ajustes.VolEfectos); }

    void AlDanio(float actual, float max) { if (estado == Estado.Dormida && actual > 0f) Despertar(); }

    void Despertar()
    {
        if (estado != Estado.Dormida) return;
        estado = Estado.Pelea;
        Sonar(chillido, 1f);
        if (Colmena.Instancia != null) Colmena.Instancia.Alarma(centroCamara, radioCamara + 25f);
        angulo = Mathf.Atan2(transform.position.z - centroCamara.z, transform.position.x - centroCamara.x);
    }

    // ------------------------------------------------------------------ bucle
    void Update()
    {
        if (estado == Estado.Muerta || jugador == null) return;
        float dt = Time.deltaTime;
        Vector3 aJugador = jugador.position - centroCamara;
        bool cerca = aJugador.sqrMagnitude < 70f * 70f;
        if (cerca) RatHealthBarUI.jefe = vida;
        else if (RatHealthBarUI.jefe == vida) RatHealthBarUI.jefe = null;

        if (estado == Estado.Dormida)
        {
            // reposa sobre el panal real moviendo un poco las alas
            if (new Vector2(aJugador.x, aJugador.z).magnitude < radioCamara + 4f && Mathf.Abs(aJugador.y) < 20f) Despertar();
            return;
        }
        if (estado == Estado.Accion) return;

        // vuela en circulos por su camara, mirando al jugador
        angulo += dt * (Fase2 ? 0.75f : 0.5f);
        float r = radioCamara * 0.65f;
        Vector3 obj = centroCamara + new Vector3(Mathf.Cos(angulo) * r, alturaVuelo + Mathf.Sin(Time.time * 1.3f) * 1.5f, Mathf.Sin(angulo) * r);
        transform.position = Vector3.Lerp(transform.position, obj, 1f - Mathf.Exp(-1.8f * dt));
        Mirar(jugador.position - transform.position, dt, 4f);
        Anim("Run");

        float k = Fase2 ? 0.65f : 1f;
        cdPicada -= dt; cdAguijon -= dt; cdLlamado -= dt; cdVendaval -= dt;
        bool enCamara = new Vector2(aJugador.x, aJugador.z).magnitude < radioCamara + 6f && Mathf.Abs(aJugador.y) < 25f;
        if (!enCamara) return;
        if (cdPicada <= 0f) { cdPicada = 6.5f * k; StartCoroutine(Picada()); }
        else if (cdAguijon <= 0f) { cdAguijon = 4.5f * k; StartCoroutine(Aguijones()); }
        else if (cdLlamado <= 0f) { cdLlamado = 15f * k; StartCoroutine(Llamado()); }
        else if (Fase2 && cdVendaval <= 0f && (jugador.position - transform.position).magnitude < 16f) { cdVendaval = 8f; StartCoroutine(Vendaval()); }
    }

    void Mirar(Vector3 d, float dt, float v)
    {
        d.y *= 0.3f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), v * dt);
    }

    IEnumerator Temblar(float dur)
    {
        if (visual == null) yield break;
        Vector3 b = visual.localPosition;
        for (float t = 0f; t < dur; t += Time.deltaTime) { visual.localPosition = b + Random.insideUnitSphere * 0.15f; yield return null; }
        visual.localPosition = b;
    }

    // ------------------------------------------------------------------ ataques
    IEnumerator Picada()
    {
        estado = Estado.Accion;
        Sonar(zumbido, 0.9f);
        yield return Temblar(0.6f);
        Vector3 desde = transform.position;
        Vector3 destino = jugador.position + Vector3.up * 0.8f;
        Anim("Attack");
        bool golpeo = false;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.55f)
        {
            destino = Vector3.Lerp(destino, jugador.position + Vector3.up * 0.8f, 0.04f);
            transform.position = Vector3.Lerp(desde, destino, t * t);
            Mirar(destino - desde, Time.deltaTime, 10f);
            if (!golpeo && (jugador.position + Vector3.up - transform.position).magnitude < 3.2f) { golpeo = true; if (vidaJugador != null) vidaJugador.TakeDamage(danioPicada); }
            yield return null;
        }
        // queda aturdida en el suelo: momento de golpearla
        aturdida = true;
        Anim("Death");
        Vector3 suelo = transform.position;
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)) suelo = hit.point + Vector3.up * 0.6f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f) { transform.position = Vector3.Lerp(transform.position, suelo, t); yield return null; }
        yield return new WaitForSeconds(Fase2 ? 1.4f : 2.2f);
        aturdida = false;
        Anim("Run");
        estado = Estado.Pelea;
    }

    IEnumerator Aguijones()
    {
        estado = Estado.Accion;
        yield return Temblar(0.45f);
        Anim("Attack");
        int n = Fase2 ? 9 : 6;
        Vector3 boca = transform.position + transform.forward * 2.5f;
        for (int i = 0; i < n; i++)
        {
            float a = (i - (n - 1) * 0.5f) * 8f;
            Vector3 dir = Quaternion.Euler(0f, a, 0f) * (jugador.position + Vector3.up - boca);
            EnemyProjectile.Spawn(boca, boca + dir, 17f, danioAguijon, colorAguijon, 0.65f, 1.5f, 0.45f);
            if (Fase2 && i % 3 == 2) yield return new WaitForSeconds(0.08f);
        }
        Sonar(chillido, 0.35f);
        yield return new WaitForSeconds(0.4f);
        Anim("Run");
        estado = Estado.Pelea;
    }

    IEnumerator Llamado()
    {
        estado = Estado.Accion;
        Sonar(zumbido, 1f);
        yield return Temblar(1.1f);
        if (plantillaSoldado != null && soldadosVivos < 10)
        {
            int n = Fase2 ? 5 : 3;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = centroCamara + new Vector3(Random.Range(-radioCamara, radioCamara) * 0.6f, alturaVuelo + 4f, Random.Range(-radioCamara, radioCamara) * 0.6f);
                var a = Instantiate(plantillaSoldado, p, Quaternion.identity, transform.parent);
                a.gameObject.SetActive(true);
                a.Enfurecer();
                soldadosVivos++;
                StartCoroutine(ContarSoldado(a));
            }
        }
        estado = Estado.Pelea;
    }

    IEnumerator ContarSoldado(AbejaColmena a) { while (a != null && !a.Muerta) yield return new WaitForSeconds(1f); soldadosVivos--; }

    IEnumerator Vendaval()
    {
        estado = Estado.Accion;
        Sonar(zumbido, 1f);
        yield return Temblar(0.8f);
        if ((jugador.position - transform.position).magnitude < 16f)
        {
            if (vidaJugador != null) vidaJugador.TakeDamage(danioVendaval);
            StatusEffects.Slow(0.45f, 2.5f);
        }
        estado = Estado.Pelea;
    }

    // ------------------------------------------------------------------ muerte
    void Morir()
    {
        if (estado == Estado.Muerta) return;
        StopAllCoroutines();
        estado = Estado.Muerta;
        aturdida = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Anim("Death");
        Sonar(chillido, 1f);
        StartCoroutine(RutinaMuerte());
    }

    IEnumerator RutinaMuerte()
    {
        Vector3 suelo = transform.position;
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) suelo = hit.point + Vector3.up * 0.4f;
        float vy = 0f;
        while (transform.position.y > suelo.y + 0.01f) { vy -= 20f * Time.deltaTime; transform.position = new Vector3(transform.position.x, Mathf.Max(suelo.y, transform.position.y + vy * Time.deltaTime), transform.position.z); yield return null; }
        if (particulasMuerte != null)
            for (int i = 0; i < 5; i++) Instantiate(particulasMuerte, transform.position + Random.insideUnitSphere * 2.5f + Vector3.up, Quaternion.identity);
        var drops = GetComponent<DropTable>();
        if (drops != null) drops.Drop(transform.position + transform.forward * 2f);
        if (RatHealthBarUI.jefe == vida) RatHealthBarUI.jefe = null;
        yield return new WaitForSeconds(0.8f);
        if (Colmena.Instancia != null) Colmena.Instancia.ReinaDerrotada();
        enabled = false;   // el cuerpo queda en el panal real
    }
}
