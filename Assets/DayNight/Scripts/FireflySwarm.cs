using System.Collections.Generic;
using UnityEngine;

// Administra todas las luciernagas: salen de la vegetacion cuando cae la noche y se van al amanecer.
// Viven repartidas por el escenario, NUNCA pegadas al jugador ni bajo tierra:
//   - unas en el salon abandonado (lleno de plantas y con el techo roto),
//   - otras por el jardin: cuando el jugador se aleja mucho de una, esa se va volando y otra sale de la hierba
//     lejos de el (25-60 m), asi siempre hay algunas por la zona en la que esta, sin aparecer de la nada a su lado.
// Dentro del hormiguero, del arbol de la colmena o de la cloaca no hay ninguna (y su luz no atraviesa el suelo).
// La luz de la noche es sobre todo la luna; las luciernagas solo dan pequeños puntos de luz.
public class FireflySwarm : MonoBehaviour
{
    public Firefly prefab;
    public Transform jugador;
    [Tooltip("Total de luciernagas")]
    public int cantidad = 36;
    [Tooltip("Cuantas de ellas viven en el salon (el resto, por el jardin)")]
    public int enSalon = 12;
    [Tooltip("Distancia al jugador a la que salen las del jardin")]
    public Vector2 radioJardin = new Vector2(12f, 50f);
    [Tooltip("Mas lejos que esto, la luciernaga del jardin se va y sale otra cerca de la zona del jugador")]
    public float distanciaRelevo = 75f;

    [Header("Zona del salon")]
    public Vector3 centroZona = Vector3.zero;
    public Vector3 tamanoZona = new Vector3(150f, 10f, 170f);

    [Header("Momento de llegada / partida")]
    [Range(0f, 1f)] public float nocheParaLlegar = 0.55f;

    [Tooltip("Fraccion de luciernagas que se quedan si el jugador lleva la antorcha encendida")]
    [Range(0f, 1f)] public float conAntorcha = 0.3f;

    [Header("Luz de cada luciernaga (la luna es la luz principal)")]
    public float intensidadLuz = 2.2f;
    public float rangoLuz = 5f;

    readonly List<Firefly> luciernagas = new List<Firefly>();
    readonly List<Firefly> jardin = new List<Firefly>();
    readonly HashSet<Firefly> espantadas = new HashSet<Firefly>();
    readonly HashSet<Firefly> relevando = new HashSet<Firefly>();
    bool activas, antorchaAntes;
    float tRelevo;

