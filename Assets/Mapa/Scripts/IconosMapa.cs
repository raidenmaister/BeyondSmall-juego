using System.Collections.Generic;
using UnityEngine;

/// <summary>Iconos del mapa y la brujula, dibujados por codigo (estilo lapiz/tinta) y guardados en cache.</summary>
public static class IconosMapa
{
    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
    const int N = 128;

    /// <summary>Dibuja (y guarda en cache) un icono de 128 px a partir de una funcion u,v en -1..1.</summary>
    public static Sprite Dibujar(string clave, System.Func<float, float, Color> pintar) => Hacer(clave, pintar, new Vector2(0.5f, 0.5f));

    static Sprite Hacer(string clave, System.Func<float, float, Color> pintar, Vector2 pivote)
    {
        Sprite s;
        if (cache.TryGetValue(clave, out s) && s != null) return s;
        var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, name = "Icono_" + clave };
        var px = new Color[N * N];
        // 4 muestras por pixel: bordes suaves
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                Color acc = Color.clear;
                for (int j = 0; j < 2; j++) for (int i = 0; i < 2; i++)
                {
                    float u = (x + 0.25f + i * 0.5f) / N * 2f - 1f, v = (y + 0.25f + j * 0.5f) / N * 2f - 1f;
                    Color c = pintar(u, v);
                    acc += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                }
                acc /= 4f;
                px[y * N + x] = acc.a > 0.0001f ? new Color(acc.r / acc.a, acc.g / acc.a, acc.b / acc.a, acc.a) : Color.clear;
            }
        t.SetPixels(px); t.Apply(true);
        s = Sprite.Create(t, new Rect(0, 0, N, N), pivote, N);
        s.name = clave;
        cache[clave] = s;
        return s;
    }

    static readonly Color Tinta = new Color(0.18f, 0.15f, 0.13f, 1f);

    static Color Sobre(Color abajo, Color arriba)
    {
        float a = arriba.a + abajo.a * (1 - arriba.a);
        if (a < 0.0001f) return Color.clear;
        Color c = (arriba * arriba.a + abajo * abajo.a * (1 - arriba.a)) / a; c.a = a; return c;
    }

    static float Circulo(float u, float v, float cx, float cy, float r) => Mathf.Clamp01((r - Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy))) * 40f + 0.5f);
    static float Anillo(float u, float v, float cx, float cy, float r, float g)
    {
        float d = Mathf.Abs(Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy)) - r);
        return Mathf.Clamp01((g - d) * 40f + 0.5f);
    }

    /// <summary>Chincheta vista desde arriba: cabeza redonda blanca (se tiñe con Image.color) y brillo.</summary>
    public static Sprite Chincheta() => Hacer("chincheta", (u, v) =>
    {
        Color c = Color.clear;
        // sombra
        c = Sobre(c, new Color(0, 0, 0, 0.35f * Circulo(u, v, 0.12f, -0.18f, 0.62f)));
        // aguja (se ve un poco abajo)
        float aguja = Mathf.Clamp01((0.05f - Mathf.Abs(u + (v + 0.25f) * 0.1f)) * 40f) * (v < -0.2f && v > -0.95f ? 1f : 0f);
        c = Sobre(c, new Color(0.55f, 0.55f, 0.58f, aguja));
        c = Sobre(c, new Color(1f, 1f, 1f, Circulo(u, v, 0f, 0.05f, 0.58f)));
        c = Sobre(c, new Color(0.62f, 0.62f, 0.62f, Anillo(u, v, 0f, 0.05f, 0.56f, 0.04f)));
        // brillo
        c = Sobre(c, new Color(1f, 1f, 1f, 0.9f * Circulo(u, v, -0.2f, 0.27f, 0.14f)));
        return c;
    }, new Vector2(0.5f, 0.42f));

    /// <summary>Flecha del jugador (apunta hacia arriba).</summary>
    public static Sprite Flecha() => Hacer("flecha", (u, v) =>
    {
        // triangulo con muesca atras
        float dentro = 1f;
        dentro *= Mathf.Clamp01((v + 0.75f) * 30f);
        dentro *= Mathf.Clamp01(((0.85f - v) * 0.62f - Mathf.Abs(u)) * 30f);
        float muesca = Mathf.Clamp01((v + 0.35f - (0.62f - Mathf.Abs(u)) * 0.6f) * 30f);
        float a = dentro * muesca;
        // borde oscuro
        float b1 = Mathf.Clamp01((v + 0.88f) * 30f) * Mathf.Clamp01(((0.98f - v) * 0.62f - Mathf.Abs(u)) * 30f) * Mathf.Clamp01((v + 0.47f - (0.7f - Mathf.Abs(u)) * 0.6f) * 30f);
        Color c = new Color(Tinta.r, Tinta.g, Tinta.b, b1);
        return Sobre(c, new Color(1f, 1f, 1f, a));
    }, new Vector2(0.5f, 0.5f));

    /// <summary>Cabina vista desde arriba: plataforma y anillo de energia.</summary>
    public static Sprite Cabina() => Hacer("cabina", (u, v) =>
    {
        Color c = Color.clear;
        c = Sobre(c, new Color(0.12f, 0.14f, 0.15f, Circulo(u, v, 0, 0, 0.92f)));
        c = Sobre(c, new Color(0.85f, 0.87f, 0.88f, Circulo(u, v, 0, 0, 0.74f)));
        c = Sobre(c, new Color(0.28f, 1f, 0.5f, Anillo(u, v, 0, 0, 0.5f, 0.09f)));
        c = Sobre(c, new Color(0.28f, 1f, 0.5f, Circulo(u, v, 0, 0, 0.16f)));
        // puerta (hueco hacia abajo)
        float puerta = Mathf.Clamp01((0.22f - Mathf.Abs(u)) * 40f) * (v < -0.45f ? 1f : 0f);
        c = Sobre(c, new Color(0.12f, 0.14f, 0.15f, puerta * Circulo(u, v, 0, 0, 0.92f)));
        return c;
    }, new Vector2(0.5f, 0.5f));

    public static Sprite Lugar(MapaMundo.TipoLugar tipo)
    {
        switch (tipo)
        {
            case MapaMundo.TipoLugar.Cabina: return Cabina();
            case MapaMundo.TipoLugar.Fogata: return Hacer("fogata", (u, v) =>
            {
                Color c = Color.clear;
                // leños cruzados
                float l1 = Mathf.Clamp01((0.1f - Mathf.Abs(u * 0.45f + v + 0.55f)) * 30f) * (Mathf.Abs(u) < 0.75f ? 1 : 0);
                float l2 = Mathf.Clamp01((0.1f - Mathf.Abs(-u * 0.45f + v + 0.55f)) * 30f) * (Mathf.Abs(u) < 0.75f ? 1 : 0);
                c = Sobre(c, new Color(0.4f, 0.25f, 0.13f, Mathf.Max(l1, l2)));
                // llama: gota
                float yy = (v + 0.3f) / 1.1f;
                float ancho = yy > 0 && yy < 1 ? Mathf.Sin(yy * Mathf.PI) * (1 - yy) * 0.9f : 0f;
                float llama = Mathf.Clamp01((ancho - Mathf.Abs(u)) * 30f);
                c = Sobre(c, new Color(0.95f, 0.45f, 0.1f, llama));
                float yi = (v + 0.25f) / 0.7f; float ai = yi > 0 && yi < 1 ? Mathf.Sin(yi * Mathf.PI) * (1 - yi) * 0.5f : 0f;
                c = Sobre(c, new Color(1f, 0.85f, 0.3f, Mathf.Clamp01((ai - Mathf.Abs(u)) * 30f)));
                return c;
            }, new Vector2(0.5f, 0.5f));
            case MapaMundo.TipoLugar.Puerta: return Hacer("puerta", (u, v) =>
            {
                Color c = Color.clear;
                float marco = (Mathf.Abs(u) < 0.55f && v > -0.85f && v < 0.85f ? 1f : 0f);
                float hueco = (Mathf.Abs(u) < 0.4f && v > -0.85f && v < 0.7f ? 1f : 0f);
                c = Sobre(c, new Color(Tinta.r, Tinta.g, Tinta.b, marco));
                c = Sobre(c, new Color(0.62f, 0.42f, 0.24f, hueco));
                c = Sobre(c, new Color(Tinta.r, Tinta.g, Tinta.b, Circulo(u, v, 0.22f, -0.05f, 0.07f)));
                return c;
            }, new Vector2(0.5f, 0.5f));
            case MapaMundo.TipoLugar.Hormiguero: return Hacer("hormiguero", (u, v) =>
            {
                Color c = Color.clear;
                c = Sobre(c, new Color(0.55f, 0.37f, 0.2f, Circulo(u, v, 0, 0, 0.88f)));
                c = Sobre(c, new Color(0.42f, 0.27f, 0.14f, Anillo(u, v, 0, 0, 0.58f, 0.05f)));
                c = Sobre(c, new Color(0.08f, 0.05f, 0.03f, Circulo(u, v, 0, 0, 0.3f)));
                // hormiguitas (puntos)
                for (int i = 0; i < 5; i++) { float a = i * 1.26f + 0.4f; c = Sobre(c, new Color(0.08f, 0.05f, 0.03f, Circulo(u, v, Mathf.Cos(a) * 0.72f, Mathf.Sin(a) * 0.72f, 0.06f))); }
                return c;
            }, new Vector2(0.5f, 0.5f));
            case MapaMundo.TipoLugar.Arbol: return Hacer("arbol", (u, v) =>
            {
                Color c = Color.clear;
                float copa = 0f;
                for (int i = 0; i < 7; i++) { float a = i * 0.9f; copa = Mathf.Max(copa, Circulo(u, v, Mathf.Cos(a) * 0.45f, Mathf.Sin(a) * 0.45f, 0.42f)); }
                copa = Mathf.Max(copa, Circulo(u, v, 0, 0, 0.5f));
                c = Sobre(c, new Color(0.2f, 0.36f, 0.15f, copa));
                c = Sobre(c, new Color(0.35f, 0.55f, 0.25f, Circulo(u, v, -0.12f, 0.12f, 0.42f) * copa));
                // colmena en el centro
                c = Sobre(c, new Color(0.95f, 0.72f, 0.15f, Circulo(u, v, 0.05f, -0.05f, 0.2f)));
                c = Sobre(c, new Color(0.35f, 0.22f, 0.05f, Anillo(u, v, 0.05f, -0.05f, 0.2f, 0.035f)));
                return c;
            }, new Vector2(0.5f, 0.5f));
            default: return Hacer("marca", (u, v) => new Color(Tinta.r, Tinta.g, Tinta.b, Anillo(u, v, 0, 0, 0.55f, 0.1f)), new Vector2(0.5f, 0.5f));
        }
    }

    /// <summary>Mochila de frente (tela marron, solapa y bolsillo). Image.color la tiñe (rojiza = robada).</summary>
    public static Sprite Mochila() => Hacer("mochila", (u, v) =>
    {
        Color c = Color.clear;
        // asa
        c = Sobre(c, new Color(0.25f, 0.16f, 0.09f, Anillo(u, v, 0f, 0.62f, 0.2f, 0.06f) * (v > 0.6f ? 1f : 0f)));
        // cuerpo redondeado
        float cx = Mathf.Max(Mathf.Abs(u) - 0.42f, 0f), cy = Mathf.Max(Mathf.Abs(v + 0.08f) - 0.52f, 0f);
        float cuerpo = Mathf.Clamp01((0.2f - Mathf.Sqrt(cx * cx + cy * cy)) * 40f + 0.5f);
        c = Sobre(c, new Color(0.18f, 0.12f, 0.07f, cuerpo));
        float cx2 = Mathf.Max(Mathf.Abs(u) - 0.38f, 0f), cy2 = Mathf.Max(Mathf.Abs(v + 0.08f) - 0.48f, 0f);
        c = Sobre(c, new Color(0.62f, 0.44f, 0.25f, Mathf.Clamp01((0.18f - Mathf.Sqrt(cx2 * cx2 + cy2 * cy2)) * 40f + 0.5f)));
        // solapa
        float solapa = (Mathf.Abs(u) < 0.5f && v > 0.08f && v < 0.48f ? 1f : 0f) * Mathf.Clamp01((0.5f - v + 0.05f) * 30f);
        c = Sobre(c, new Color(0.42f, 0.28f, 0.15f, solapa));
        c = Sobre(c, new Color(0.2f, 0.13f, 0.07f, Mathf.Clamp01((0.035f - Mathf.Abs(v - 0.08f)) * 40f) * (Mathf.Abs(u) < 0.5f ? 1f : 0f)));
        // hebilla y bolsillo
        c = Sobre(c, new Color(0.9f, 0.82f, 0.5f, (Mathf.Abs(u) < 0.08f && Mathf.Abs(v - 0.08f) < 0.07f) ? 1f : 0f));
        float bol = (Mathf.Abs(u) < 0.28f && v > -0.5f && v < -0.15f) ? 1f : 0f;
        c = Sobre(c, new Color(0.48f, 0.33f, 0.18f, bol));
        return c;
    }, new Vector2(0.5f, 0.5f));

    /// <summary>Degradado horizontal que se desvanece en los extremos (fondo de la brujula).</summary>
    public static Sprite FondoBrujula() => Hacer("fondoBrujula", (u, v) =>
    {
        float a = Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(u), 3f)) * 0.75f;
        return new Color(0.02f, 0.03f, 0.04f, a);
    }, new Vector2(0.5f, 0.5f));

    public static Sprite Blanco() => Hacer("blanco", (u, v) => Color.white, new Vector2(0.5f, 0.5f));
}
