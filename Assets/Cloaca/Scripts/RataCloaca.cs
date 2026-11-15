using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Rata de la cloaca (modelo animado de rata). Papeles:
///  Forrajera : sale por el derrumbe al jardin, husmea, vuelve a la madriguera con comida en el hocico.
///              Con comida es cobarde: huye del jugador. Si la matas suelta la carne.
///  Guardia   : patrulla una zona de los tuneles y ataca si te ve.
///  Nadadora  : nada por el canal de agua sucia; si te metes al agua cerca, te embosca.
///  Reposo    : duerme/deambula en la madriguera; defiende el nido.
///  Ladrona   : si llevas carne, te acecha, te roba un trozo y corre a la madriguera con ella
///              (sale en el mapa como ladron). Golpeala y la suelta.
/// Todas le temen al fuego: con la antorcha encendida muerden y retroceden. Al morir el Rey Rata entran en panico.
/// Se mueven por el NavMesh de la cloaca (incluye el jardin alrededor del derrumbe).
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class RataCloaca : MonoBehaviour
{
    public enum Rol { Forrajera, Guardia, Nadadora, Reposo, Ladrona }
    enum Fase { Salir, Husmear, Volver, Descansar }

    public Rol rol = Rol.Guardia;
    public Animator animator;
    public Transform visual;
    public GameObject carga;
    public Vector3 hogar;
    public float radioHogar = 8f;
    public Vector3[] patrulla;

    [Header("Movimiento")]
    public float velocidadCamina = 3.2f;
    public float velocidadCorre = 6.6f;
    public float velocidadHuida = 5.6f;     // un poco menos que el jugador corriendo: se le puede alcanzar

    [Header("Ataque")]
    public float danio = 8f;
    public float alcance = 3.4f;
    public float intervaloAtaque = 1.4f;
    public float radioVista = 16f;
    public GameObject particulasMuerte;
    public AudioClip[] chillidos;

    [Tooltip("La saco el gestor (forrajera extra): desaparece al volver a la madriguera si no la ves")]
    public bool temporal;

    public bool Hostil { get; private set; }
    public bool Muerta { get; private set; }
    public bool Carga => carga != null && carga.activeSelf;

    static readonly int HashEstado = Animator.StringToHash("State");
    const int AQuieta = 0, ACorre = 1, ACamina = 2, AAtaca = 3, AMuere = 5;

    RatHealth vida;
    NavMeshAgent agente;
    Transform jugador;
    PlayerHealth vidaJugador;
    AudioSource audioSrc;
    Fase fase = Fase.Descansar;
    int animActual = -1, iPatrulla;
    float espera, cdAtaque, timerDestino, timerCalma, retroceso, timerHuida, cdChillido, tGolpe = -99f;
    bool atacando, huyendo, robando;
    string comidaRobada;

    // ------------------------------------------------------------------ ciclo
    void Awake()
    {
        vida = GetComponent<RatHealth>();
        agente = GetComponent<NavMeshAgent>();
        audioSrc = GetComponent<AudioSource>();
        if (hogar == Vector3.zero) hogar = transform.position;
        if (carga != null) carga.SetActive(false);
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.isTrigger = true;
    }

    void OnEnable()
    {
        vida.OnHealthChanged += AlDanio;
        vida.OnDeath += Morir;
        if (Cloaca.Instancia != null) Cloaca.Instancia.Registrar(this);
    }

    void OnDisable()
    {
        vida.OnHealthChanged -= AlDanio;
        vida.OnDeath -= Morir;
        if (Cloaca.Instancia != null) Cloaca.Instancia.Quitar(this);
    }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) { jugador = pc.transform; vidaJugador = pc.GetComponent<PlayerHealth>(); }
        if (Cloaca.Instancia != null) Cloaca.Instancia.Registrar(this);
        if (agente != null && !agente.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out var hit, 8f, NavMesh.AllAreas)) agente.Warp(hit.position);
            else agente.enabled = false;
        }
        espera = Random.Range(0.5f, 6f);
        cdChillido = Random.Range(2f, 8f);
        if (animator != null) animator.SetInteger(HashEstado, AQuieta);
    }

    bool AgenteListo => agente != null && agente.enabled && agente.isOnNavMesh;
    Cloaca C => Cloaca.Instancia;

    void Anim(int estado)
    {
        if (animator == null || animActual == estado) return;
        animActual = estado;
        animator.SetInteger(HashEstado, estado);
    }

    void Chillar(float vol = 0.7f)
    {
        if (chillidos == null || chillidos.Length == 0) return;
        var clip = chillidos[Random.Range(0, chillidos.Length)];
        if (clip == null) return;
        if (audioSrc != null) { audioSrc.pitch = Random.Range(0.9f, 1.25f); audioSrc.PlayOneShot(clip, vol * Ajustes.VolEfectos); }
        else AudioSource.PlayClipAtPoint(clip, transform.position, vol * Ajustes.VolEfectos);
    }

    // ------------------------------------------------------------------ dano
    void AlDanio(float actual, float max)
    {
        if (Muerta || actual <= 0f) return;
        // si llevaba lo que te robo, lo suelta al primer golpe
        var lad = GetComponent<Ladron>();
        if (lad != null) lad.Soltar();
        if (Carga) SoltarComida();
        robando = false;
        tGolpe = Time.time;
        Chillar(0.9f);
        if (!Hostil)
        {
            Enfurecer();
            if (C != null && !C.Panico) C.Alarma(transform.position, 16f);
        }
        timerCalma = 0f;
        if (jugador != null && AgenteListo)
        {
            Vector3 dir = transform.position - jugador.position; dir.y = 0f;
            agente.Move(dir.normalized * 0.9f);
        }
    }

    public void Enfurecer()
    {
        if (Muerta || Hostil) return;
        if (!Partida.Agresivos) return;
        Hostil = true; huyendo = false; robando = false;
        timerCalma = 0f;
        if (AgenteListo) { agente.speed = velocidadCorre; agente.stoppingDistance = alcance * 0.75f; agente.autoBraking = false; }
    }

    void Calmarse()
    {
        Hostil = false; atacando = false;
        if (AgenteListo) { agente.speed = velocidadCamina; agente.stoppingDistance = 0.4f; agente.autoBraking = true; agente.SetDestination(hogar); }
        fase = Fase.Volver;
    }

    void Morir()
    {
        if (Muerta) return;
        Muerta = true;
        StopAllCoroutines();
        if (agente != null && agente.enabled) agente.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (C != null) C.Quitar(this);
        Anim(AMuere);
        if (animator != null) animator.speed = 1f;
        Chillar(1f);
        StartCoroutine(RutinaMuerte());
    }

    IEnumerator RutinaMuerte()
    {
        yield return new WaitForSeconds(1.1f);
        if (particulasMuerte != null) Instantiate(particulasMuerte, transform.position + Vector3.up * 0.8f, Quaternion.identity);
        var drops = GetComponent<DropTable>();
        if (drops != null) drops.Drop(transform.position);
        if (Carga) SoltarComida();
        // se hunde en el suelo y desaparece
        Vector3 p0 = transform.position;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 1.2f) { transform.position = p0 + Vector3.down * t * 1.6f; yield return null; }
        Destroy(gameObject);
    }

    /// <summary>Deja caer la carne que llevaba en el hocico (se puede recoger).</summary>
    void SoltarComida()
    {
        if (carga != null) carga.SetActive(false);
        var pref = Resources.Load<GameObject>("Drops/Carne");
        if (pref == null) return;
        Vector3 p = transform.position + transform.forward * 1.5f + Vector3.up * 0.8f;
        var go = Instantiate(pref, p, Quaternion.Euler(0f, Random.value * 360f, 0f));
        go.name = "Drop_Carne";
        var wi = go.GetComponent<WorldItem>(); if (wi != null) { wi.itemId = "Carne"; wi.quantity = 1; }
        if (FloorItemPromptManager.Instance != null) FloorItemPromptManager.Instance.RegisterTrackedItem(go);
    }

    // ------------------------------------------------------------------ bucle
    void Update()
    {
        if (Muerta || jugador == null) return;
        float dt = Time.deltaTime;
        cdAtaque -= dt;
        cdChillido -= dt;
        if (cdChillido <= 0f) { cdChillido = Random.Range(5f, 12f); if ((jugador.position - transform.position).sqrMagnitude < 30f * 30f) Chillar(0.35f); }

        if (!AgenteListo) { Anim(AQuieta); return; }
        if (Hostil) Atacar(dt);
        else if (robando) Robar(dt);
        else if (huyendo) Huir(dt);
        else Comportamiento(dt);
        AnimarSegunVelocidad();
    }

    void AnimarSegunVelocidad()
    {
        if (atacando || animator == null) return;
        float v = agente.velocity.magnitude;
        if (v < 0.25f) { Anim(AQuieta); animator.speed = 1f; }
        else if (v < velocidadCamina * 1.25f) { Anim(ACamina); animator.speed = Mathf.Clamp(v / velocidadCamina, 0.6f, 1.6f); }
        else { Anim(ACorre); animator.speed = Mathf.Clamp(v / velocidadCorre, 0.7f, 1.5f); }
    }

    float DistJugador => Vector3.Distance(transform.position, jugador.position);
    bool AntorchaCerca => Antorcha.Encendida && (Antorcha.Posicion - transform.position).sqrMagnitude < 9f * 9f;

    void Ir(Vector3 destino, float vel)
    {
        agente.speed = vel;
        agente.stoppingDistance = 0.4f;
        agente.autoBraking = true;
        if (NavMesh.SamplePosition(destino, out var hit, 10f, NavMesh.AllAreas)) agente.SetDestination(hit.position);
    }

    bool Llego => !agente.pathPending && agente.remainingDistance <= agente.stoppingDistance + 0.6f;

    void Comportamiento(float dt)
    {
        float d = DistJugador;
        bool agresivos = Partida.Agresivos && (C == null || !C.Panico);

        // en panico (murio el rey) o con la antorcha encima: se apartan del jugador
        if ((C != null && C.Panico && d < 12f) || (AntorchaCerca && d < 7f && rol != Rol.Ladrona))
        {
            EmpezarHuida(1.5f);
            return;
        }

        switch (rol)
        {
            case Rol.Forrajera: Forrajear(dt, d); break;
            case Rol.Guardia:
                if (agresivos && d < radioVista && LoVe()) { Enfurecer(); if (C != null) C.Alarma(transform.position, 14f); return; }
                Patrullar(dt, 0.9f);
                break;
            case Rol.Nadadora:
                if (agresivos && ((d < radioVista && C != null && C.JugadorEnAgua) || d < 6f)) { Enfurecer(); return; }
                Patrullar(dt, 0.75f);
                break;
            case Rol.Reposo:
                if (agresivos && d < radioVista && GetComponent<Ladron>() == null) { Enfurecer(); if (C != null) C.Alarma(transform.position, 12f); return; }
                Deambular(dt, 0.5f);
                break;
            case Rol.Ladrona:
                if (GetComponent<Ladron>() == null && C != null && d < 30f && C.PuedeRobar(out comidaRobada)) { robando = true; return; }
                Deambular(dt, 0.7f);
                break;
        }
    }

    bool LoVe()
    {
        Vector3 ojo = transform.position + Vector3.up * 1.2f, obj = jugador.position + Vector3.up * 0.5f;
        return !Physics.Linecast(ojo, obj, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    void Patrullar(float dt, float factorVel)
    {
        if (patrulla == null || patrulla.Length == 0) { Deambular(dt, factorVel); return; }
        if (espera > 0f) { espera -= dt; if (espera <= 0f) Ir(patrulla[iPatrulla], velocidadCamina * factorVel); return; }
        if (Llego)
        {
            iPatrulla = (iPatrulla + 1) % patrulla.Length;
            espera = Random.Range(0.6f, 3f);
        }
    }

    void Deambular(float dt, float factorVel)
    {
        if (espera > 0f)
        {
            espera -= dt;
            if (espera <= 0f)
            {
                Vector2 c = Random.insideUnitCircle * radioHogar;
                Ir(hogar + new Vector3(c.x, 0f, c.y), velocidadCamina * factorVel);
            }
            return;
        }
        if (Llego) espera = Random.Range(2f, 7f);
    }

    // ------------------------------------------------------------------ forrajeras
    void Forrajear(float dt, float d)
    {
        // con comida en el hocico son cobardes
        if (Carga && d < 9f && Partida.Agresivos) { EmpezarHuida(2f); return; }
        switch (fase)
        {
            case Fase.Descansar:
                espera -= dt;
                if (espera > 0f) return;
                if (temporal && C != null && d > 45f && Vector3.Distance(transform.position, C.madriguera) < 20f) { Destroy(gameObject); return; }
                fase = Fase.Salir;
                Ir(C != null ? C.PuntoJardin() : hogar, velocidadCamina * 1.15f);
                break;
            case Fase.Salir:
                if (Llego) { fase = Fase.Husmear; espera = Random.Range(2.5f, 6f); agente.ResetPath(); }
                break;
            case Fase.Husmear:
                espera -= dt;
                // de vez en cuando husmea un poco mas alla
                if (espera <= 0f)
                {
                    if (carga != null) carga.SetActive(true);
                    fase = Fase.Volver;
                    Ir(C != null ? C.PuntoMadriguera() : hogar, velocidadCamina * 1.2f);
                }
                break;
            case Fase.Volver:
                if (Llego)
                {
                    if (carga != null) carga.SetActive(false);
                    fase = Fase.Descansar;
                    bool noche = DayNightCycle.Instance != null && DayNightCycle.Instance.Noche > 0.5f;
                    espera = noche ? Random.Range(3f, 8f) : Random.Range(10f, 25f);   // de noche salen mas
                }
                break;
        }
    }

    // ------------------------------------------------------------------ huir
    void EmpezarHuida(float segundos)
    {
        huyendo = true;
        timerHuida = segundos;
        Vector3 lejos = transform.position - jugador.position; lejos.y = 0f;
        Vector3 obj = transform.position + lejos.normalized * 14f;
        agente.speed = velocidadHuida; agente.stoppingDistance = 0.3f; agente.autoBraking = false;
        if (NavMesh.SamplePosition(obj, out var hit, 8f, NavMesh.AllAreas)) agente.SetDestination(hit.position);
        else if (C != null) agente.SetDestination(C.PuntoMadriguera());
        if (Random.value < 0.5f) Chillar(0.6f);
    }

    void Huir(float dt)
    {
        timerHuida -= dt;
        if (timerHuida > 0f && !Llego) return;
        huyendo = false;
        // retoma lo que estaba haciendo
        if (rol == Rol.Forrajera)
        {
            if (Carga || fase == Fase.Volver) Ir(C != null ? C.PuntoMadriguera() : hogar, velocidadCamina * 1.2f);
            else if (fase == Fase.Salir && C != null) Ir(C.PuntoJardin(), velocidadCamina * 1.15f);
        }
        else espera = 0.5f;
    }

    // ------------------------------------------------------------------ ladrona
    void Robar(float dt)
    {
        if (!Partida.Agresivos || C == null || C.Panico) { robando = false; return; }
        var inv = ControladorInventario.Instance;
        if (inv == null || string.IsNullOrEmpty(comidaRobada) || inv.GetTotalCount(comidaRobada) <= 0)
        {   // ya no lleva esa comida: busca otra o se rinde
            if (!C.PuedeRobar(out comidaRobada)) { robando = false; espera = 2f; return; }
        }
        float d = DistJugador;
        if (d > 40f) { robando = false; espera = 2f; return; }
        timerDestino -= dt;
        if (timerDestino <= 0f)
        {
            timerDestino = 0.25f;
            // se acerca despacito y al final da el tiron
            agente.speed = d > 11f ? velocidadCamina * 0.9f : velocidadCorre * 1.15f;
            agente.stoppingDistance = 1.6f; agente.autoBraking = false;
            agente.SetDestination(jugador.position);
        }
        if (d < 2.9f)
        {
            if (inv.RemoveItem(comidaRobada, 1))
            {
                var items = new List<SlotGuardado> { new SlotGuardado { grupo = "inv", indice = 0, id = comidaRobada, cantidad = 1, icono = comidaRobada } };
                Ladron.Asignar(vida, items);
                C.RegistrarRobo();
                string nombre = comidaRobada == "Carne_Cocinada" ? "carne cocinada" : "carne";
                SurvivalUI.Aviso("¡Una rata te robo " + nombre + "! Corre a su madriguera", new Color(1f, 0.6f, 0.35f));
                Chillar(1f);
                robando = false;
                // corre a la madriguera; ahi se queda cuidando lo robado
                hogar = C.PuntoMadriguera();
                rol = Rol.Reposo;
                radioHogar = 5f;
                huyendo = true; timerHuida = 60f;
                agente.speed = velocidadHuida; agente.stoppingDistance = 0.5f; agente.autoBraking = true;
                agente.SetDestination(hogar);
            }
            else robando = false;
        }
    }

    // ------------------------------------------------------------------ pelea
    void Atacar(float dt)
    {
        float d = DistJugador;
        // murio el rey: salen huyendo (solo se defienden si las acabas de golpear)
        if (C != null && C.Panico && Time.time - tGolpe > 3f)
        {
            Hostil = false; atacando = false;
            EmpezarHuida(3f);
            return;
        }
        // se calman si las dejas atras
        if (d > 42f) { timerCalma += dt; if (timerCalma > 8f) { Calmarse(); return; } }
        else timerCalma = 0f;

        if (retroceso > 0f)
        {   // el fuego las asusta: muerden y se apartan
            retroceso -= dt;
            return;
        }
        if (atacando) return;

        timerDestino -= dt;
        if (timerDestino <= 0f)
        {
            timerDestino = 0.25f;
            agente.speed = velocidadCorre;
            agente.stoppingDistance = alcance * 0.75f;
            agente.SetDestination(jugador.position);
        }
        if (d <= alcance)
        {
            Vector3 hacia = jugador.position - transform.position; hacia.y = 0f;
            if (hacia.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(hacia), 10f * dt);
            if (cdAtaque <= 0f) StartCoroutine(Mordida());
        }
    }

    IEnumerator Mordida()
    {
        atacando = true;
        cdAtaque = intervaloAtaque * Random.Range(0.85f, 1.2f);
        agente.ResetPath();
        Anim(AAtaca);
        if (animator != null) animator.speed = 1.1f;
        yield return new WaitForSeconds(0.38f);
        if (!Muerta && jugador != null)
        {
            Vector3 hacia = jugador.position - transform.position; hacia.y = 0f;
            if (hacia.magnitude < alcance + 0.8f && Vector3.Angle(transform.forward, hacia) < 75f && vidaJugador != null)
            {
                ModeloVivo.AtacarEn(this);
                vidaJugador.TakeDamage(danio);
                if (Random.value < 0.5f) Chillar(0.6f);
            }
        }
        yield return new WaitForSeconds(0.5f);
        atacando = false;
        animActual = -1;
        if (!Muerta && AntorchaCerca && AgenteListo)
        {
            // retrocede ante la llama
            retroceso = Random.Range(1.2f, 2f);
            Vector3 lejos = transform.position - jugador.position; lejos.y = 0f;
            if (NavMesh.SamplePosition(transform.position + lejos.normalized * 7f, out var hit, 5f, NavMesh.AllAreas))
            { agente.speed = velocidadCorre; agente.stoppingDistance = 0.3f; agente.SetDestination(hit.position); }
        }
    }
}
