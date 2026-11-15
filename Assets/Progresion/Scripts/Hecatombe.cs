using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// La hecatombe final (la pone Campana): oleadas de bichos que salen alrededor del jugador, sobre todo entre el y la
/// cabina, y cada ~25 s una situacion absurda con su cartel:
///   - las ratas empujan un borrador gigante cuesta abajo (rueda y aplasta)
///   - lluvia de chinchetas desde arriba (con su sombra roja para esquivarlas)
///   - las cochinillas disparan tuercas con una liga
///   - hormigas en conga
///   - escuadron kamikaze de abejas
///   - una hormiga se armo con una lanza de lapiz
/// Los bichos son copias de los que hay en el mundo (con su IA de siempre, pero furiosos y sin perder el rastro).
/// </summary>
public class Hecatombe : MonoBehaviour
{
    public int Derrotados { get; private set; }

    Transform jugador;
    Transform plantillas;
    readonly Dictionary<string, GameObject> modelos = new Dictionary<string, GameObject>();
    readonly List<GameObject> vivos = new List<GameObject>();
    float proxOleada = 2f, proxEvento = 12f;
    int ultimoEvento = -1;
    bool terminado;

    float Intervalo => Partida.Dif == Dificultad.Extremo ? 1.6f : Partida.Dif == Dificultad.Dificil ? 2.1f : Partida.Dif == Dificultad.Pacifico ? 3.5f : 2.7f;
    int MaxVivos => Partida.Dif == Dificultad.Extremo ? 34 : Partida.Dif == Dificultad.Dificil ? 28 : Partida.Dif == Dificultad.Pacifico ? 16 : 22;

