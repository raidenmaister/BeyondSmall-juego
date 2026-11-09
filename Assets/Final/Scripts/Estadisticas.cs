using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lo que hizo el jugador en la partida (se guarda con ella y se muestra al empezar los creditos).
/// Los tipos de bicho usan claves fijas (Hormiga, Abeja, Arana...) que tambien nombran sus retratos.
/// </summary>
[System.Serializable]
public class Estadisticas
{
    public static Estadisticas Actual = new Estadisticas();

    public int dias = 1;
    public float distancia;              // unidades del mundo (1 m real = 18)
    public int muertes;
    public int objetosRecogidos;
    public int comidas;
    public int creados;
    public int antorchasCreadas, escudosCreados;
    public int bloqueosPerfectos;
    public int mochilasRecuperadas;
    public List<Conteo> bichos = new List<Conteo>();

    [System.Serializable] public class Conteo { public string tipo; public int n; }

    public int TotalBichos { get { int t = 0; foreach (var c in bichos) t += c.n; return t; } }

    public int Bichos(string tipo)
    {
        foreach (var c in bichos) if (c.tipo == tipo) return c.n;
        return 0;
    }

    /// <summary>Tipos en el orden en que salen en los creditos, con su nombre bonito.</summary>
    public static readonly (string clave, string nombre)[] Tipos =
    {
        ("Hormiga", "Hormigas"), ("Abeja", "Abejas"), ("Arana", "Arañas"), ("Escarabajo", "Escarabajos"),
        ("Ciempies", "Ciempies"), ("Oruga", "Orugas"), ("Cochinilla", "Cochinillas"), ("Lombriz", "Lombrices"),
        ("Mariposa", "Mariposas"), ("Rata", "Ratas"), ("Larva", "Larvas"), ("ReinaHormiga", "Reina hormiga"), ("ReinaAbeja", "Reina abeja"),
    };

    public static string Tipo(GameObject go)
    {
        if (go.GetComponent<ReinaHormiga>() != null) return "ReinaHormiga";
        if (go.GetComponent<ReinaAbeja>() != null) return "ReinaAbeja";
        if (go.GetComponent<LarvaAbeja>() != null) return "Larva";
        if (go.GetComponent<HormigaNido>() != null) return "Hormiga";
        if (go.GetComponent<AbejaColmena>() != null) return "Abeja";
        if (go.GetComponent<SpiderAI>() != null) return "Arana";
        if (go.GetComponent<RatAI>() != null) return "Rata";
        var ai = go.GetComponent<CreatureAI>();
        if (ai != null)
            switch (ai.kind)
            {
                case CreatureAI.Kind.Ants: return "Hormiga";
                case CreatureAI.Kind.Bee: return "Abeja";
                case CreatureAI.Kind.Beetle: return "Escarabajo";
                case CreatureAI.Kind.Centipede: return "Ciempies";
                case CreatureAI.Kind.Caterpillar: return "Oruga";
                case CreatureAI.Kind.Woodlouse: return "Cochinilla";
                case CreatureAI.Kind.Worm: return "Lombriz";
                case CreatureAI.Kind.Butterfly: return "Mariposa";
            }
        return null;
    }

    // ------------------------------------------------------------------ registro
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Enganchar()
    {
        Actual = new Estadisticas();
        RatHealth.AlguienMurio -= AlMorirBicho;
        RatHealth.AlguienMurio += AlMorirBicho;
    }

    static void AlMorirBicho(RatHealth rh)
    {
        if (rh == null || GestorPartida.Instancia == null) return;
        string t = Tipo(rh.gameObject);
        if (t == null) return;
        var c = Actual.bichos.Find(x => x.tipo == t);
        if (c == null) { c = new Conteo { tipo = t }; Actual.bichos.Add(c); }
        c.n++;
    }

    public static void Creado(string item)
    {
        Actual.creados++;
        if (item == "Antorcha") Actual.antorchasCreadas++;
        if (item == "Escudo") Actual.escudosCreados++;
    }
}
