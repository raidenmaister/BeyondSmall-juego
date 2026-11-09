using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum Dificultad { Pacifico, Normal, Dificil, Extremo }

/// <summary>
/// Estado global de la partida en curso: ranura (0..2), dificultad, tiempo jugado y la partida
/// pendiente de cargar al entrar a la escena del juego. Lee y escribe las partidas en
/// persistentDataPath/partidas/ranuraN.json (+ ranuraN.png con la miniatura).
/// GestorPartida (en la escena del juego) es quien captura y aplica el estado del mundo.
/// </summary>
public static class Partida
{
    public const int Ranuras = 3;

    /// <summary>-1 = partida de prueba (se dio Play directo a la escena): no se guarda.</summary>
    public static int RanuraActual = -1;
    public static Dificultad Dif = Dificultad.Normal;
    public static float TiempoJugado;
    /// <summary>Partida leida del disco que GestorPartida aplica al cargar la escena (null = partida nueva).</summary>
    public static DatosPartida Pendiente;

    // el editor tiene la recarga de dominio desactivada: sin esto, un Play directo a la escena del juego
    // heredaria la ranura de la sesion anterior y la sobrescribiria
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar()
    {
        RanuraActual = -1; Dif = Dificultad.Normal; TiempoJugado = 0f; Pendiente = null;
    }

    /// <summary>En pacifico los animales no persiguen ni hacen daño.</summary>
    public static bool Agresivos => Dif != Dificultad.Pacifico;
    /// <summary>Vida de los animales: mas en dificil y extremo.</summary>
    public static float MultiplicadorVida => Dif >= Dificultad.Dificil ? 1.6f : 1f;
    public static bool NochePeligrosa => Dif >= Dificultad.Dificil;
    public static bool UnaVida => Dif == Dificultad.Extremo;

    static string Carpeta => Path.Combine(Application.persistentDataPath, "partidas");
    public static string RutaDatos(int r) => Path.Combine(Carpeta, "ranura" + (r + 1) + ".json");
    public static string RutaMiniatura(int r) => Path.Combine(Carpeta, "ranura" + (r + 1) + ".png");

    public static bool Existe(int r) => r >= 0 && File.Exists(RutaDatos(r));

    public static DatosPartida Leer(int r)
    {
        if (!Existe(r)) return null;
        try { return JsonUtility.FromJson<DatosPartida>(File.ReadAllText(RutaDatos(r))); }
        catch (System.Exception e) { Debug.LogWarning("[Partida] No se pudo leer la ranura " + (r + 1) + ": " + e.Message); return null; }
    }

    public static void Escribir(int r, DatosPartida d, byte[] miniaturaPng)
    {
        Directory.CreateDirectory(Carpeta);
        // escritura atomica: primero a un temporal y luego se reemplaza
        string ruta = RutaDatos(r), tmp = ruta + ".tmp";
        File.WriteAllText(tmp, JsonUtility.ToJson(d, false));
        if (File.Exists(ruta)) File.Delete(ruta);
        File.Move(tmp, ruta);
        if (miniaturaPng != null) File.WriteAllBytes(RutaMiniatura(r), miniaturaPng);
    }

    public static void Borrar(int r)
    {
        if (r < 0) return;
        foreach (var p in new[] { RutaDatos(r), RutaMiniatura(r), RutaDatos(r) + ".tmp" })
            if (File.Exists(p)) File.Delete(p);
    }

    public static Texture2D Miniatura(int r)
    {
        string p = RutaMiniatura(r);
        if (r < 0 || !File.Exists(p)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGB24, false);
        return t.LoadImage(File.ReadAllBytes(p)) ? t : null;
    }

    /// <summary>Prepara una partida nueva en la ranura (borra lo que hubiera). Luego hay que cargar la escena del juego.</summary>
    public static void Nueva(int r, Dificultad d)
    {
        Borrar(r);
        RanuraActual = r; Dif = d; TiempoJugado = 0f; Pendiente = null;
        Estadisticas.Actual = new Estadisticas();
        Ajustes.IntroPendiente = true;
    }