    void Start()
    {
        if (prefab == null) { enabled = false; return; }
        if (jugador == null)
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) jugador = pc.transform;
            else { var j = GameObject.Find("Jugador"); if (j != null) jugador = j.transform; }
        }
        for (int i = 0; i < cantidad; i++)
        {
            var f = Instantiate(prefab, transform);
            f.name = "Luciernaga_" + i;
            f.sigueA = null;                         // ninguna sigue al jugador
            f.intensidadLuz = intensidadLuz;
            f.rangoLuz = rangoLuz;
            if (i < enSalon)
            {
                f.hogar = centroZona + new Vector3(Random.Range(-0.45f, 0.45f) * tamanoZona.x, 0f, Random.Range(-0.45f, 0.45f) * tamanoZona.z);
                f.radio = Random.Range(4f, 9f);
            }
            else
            {
                f.hogar = PuntoJardin(jugador != null ? jugador.position : ZonaJuego.centro, true);
                f.radio = Random.Range(5f, 10f);
                jardin.Add(f);
            }
            f.gameObject.SetActive(false);
            luciernagas.Add(f);
        }
    }

    /// <summary>Un sitio del jardin para una luciernaga: lejos del jugador, sobre la hierba y fuera de edificios y madrigueras.</summary>
    Vector3 PuntoJardin(Vector3 cerca, bool alInicio)
    {
        for (int intento = 0; intento < 30; intento++)
        {
            Vector3 p;
            if (alInicio && intento < 20)
            {   // al empezar: repartidas por todo el jardin
                Vector2 r = Random.insideUnitCircle * (ZonaJuego.radioCerca - 30f);
                p = ZonaJuego.centro + new Vector3(r.x, 0f, r.y);
            }
            else
            {
                float a = Random.value * Mathf.PI * 2f;
                p = cerca + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(radioJardin.x, radioJardin.y);
            }
            if (!SitioValido(p)) continue;
            p.y = ZonaJuego.Altura(p.x, p.z);
            // nunca salen delante de la camara: siempre fuera de la vista (o detras de lo que se ve)
            if (!alInicio && intento < 25 && Visible(p)) continue;
            return p;
        }
        return ZonaJuego.centro + new Vector3(0f, 0f, ZonaJuego.radioCerca * 0.5f);
    }

    static bool Visible(Vector3 p)
    {
        var cam = Camera.main;
        if (cam == null) return false;
        var v = cam.WorldToViewportPoint(p + Vector3.up);
        return v.z > 0f && v.z < 70f && v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
    }

    static bool SitioValido(Vector3 p)
    {
        if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(ZonaJuego.centro.x, ZonaJuego.centro.z)) > ZonaJuego.radioCerca - 20f) return false;
        if (ZonaJuego.EnEdificio(p, 6f)) return false;                                               // el salon tiene las suyas
        if (ZonaJuego.DistanciaHormiguero(p.x, p.z) < ZonaJuego.hormigueroRadio + 6f) return false;   // ni sobre la boca del hormiguero
        if (ZonaJuego.DistanciaArbol(p.x, p.z) < ZonaJuego.arbolRadio + 6f) return false;             // ni dentro del arbol de la colmena
        if (ZonaJuego.DistanciaCloaca(p.x, p.z) < 12f) return false;                                  // ni por la rejilla de la cloaca
        return true;
    }

    static bool JugadorEncerrado()
    {
        if (Hormiguero.Instancia != null && Hormiguero.Instancia.JugadorDentro) return true;
        if (Colmena.Instancia != null && Colmena.Instancia.JugadorDentro) return true;
        if (Cloaca.Instancia != null && Cloaca.Instancia.JugadorDentro) return true;
        return DayNightCycle.Encierro > 0.5f;
    }

    void Update()
    {
        var ciclo = DayNightCycle.Instance;
        if (ciclo == null) return;
        // bajo tierra o dentro del arbol no se ve ninguna: sus luces no deben atravesar el suelo
        Firefly.Apagadas = JugadorEncerrado();

        if (!activas && ciclo.Noche >= nocheParaLlegar && !ciclo.EsMadrugadaOManana)
        {
            activas = true;
            // al anochecer las del jardin salen de la hierba por la zona del jugador (si esta fuera), sin verse nacer
            if (jugador != null && !JugadorEncerrado() && !ZonaJuego.EnEdificio(jugador.position))
                foreach (var f in jardin) f.hogar = PuntoJardin(jugador.position, false);
            foreach (var f in luciernagas) f.Llegar(Random.Range(0f, 10f));
            antorchaAntes = false;          // si ya llevaba la antorcha, se espantan enseguida
        }
        else if (activas && (ciclo.EsMadrugadaOManana || ciclo.Noche < 0.3f))
        {
            // amanece: todas se van volando, escalonadas
            activas = false;
            espantadas.Clear(); relevando.Clear();
            foreach (var f in luciernagas) if (f.gameObject.activeSelf) f.Irse(Random.Range(0f, 8f));
        }

        // relevo en el jardin: las que quedaron muy lejos del jugador se van y otra sale de la hierba por su zona
        if (activas && jugador != null && !JugadorEncerrado() && !ZonaJuego.EnEdificio(jugador.position))
        {
            tRelevo -= Time.deltaTime;
            if (tRelevo <= 0f)
            {
                tRelevo = 1.5f;
                foreach (var f in jardin)
                {
                    if (espantadas.Contains(f)) continue;
                    if (relevando.Contains(f))
                    {
                        if (f.EstadoActual != Firefly.Estado.Oculta) continue;
                        relevando.Remove(f);
                        f.hogar = PuntoJardin(jugador.position, false);
                        f.Llegar(Random.Range(0f, 3f));
                        continue;
                    }
                    Vector3 d = f.hogar - jugador.position; d.y = 0f;
                    if (d.magnitude > distanciaRelevo) { relevando.Add(f); f.Irse(0f); break; }   // de una en una
                }
            }
        }

        // la antorcha ya alumbra: la mayoria se aleja y vuelven al apagarla
        bool antorcha = Antorcha.Encendida;
        if (activas && antorcha != antorchaAntes)
        {
            if (antorcha)
            {
                int quedan = Mathf.RoundToInt(luciernagas.Count * conAntorcha);
                // primero se espantan las mas cercanas al jugador
                var orden = new List<Firefly>(luciernagas);
                if (jugador != null) orden.Sort((x, y) => (x.transform.position - jugador.position).sqrMagnitude.CompareTo((y.transform.position - jugador.position).sqrMagnitude));
                for (int i = 0; i < orden.Count - quedan; i++)
                {
                    var f = orden[i];
                    espantadas.Add(f);
                    if (f.gameObject.activeSelf) f.Irse(Random.Range(0f, 2.5f));
                }
            }
            else
            {
                foreach (var f in espantadas) f.Llegar(Random.Range(0f, 6f));
                espantadas.Clear();
            }
        }
        antorchaAntes = antorcha;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 1f, 0.3f, 0.4f);
        Gizmos.DrawWireCube(centroZona, tamanoZona);
    }
}
