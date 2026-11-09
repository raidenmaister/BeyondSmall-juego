using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Herramientas > Mapa > Capturar mapa base (con el juego en Play).
/// Una camara ortografica fotografia el mundo desde arriba (sin el techo del salon, de dia, sin niebla,
/// con toda la vegetacion) y lo convierte en un dibujo a lapiz sobre papel cuadriculado viejo:
/// Assets/Mapa/Resources/MapaBase.png (dibujo) y MapaPapel.png (el mismo papel en blanco, para la niebla).
/// Herramientas > Mapa > Estilizar de nuevo: repite solo el dibujo a partir de la ultima captura.
/// </summary>
public static class MapaBuilder
{
    const string Raiz = "Assets/Mapa/";
    const string Res = Raiz + "Resources/";
    const string Cruda = "Temp/MapaCaptura.png";   // fuera de Assets: pesa mucho y solo sirve para re-estilizar
    const int ResCaptura = 4096, ResMapa = 2048;

    [MenuItem("Herramientas/Mapa/Capturar mapa base (en Play)")]
    public static void Capturar()
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[MapaBuilder] Dale Play a la escena del juego y vuelve a usar el menu."); return; }
        Directory.CreateDirectory("Temp"); Directory.CreateDirectory(Res);

        // --- preparar la escena (todo se restaura al final)
        var restaurar = new List<System.Action>();
        var dn = Object.FindAnyObjectByType<DayNightCycle>();
        if (dn != null)
        {
            float h = dn.hora; bool p = dn.pausado;
            dn.hora = 12.5f; dn.pausado = true; dn.AplicarAhora();
            restaurar.Add(() => { dn.hora = h; dn.pausado = p; dn.AplicarAhora(); });
        }
        bool niebla = RenderSettings.fog; RenderSettings.fog = false; restaurar.Add(() => RenderSettings.fog = niebla);
        // los LODGroup esconden lo pequeño visto desde tan alto
        float bias = QualitySettings.lodBias; QualitySettings.lodBias = 200f; restaurar.Add(() => QualitySettings.lodBias = bias);
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (l.type != LightType.Directional) continue;
            var s = l.shadows; l.shadows = LightShadows.None; restaurar.Add(() => l.shadows = s);
        }
        // techo del salon y todo lo que cuelga arriba (menos la cabina)
        var maq = GameObject.Find("MAQUINA");
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (!r.enabled) continue;
            var b = r.bounds;
            bool sobreSalon = ZonaJuego.EnEdificio(b.center, 2f) && b.min.y > 30f;
            bool jugadorOBicho = r.GetComponentInParent<PlayerController>() != null || r.GetComponentInParent<RatHealth>() != null;
            if ((sobreSalon && (maq == null || !r.transform.IsChildOf(maq.transform))) || jugadorOBicho || r is ParticleSystemRenderer)
            {
                r.enabled = false; var rr = r; restaurar.Add(() => rr.enabled = true);
            }
        }
        // vegetacion: todo a maxima distancia (sin impostores planos); el pasto fuera (ensucia el dibujo)
        var veg = Object.FindAnyObjectByType<VegetacionExterior>();
        if (veg != null)
        {
            foreach (var p in veg.prototipos)
            {
                var pp = p; float lod = p.distanciaLod, max = p.distanciaMax, imp = p.distanciaImpostor, dens = p.densidadLejos;
                bool pasto = p.nombre == "Pasto" || p.nombre == "PastoAlto" || p.nombre == "Trebol" || p.nombre == "Musgo";
                p.distanciaLod = pasto ? 0f : 1e5f; p.distanciaMax = pasto ? 0f : 1e5f; p.distanciaImpostor = 0f; p.densidadLejos = 1f;
                restaurar.Add(() => { pp.distanciaLod = lod; pp.distanciaMax = max; pp.distanciaImpostor = imp; pp.densidadLejos = dens; });
            }
            bool oc = VegetacionExterior.ocultoGlobal; VegetacionExterior.ocultoGlobal = false; restaurar.Add(() => VegetacionExterior.ocultoGlobal = oc);
        }

        // --- camara cenital
        var go = new GameObject("CamaraMapa");
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = MapaMundo.Tamano * 0.5f;
        cam.transform.SetPositionAndRotation(new Vector3(MapaMundo.Centro.x, 1500f, MapaMundo.Centro.y), Quaternion.Euler(90f, 0f, 0f));
        cam.nearClipPlane = 1f; cam.farClipPlane = 3000f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.8f, 0.72f, 0.58f);
        cam.useOcclusionCulling = false; cam.allowHDR = false; cam.allowMSAA = true;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false; data.renderShadows = false;
        var rt = new RenderTexture(ResCaptura, ResCaptura, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.aspect = 1f;
        Texture2D tex = null;
        try
        {
            cam.Render();
            cam.Render();          // la vegetacion calcula sus visibles en el primer render
            var antes = RenderTexture.active; RenderTexture.active = rt;
            tex = new Texture2D(ResCaptura, ResCaptura, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, ResCaptura, ResCaptura), 0, 0); tex.Apply();
            RenderTexture.active = antes;
            File.WriteAllBytes(Cruda, tex.EncodeToPNG());
        }
        finally
        {
            cam.targetTexture = null; Object.DestroyImmediate(go); rt.Release(); Object.DestroyImmediate(rt);
            for (int i = restaurar.Count - 1; i >= 0; i--) restaurar[i]();
        }
        Estilizar(tex);
        Object.DestroyImmediate(tex);
        Debug.Log("[MapaBuilder] mapa base capturado y dibujado");
    }

    [MenuItem("Herramientas/Mapa/Estilizar de nuevo")]
    public static void EstilizarDeNuevo()
    {
        if (!File.Exists(Cruda)) { Debug.LogError("[MapaBuilder] Primero captura el mapa."); return; }
        var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(Cruda));
        Estilizar(t); Object.DestroyImmediate(t);
    }

    // ------------------------------------------------------------------ dibujo a lapiz
    static void Estilizar(Texture2D cruda)
    {
        int n = ResMapa;
        // reducir a la mitad promediando (menos ruido)
        var src = cruda.GetPixels();
        int ns = cruda.width, k = ns / n;
        var c = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Color acc = Color.clear;
                for (int j = 0; j < k; j++) for (int i = 0; i < k; i++) acc += src[(y * k + j) * ns + x * k + i];
                c[y * n + x] = acc / (k * k);
            }
        var L = new float[n * n];
        for (int i = 0; i < c.Length; i++) L[i] = c[i].r * 0.3f + c[i].g * 0.59f + c[i].b * 0.11f;
        // suavizado ligero para los bordes (menos "pelusa")
        var Ls = Desenfocar(L, n, 2);
        // color suavizado para el lavado (menos ruido en hojas y pasto)
        var cr = new float[n * n]; var cg = new float[n * n]; var cb = new float[n * n];
        for (int i = 0; i < c.Length; i++) { cr[i] = c[i].r; cg[i] = c[i].g; cb[i] = c[i].b; }
        cr = Desenfocar(cr, n, 1); cg = Desenfocar(cg, n, 1); cb = Desenfocar(cb, n, 1);

        var papel = Papel(n);
        var dibujo = new Color[n * n];
        Color grafito = new Color(0.2f, 0.18f, 0.16f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                // bordes (Sobel sobre la luminancia suavizada)
                float gx = M(Ls, n, x + 1, y - 1) + 2 * M(Ls, n, x + 1, y) + M(Ls, n, x + 1, y + 1) - M(Ls, n, x - 1, y - 1) - 2 * M(Ls, n, x - 1, y) - M(Ls, n, x - 1, y + 1);
                float gy = M(Ls, n, x - 1, y + 1) + 2 * M(Ls, n, x, y + 1) + M(Ls, n, x + 1, y + 1) - M(Ls, n, x - 1, y - 1) - 2 * M(Ls, n, x, y - 1) - M(Ls, n, x + 1, y - 1);
                float borde = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.16f, 0.62f, Mathf.Sqrt(gx * gx + gy * gy)));

                // lavado de color (lapices de colores): algo desaturado y aclarado, multiplicado sobre el papel
                Color col = new Color(cr[i], cg[i], cb[i]);
                float g = Ls[i];
                col = Color.Lerp(new Color(g, g, g), col, 0.8f);
                col = new Color(Mathf.Clamp01(col.r * 1.35f + 0.08f), Mathf.Clamp01(col.g * 1.35f + 0.08f), Mathf.Clamp01(col.b * 1.35f + 0.08f));
                Color lavado = Color.Lerp(Color.white, col, 0.72f);
                Color p = papel[i];
                Color r = new Color(p.r * lavado.r, p.g * lavado.g, p.b * lavado.b);

                // sombreado a rayas en lo oscuro
                float tono = Mathf.InverseLerp(0.36f, 0.06f, g);
                if (tono > 0f)
                {
                    float raya = Mathf.Abs(((x + y) % 7) - 3.5f) < 0.9f ? 1f : 0f;
                    float raya2 = tono > 0.55f && Mathf.Abs(((x - y + 4096) % 7) - 3.5f) < 0.9f ? 1f : 0f;
                    r = Color.Lerp(r, grafito, 0.22f * tono * Mathf.Max(raya, raya2));
                }
                // trazo de lapiz
                r = Color.Lerp(r, grafito, 0.8f * borde);
                r.a = 1f;
                dibujo[i] = r;
            }
        Guardar(papel, n, Res + "MapaPapel.png");
        Guardar(dibujo, n, Res + "MapaBase.png");
        CrearMaterial();
    }

    static float M(float[] a, int n, int x, int y)
    {
        x = Mathf.Clamp(x, 0, n - 1); y = Mathf.Clamp(y, 0, n - 1);
        return a[y * n + x];
    }

    static float[] Desenfocar(float[] a, int n, int r)
    {
        var tmp = new float[a.Length]; var o = new float[a.Length];
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float s = 0; for (int i = -r; i <= r; i++) s += M(a, n, x + i, y); tmp[y * n + x] = s / (2 * r + 1); }
        for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) { float s = 0; for (int i = -r; i <= r; i++) s += M(tmp, n, x, y + i); o[y * n + x] = s / (2 * r + 1); }
        return o;
    }

    /// <summary>Papel cuadriculado viejo, con manchas y grano (el mismo del inventario).</summary>
    static Color[] Papel(int n)
    {
        var px = new Color[n * n];
        var baseCol = new Color(0.86f, 0.78f, 0.64f);
        var rnd = new System.Random(11);
        float ox = (float)rnd.NextDouble() * 100f, oy = (float)rnd.NextDouble() * 100f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float mancha = Mathf.PerlinNoise(ox + u * 5f, oy + v * 5f) * 0.6f + Mathf.PerlinNoise(ox + u * 17f, oy + v * 17f) * 0.4f;
                float grano = Mathf.PerlinNoise(x * 0.35f, y * 0.35f) * 0.5f + Mathf.PerlinNoise(x * 1.3f + 50f, y * 1.3f) * 0.5f;
                float k = 1f - 0.13f * Mathf.SmoothStep(0.45f, 0.85f, mancha) - 0.05f * (grano - 0.5f);
                Color c = baseCol * k;
                // cuadricula fina y una mas marcada cada 5 cuadros
                bool fina = (x % 32) == 0 || (y % 32) == 0;
                bool gruesa = (x % 160) == 0 || (y % 160) == 0;
                if (gruesa) c = Color.Lerp(c, new Color(0.45f, 0.37f, 0.28f), 0.32f);
                else if (fina) c = Color.Lerp(c, new Color(0.5f, 0.42f, 0.32f), 0.16f);
                c.a = 1f;
                px[y * n + x] = c;
            }
        return px;
    }

    static void Guardar(Color[] px, int n, string ruta)
    {
        var t = new Texture2D(n, n, TextureFormat.RGB24, false);
        t.SetPixels(px); t.Apply();
        File.WriteAllBytes(ruta, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(ruta);
        ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp;
        ti.maxTextureSize = 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.anisoLevel = 2;
        ti.SaveAndReimport();
    }

    [MenuItem("Herramientas/Mapa/Crear material")]
    public static void CrearMaterial()
    {
        Directory.CreateDirectory(Res);
        string ruta = Res + "MapaNiebla.mat";
        var sh = Shader.Find("UI/MapaNiebla");
        if (sh == null) { Debug.LogError("[MapaBuilder] falta el shader UI/MapaNiebla"); return; }
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
        m.shader = sh;
        m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Res + "MapaBase.png"));
        m.SetTexture("_Papel", AssetDatabase.LoadAssetAtPath<Texture2D>(Res + "MapaPapel.png"));
        EditorUtility.SetDirty(m);
        AssetDatabase.SaveAssets();
    }
}