    float edad;
    /// <summary>0..1: la hecatombe arranca suave y en 90 s llega a todo su furor.</summary>
    float Furor => Mathf.Clamp01(0.35f + edad / 90f * 0.65f);

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        // plantillas inactivas (si la original muere, la copia sigue)
        var raiz = new GameObject("PlantillasHecatombe");
        raiz.SetActive(false);
        plantillas = raiz.transform;
        foreach (var ai in FindObjectsByType<CreatureAI>(FindObjectsInactive.Exclude))
        {
            string k = ai.kind.ToString();
            if (modelos.ContainsKey(k)) continue;
            if (ai.kind != CreatureAI.Kind.Ants && ai.kind != CreatureAI.Kind.Bee && ai.kind != CreatureAI.Kind.Beetle
                && ai.kind != CreatureAI.Kind.Centipede && ai.kind != CreatureAI.Kind.Woodlouse) continue;
            var rh = ai.GetComponent<RatHealth>();
            if (rh == null || rh.IsDead) continue;
            modelos[k] = Instantiate(ai.gameObject, plantillas);
        }
        var araña = FindAnyObjectByType<SpiderAI>();
        if (araña != null) modelos["Arana"] = Instantiate(araña.gameObject, plantillas);
        RatHealth.AlguienMurio += AlMorir;
    }

    void OnDestroy() { RatHealth.AlguienMurio -= AlMorir; if (plantillas != null) Destroy(plantillas.gameObject); }

    void AlMorir(RatHealth rh) { if (rh != null && vivos.Contains(rh.gameObject)) Derrotados++; }

    public void Terminar()
    {
        terminado = true;
        StartCoroutine(Desbandada());
    }

    IEnumerator Desbandada()
    {
        // todos los bichos de la hecatombe salen disparados en confeti
        Color[] c = { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.85f, 0.2f), new Color(0.4f, 1f, 0.5f), new Color(0.5f, 0.7f, 1f) };
        foreach (var g in vivos.ToArray())
        {
            if (g == null) continue;
            Juice.Chispas(g.transform.position + Vector3.up * 0.5f, c[Random.Range(0, c.Length)], 14, 4f);
            Juice.Polvo(g.transform.position, 0.8f, 5);
            Destroy(g);
            yield return new WaitForSecondsRealtime(0.04f);
        }
        vivos.Clear();
    }

    void Update()
    {
        if (terminado || jugador == null || GestorPartida.Muerto) return;
        vivos.RemoveAll(g => g == null);
        // los que se quedaron muy atras desaparecen (y dejan sitio a los de delante)
        for (int i = vivos.Count - 1; i >= 0; i--)
            if ((vivos[i].transform.position - jugador.position).sqrMagnitude > 115f * 115f) { Destroy(vivos[i]); vivos.RemoveAt(i); }

        edad += Time.deltaTime;
        proxOleada -= Time.deltaTime;
        if (proxOleada <= 0f)
        {
            proxOleada = Intervalo / Furor;
            int n = Random.Range(2, 4);
            int tope = Mathf.RoundToInt(MaxVivos * Furor);
            for (int i = 0; i < n && vivos.Count < tope; i++) Aparecer(ElegirTipo(), PuntoAlrededor(24f, 40f));
        }
        proxEvento -= Time.deltaTime;
        if (proxEvento <= 0f)
        {
            proxEvento = Random.Range(20f, 28f);
            int e; do { e = Random.Range(0, 6); } while (e == ultimoEvento);
            ultimoEvento = e;
            StartCoroutine(Evento(e));
        }
    }

    string ElegirTipo()
    {
        var lista = new List<string>(modelos.Keys);
        if (lista.Count == 0) return null;
        // mas hormigas que nada (son la plaga)
        if (modelos.ContainsKey("Ants") && Random.value < 0.4f) return "Ants";
        return lista[Random.Range(0, lista.Count)];
    }

    /// <summary>Un punto de suelo alrededor del jugador; con mas probabilidad hacia la cabina (te cortan el paso).</summary>
    Vector3 PuntoAlrededor(float rMin, float rMax)
    {
        Vector3 haciaCabina = MaquinaCrecimiento.Instancia != null ? MaquinaCrecimiento.Instancia.transform.position - jugador.position : jugador.forward;
        haciaCabina.y = 0f; haciaCabina.Normalize();
        for (int i = 0; i < 10; i++)
        {
            float ang = Random.value < 0.65f ? Random.Range(-70f, 70f) : Random.Range(0f, 360f);
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * haciaCabina;
            Vector3 p = jugador.position + dir * Random.Range(rMin, rMax);
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out hit, 90f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.6f
                && Mathf.Abs(hit.point.y - jugador.position.y) < 14f)
                return hit.point;
        }
        return jugador.position + haciaCabina * rMin;
    }

    GameObject Aparecer(string tipo, Vector3 pos)
    {
        if (tipo == null || !modelos.ContainsKey(tipo)) return null;
        var g = Instantiate(modelos[tipo], pos + Vector3.up * 0.3f, Quaternion.LookRotation(Vector3.ProjectOnPlane(jugador.position - pos, Vector3.up).normalized + Vector3.forward * 0.001f));
        g.name = "Hecatombe_" + tipo;
        g.transform.SetParent(null, true);
        g.SetActive(true);
        // furiosos: no se calman ni pierden el rastro
        var ai = g.GetComponent<CreatureAI>();
        if (ai != null) { ai.pasivo = false; ai.detectionDistance = 90f; ai.loseDistance = 220f; ai.speedMultiplier = Mathf.Max(ai.speedMultiplier, 0.75f); }
        var sp = g.GetComponent<SpiderAI>();
        if (sp != null) { sp.detectionDistance = 90f; sp.loseDistance = 220f; }
        // poco botin (si no, el suelo se llena de cosas)
        var dt = g.GetComponent<DropTable>();
        if (dt != null && Random.value < 0.75f) Destroy(dt);
        Juice.Polvo(pos, 1f, 8);
        vivos.Add(g);
        return g;
    }

    // ------------------------------------------------------------------ situaciones absurdas
    IEnumerator Evento(int e)
    {
        switch (e)
        {
            case 0:   // borrador gigante
                Cartel("¡Las ratas empujan un BORRADOR GIGANTE!");
                yield return new WaitForSeconds(1.2f);
                BorradorGigante();
                break;
            case 1:   // lluvia de chinchetas
                Cartel("¡Las arañas tiran chinchetas desde arriba!");
                for (int i = 0; i < 14; i++)
                {
                    Vector2 r = Random.insideUnitCircle * 9f;
                    Vector3 p = jugador.position + new Vector3(r.x, 0f, r.y) + Vector3.ProjectOnPlane(jugador.forward, Vector3.up) * 3f;
                    PeligroCaida.Lanzar(Suelo(p), "chincheta", 14f, 1.3f);
                    yield return new WaitForSeconds(0.22f);
                }
                break;
            case 2:   // tuercas con una liga
                Cartel("¡Las cochinillas disparan tuercas con una liga!");
                for (int i = 0; i < 6; i++)
                {
                    var pc = jugador.GetComponent<CharacterController>();
                    Vector3 pred = jugador.position + (pc != null ? Vector3.ProjectOnPlane(pc.velocity, Vector3.up) * 1.1f : Vector3.zero);
                    PeligroCaida.Lanzar(Suelo(pred), "tuerca", 22f, 2f);
                    yield return new WaitForSeconds(0.55f);
                }
                break;
            case 3:   // conga de hormigas
                Cartel("¡Las hormigas vienen bailando en CONGA!");
                {
                    Vector3 inicio = PuntoAlrededor(20f, 26f);
                    Vector3 lado = Vector3.Cross(Vector3.up, (jugador.position - inicio).normalized);
                    for (int i = 0; i < 9 && vivos.Count < MaxVivos + 9; i++)
                    {
                        var g = Aparecer(modelos.ContainsKey("Ants") ? "Ants" : ElegirTipo(), Suelo(inicio + lado * (i - 4) * 1.6f));
                        if (g != null) Juice.Aplastar(g.transform, 0.3f);
                        yield return new WaitForSeconds(0.12f);
                    }
                }
                break;
            case 4:   // abejas kamikaze
                Cartel("¡Escuadron KAMIKAZE de abejas!");
                for (int i = 0; i < 6 && modelos.ContainsKey("Bee"); i++)
                {
                    Aparecer("Bee", PuntoAlrededor(16f, 22f) + Vector3.up * 3f);
                    yield return new WaitForSeconds(0.15f);
                }
                break;
            case 5:   // hormiga con lanza de lapiz
                Cartel("¡Una hormiga se armo con una LANZA DE LAPIZ!");
                {
                    var g = Aparecer(modelos.ContainsKey("Ants") ? "Ants" : ElegirTipo(), PuntoAlrededor(16f, 20f));
                    if (g != null)
                    {
                        var lanza = ModelosArmas.Crear("Lanza");
                        if (lanza != null)
                        {
                            lanza.transform.SetParent(g.transform, false);
                            lanza.transform.localPosition = Vector3.up * 0.6f + Vector3.back * 0.6f;
                            lanza.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // apuntando hacia delante
                            lanza.transform.localScale = Vector3.one * 1.3f / Mathf.Max(0.01f, g.transform.lossyScale.x);
                        }
                        var rh = g.GetComponent<RatHealth>(); if (rh != null) rh.EscalarVida(2f);
                        var ai = g.GetComponent<CreatureAI>(); if (ai != null) { ai.attackDamage *= 2f; ai.attackRange += 1.2f; }
                        g.transform.localScale *= 1.35f;
                    }
                }
                break;
        }
    }

    void Cartel(string s)
    {
        Aviso.Mostrar("<ola>" + s + "</ola>", new Color(1f, 0.8f, 0.35f), 3f);
        SonidosJuice.Tocar(SonidosJuice.Rebote, jugador.position, 0.6f);
    }

    static Vector3 Suelo(Vector3 p)
    {
        RaycastHit hit;
        if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out hit, 70f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return p;
    }

    void BorradorGigante()
    {
        Vector3 dir = Vector3.ProjectOnPlane(jugador.forward, Vector3.up).normalized;
        Vector3 inicio = Suelo(jugador.position + dir * 32f + Vector3.Cross(Vector3.up, dir) * Random.Range(-4f, 4f)) + Vector3.up * 4f;
        var go = new GameObject("BorradorGigante");
        go.transform.position = inicio;
        var cuerpo = new GameObject("Goma");
        cuerpo.transform.SetParent(go.transform, false);
        cuerpo.AddComponent<MeshFilter>().sharedMesh = ModelosArmas.Caja(new Vector3(4.5f, 2.6f, 2.6f));
        cuerpo.AddComponent<MeshRenderer>().sharedMaterial = ModelosArmas.Mat("BorradorGigante", new Color(0.97f, 0.62f, 0.66f), 0.1f);
        cuerpo.transform.localPosition = Vector3.down * 1.3f;
        var banda = new GameObject("Banda");
        banda.transform.SetParent(go.transform, false);
        banda.AddComponent<MeshFilter>().sharedMesh = ModelosArmas.Caja(new Vector3(1.6f, 2.7f, 2.7f));
        banda.AddComponent<MeshRenderer>().sharedMaterial = ModelosArmas.Mat("BorradorBanda", new Color(0.25f, 0.45f, 0.9f), 0.2f);
        banda.transform.localPosition = new Vector3(0.9f, -1.35f, 0f);
        var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(4.5f, 2.6f, 2.6f);
        var rb = go.AddComponent<Rigidbody>(); rb.mass = 80f;
        go.transform.rotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, -dir));
        rb.linearVelocity = -dir * 11f; rb.angularVelocity = Vector3.Cross(Vector3.up, -dir) * 3f;
        go.AddComponent<Aplastador>().danio = 30f;
        Destroy(go, 14f);
    }
}

