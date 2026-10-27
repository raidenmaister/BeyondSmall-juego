using System.Collections.Generic;
using UnityEngine;

// Administra todas las luciernagas: las hace llegar cuando cae la noche y
// las despide al amanecer. Una parte acompaña al jugador para que pueda ver
// en la oscuridad; el resto se reparte por el escenario.
public class FireflySwarm : MonoBehaviour
{
    public Firefly prefab;
    public Transform jugador;
    [Tooltip("Total de luciernagas")]
    public int cantidad = 60;
    [Tooltip("Cuantas de ellas revolotean cerca del jugador")]
    public int acompanantes = 22;
    [Tooltip("Distancia a la que vuelan las acompanantes")]
    public Vector2 radioJugador = new Vector2(3f, 11f);

    [Header("Zona del escenario")]
    public Vector3 centroZona = Vector3.zero;
    public Vector3 tamanoZona = new Vector3(150f, 10f, 170f);

    [Header("Momento de llegada / partida")]
    [Range(0f, 1f)] public float nocheParaLlegar = 0.55f;

    readonly List<Firefly> luciernagas = new List<Firefly>();
    bool activas;

    void Start()
    {
        if (prefab == null) { enabled = false; return; }
        if (jugador == null)
        {
            var j = GameObject.Find("Jugador");
            if (j != null) jugador = j.transform;
        }
        for (int i = 0; i < cantidad; i++)
        {
            var f = Instantiate(prefab, transform);
            f.name = "Luciernaga_" + i;
            if (i < acompanantes && jugador != null)
            {
                f.sigueA = jugador;
                Vector2 d = Random.insideUnitCircle.normalized * Random.Range(radioJugador.x, radioJugador.y);
                f.desplazamientoHogar = new Vector3(d.x, 0f, d.y);
                f.radio = 3.5f;
            }
            else
            {
                f.hogar = centroZona + new Vector3(
                    Random.Range(-0.5f, 0.5f) * tamanoZona.x,
                    0f,
                    Random.Range(-0.5f, 0.5f) * tamanoZona.z);
                f.radio = Random.Range(4f, 9f);
            }
            f.gameObject.SetActive(false);
            luciernagas.Add(f);
        }
    }

    void Update()
    {
        var ciclo = DayNightCycle.Instance;
        if (ciclo == null) return;

        if (!activas && ciclo.Noche >= nocheParaLlegar && !ciclo.EsMadrugadaOManana)
        {
            activas = true;
            foreach (var f in luciernagas) f.Llegar(Random.Range(0f, 10f));
        }
        else if (activas && (ciclo.EsMadrugadaOManana || ciclo.Noche < 0.3f))
        {
            // amanece: todas se van volando, escalonadas
            activas = false;
            foreach (var f in luciernagas) if (f.gameObject.activeSelf) f.Irse(Random.Range(0f, 8f));
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 1f, 0.3f, 0.4f);
        Gizmos.DrawWireCube(centroZona, tamanoZona);
    }
}
