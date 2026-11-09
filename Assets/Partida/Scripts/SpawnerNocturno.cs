using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dificil y extremo: de noche aparecen muchos mas bichos alrededor del jugador (en el salon o afuera),
/// copiados de los que ya hay en la escena. Al amanecer los que quedan lejos se van.
/// Los extra no se guardan con la partida; lo que suelten al morir si.
/// </summary>
public class SpawnerNocturno : MonoBehaviour
{
    public int maxExtras = 16;
    public float intervalo = 9f;
    public int porTanda = 2;
    public Vector2 distancia = new Vector2(28f, 50f);

    class Plantilla { public GameObject go; public float alturaSuelo; public bool vuela; }
    readonly List<Plantilla> salon = new List<Plantilla>(), exterior = new List<Plantilla>();
    readonly List<GameObject> extras = new List<GameObject>();
    Transform jugador;
    float timer = 4f, timerDia;

    /// <summary>Hace copias inactivas de un bicho de cada tipo (antes de que nadie se mueva ni pierda vida).</summary>
    public void Preparar(IEnumerable<RatHealth> enemigos)
    {
        var vistos = new HashSet<string>();
        foreach (var rh in enemigos)
        {
            if (rh == null) continue;
            var ai = rh.GetComponent<CreatureAI>();
            var sp = rh.GetComponent<SpiderAI>();
            if (ai == null && sp == null) continue;
            if (ai != null && ai.kind == CreatureAI.Kind.Butterfly) continue;      // inofensivas
            bool enSalon = ZonaJuego.EnEdificio(rh.transform.position);
            string tipo = (ai != null ? ai.kind.ToString() : "Arana") + (enSalon ? "_salon" : "_ext");
            if (!vistos.Add(tipo)) continue;
            var go = rh.gameObject;
            bool activo = go.activeSelf;
            go.SetActive(false);                       // la copia nace inactiva (sin Awake)
            var copia = Instantiate(go, transform);
            go.SetActive(activo);
            copia.name = "Plantilla_" + tipo;
            var p = new Plantilla
            {
                go = copia,
                alturaSuelo = go.transform.position.y - ZonaJuego.Suelo(go.transform.position, enSalon),
                vuela = ai != null && ai.kind == CreatureAI.Kind.Bee,
            };
            (enSalon ? salon : exterior).Add(p);
        }
        // si una zona no tiene bichos propios, usa los de la otra
        if (salon.Count == 0) salon.AddRange(exterior);
        if (exterior.Count == 0) exterior.AddRange(salon);
    }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
    }

    void Update()
    {
        if (jugador == null || DayNightCycle.Instance == null) return;
        extras.RemoveAll(e => e == null);
        float noche = DayNightCycle.Instance.Noche;

        if (noche > 0.55f)
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = intervalo;
            if (Bajo()) return;
            int vivos = 0;
            foreach (var e in extras) { var h = e.GetComponent<RatHealth>(); if (h != null && !h.IsDead) vivos++; }
            for (int i = 0; i < porTanda && vivos < maxExtras; i++) if (Aparecer()) vivos++;
        }
        else if (noche < 0.2f && extras.Count > 0)
        {
            // amanece: se retiran los que el jugador no ve de cerca (y los cuerpos)
            timerDia -= Time.deltaTime;
            if (timerDia > 0f) return;
            timerDia = 2f;
            for (int i = extras.Count - 1; i >= 0; i--)
            {
                var e = extras[i];
                var h = e.GetComponent<RatHealth>();
                if ((h != null && h.IsDead) || (e.transform.position - jugador.position).magnitude > 35f)
                {
                    var l = e.GetComponent<Ladron>(); if (l != null) l.Soltar();     // si cargaba tus cosas, las deja
                    Destroy(e); extras.RemoveAt(i);
                    break;
                }
            }
        }
    }

    // bajo tierra o dentro del arbol no aparecen bichos de afuera
    bool Bajo()
    {
        if (Hormiguero.Instancia != null && Hormiguero.Instancia.JugadorDentro) return true;
        if (Colmena.Instancia != null && Colmena.Instancia.JugadorDentro) return true;
        return false;
    }

    bool Aparecer()
    {
        bool enSalon = ZonaJuego.EnEdificio(jugador.position);
        var lista = enSalon ? salon : exterior;
        if (lista.Count == 0) return false;
        var pl = lista[Random.Range(0, lista.Count)];
        for (int intento = 0; intento < 8; intento++)
        {
            float a = Random.value * Mathf.PI * 2f;
            Vector3 p = jugador.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(distancia.x, distancia.y);
            p = ZonaJuego.Limitar(p, enSalon, 6f);
            if (!enSalon)
            {
                if (ZonaJuego.EnEdificio(p, 4f)) continue;
                if (ZonaJuego.DistanciaHormiguero(p.x, p.z) < ZonaJuego.hormigueroRadio + 8f) continue;
                if (ZonaJuego.DistanciaArbol(p.x, p.z) < ZonaJuego.arbolRadio + 8f) continue;
            }
            if ((p - jugador.position).magnitude < distancia.x * 0.6f) continue;
            p.y = ZonaJuego.Suelo(p, enSalon) + pl.alturaSuelo;
            var go = Instantiate(pl.go, p, Quaternion.Euler(0f, Random.value * 360f, 0f));
            go.name = pl.go.name.Replace("Plantilla_", "Noche_");
            go.SetActive(true);
            extras.Add(go);
            return true;
        }
        return false;
    }
}
