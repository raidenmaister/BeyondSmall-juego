using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Jefe de la cloaca: el Rey Rata, en su trono de basura del Gran Colector. Despierta cuando entras a su camara.
///   Mordisco   : cuerpo a cuerpo con aviso (se encoge y tiembla).
///   Coletazo   : gira sobre si mismo barriendo con la cola; se esquiva saltando.
///   Basura     : escupe una rafaga de mugre pegajosa (te frena).
///   Llamado    : chilla y salen crias por las tuberias de la pared.
/// Con menos de la mitad de vida:
///   Festin     : corre a un monton de comida y se cura comiendo; si le pegas fuerte mientras come, se atraganta.
///                Puedes destrozar los montones antes.
///   Zambullida : se mete al estanque, desaparece y sale de golpe bajo tus pies (mira las burbujas).
/// Al morir suelta una bateria verde y las ratas de la cloaca entran en panico.
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class ReyRata : MonoBehaviour
{
    enum Estado { Dormido, Pelea, Accion, Muerto }

    public Transform visual;
    public Animator animator;
    public Vector3 hogar;
    public float radioCorrea = 44f;
    public float radioDespertar = 34f;
    public float velocidad = 5.5f;

    [Header("Estanque (zambullida)")]
    public Vector3 centroEstanque;
    public float radioEstanque = 15f;
    public float nivelAgua;

    [Header("Ataques")]
    public float danioMordisco = 24f;
    public float alcanceMordisco = 9f;
    public float danioColetazo = 18f;
    public float radioColetazo = 13f;
    public float danioBasura = 9f;
    public float danioZambullida = 28f;
    public Color colorBasura = new Color(0.42f, 0.4f, 0.16f);
    public RataCloaca plantillaCria;
    public Transform[] puntosLlamado;

    [Header("Sonido y efectos")]
    public AudioClip rugido, chillido, siseo, chapoteo;
    public GameObject particulasMuerte;
    public Material matRemolino;

    static readonly int HashEstado = Animator.StringToHash("State");
    const int AQuieta = 0, ACorre = 1, ACamina = 2, AAtaca = 3, AMuere = 5;

    Estado estado = Estado.Dormido;
    RatHealth vida;
    NavMeshAgent agente;
    Transform jugador;
    PlayerHealth vidaJugador;
    CharacterController ccJugador;
    AudioSource audioSrc;
    float cdAtaque = 2f, cdColetazo = 6f, cdBasura = 4f, cdLlamado = 9f, cdFestin = 4f, cdZambullida = 8f, timerGrunido = 4f, timerDestino;
    float pitch, temblor, hundido, danioEnFestin;
    bool fase2Avisada, avisoFestin, avisoColetazo, sumergido, comiendo;
    int animActual = -1;
    Vector3 visualBase;

    bool Fase2 => vida.HealthPercent < 0.5f;
    public bool Muerto => estado == Estado.Muerto;

    void Awake()
    {
        vida = GetComponent<RatHealth>();
        agente = GetComponent<NavMeshAgent>();
        audioSrc = GetComponent<AudioSource>();
        if (hogar == Vector3.zero) hogar = transform.position;
        if (visual != null) visualBase = visual.localPosition;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.isTrigger = true;
        vida.DamageFilter = a =>
        {
            if (sumergido) return 0f;                       // bajo el agua no se le puede pegar
            if (comiendo) danioEnFestin += a;
            return estado == Estado.Dormido ? a * 1.5f : a; // golpe a traicion
        };
    }

    void OnEnable() { vida.OnDeath += Morir; vida.OnHealthChanged += AlDanio; }
    void OnDisable() { vida.OnDeath -= Morir; vida.OnHealthChanged -= AlDanio; }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) { jugador = pc.transform; vidaJugador = pc.GetComponent<PlayerHealth>(); ccJugador = pc.GetComponent<CharacterController>(); }
        if (agente != null && !agente.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 10f, NavMesh.AllAreas)) agente.Warp(hit.position);
        if (agente != null) { agente.updateRotation = false; agente.speed = velocidad; }
        Anim(AQuieta);
    }

    void Anim(int e)
    {
        if (animator == null || animActual == e) return;
        animActual = e;
        animator.SetInteger(HashEstado, e);
    }

    void Sonar(AudioClip c, float vol, float tono = 0.75f)
    {
        if (c == null || audioSrc == null) return;
        audioSrc.pitch = tono;
        audioSrc.PlayOneShot(c, vol * Ajustes.VolEfectos);
    }

    bool AgenteListo => agente != null && agente.enabled && agente.isOnNavMesh;

    void AlDanio(float actual, float max)
    {
        if (estado == Estado.Dormido && actual > 0f) Despertar();
    }

    void Despertar()
    {
        if (estado != Estado.Dormido) return;
        estado = Estado.Pelea;
        Sonar(rugido, 1f, 0.7f);
        Aviso.Mostrar("¡El Rey Rata desperto!", new Color(1f, 0.45f, 0.3f));
        if (Cloaca.Instancia != null) Cloaca.Instancia.Alarma(hogar, radioCorrea + 20f);
        cdLlamado = 6f;
    }

    // ------------------------------------------------------------------ bucle
    void Update()
    {
        if (estado == Estado.Muerto || jugador == null) return;
        float dt = Time.deltaTime;
        Vector3 aJug = jugador.position - hogar;
        if (aJug.sqrMagnitude < 120f * 120f && Mathf.Abs(aJug.y) < 40f) RatHealthBarUI.jefe = vida;
        AnimarVisual(dt);

        if (estado == Estado.Dormido)
        {
            Anim(AQuieta);
            if (animator != null) animator.speed = 0.5f;     // respira despacio
            if (Partida.Agresivos && aJug.magnitude < radioDespertar) Despertar();
            return;
        }
        if (animator != null && animActual != AAtaca) animator.speed = 1f;
        if (estado == Estado.Accion) return;

        if (!fase2Avisada && Fase2)
        {
            fase2Avisada = true;
            Aviso.Mostrar("¡El Rey Rata esta furioso!", new Color(1f, 0.3f, 0.2f));
            StartCoroutine(Llamado());
            return;
        }

        Vector3 hacia = jugador.position - transform.position; hacia.y = 0f;
        float dist = hacia.magnitude;
        Girar(hacia, dt, 3f);
        timerGrunido -= dt;
        if (timerGrunido <= 0f) { timerGrunido = Random.Range(4f, 8f); Sonar(siseo, 0.45f, Random.Range(0.6f, 0.8f)); }

        // si el jugador se va de la camara, vuelve a su trono
        bool fuera = aJug.magnitude > radioCorrea + 25f || !Partida.Agresivos;
        Vector3 destino = fuera ? hogar : jugador.position;
        Vector3 off = destino - hogar; off.y = 0f;
        if (off.magnitude > radioCorrea) destino = hogar + off.normalized * radioCorrea;
        float vel = velocidad * (Fase2 ? 1.3f : 1f);
        if (AgenteListo)
        {
            timerDestino -= dt;
            if (timerDestino <= 0f) { timerDestino = 0.25f; agente.speed = vel; agente.stoppingDistance = fuera ? 1f : alcanceMordisco * 0.7f; agente.SetDestination(destino); }
            float v = agente.velocity.magnitude;
            if (v > 0.3f) { Anim(v > velocidad * 1.1f ? ACorre : ACamina); if (animator != null) animator.speed = Mathf.Clamp(v / velocidad, 0.6f, 1.4f) * 0.8f; }
            else Anim(AQuieta);
        }
        if (fuera) return;

        float k = Fase2 ? 0.7f : 1f;
        cdAtaque -= dt; cdColetazo -= dt; cdBasura -= dt; cdLlamado -= dt; cdFestin -= dt; cdZambullida -= dt;
        if (Fase2 && cdFestin <= 0f && vida.HealthPercent < 0.42f && MontonCercano() != null) { cdFestin = 20f; StartCoroutine(Festin()); }
        else if (Fase2 && cdZambullida <= 0f && dist > 10f) { cdZambullida = 15f; StartCoroutine(Zambullida()); }
        else if (cdColetazo <= 0f && dist < radioColetazo - 1f) { cdColetazo = 7.5f * k; StartCoroutine(Coletazo()); }
        else if (cdAtaque <= 0f && dist < alcanceMordisco) { cdAtaque = 2f * k; StartCoroutine(Mordisco()); }
        else if (cdBasura <= 0f && dist > 11f) { cdBasura = 6f * k; StartCoroutine(Basura()); }
        else if (cdLlamado <= 0f) { cdLlamado = 20f * k; StartCoroutine(Llamado()); }
    }

    void Girar(Vector3 dir, float dt, float v)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), v * dt);
    }

    void AnimarVisual(float dt)
    {
        if (visual == null) return;
        Vector3 shk = temblor > 0f ? new Vector3(Mathf.Sin(Time.time * 60f), 0f, Mathf.Cos(Time.time * 53f)) * 0.3f * temblor : Vector3.zero;
        visual.localPosition = visualBase + shk + Vector3.down * hundido;
        visual.localRotation = Quaternion.Euler(-pitch, 0f, 0f);
    }

    void Parar() { if (AgenteListo) agente.ResetPath(); Anim(AQuieta); }

    bool JugadorEnSuelo => ccJugador == null || ccJugador.isGrounded;

    void Empujar(Vector3 desde, float fuerza)
    {
        if (ccJugador == null) return;
        Vector3 d = jugador.position - desde; d.y = 0f;
        StartCoroutine(RutinaEmpuje(d.normalized * fuerza));
    }

    IEnumerator RutinaEmpuje(Vector3 v)
    {
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            if (ccJugador != null && ccJugador.enabled) ccJugador.Move(v * (1f - t / 0.3f) * Time.deltaTime);
            yield return null;
        }
    }

    // ------------------------------------------------------------------ ataques
    IEnumerator Mordisco()
    {
        estado = Estado.Accion; Parar();
        for (float t = 0f; t < 0.55f; t += Time.deltaTime) { pitch = Mathf.Lerp(0f, 12f, t / 0.55f); temblor = t / 0.55f; Girar(jugador.position - transform.position, Time.deltaTime, 6f); yield return null; }
        temblor = 0f;
        Anim(AAtaca); if (animator != null) animator.speed = 1.3f;
        Sonar(chillido, 0.6f, 0.65f);
        Vector3 f = transform.forward;
        for (float t = 0f; t < 0.2f; t += Time.deltaTime)
        {
            pitch = Mathf.Lerp(12f, -10f, t / 0.2f);
            if (AgenteListo) agente.Move(f * 14f * Time.deltaTime);
            yield return null;
        }
        Vector3 hacia = jugador.position - transform.position; hacia.y = 0f;
        if (hacia.magnitude < alcanceMordisco + 2f && Vector3.Angle(transform.forward, hacia) < 70f && vidaJugador != null)
        {
            vidaJugador.TakeDamage(danioMordisco);
            Empujar(transform.position, 10f);
        }
        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { pitch = Mathf.Lerp(-10f, 0f, t / 0.4f); yield return null; }
        animActual = -1;
        estado = Estado.Pelea;
    }

    IEnumerator Coletazo()
    {
        estado = Estado.Accion; Parar();
        if (!avisoColetazo) { avisoColetazo = true; Aviso.Mostrar("¡Va a dar un coletazo! Salta para esquivarlo", new Color(1f, 0.85f, 0.4f), 3f); }
        Sonar(siseo, 0.9f, 0.7f);
        for (float t = 0f; t < 0.75f; t += Time.deltaTime) { pitch = -7f; temblor = 0.4f + t / 0.75f * 0.6f; yield return null; }
        temblor = 0f;
        // gira una vuelta entera: la cola barre todo alrededor
        Quaternion r0 = transform.rotation;
        bool golpeo = false;
        float dur = Fase2 ? 0.4f : 0.5f;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            transform.rotation = r0 * Quaternion.Euler(0f, 360f * (t / dur), 0f);
            Vector3 d = jugador.position - transform.position; d.y = 0f;
            if (!golpeo && t > dur * 0.2f && d.magnitude < radioColetazo && JugadorEnSuelo && vidaJugador != null)
            {
                golpeo = true;
                vidaJugador.TakeDamage(danioColetazo);
                Empujar(transform.position, 22f);
            }
            yield return null;
        }
        transform.rotation = r0;
        pitch = 0f;
        yield return new WaitForSeconds(0.5f);    // queda mareado un instante: momento para pegarle
        estado = Estado.Pelea;
    }

    IEnumerator Basura()
    {
        estado = Estado.Accion; Parar();
        for (float t = 0f; t < 0.7f; t += Time.deltaTime) { pitch = Mathf.Lerp(0f, 18f, t / 0.7f); temblor = 0.5f; Girar(jugador.position - transform.position, Time.deltaTime, 6f); yield return null; }
        temblor = 0f;
        Sonar(chillido, 0.5f, 0.55f);
        Vector3 boca = transform.position + transform.forward * 9f + Vector3.up * 3.5f;
        int n = Fase2 ? 7 : 5;
        for (int i = 0; i < n; i++)
        {
            float ang = (i - (n - 1) * 0.5f) * 8f;
            Vector3 obj = jugador.position + Vector3.up * 0.6f;
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * (obj - boca);
            var pr = EnemyProjectile.Spawn(boca, boca + dir, 15f, danioBasura, colorBasura, 0.55f, 1.6f, 1.1f);
            if (pr != null) pr.textoLento = "Cubierto de basura";
        }
        for (float t = 0f; t < 0.4f; t += Time.deltaTime) { pitch = Mathf.Lerp(18f, 0f, t / 0.4f); yield return null; }
        estado = Estado.Pelea;
    }

    IEnumerator Llamado()
    {
        estado = Estado.Accion; Parar();
        Sonar(rugido, 1f, 0.8f);
        for (float t = 0f; t < 1.3f; t += Time.deltaTime) { pitch = 24f * Mathf.Sin(t / 1.3f * Mathf.PI); temblor = 1f; yield return null; }
        temblor = 0f; pitch = 0f;
        if (plantillaCria != null && puntosLlamado != null && puntosLlamado.Length > 0)
        {
            int n = Fase2 ? 4 : 3;
            int vivas = 0;
            if (Cloaca.Instancia != null) foreach (var r in Cloaca.Instancia.ratas) if (r != null && !r.Muerta && r.name.StartsWith("Cria")) vivas++;
            n = Mathf.Min(n, 8 - vivas);
            for (int i = 0; i < n; i++)
            {
                var p = puntosLlamado[Random.Range(0, puntosLlamado.Length)];
                if (p == null) continue;
                Vector3 pos = p.position + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
                if (NavMesh.SamplePosition(pos, out var hit, 6f, NavMesh.AllAreas)) pos = hit.position;
                var r = Instantiate(plantillaCria, pos, p.rotation, transform.parent);
                r.name = "Cria_" + Time.frameCount + "_" + i;
                r.hogar = pos;
                r.gameObject.SetActive(true);
                r.Enfurecer();
            }
        }
        estado = Estado.Pelea;
    }

    MontonComida MontonCercano()
    {
        MontonComida mejor = null; float dMin = float.MaxValue;
        foreach (var m in MontonComida.Todos)
        {
            if (m == null || !m.Disponible) continue;
            float d = (m.transform.position - transform.position).sqrMagnitude;
            if (d < dMin) { dMin = d; mejor = m; }
        }
        return mejor;
    }

    IEnumerator Festin()
    {
        var m = MontonCercano();
        if (m == null) yield break;
        estado = Estado.Accion;
        if (!avisoFestin) { avisoFestin = true; Aviso.Mostrar("¡El Rey Rata se cura comiendo! Destroza sus montones de comida", new Color(1f, 0.8f, 0.35f), 4f); }
        Sonar(chillido, 0.8f, 0.7f);
        // corre al monton
        float limite = 6f;
        while (limite > 0f && m != null && m.Disponible)
        {
            limite -= Time.deltaTime;
            Vector3 d = m.transform.position - transform.position; d.y = 0f;
            if (d.magnitude < 7f) break;
            if (AgenteListo) { agente.speed = velocidad * 1.6f; agente.stoppingDistance = 5f; agente.SetDestination(m.transform.position); }
            Anim(ACorre);
            Girar(d, Time.deltaTime, 6f);
            yield return null;
        }
        Parar();
        if (m == null || !m.Disponible) { estado = Estado.Pelea; yield break; }
        // come: se cura a mordiscos mientras no lo interrumpan
        comiendo = true; danioEnFestin = 0f;
        float umbral = vida.MaxHealth * 0.05f;
        for (int i = 0; i < 4 && m != null && m.Disponible; i++)
        {
            Anim(AAtaca); animActual = -1;
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                pitch = 14f * Mathf.Abs(Mathf.Sin(t / 0.8f * Mathf.PI * 2f));
                Girar(m.transform.position - transform.position, Time.deltaTime, 5f);
                if (danioEnFestin >= umbral) break;
                yield return null;
            }
            if (danioEnFestin >= umbral)
            {
                // se atraganta: queda aturdido
                comiendo = false; pitch = 0f;
                Aviso.Mostrar("¡Le arruinaste el festin!", new Color(0.6f, 1f, 0.5f), 2.5f);
                Sonar(chillido, 1f, 0.9f);
                Anim(AQuieta);
                for (float t = 0f; t < 2.2f; t += Time.deltaTime) { temblor = 0.6f; yield return null; }
                temblor = 0f;
                estado = Estado.Pelea;
                yield break;
            }
            if (m.Comer()) vida.Heal(vida.MaxHealth * 0.045f);
        }
        comiendo = false; pitch = 0f;
        Sonar(rugido, 0.7f, 0.85f);
        estado = Estado.Pelea;
    }

    IEnumerator Zambullida()
    {
        if (radioEstanque <= 0f) yield break;
        estado = Estado.Accion;
        // corre al estanque
        Vector3 borde = centroEstanque + Vector3.ProjectOnPlane(transform.position - centroEstanque, Vector3.up).normalized * (radioEstanque * 0.5f);
        float limite = 5f;
        while (limite > 0f)
        {
            limite -= Time.deltaTime;
            Vector3 d = borde - transform.position; d.y = 0f;
            if (d.magnitude < 4f) break;
            if (AgenteListo) { agente.speed = velocidad * 1.7f; agente.stoppingDistance = 2f; agente.SetDestination(borde); }
            Anim(ACorre);
            Girar(d, Time.deltaTime, 6f);
            yield return null;
        }
        Parar();
        Sonar(chapoteo, 1f, 0.7f);
        // se hunde
        sumergido = true;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime) { hundido = Mathf.Lerp(0f, 9f, t / 0.6f); pitch = 20f; yield return null; }
        var rends = GetComponentsInChildren<Renderer>();
        foreach (var r in rends) r.enabled = false;
        // burbujas que te siguen
        var remolino = CrearRemolino();
        Vector3 objetivo = transform.position;
        for (float t = 0f; t < 2.2f; t += Time.deltaTime)
        {
            if (t < 1.5f) objetivo = PuntoEmerger();
            if (remolino != null)
            {
                remolino.transform.position = objetivo + Vector3.up * 0.15f;
                float s = 5f + Mathf.Sin(t * 14f) * 0.6f + t * 1.5f;
                remolino.transform.localScale = new Vector3(s, 0.04f, s);
                remolino.transform.Rotate(0f, 360f * Time.deltaTime, 0f);
            }
            yield return null;
        }
        if (remolino != null) Destroy(remolino);
        // sale de golpe
        if (AgenteListo && NavMesh.SamplePosition(objetivo, out var hit, 6f, NavMesh.AllAreas)) agente.Warp(hit.position);
        Vector3 hacia = jugador.position - transform.position; hacia.y = 0f;
        if (hacia.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(hacia);
        foreach (var r in rends) if (r != null) r.enabled = true;
        Sonar(chapoteo, 1f, 0.6f);
        Sonar(rugido, 0.9f, 0.75f);
        for (float t = 0f; t < 0.3f; t += Time.deltaTime) { hundido = Mathf.Lerp(9f, 0f, t / 0.3f); pitch = Mathf.Lerp(35f, 0f, t / 0.3f); yield return null; }
        hundido = 0f; pitch = 0f;
        sumergido = false;
        Vector3 dd = jugador.position - transform.position; dd.y = 0f;
        if (dd.magnitude < 8f && vidaJugador != null) { vidaJugador.TakeDamage(danioZambullida); Empujar(transform.position, 18f); }
        yield return new WaitForSeconds(0.6f);
        estado = Estado.Pelea;
    }

    // donde va a salir: bajo el jugador si esta en el agua o cerca; si no, en la orilla mas cercana a el
    Vector3 PuntoEmerger()
    {
        Vector3 p = jugador.position;
        Vector3 d = p - centroEstanque; d.y = 0f;
        float r = d.magnitude;
        if (r > radioEstanque + 9f) p = centroEstanque + d.normalized * (radioEstanque + 6f);
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)) p.y = Mathf.Max(hit.point.y, nivelAgua);
        else p.y = nivelAgua;
        return p;
    }

    GameObject CrearRemolino()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Remolino";
        Destroy(go.GetComponent<Collider>());
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (matRemolino != null) r.sharedMaterial = matRemolino;
        return go;
    }

    // ------------------------------------------------------------------ muerte
    void Morir()
    {
        if (estado == Estado.Muerto) return;
        StopAllCoroutines();
        estado = Estado.Muerto;
        sumergido = false; comiendo = false; hundido = 0f; temblor = 0f; pitch = 0f;
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (agente != null && agente.enabled) agente.enabled = false;
        AnimarVisual(0f);
        Anim(AMuere);
        if (animator != null) animator.speed = 0.6f;
        Sonar(chillido, 1f, 0.5f);
        StartCoroutine(RutinaMuerte());
    }

    IEnumerator RutinaMuerte()
    {
        yield return new WaitForSeconds(2f);
        if (particulasMuerte != null)
            for (int i = 0; i < 6; i++)
                Instantiate(particulasMuerte, transform.position + Random.insideUnitSphere * 4f + Vector3.up * 2f, Quaternion.identity);
        var drops = GetComponent<DropTable>();
        if (drops != null) drops.Drop(transform.position + transform.forward * 6f);
        if (RatHealthBarUI.jefe == vida) RatHealthBarUI.jefe = null;
        Aviso.Mostrar("¡Derrotaste al Rey Rata! Las ratas huyen despavoridas", new Color(0.55f, 1f, 0.55f), 4.5f);
        if (Cloaca.Instancia != null) Cloaca.Instancia.DesatarPanico(true);
        // el cuerpo queda en la camara como trofeo
        yield return new WaitForSeconds(1.5f);
        if (animator != null) animator.speed = 0f;
        enabled = false;
    }
}