/// <summary>Algo grande que hace daño al tocar al jugador (el borrador gigante).</summary>
public class Aplastador : MonoBehaviour
{
    public float danio = 30f;
    float enfriamiento;
    Transform jugador; PlayerHealth vida;

    void Start() { var pc = FindAnyObjectByType<PlayerController>(); if (pc != null) { jugador = pc.transform; vida = pc.GetComponent<PlayerHealth>(); } }

    void Update()
    {
        enfriamiento -= Time.deltaTime;
        if (jugador == null || enfriamiento > 0f) return;
        var rb = GetComponent<Rigidbody>();
        if (rb != null && rb.linearVelocity.sqrMagnitude < 4f) return;   // parado ya no aplasta
        if ((jugador.position + Vector3.up - transform.position).sqrMagnitude < 3.2f * 3.2f)
        {
            enfriamiento = 1.2f;
            GestorPartida.CausaMuerte = "Aplastado por un borrador gigante";
            if (vida != null) vida.TakeDamage(danio);
            Juice.Sacudir(0.5f);
        }
        if (Random.value < 0.15f) Juice.SacudirEn(transform.position, 0.06f, 20f);
    }
}

/// <summary>Chincheta o tuerca que cae del cielo: una sombra roja avisa de donde va a caer (claridad visual).</summary>
public class PeligroCaida : MonoBehaviour
{
    Vector3 destino; float danio, radio, t, dur = 1.1f;
    GameObject marca, objeto;