    /// <summary>Prepara la carga de la ranura. Devuelve false si no hay partida valida.</summary>
    public static bool Continuar(int r)
    {
        var d = Leer(r);
        if (d == null) return false;
        RanuraActual = r; Dif = (Dificultad)d.dificultad; TiempoJugado = d.tiempo; Pendiente = d;
        Estadisticas.Actual = d.stats ?? new Estadisticas();
        Ajustes.IntroPendiente = false;
        return true;
    }

    // ------------------------------------------------------------------ textos
    public static string Nombre(Dificultad d)
    {
        switch (d) { case Dificultad.Pacifico: return "Pacifico"; case Dificultad.Dificil: return "Dificil"; case Dificultad.Extremo: return "Extremo"; default: return "Normal"; }
    }

    public static string ColorHex(Dificultad d)
    {
        switch (d) { case Dificultad.Pacifico: return "#7fd6a0"; case Dificultad.Dificil: return "#f0a040"; case Dificultad.Extremo: return "#ff5050"; default: return "#46dcc8"; }
    }

    public static string Descripcion(Dificultad d)
    {
        switch (d)
        {
            case Dificultad.Pacifico: return "Los animales no te atacan ni te persiguen. Explora con calma; solo el hambre puede hacerte daño.";
            case Dificultad.Dificil: return "De noche salen muchos mas animales, y todos los bichos tienen mas vida.";
            case Dificultad.Extremo: return "Como Dificil, pero solo tienes una vida: si mueres, la partida se borra y todo vuelve a empezar desde cero.";
            default: return "La experiencia original: los bichos defienden su territorio y atacan si te acercas.";
        }
    }

    public static string Tiempo(float segundos)
    {
        int s = Mathf.FloorToInt(segundos), h = s / 3600, m = s / 60 % 60;
        return h > 0 ? h + " h " + m.ToString("00") + " min" : m > 0 ? m + " min" : s + " s";
    }
}

[System.Serializable]
public class DatosPartida
{
    public int version = 1;
    public int dificultad = (int)Dificultad.Normal;
    public float tiempo;
    public string fecha = "";
    public string lugar = "";

    public Vector3 posJugador;
    public float rotJugador;
    public float vida = -1f, vidaMax = -1f, comida = -1f;
    public float hora = -1f;

    public List<SlotGuardado> inventario = new List<SlotGuardado>();
    public List<string> recogidos = new List<string>();
    public List<ItemDinamico> dinamicos = new List<ItemDinamico>();
    public List<EnemigoGuardado> enemigos = new List<EnemigoGuardado>();

    public int baterias;
    public bool hormigueroFuria, colmenaFuria, colmenaReinaMuerta;

    public string mapaNiebla = "";          // zonas exploradas del mapa (gzip + base64)
    public List<ChinchetaGuardada> chinchetas = new List<ChinchetaGuardada>();

    public List<MochilaGuardada> mochilas = new List<MochilaGuardada>();   // mochilas y botines en el suelo
    public List<LadronGuardado> ladrones = new List<LadronGuardado>();     // bichos que cargan cosas robadas

    public Estadisticas stats = new Estadisticas();
    public bool completada;                 // ya vio el final
}

[System.Serializable] public class SlotGuardado { public string grupo; public int indice; public string id; public int cantidad; public string icono; }
[System.Serializable] public class ItemDinamico { public string id; public Vector3 pos; public float rot; }
[System.Serializable] public class MochilaGuardada { public Vector3 pos; public int tipo; public string titulo; public List<SlotGuardado> items = new List<SlotGuardado>(); }
[System.Serializable] public class LadronGuardado { public string clave; public List<SlotGuardado> items = new List<SlotGuardado>(); }
[System.Serializable] public class ChinchetaGuardada { public float x, z; public int color; }
[System.Serializable] public class EnemigoGuardado { public string clave; public bool muerto; public Vector3 pos; public float rot; public float vida; }
