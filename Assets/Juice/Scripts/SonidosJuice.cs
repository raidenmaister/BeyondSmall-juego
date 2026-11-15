using UnityEngine;

/// <summary>
/// Sonidos cortos del "juice" sintetizados por codigo (como la musica de Hamsterlandia):
/// asi no hacen falta archivos y suenan igual en todas partes.
/// </summary>
public static class SonidosJuice
{
    static AudioClip rebote, golpeFuerte, dolor, aterrizar, sinAliento, recoger, letra, logro, mision;

    /// <summary>Logro: arpegio brillante de cuatro notas.</summary>
    public static AudioClip Logro => logro != null ? logro : (logro = Crear("Juice_Logro", 0.9f, (t, k) =>
    {
        float[] notas = { 783.99f, 987.77f, 1174.66f, 1567.98f };
        float s = 0f;
        for (int i = 0; i < 4; i++)
        {
            float t0 = i * 0.09f; if (t < t0) continue;
            float tt = t - t0;
            s += (Mathf.Sin(2f * Mathf.PI * notas[i] * tt) + 0.3f * Mathf.Sin(4f * Mathf.PI * notas[i] * tt)) * Mathf.Exp(-tt * 4.5f) * 0.22f;
        }
        return s;
    }));

    /// <summary>Mision cumplida: acorde mayor que sube, mas solemne.</summary>
    public static AudioClip Mision => mision != null ? mision : (mision = Crear("Juice_Mision", 1.6f, (t, k) =>
    {
        float[] notas = { 523.25f, 659.25f, 783.99f, 1046.5f };
        float s = 0f;
        for (int i = 0; i < 4; i++)
        {
            float t0 = i * 0.14f; if (t < t0) continue;
            float tt = t - t0;
            float env = Mathf.Min(1f, tt * 30f) * Mathf.Exp(-tt * 2.2f);
            s += (Mathf.Sin(2f * Mathf.PI * notas[i] * tt) + 0.5f * Mathf.Sin(2f * Mathf.PI * notas[i] * 0.5f * tt)) * env * 0.2f;
        }
        return s;
    }));
    const int Hz = 44100;

    /// <summary>"Clink" metalico: el golpe no le hace nada.</summary>
    public static AudioClip Rebote => rebote != null ? rebote : (rebote = Crear("Juice_Rebote", 0.22f, (t, k) =>
        (Mathf.Sin(2f * Mathf.PI * 2350f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 3720f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 1530f * t) * 0.3f)
        * Mathf.Exp(-t * 26f)));

    /// <summary>Golpe cargado: un "bum" grave con algo de ruido.</summary>
    public static AudioClip GolpeFuerte => golpeFuerte != null ? golpeFuerte : (golpeFuerte = Crear("Juice_GolpeFuerte", 0.35f, (t, k) =>
    {
        float f = Mathf.Lerp(150f, 45f, Mathf.Clamp01(t / 0.25f));
        return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.9f + Ruido() * 0.35f * Mathf.Exp(-t * 40f)) * Mathf.Exp(-t * 9f);
    }));

    /// <summary>Recibir un golpe: golpe sordo + un "uf".</summary>
    public static AudioClip Dolor => dolor != null ? dolor : (dolor = Crear("Juice_Dolor", 0.3f, (t, k) =>
    {
        float f = Mathf.Lerp(220f, 90f, Mathf.Clamp01(t / 0.2f));
        float cuerpo = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 14f);
        return cuerpo * 0.8f + Ruido() * 0.5f * Mathf.Exp(-t * 55f);
    }));

    /// <summary>Aterrizaje: "tup" suave.</summary>
    public static AudioClip Aterrizar => aterrizar != null ? aterrizar : (aterrizar = Crear("Juice_Aterrizar", 0.18f, (t, k) =>
        (Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(120f, 60f, t / 0.18f) * t) * 0.7f + Ruido() * 0.4f * Mathf.Exp(-t * 60f)) * Mathf.Exp(-t * 22f)));

    /// <summary>Sin stamina: resoplido.</summary>
    public static AudioClip SinAliento => sinAliento != null ? sinAliento : (sinAliento = Crear("Juice_SinAliento", 0.45f, (t, k) =>
        Ruido() * 0.45f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.45f)) * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 9f * t))));

    /// <summary>Recoger algo: dos notas que suben.</summary>
    public static AudioClip Recoger => recoger != null ? recoger : (recoger = Crear("Juice_Recoger", 0.22f, (t, k) =>
    {
        float f = t < 0.08f ? 880f : 1318.5f;
        return Mathf.Sin(2f * Mathf.PI * f * t) * 0.45f * Mathf.Exp(-(t < 0.08f ? t : t - 0.08f) * 18f);
    }));

    /// <summary>Tecleo del texto que se escribe solo.</summary>
    public static AudioClip Letra => letra != null ? letra : (letra = Crear("Juice_Letra", 0.035f, (t, k) =>
        Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 520f * t)) * 0.18f * Mathf.Exp(-t * 90f)));

    public static void Tocar(AudioClip c, Vector3 donde, float volumen = 1f)
    {
        if (c == null) return;
        var cam = Camera.main;
        // cerca del oyente para que no se pierda por la distancia
        Vector3 p = cam != null ? Vector3.Lerp(cam.transform.position, donde, 0.3f) : donde;
        AudioSource.PlayClipAtPoint(c, p, volumen * Ajustes.VolEfectos);
    }

    static uint semilla = 22222;
    static float Ruido()
    {
        semilla ^= semilla << 13; semilla ^= semilla >> 17; semilla ^= semilla << 5;
        return (semilla / (float)uint.MaxValue) * 2f - 1f;
    }

    static AudioClip Crear(string nombre, float dur, System.Func<float, float, float> f)
    {
        int n = Mathf.CeilToInt(dur * Hz);
        var datos = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Hz;
            float fade = Mathf.Clamp01((dur - t) / 0.01f) * Mathf.Clamp01(t / 0.002f);
            datos[i] = Mathf.Clamp(f(t, i / (float)n), -1f, 1f) * fade;
        }
        var c = AudioClip.Create(nombre, n, 1, Hz, false);
        c.SetData(datos, 0);
        return c;
    }
}
