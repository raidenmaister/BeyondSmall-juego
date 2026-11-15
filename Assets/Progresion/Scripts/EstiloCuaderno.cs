using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Estilo visual de las paginas nuevas (libro de recetas, misiones y logros): papel cuadriculado de cuaderno
/// con bordes gastados, tinta y letra a mano, para que encajen con el inventario de papel.
/// </summary>
public static class EstiloCuaderno
{
    public static readonly Color Tinta = new Color(0.17f, 0.14f, 0.12f);
    public static readonly Color TintaSuave = new Color(0.32f, 0.27f, 0.23f, 0.85f);
    public static readonly Color Rojo = new Color(0.72f, 0.16f, 0.14f);
    public static readonly Color Verde = new Color(0.16f, 0.5f, 0.22f);
    public static readonly Color Lapiz = new Color(0.35f, 0.35f, 0.38f, 0.55f);

    static Sprite papel, cinta, caja, check, trofeo, candado;
    static TMP_FontAsset fuente, titulos;

    /// <summary>Letra a mano (Patrick Hand, OFL) o la del menu si no esta.</summary>
    public static TMP_FontAsset Fuente
    {
        get
        {
            if (fuente != null) return fuente;
            fuente = Resources.Load<TMP_FontAsset>("Fuentes/PatrickHand SDF");
            if (fuente == null) { var r = Resources.Load<MenuRecursos>("MenuRecursos"); if (r != null) fuente = r.fuenteTexto; }
            return fuente;
        }
    }

    public static TMP_FontAsset Titulos
    {
        get
        {
            if (titulos != null) return titulos;
            titulos = Resources.Load<TMP_FontAsset>("Fuentes/PermanentMarker SDF");
            if (titulos == null) titulos = Fuente;
            return titulos;
        }
    }