    public static void Lanzar(Vector3 destino, string que, float danio, float radio)
    {
        var go = new GameObject("Caida_" + que);
        var p = go.AddComponent<PeligroCaida>();
        p.destino = destino; p.danio = danio; p.radio = radio;
        // sombra roja en el suelo
        p.marca = new GameObject("Marca");
        p.marca.transform.SetParent(go.transform, false);
        p.marca.transform.position = destino + Vector3.up * 0.06f;
        p.marca.AddComponent<MeshFilter>().sharedMesh = ModelosArmas.Cilindro(radio, 0.02f, 24);
        p.marca.AddComponent<MeshRenderer>().sharedMaterial = ModelosArmas.Mat("MarcaCaida", new Color(0.9f, 0.1f, 0.05f), 0f, 0f, new Color(1f, 0.15f, 0.05f) * 1.5f);
        // el objeto que cae
        p.objeto = new GameObject(que);
        p.objeto.transform.SetParent(go.transform, false);
        var mf = p.objeto.AddComponent<MeshFilter>(); var mr = p.objeto.AddComponent<MeshRenderer>();
        if (que == "tuerca")
        {
            mf.sharedMesh = ModelosArmas.TuercaMalla(0.6f, 0.25f, 0.45f);
            mr.sharedMaterial = ModelosArmas.Mat("MetalOscuro", new Color(0.35f, 0.36f, 0.4f), 0.7f, 0.9f);
        }
        else
        {
            mf.sharedMesh = ModelosArmas.Cono(0.12f, 0.9f, 8);
            mr.sharedMaterial = ModelosArmas.Mat("Metal", new Color(0.78f, 0.8f, 0.84f), 0.8f, 0.9f);
            var cabeza = new GameObject("Cabeza");
            cabeza.transform.SetParent(p.objeto.transform, false);
            cabeza.transform.localPosition = Vector3.down * 0.15f;   // la cabeza en la base del pincho
            cabeza.AddComponent<MeshFilter>().sharedMesh = ModelosArmas.Cilindro(0.45f, 0.15f, 16);
            cabeza.AddComponent<MeshRenderer>().sharedMaterial = ModelosArmas.Mat("ChinchetaRoja", new Color(0.9f, 0.15f, 0.15f), 0.6f);
            p.objeto.transform.rotation = Quaternion.Euler(180f, 0f, 0f);   // la punta hacia abajo al caer
        }
        p.objeto.transform.position = destino + Vector3.up * 26f;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / dur);
        objeto.transform.position = Vector3.Lerp(destino + Vector3.up * 26f, destino, k * k);
        objeto.transform.Rotate(0f, 400f * Time.deltaTime, 0f, Space.World);
        marca.transform.localScale = new Vector3(0.3f + 0.7f * k, 1f, 0.3f + 0.7f * k);
        if (k < 1f) return;
        // impacto
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null && Vector3.Distance(pc.transform.position, destino) < radio + 0.4f)
        {
            GestorPartida.CausaMuerte = name.Contains("tuerca") ? "Una tuerca lanzada con una liga" : "Lluvia de chinchetas";
            pc.GetComponent<PlayerHealth>().TakeDamage(danio);
        }
        Juice.SacudirEn(destino, 0.15f, 25f);
        Juice.Polvo(destino, radio, 6);
        Juice.Chispas(destino + Vector3.up * 0.3f, new Color(0.9f, 0.9f, 1f), 8, 3f);
        SonidosJuice.Tocar(SonidosJuice.Aterrizar, destino, 0.8f);
        Destroy(marca);
        Destroy(gameObject, 1.5f);
        enabled = false;
    }
}
