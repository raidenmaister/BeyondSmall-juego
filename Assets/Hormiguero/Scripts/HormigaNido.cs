using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Hormiga del hormiguero. Papeles:
///  Obrera  : recorre su ruta entre una camara y el exterior; de regreso carga una hoja o un grano de maiz.
///  Reposo  : deambula despacio por su camara (dormitorios, guarderias).
///  Soldado : custodia a la reina; ataca si el jugador se acerca durante la pelea.
/// Son pacificas hasta que las golpeas (avisan a las cercanas) o hasta que muere la reina:
/// entonces persiguen al jugador por el NavMesh. Usan RatHealth, asi el golpe del jugador las daña.
/// Las patas se animan en el shader (sin costo de CPU por hormiga).
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class HormigaNido : MonoBehaviour
{
    public enum Rol { Obrera, Reposo, Soldado }

    public Rol rol = Rol.Obrera;
    public Renderer cuerpo;
    public Material matQuieta, matCamina, matCorre;
    public GameObject carga;

    [Header("Obrera")]
    public Vector3[] ruta;
    public float velocidad = 4f;
    public float carril;            // desplazamiento lateral para que no caminen en fila exacta
    [Range(0f, 1f)] public float inicio;

    [Header("Reposo / soldado")]
    public Vector3 hogar;
    public float radioHogar = 8f;

    [Header("Ataque")]
    public float danio = 6f;
    public float intervaloAtaque = 0.9f;
    public float alcance = 2.4f;
    public float velocidadAtaque = 4.2f;
    public GameObject particulasMuerte;

    public bool Hostil { get; private set; }
    public bool Muerta { get; private set; }

    RatHealth vida;
    NavMeshAgent agente;
    Transform jugador;
    PlayerHealth vidaJugador;
    float s, largo, pausa, cdAtaque, timerDestino, timerSuelo;
    float[] acum;
    bool regreso;
    Vector3 objetivoReposo, normalSuelo = Vector3.up;
    float alturaSuelo;
    int mascaraSuelo;

    void Awake()
    {
        vida = GetComponent<RatHealth>();
        agente = GetComponent<NavMeshAgent>();
        if (agente != null) agente.enabled = false;
        mascaraSuelo = Physics.DefaultRaycastLayers;
        if (hogar == Vector3.zero) hogar = transform.position;
        alturaSuelo = transform.position.y;
        if (ruta != null && ruta.Length > 1)
        {
            acum = new float[ruta.Length];
            for (int i = 1; i < ruta.Length; i++) acum[i] = acum[i - 1] + Vector3.Distance(ruta[i - 1], ruta[i]);
            largo = acum[acum.Length - 1];
            // empiezan repartidas: unas yendo, otras regresando con carga
            float u = inicio * 2f;
            regreso = u > 1f;
            s = (regreso ? 2f - u : u) * largo;
        }
        if (carga != null) carga.SetActive(regreso);
    }

    void OnEnable()
    {
        vida.OnHealthChanged += AlRecibirDanio;
        vida.OnDeath += Morir;
        if (Hormiguero.Instancia != null) Hormiguero.Instancia.Registrar(this);
    }

    void OnDisable()
    {
        vida.OnHealthChanged -= AlRecibirDanio;
        vida.OnDeath -= Morir;
        if (Hormiguero.Instancia != null) Hormiguero.Instancia.Quitar(this);
    }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) { jugador = pc.transform; vidaJugador = pc.GetComponent<PlayerHealth>(); }
        if (Hormiguero.Instancia != null) Hormiguero.Instancia.Registrar(this);
        objetivoReposo = hogar;
        Material(rol == Rol.Obrera ? matCamina : matQuieta);
    }

    void Material(Material m) { if (cuerpo != null && m != null && cuerpo.sharedMaterial != m) cuerpo.sharedMaterial = m; }

    // ------------------------------------------------------------------ dano y furia
    void AlRecibirDanio(float actual, float max)
    {
        if (Muerta || actual <= 0f) return;
        if (!Hostil)
        {
            Enfurecer();
            if (Hormiguero.Instancia != null) Hormiguero.Instancia.Alarma(transform.position, 16f);
        }
        // pequeño retroceso al ser golpeada
        if (jugador != null)
        {
            Vector3 dir = transform.position - jugador.position; dir.y = 0f;
            if (agente != null && agente.enabled && agente.isOnNavMesh) agente.Move(dir.normalized * 0.9f);
        }
    }

    public void Enfurecer()
    {
        if (Muerta || Hostil) return;
        Hostil = true;
        if (carga != null && rol != Rol.Obrera) carga.SetActive(false);
        Material(matCorre);
        if (agente != null)
        {
            if (NavMesh.SamplePosition(transform.position, out var hit, 6f, NavMesh.AllAreas))
            {
                agente.enabled = true;
                agente.Warp(hit.position);
                agente.speed = velocidadAtaque;
            }
        }
    }

    void Morir()
    {
        if (Muerta) return;
        Muerta = true;
        if (agente != null && agente.enabled) agente.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Material(matQuieta);
        if (Hormiguero.Instancia != null) Hormiguero.Instancia.Quitar(this);
        StartCoroutine(RutinaMuerte());
    }

    System.Collections.IEnumerator RutinaMuerte()
    {
        // se voltea panza arriba y se hunde
        Quaternion r0 = transform.rotation, r1 = r0 * Quaternion.Euler(0f, 0f, 180f);
        Vector3 p0 = transform.position;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.5f)
        {
            transform.rotation = Quaternion.Slerp(r0, r1, t);
            transform.position = p0 + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.6f;
            yield return null;
        }
        yield return new WaitForSeconds(0.6f);
        if (particulasMuerte != null) Instantiate(particulasMuerte, transform.position + Vector3.up * 0.4f, Quaternion.identity);
        var drops = GetComponent<DropTable>();
        if (drops != null) drops.Drop(transform.position);
        Destroy(gameObject);
    }

    // ------------------------------------------------------------------ bucle
    void Update()
    {
        if (Muerta) return;
        float dt = Time.deltaTime;
        if (Hostil) { Atacar(dt); return; }
        if (rol == Rol.Obrera && acum != null) Recorrer(dt);
        else Deambular(dt);
    }

    void Recorrer(float dt)
    {
        if (pausa > 0f)
        {
            pausa -= dt;
            if (pausa <= 0f) Material(matCamina);
            return;
        }
        s += (regreso ? -1f : 1f) * velocidad * dt;
        if (s >= largo) { s = largo; regreso = true; Pausar(true); }
        else if (s <= 0f) { s = 0f; regreso = false; Pausar(false); }

        // punto de la ruta y direccion
        int i = 1;
        while (i < acum.Length - 1 && acum[i] < s) i++;
        float t = Mathf.InverseLerp(acum[i - 1], acum[i], s);
        Vector3 a = ruta[i - 1], b = ruta[i];
        Vector3 dir = (b - a); if (regreso) dir = -dir;
        Vector3 lado = Vector3.Cross(Vector3.up, dir).normalized;
        Vector3 p = Vector3.Lerp(a, b, t) + lado * carril;
        Mover(p, dir, dt, 10f);
    }

    void Pausar(bool cargar)
    {
        pausa = Random.Range(1.2f, 2.8f);
        Material(matQuieta);
        if (carga != null) carga.SetActive(cargar);
    }

    void Deambular(float dt)
    {
        // soldados: atacan si el jugador se mete en su zona
        if (rol == Rol.Soldado && jugador != null && (jugador.position - hogar).sqrMagnitude < (radioHogar + 10f) * (radioHogar + 10f))
        {
            Enfurecer();
            return;
        }
        if (pausa > 0f)
        {
            pausa -= dt;
            if (pausa <= 0f)
            {   // siguiente paseo corto dentro de su camara
                Vector2 c = Random.insideUnitCircle * radioHogar;
                objetivoReposo = hogar + new Vector3(c.x, 0f, c.y);
                Material(matCamina);
            }
            return;
        }
        Vector3 d = objetivoReposo - transform.position; d.y = 0f;
        if (d.magnitude < 0.6f) { pausa = Random.Range(2f, 6f); Material(matQuieta); return; }
        Vector3 p = transform.position + d.normalized * velocidad * 0.35f * dt;
        Mover(p, d, dt, 5f);
    }

    // coloca la hormiga en el suelo (raycast cada pocos frames) y la orienta segun la pendiente
    void Mover(Vector3 p, Vector3 dir, float dt, float giro)
    {
        timerSuelo -= dt;
        if (timerSuelo <= 0f)
        {
            timerSuelo = 0.12f;
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, mascaraSuelo, QueryTriggerInteraction.Ignore))
            { alturaSuelo = hit.point.y; normalSuelo = hit.normal; }
            else alturaSuelo = p.y;
        }
        p.y = Mathf.Lerp(transform.position.y, alturaSuelo, 12f * dt);
        transform.position = p;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            Vector3 f = Vector3.ProjectOnPlane(dir.normalized, normalSuelo);
            if (f.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(f, normalSuelo), giro * dt);
        }
    }

    void Atacar(float dt)
    {
        if (jugador == null) return;
        cdAtaque -= dt;
        Vector3 hacia = jugador.position - transform.position;
        float dist = hacia.magnitude;
        if (agente != null && agente.enabled && agente.isOnNavMesh)
        {
            timerDestino -= dt;
            if (timerDestino <= 0f) { timerDestino = 0.3f; agente.SetDestination(jugador.position); }
        }
        else
        {
            // sin NavMesh (fuera del area horneada): van directo hacia el jugador
            if (dist > alcance * 0.8f) Mover(transform.position + hacia.normalized * velocidadAtaque * dt, hacia, dt, 10f);
        }
        if (dist <= alcance)
        {
            hacia.y = 0f;
            if (hacia.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(hacia), 10f * dt);
            if (cdAtaque <= 0f)
            {
                cdAtaque = intervaloAtaque;
                if (vidaJugador != null) vidaJugador.TakeDamage(danio);
            }
        }
    }
}