    /// <summary>Hoja de cuaderno: beige, cuadricula azul tenue, margen rojo y bordes oscurecidos.</summary>
    public static Sprite Papel()
    {
        if (papel != null) return papel;
        const int W = 512, H = 640;
        var t = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "PapelCuaderno" };
        var px = new Color[W * H];
        var rnd = new System.Random(7);
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (float)x / W, v = (float)y / H;
                Color c = new Color(0.93f, 0.88f, 0.76f);
                // manchas y grano de papel viejo
                float ruido = Mathf.PerlinNoise(u * 6f, v * 6f) * 0.06f + (float)rnd.NextDouble() * 0.025f;
                c *= 1f - ruido;
                // cuadricula
                if (x % 22 == 0 || y % 22 == 0) c = Color.Lerp(c, new Color(0.55f, 0.66f, 0.8f), 0.25f);
                // margen rojo
                if (x >= 58 && x <= 59) c = Color.Lerp(c, new Color(0.85f, 0.35f, 0.35f), 0.55f);
                // bordes quemados
                float borde = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                float oscuro = Mathf.Clamp01(1f - borde / 0.06f) * (0.45f + Mathf.PerlinNoise(u * 20f, v * 20f) * 0.4f);
                c = Color.Lerp(c, new Color(0.45f, 0.32f, 0.2f), oscuro);
                // borde roto: algunos pixeles transparentes en el filo
                float filo = Mathf.PerlinNoise(u * 40f + 3f, v * 40f) * 0.012f;
                c.a = borde < filo ? 0f : 1f;
                px[y * W + x] = c;
            }
        t.SetPixels(px); t.Apply(true);
        papel = Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(40, 40, 40, 40));
        return papel;
    }

    /// <summary>Casilla dibujada a lapiz (como las del inventario).</summary>
    public static Sprite Caja() => caja != null ? caja : (caja = IconosMapa.Dibujar("cajaLapiz", (u, v) =>
    {
        float d = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
        float linea = Mathf.Clamp01((0.045f - Mathf.Abs(d - 0.9f)) * 30f);
        float cruz = Mathf.Clamp01((0.02f - Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v))) * 30f) * (d < 0.88f ? 0.25f : 0f);
        return new Color(0.25f, 0.22f, 0.2f, Mathf.Max(linea * 0.8f, cruz) );
    }));

    public static Sprite Check() => check != null ? check : (check = IconosMapa.Dibujar("checkTinta", (u, v) =>
    {
        // palomita: dos trazos
        float a = Segmento(u, v, -0.55f, 0.0f, -0.15f, -0.45f, 0.13f);
        float b = Segmento(u, v, -0.15f, -0.45f, 0.65f, 0.6f, 0.13f);
        return new Color(0.16f, 0.5f, 0.22f, Mathf.Max(a, b));
    }));

    public static Sprite Trofeo() => trofeo != null ? trofeo : (trofeo = IconosMapa.Dibujar("trofeo", (u, v) =>
    {
        Color c = Color.clear;
        float copa = (v > -0.05f && v < 0.7f && Mathf.Abs(u) < 0.55f - (0.7f - v) * 0.25f * 0f && Mathf.Abs(u) < Mathf.Lerp(0.2f, 0.55f, Mathf.InverseLerp(-0.05f, 0.7f, v))) ? 1f : 0f;
        float asas = Mathf.Clamp01((0.07f - Mathf.Abs(Mathf.Sqrt((Mathf.Abs(u) - 0.55f) * (Mathf.Abs(u) - 0.55f) + (v - 0.4f) * (v - 0.4f)) - 0.18f)) * 25f) * (Mathf.Abs(u) > 0.55f ? 1f : 0f);
        float pie = (Mathf.Abs(u) < 0.08f && v < -0.05f && v > -0.5f) ? 1f : 0f;
        float base_ = (Mathf.Abs(u) < 0.35f && v < -0.5f && v > -0.68f) ? 1f : 0f;
        float a = Mathf.Max(Mathf.Max(copa, asas), Mathf.Max(pie, base_));
        float brillo = (u < -0.15f && u > -0.28f && v > 0.15f && v < 0.6f) ? 0.5f : 0f;
        return Color.Lerp(new Color(1f, 0.75f, 0.18f, a), new Color(1f, 0.97f, 0.8f, a), brillo);
    }));

    public static Sprite Candado() => candado != null ? candado : (candado = IconosMapa.Dibujar("candado", (u, v) =>
    {
        float cuerpo = (Mathf.Abs(u) < 0.45f && v < 0.1f && v > -0.65f) ? 1f : 0f;
        float arco = Mathf.Clamp01((0.08f - Mathf.Abs(Mathf.Sqrt(u * u + (v - 0.1f) * (v - 0.1f)) - 0.3f)) * 25f) * (v > 0.1f ? 1f : 0f);
        float ojo = Mathf.Sqrt(u * u + (v + 0.25f) * (v + 0.25f)) < 0.1f ? 1f : 0f;
        float a = Mathf.Max(cuerpo, arco);
        return Color.Lerp(new Color(0.45f, 0.42f, 0.4f, a), new Color(0.2f, 0.18f, 0.17f, a), ojo);
    }));

    static float Segmento(float u, float v, float x0, float y0, float x1, float y1, float g)
    {
        Vector2 p = new Vector2(u, v), a = new Vector2(x0, y0), b = new Vector2(x1, y1);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);
        float d = (p - (a + (b - a) * t)).magnitude;
        return Mathf.Clamp01((g - d) * 30f);
    }

    // ------------------------------------------------------------------ fabrica de UI
    public static Image Img(Transform padre, string n, Sprite s, Color c)
    {
        var i = new GameObject(n, typeof(RectTransform)).AddComponent<Image>();
        i.transform.SetParent(padre, false);
        i.sprite = s; i.color = c; i.raycastTarget = false;
        return i;
    }

    public static TextMeshProUGUI Txt(Transform padre, string n, string s, float tam, Color c, TextAlignmentOptions al = TextAlignmentOptions.TopLeft, bool titulo = false)
    {
        var t = new GameObject(n, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(padre, false);
        var f = titulo ? Titulos : Fuente;
        if (f != null) t.font = f;
        t.text = s; t.fontSize = tam; t.color = c; t.alignment = al; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    public static RectTransform Rect(Component c, Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
    {
        var r = (RectTransform)c.transform;
        r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.offsetMin = offMin; r.offsetMax = offMax;
        return r;
    }

    /// <summary>Canvas propio por encima del HUD (las paginas del cuaderno).</summary>
    public static Canvas Lienzo(string nombre, int orden)
    {
        var go = new GameObject(nombre);
        var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = orden;
        var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return c;
    }
}
