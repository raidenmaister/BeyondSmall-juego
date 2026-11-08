using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Teclas del juego, reasignables desde Opciones > Controles y guardadas en PlayerPrefs.
/// Todo el juego lee el teclado por aqui (Controles.Pulsada / Bajada / Soltada).
/// Mientras un menu o la intro estan activos, Bloqueado = true y el jugador no responde.
/// </summary>
public static class Controles
{
    public enum Accion
    {
        Adelante, Atras, Izquierda, Derecha, Correr, Agacharse, Saltar,
        Atacar, Bloquear, Interactuar, Inventario, Comer, Cocinar, Antorcha, AcelerarTiempo
    }

    public static readonly Dictionary<Accion, KeyCode> Predeterminadas = new Dictionary<Accion, KeyCode>
    {
        { Accion.Adelante, KeyCode.W }, { Accion.Atras, KeyCode.S }, { Accion.Izquierda, KeyCode.A }, { Accion.Derecha, KeyCode.D },
        { Accion.Correr, KeyCode.LeftShift }, { Accion.Agacharse, KeyCode.LeftControl }, { Accion.Saltar, KeyCode.Space },
        { Accion.Atacar, KeyCode.Mouse0 }, { Accion.Bloquear, KeyCode.Mouse1 }, { Accion.Interactuar, KeyCode.E },
        { Accion.Inventario, KeyCode.I }, { Accion.Comer, KeyCode.F }, { Accion.Cocinar, KeyCode.C }, { Accion.Antorcha, KeyCode.T }, { Accion.AcelerarTiempo, KeyCode.N },
    };

    public static readonly Dictionary<Accion, string> Nombres = new Dictionary<Accion, string>
    {
        { Accion.Adelante, "Avanzar" }, { Accion.Atras, "Retroceder" }, { Accion.Izquierda, "Izquierda" }, { Accion.Derecha, "Derecha" },
        { Accion.Correr, "Correr" }, { Accion.Agacharse, "Agacharse" }, { Accion.Saltar, "Saltar" },
        { Accion.Atacar, "Golpear" }, { Accion.Bloquear, "Bloquear" }, { Accion.Interactuar, "Recoger / interactuar" },
        { Accion.Inventario, "Inventario" }, { Accion.Comer, "Comer" }, { Accion.Cocinar, "Cocinar" }, { Accion.Antorcha, "Antorcha" }, { Accion.AcelerarTiempo, "Acelerar el tiempo" },
    };

    /// <summary>Menu abierto, pausa o intro: el juego ignora las teclas de accion.</summary>
    public static bool Bloqueado;

    static Dictionary<Accion, KeyCode> teclas;

    static void Cargar()
    {
        if (teclas != null) return;
        teclas = new Dictionary<Accion, KeyCode>();
        foreach (var kv in Predeterminadas)
            teclas[kv.Key] = (KeyCode)PlayerPrefs.GetInt("tecla_" + kv.Key, (int)kv.Value);
    }

    public static KeyCode Tecla(Accion a) { Cargar(); return teclas[a]; }

    public static bool Pulsada(Accion a) { return !Bloqueado && Input.GetKey(Tecla(a)); }
    public static bool Bajada(Accion a) { return !Bloqueado && Input.GetKeyDown(Tecla(a)); }
    public static bool Soltada(Accion a) { return !Bloqueado && Input.GetKeyUp(Tecla(a)); }

    /// <summary>Eje de movimiento (-1..1); las flechas siempre funcionan como alternativa.</summary>
    public static float Eje(Accion negativa, Accion positiva, KeyCode altNeg, KeyCode altPos)
    {
        if (Bloqueado) return 0f;
        float v = 0f;
        if (Input.GetKey(Tecla(positiva)) || Input.GetKey(altPos)) v += 1f;
        if (Input.GetKey(Tecla(negativa)) || Input.GetKey(altNeg)) v -= 1f;
        return v;
    }

    /// <summary>Asigna una tecla; si otra accion ya la usaba, intercambian teclas.</summary>
    public static void Asignar(Accion a, KeyCode k)
    {
        Cargar();
        foreach (var otra in new List<Accion>(teclas.Keys))
            if (otra != a && teclas[otra] == k) { teclas[otra] = teclas[a]; PlayerPrefs.SetInt("tecla_" + otra, (int)teclas[otra]); }
        teclas[a] = k;
        PlayerPrefs.SetInt("tecla_" + a, (int)k);
        PlayerPrefs.Save();
    }

    public static void Restablecer()
    {
        Cargar();
        foreach (var kv in Predeterminadas) { teclas[kv.Key] = kv.Value; PlayerPrefs.DeleteKey("tecla_" + kv.Key); }
        PlayerPrefs.Save();
    }

    public static string NombreTecla(Accion a) { return NombreTecla(Tecla(a)); }

    public static string NombreTecla(KeyCode k)
    {
        switch (k)
        {
            case KeyCode.Mouse0: return "Clic izq.";
            case KeyCode.Mouse1: return "Clic der.";
            case KeyCode.Mouse2: return "Clic central";
            case KeyCode.Mouse3: return "Raton 4";
            case KeyCode.Mouse4: return "Raton 5";
            case KeyCode.Space: return "Espacio";
            case KeyCode.LeftShift: return "Shift izq.";
            case KeyCode.RightShift: return "Shift der.";
            case KeyCode.LeftControl: return "Ctrl izq.";
            case KeyCode.RightControl: return "Ctrl der.";
            case KeyCode.LeftAlt: return "Alt izq.";
            case KeyCode.RightAlt: return "Alt der.";
            case KeyCode.Return: return "Enter";
            case KeyCode.Tab: return "Tab";
            case KeyCode.Backspace: return "Retroceso";
            case KeyCode.UpArrow: return "Flecha arriba";
            case KeyCode.DownArrow: return "Flecha abajo";
            case KeyCode.LeftArrow: return "Flecha izq.";
            case KeyCode.RightArrow: return "Flecha der.";
            case KeyCode.CapsLock: return "Bloq Mayus";
        }
        string s = k.ToString();
        if (s.StartsWith("Alpha")) return s.Substring(5);
        if (s.StartsWith("Keypad")) return "Num " + s.Substring(6);
        return s;
    }

    /// <summary>Teclas que no se pueden asignar (las usa el menu).</summary>
    public static bool Reservada(KeyCode k) { return k == KeyCode.Escape || k == KeyCode.None; }
}
