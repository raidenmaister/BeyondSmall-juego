using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Construye el exterior del salon: suelo con colinas, cerca circular de madera, vegetacion
/// instanciada, bosque lejano, montañas y bichos. Menu: Herramientas > Exterior > Construir.
/// Se puede volver a ejecutar: borra el exterior anterior y lo genera de nuevo.
/// </summary>
public static class ExteriorBuilder
{
    const string Raiz = "Assets/Exterior";
    const string Mod = Raiz + "/Modelos/";
    const string Tex = Raiz + "/Texturas/";
    const string Mat = Raiz + "/Materiales/";
    const string Gen = Raiz + "/Generado/";

    const float RadioSuelo = 436f;
    const float PasoSuelo = 4f;

    [MenuItem("Herramientas/Exterior/Construir")]
    public static void Construir()
    {
        Directory.CreateDirectory(Gen);
        Directory.CreateDirectory(Mat);
        ConfigurarImportaciones();

        var viejo = GameObject.Find("EXTERIOR");
        if (viejo != null) Object.DestroyImmediate(viejo);
        var raiz = new GameObject("EXTERIOR");

        CrearSuelo(raiz.transform);
        CrearSueloLejano(raiz.transform);
        CrearMontanas(raiz.transform);
        CrearCerca(raiz.transform);
        var veg = CrearVegetacion(raiz.transform);
        CrearBosqueLejano(raiz.transform);
        CrearBichos(raiz.transform, veg);
        AjustarSalonYCamara();

        EditorSceneManager.MarkSceneDirty(raiz.scene);
        EditorSceneManager.SaveScene(raiz.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Exterior] Construido. Instancias de vegetacion: " + veg.TotalInstancias);
    }

    // ------------------------------------------------------------------ importacion
    static void ConfigurarImportaciones()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Raiz + "/Modelos" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            bool cambia = mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation || !mi.isReadable;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.isReadable = true;   // los parches de pasto combinan mallas en el editor
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            if (cambia) mi.SaveAndReimport();
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Raiz + "/Texturas", Raiz + "/Impostores" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            bool normal = path.Contains("_nor");
            bool impostor = path.Contains("/Impostores/");
            bool suelo = path.Contains("forest_");
            var tipo = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            int max = suelo ? 2048 : 1024;
            bool alpha = path.EndsWith(".png") && !normal;
            if (ti.textureType == tipo && ti.maxTextureSize == max && ti.alphaIsTransparency == alpha && ti.userData == "exterior") continue;
            ti.textureType = tipo;
            ti.maxTextureSize = max;
            ti.alphaIsTransparency = alpha;
            ti.mipmapEnabled = true;
            ti.wrapMode = impostor ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            ti.anisoLevel = suelo ? 4 : 1;
            // mipmaps que conservan la cobertura del alpha: el pasto no "adelgaza" a lo lejos
            ti.mipMapsPreserveCoverage = alpha;
            ti.alphaTestReferenceValue = 0.45f;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.userData = "exterior";
            ti.SaveAndReimport();
        }
    }

    static T Cargar<T>(string path) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null) Debug.LogError("[Exterior] No se encontro " + path);
        return a;
    }

    static Texture2D Textura(string nombre)
    {
        foreach (var ext in new[] { ".png", ".jpg" })
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Tex + nombre + ext);
            if (t != null) return t;
        }
        return null;
    }

    static Material GuardarMaterial(string nombre, Shader shader, System.Action<Material> config)
    {
        string path = Mat + nombre + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        config(m);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material MatFollaje(string tex, float viento, float translucidez = 0.35f, float variacion = 0.2f, bool recorte = true)
    {
        return GuardarMaterial("Follaje_" + tex, Shader.Find("Exterior/Follaje"), m =>
        {
            m.SetTexture("_BaseMap", Textura(tex + "_diff"));
            m.SetFloat("_Viento", viento);
            m.SetFloat("_Translucidez", translucidez);
            m.SetFloat("_VariacionColor", variacion);
            m.SetFloat("_AlphaClip", recorte ? 1f : 0f);
            if (recorte) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = recorte ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
        });
    }

    static Material MatSolido(string tex, float suavidad = 0.15f)
    {
        return GuardarMaterial("Solido_" + tex, Shader.Find("Universal Render Pipeline/Lit"), m =>
        {
            m.SetTexture("_BaseMap", Textura(tex + "_diff"));
            var n = Textura(tex + "_nor");
            if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Smoothness", suavidad);
            m.SetFloat("_Metallic", 0f);
        });
    }

    static Material MatLejano(string nombre, Texture2D tex, bool billboard, bool colorVertice, float escalaNiebla, Color color)
    {
        return GuardarMaterial("Lejano_" + nombre, Shader.Find("Exterior/Lejano"), m =>
        {
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_EscalaNiebla", escalaNiebla);
            m.SetFloat("_Billboard", billboard ? 1 : 0); if (billboard) m.EnableKeyword("_BILLBOARD"); else m.DisableKeyword("_BILLBOARD");
            m.SetFloat("_AlphaClip", billboard ? 1 : 0); if (billboard) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            m.SetFloat("_ColorVertice", colorVertice ? 1 : 0); if (colorVertice) m.EnableKeyword("_COLOR_VERTICE"); else m.DisableKeyword("_COLOR_VERTICE");
            m.SetFloat("_Cutoff", 0.45f);
        });
    }

    static Mesh GuardarMalla(Mesh m, string nombre)
    {
        // se reemplaza el asset completo: copiar encima (CopySerialized) dejaba en la GPU los datos viejos
        string path = Gen + nombre + ".asset";
        NormalesSeguras.Sanear(m);   // normales nulas = NaN = destellos del Bloom
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static GameObject Objeto(string nombre, Transform padre, Mesh malla, Material mat, bool sombras, bool estatico = true)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = sombras ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        if (estatico) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    // ------------------------------------------------------------------ suelo
    static void CrearSuelo(Transform padre)
    {
        Vector3 c = ZonaJuego.centro;
        int n = Mathf.CeilToInt(2f * RadioSuelo / PasoSuelo) + 1;
        var verts = new List<Vector3>(n * n);
        var idx = new int[n * n];
        for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                float x = c.x - RadioSuelo + ix * PasoSuelo, z = c.z - RadioSuelo + iz * PasoSuelo;
                idx[iz * n + ix] = -1;
                if (ZonaJuego.DistanciaCentro(x, z) > RadioSuelo + PasoSuelo * 1.5f) continue;
                // bajo el edificio el suelo queda justo debajo del piso del salon (sin parpadeo)
                float y = ZonaJuego.EnEdificio(new Vector3(x, 0, z), -0.5f) ? ZonaJuego.alturaPiso - 0.08f : ZonaJuego.Altura(x, z);
                idx[iz * n + ix] = verts.Count;
                verts.Add(new Vector3(x, y, z));
            }
        var tris = new List<int>();
        for (int iz = 0; iz < n - 1; iz++)
            for (int ix = 0; ix < n - 1; ix++)
            {
                int a = idx[iz * n + ix], b = idx[iz * n + ix + 1], d = idx[(iz + 1) * n + ix], e = idx[(iz + 1) * n + ix + 1];
                if (a < 0 || b < 0 || d < 0 || e < 0) continue;
                // los cuadros completamente dentro del edificio no hacen falta
                var ce = (verts[a] + verts[e]) * 0.5f;
                if (ZonaJuego.EnEdificio(ce, -PasoSuelo * 1.5f)) continue;
                // hueco del hormiguero: ahi el suelo lo pone la malla del monticulo
                if (ZonaJuego.DistanciaHormiguero(ce.x, ce.z) < ZonaJuego.hormigueroRadio) continue;
                if (ZonaJuego.DistanciaArbol(ce.x, ce.z) < ZonaJuego.arbolRadio - 1f) continue;   // ahi el suelo lo pone el arbol
                tris.AddRange(new[] { a, d, b, b, d, e });
            }
        var m = new Mesh { name = "SueloExterior", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.SetVertices(verts); m.SetTriangles(tris, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        m = GuardarMalla(m, "SueloExterior");

        var mat = GuardarMaterial("Suelo", Shader.Find("Exterior/Suelo"), x =>
        {
            x.SetTexture("_CapaA", Textura("forest_ground_04_diff"));
            x.SetTexture("_NormalA", Textura("forest_ground_04_nor"));
            x.SetTexture("_CapaB", Textura("forest_leaves_04_diff"));
            x.SetTexture("_NormalB", Textura("forest_leaves_04_nor"));
            x.SetColor("_ColorA", new Color(0.8f, 0.84f, 0.66f));
            x.SetColor("_ColorB", new Color(0.86f, 0.84f, 0.7f));
            x.SetFloat("_Tamano", 40f);
            x.SetFloat("_TamanoMezcla", 170f);
        });
        var go = Objeto("Suelo", padre, m, mat, false);
        go.AddComponent<MeshCollider>().sharedMesh = m;
    }

    static void CrearSueloLejano(Transform padre)
    {
        // anillo de suelo visual (sin colision) desde la cerca hasta el horizonte
        Vector3 c = ZonaJuego.centro;
        int seg = 256; float[] radios = { 428f, 470f, 540f, 650f, 800f, 1000f, 1300f, 1700f, 2200f, 2900f, 3800f, 4600f };
        var v = new List<Vector3>(); var col = new List<Color>(); var t = new List<int>();
        for (int r = 0; r < radios.Length; r++)
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                float x = c.x + Mathf.Cos(a) * radios[r], z = c.z + Mathf.Sin(a) * radios[r];
                v.Add(new Vector3(x, ZonaJuego.Altura(x, z) - 0.6f, z));
                float k = Mathf.PerlinNoise(x * 0.004f, z * 0.004f);
                col.Add(Color.Lerp(new Color(0.24f, 0.27f, 0.13f), new Color(0.33f, 0.3f, 0.18f), k));
            }
        for (int r = 0; r < radios.Length - 1; r++)
            for (int s = 0; s < seg; s++)
            {
                int a = r * (seg + 1) + s, b = a + 1, d = a + seg + 1, e = d + 1;
                t.AddRange(new[] { a, d, b, b, d, e });
            }
        var m = new Mesh { name = "SueloLejano" };
        m.SetVertices(v); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        m = GuardarMalla(m, "SueloLejano");
        Objeto("SueloLejano", padre, m, MatLejano("Suelo", null, false, true, 1f, Color.white), false);
    }

    static void CrearMontanas(Transform padre)
    {
        var p = new GameObject("Paisaje").transform; p.SetParent(padre, false);
        // colinas boscosas redondeadas al frente y una cordillera con nieve al fondo
        var colinas = AnilloMontanas("Colinas", 1700f, 2500f, 90f, 360f, 0.0022f, 11.3f, false,
            new[] { new Color(0.14f, 0.2f, 0.09f), new Color(0.18f, 0.24f, 0.11f), new Color(0.22f, 0.27f, 0.13f) }, 2f);
        Objeto("Colinas", p, colinas, MatLejano("Colinas", null, false, true, 0.28f, Color.white), false);
        var montanas = AnilloMontanas("Montanas", 3000f, 4300f, 250f, 1350f, 0.0011f, 47.9f, true,
            new[] { new Color(0.2f, 0.24f, 0.15f), new Color(0.36f, 0.35f, 0.33f), new Color(0.92f, 0.93f, 0.96f) }, 1f);
        Objeto("Montanas", p, montanas, MatLejano("Montanas", null, false, true, 0.15f, Color.white), false);
    }

    static Mesh AnilloMontanas(string nombre, float r0, float r1, float hMin, float hMax, float freq, float sem, bool crestas, Color[] colores, float nieveMin)
    {
        Vector3 c = ZonaJuego.centro;
        int seg = 720, filas = 18;
        var v = new List<Vector3>(); var col = new List<Color>(); var t = new List<int>();
        for (int f = 0; f <= filas; f++)
        {
            float u = f / (float)filas;
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                float r = Mathf.Lerp(r0, r1, u);
                float x = c.x + Mathf.Cos(a) * r, z = c.z + Mathf.Sin(a) * r;
                // ruido de pocas octavas: los detalles no deben ser mas finos que la malla (si no salen "dientes")
                float n = 0f, amp = 1f, fr = freq, suma = 0f;
                for (int o = 0; o < 3; o++)
                {
                    float k = Mathf.PerlinNoise(x * fr + sem, z * fr - sem);
                    if (crestas) k = 1f - Mathf.Abs(k * 2f - 1f);   // crestas suaves
                    n += k * amp; suma += amp; amp *= 0.45f; fr *= 1.9f;
                }
                n /= suma;
                n = crestas ? Mathf.SmoothStep(0f, 1f, n) : n;
                float perfil = Mathf.Max(0f, Mathf.Sin(u * Mathf.PI));   // sin(pi) puede dar -1e-8: Pow daria NaN
                float h = (hMin + (hMax - hMin) * n) * Mathf.Pow(perfil, 0.6f);
                v.Add(new Vector3(x, h - 2f, z));
                float hn = h / hMax;
                Color cc = hn < 0.45f ? Color.Lerp(colores[0], colores[1], hn / 0.45f) : Color.Lerp(colores[1], colores[2], Mathf.InverseLerp(0.66f * nieveMin, 0.82f * nieveMin, hn));
                col.Add(cc);
            }
        }
        for (int f = 0; f < filas; f++)
            for (int s = 0; s < seg; s++)
            {
                int a = f * (seg + 1) + s, b = a + 1, d = a + seg + 1, e = d + 1;
                t.AddRange(new[] { a, d, b, b, d, e });
            }
        var m = new Mesh { name = nombre };
        m.SetVertices(v); m.SetColors(col); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return GuardarMalla(m, nombre);
    }

    // ------------------------------------------------------------------ cerca
    static void CrearCerca(Transform padre)
    {
        var cerca = new GameObject("Cerca").transform; cerca.SetParent(padre, false);
        var mat = MatSolido("old_planks_02", 0.1f);
        mat.SetTextureScale("_BaseMap", Vector2.one);
        Vector3 c = ZonaJuego.centro;
        float R = ZonaJuego.radioCerca;
        float ancho = 2.4f, hueco = 0.22f, grosor = 0.6f;
        float perim = 2f * Mathf.PI * R;
        int tablas = Mathf.FloorToInt(perim / (ancho + hueco));
        int trozos = 24;
        var rnd = new System.Random(5);
        for (int k = 0; k < trozos; k++)
        {
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            int i0 = k * tablas / trozos, i1 = (k + 1) * tablas / trozos;
            for (int i = i0; i < i1; i++)
            {
                float a = (i + 0.5f) / tablas * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var tan = new Vector3(-dir.z, 0, dir.x);
                var basePos = c + dir * R;
                float y0 = ZonaJuego.Altura(basePos.x, basePos.z) - 1.5f;
                float alto = 15f + (float)rnd.NextDouble() * 1.6f;
                float incl = ((float)rnd.NextDouble() - 0.5f) * 0.05f;   // tablas un poco chuecas
                float u0 = (float)rnd.NextDouble() * 0.8f;
                Tabla(v, uv, t, basePos + Vector3.up * y0, tan, dir, ancho, alto, grosor, incl, u0);
            }
            // dos travesaños por dentro
            int postes = Mathf.Max(1, (i1 - i0) / 8);
            for (int j = 0; j < postes; j++)
            {
                float a0 = (i0 + j * (i1 - i0) / (float)postes) / tablas * Mathf.PI * 2f;
                float a1 = (i0 + (j + 1) * (i1 - i0) / (float)postes) / tablas * Mathf.PI * 2f;
                var p0 = c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * (R - grosor);
                var p1 = c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * (R - grosor);
                float y0 = ZonaJuego.Altura(p0.x, p0.z), y1 = ZonaJuego.Altura(p1.x, p1.z);
                foreach (float h in new[] { 3.2f, 11f })
                    Viga(v, uv, t, p0 + Vector3.up * (y0 + h), p1 + Vector3.up * (y1 + h), 1.3f, 0.7f);
                // poste
                var dirP = (p0 - c).normalized;
                Tabla(v, uv, t, p0 - dirP * 0.6f + Vector3.up * (y0 - 2f), new Vector3(-dirP.z, 0, dirP.x), dirP, 1.6f, 16f, 1.4f, 0f, 0.3f);
            }
            var m = new Mesh { name = "Cerca_" + k };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
            m = GuardarMalla(m, "Cerca_" + k);
            Objeto("Tablas_" + k, cerca, m, mat, true);
        }

        // colision: anillo de cajas altas (mas grueso que la cerca para que nada se cuele)
        var colis = new GameObject("Colision").transform; colis.SetParent(cerca, false);
        int nCol = 180;
        float largo = perim / nCol * 1.05f;
        for (int i = 0; i < nCol; i++)
        {
            float a = (i + 0.5f) / nCol * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var p = c + dir * (R + 0.5f);
            var go = new GameObject("Muro_" + i);
            go.transform.SetParent(colis, false);
            go.transform.SetPositionAndRotation(new Vector3(p.x, ZonaJuego.Altura(p.x, p.z) + 15f, p.z), Quaternion.LookRotation(dir));
            go.AddComponent<BoxCollider>().size = new Vector3(largo, 40f, 4f);
            go.isStatic = true;
        }
    }

    // tabla con punta: prisma de 5 lados extruido hacia afuera
    static void Tabla(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 b, Vector3 tan, Vector3 fuera, float w, float h, float g, float incl, float u0)
    {
        var up = (Vector3.up + tan * incl).normalized;
        Vector3[] perfil =
        {
            b - tan * w * 0.5f, b + tan * w * 0.5f, b + tan * w * 0.5f + up * h, b + up * (h + w * 0.45f), b - tan * w * 0.5f + up * h
        };
        float[] pu = { 0f, 1f, 1f, 0.5f, 0f };
        float[] pv = { 0f, 0f, h, h + w * 0.45f, h };
        Vector3 off = fuera * g;
        // cara interior (mira al centro) y exterior
        int baseI = v.Count;
        for (int i = 0; i < 5; i++) { v.Add(perfil[i]); uv.Add(new Vector2(u0 + pu[i] * 0.18f, pv[i] / 16f)); }
        t.AddRange(new[] { baseI, baseI + 4, baseI + 1, baseI + 1, baseI + 4, baseI + 2, baseI + 2, baseI + 4, baseI + 3 });
        baseI = v.Count;
        for (int i = 0; i < 5; i++) { v.Add(perfil[i] + off); uv.Add(new Vector2(u0 + pu[i] * 0.18f, pv[i] / 16f)); }
        t.AddRange(new[] { baseI, baseI + 1, baseI + 4, baseI + 1, baseI + 2, baseI + 4, baseI + 2, baseI + 3, baseI + 4 });
        // costados
        for (int i = 0; i < 5; i++)
        {
            int j = (i + 1) % 5;
            if (i == 0) continue;   // la base queda enterrada
            int s = v.Count;
            v.Add(perfil[i]); v.Add(perfil[j]); v.Add(perfil[j] + off); v.Add(perfil[i] + off);
            uv.Add(new Vector2(u0, pv[i] / 16f)); uv.Add(new Vector2(u0, pv[j] / 16f)); uv.Add(new Vector2(u0 + 0.03f, pv[j] / 16f)); uv.Add(new Vector2(u0 + 0.03f, pv[i] / 16f));
            t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
        }
    }

    // viga horizontal entre dos puntos (caja)
    static void Viga(List<Vector3> v, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, float alto, float grosor)
    {
        var d = (b - a); float len = d.magnitude; d /= len;
        var n = Vector3.Cross(Vector3.up, d).normalized;   // hacia el centro o fuera, da igual (caja cerrada)
        var u = Vector3.up * alto * 0.5f; var g = n * grosor * 0.5f;
        Vector3[] q =
        {
            a - u - g, b - u - g, b + u - g, a + u - g,
            a - u + g, b - u + g, b + u + g, a + u + g
        };
        int[][] caras = { new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 } };
        foreach (var c in caras)
        {
            int s = v.Count;
            for (int i = 0; i < 4; i++) v.Add(q[c[i]]);
            uv.Add(new Vector2(0.5f, 0)); uv.Add(new Vector2(0.5f, len / 16f)); uv.Add(new Vector2(0.58f, len / 16f)); uv.Add(new Vector2(0.58f, 0));
            t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });   // doble cara: barato y evita huecos
        }
    }

    // ------------------------------------------------------------------ vegetacion
    static VegetacionExterior.Variante[] Variantes(string fbx, System.Func<string, bool> filtro = null)
    {
        var mallas = AssetDatabase.LoadAllAssetsAtPath(Mod + fbx + ".fbx").OfType<Mesh>().ToList();
        var lista = new List<VegetacionExterior.Variante>();
        foreach (var m0 in mallas.Where(m => m.name.EndsWith("_LOD0")).OrderBy(m => m.name))
        {
            string b = m0.name.Substring(0, m0.name.Length - 5);
            if (filtro != null && !filtro(b)) continue;
            lista.Add(new VegetacionExterior.Variante { lod0 = m0, lod1 = mallas.FirstOrDefault(m => m.name == b + "_LOD1") });
        }
        if (lista.Count == 0) Debug.LogError("[Exterior] Sin mallas en " + fbx);
        return lista.ToArray();
    }

    // combina varias matas pequeñas en un "parche" para dibujar menos instancias
    static VegetacionExterior.Variante[] Parches(string nombre, string fbx, int cuantos, Vector2Int matas, float radio, Vector2 escala, int semilla, System.Func<string, bool> filtro = null)
    {
        var fuentes = Variantes(fbx, filtro);
        var rnd = new System.Random(semilla);
        var res = new List<VegetacionExterior.Variante>();
        for (int p = 0; p < cuantos; p++)
        {
            int n = rnd.Next(matas.x, matas.y + 1);
            var c0 = new List<CombineInstance>(); var c1 = new List<CombineInstance>();
            var tam = new List<float>();
            for (int i = 0; i < n; i++)
            {
                var f = fuentes[rnd.Next(fuentes.Length)];
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = radio * Mathf.Sqrt((float)rnd.NextDouble());
                float s = Mathf.Lerp(escala.x, escala.y, (float)rnd.NextDouble());
                var m = Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0), Vector3.one * s);
                c0.Add(new CombineInstance { mesh = f.lod0, transform = m });
                tam.Add(f.lod0.bounds.size.y * s);
                if (i % 2 == 0) c1.Add(new CombineInstance { mesh = f.lod1 != null ? f.lod1 : f.lod0, transform = m });
            }
            var m0 = new Mesh { name = nombre + "_" + p + "_LOD0" }; m0.CombineMeshes(c0.ToArray(), true, true);
            var m1 = new Mesh { name = nombre + "_" + p + "_LOD1" }; m1.CombineMeshes(c1.ToArray(), true, true);
            res.Add(new VegetacionExterior.Variante { lod0 = GuardarMalla(m0, m0.name), lod1 = GuardarMalla(m1, m1.name) });
        }
        return res.ToArray();
    }

    static VegetacionExterior.Prototipo P(string nombre, VegetacionExterior.Variante[] vars, Material[] mats, float dens, Vector2 esc, float lod, float max)
    {
        return new VegetacionExterior.Prototipo { nombre = nombre, variantes = vars, materiales = mats, densidad = dens, escala = esc, distanciaLod = lod, distanciaMax = max };
    }

    static VegetacionExterior CrearVegetacion(Transform padre)
    {
        var go = new GameObject("Vegetacion");
        go.transform.SetParent(padre, false);
        var veg = go.AddComponent<VegetacionExterior>();

        var quad = Quad();
        veg.quadImpostor = quad;
        var protos = new List<VegetacionExterior.Prototipo>();
        VegetacionExterior.Prototipo p;

        // --- cobertura del suelo (sin sombras, se adelgaza a lo lejos)
        var mPasto = MatFollaje("grass_bermuda_01", 0.07f, 0.4f, 0.28f);
        p = P("Pasto", Parches("Pasto", "grass_bermuda_01", 8, new Vector2Int(12, 18), 4f, new Vector2(18f, 32f), 11, b => !b.Contains("single") && !b.Contains("seedling")),
              new[] { mPasto }, 42f, new Vector2(0.8f, 1.3f), 40f, 150f);
        p.densidadLejos = 0.3f; p.agrupamiento = 0.3f; p.frecuenciaManchas = 0.01f; p.alinearAlSuelo = true; p.margenEdificio = 3f;
        protos.Add(p);
        p = P("PastoAlto", Parches("PastoAlto", "grass_bermuda_01", 4, new Vector2Int(6, 10), 3f, new Vector2(40f, 60f), 23, b => b.Contains("seedling") || b.Contains("medium")),
              new[] { mPasto }, 5f, new Vector2(0.85f, 1.25f), 35f, 120f);
        p.densidadLejos = 0.4f; p.agrupamiento = 0.75f; p.frecuenciaManchas = 0.018f; p.margenEdificio = 15f;
        protos.Add(p);
        p = P("Trebol", Parches("Trebol", "shrub_sorrel_01", 4, new Vector2Int(5, 9), 3.2f, new Vector2(60f, 95f), 31, b => b.EndsWith("_a") || b.EndsWith("_b") || b.EndsWith("_d") || b.EndsWith("_e")),
              new[] { MatFollaje("shrub_sorrel_01", 0.05f) }, 3.5f, new Vector2(0.8f, 1.25f), 40f, 100f);
        p.densidadLejos = 0.5f; p.agrupamiento = 0.75f; p.frecuenciaManchas = 0.02f; p.alinearAlSuelo = true;
        protos.Add(p);
        p = P("Musgo", Parches("Musgo", "moss_01", 3, new Vector2Int(10, 16), 3f, new Vector2(110f, 160f), 41),
              new[] { MatFollaje("moss_01", 0.0f, 0.2f, 0.25f) }, 2.5f, new Vector2(0.8f, 1.3f), 35f, 85f);
        p.densidadLejos = 0.5f; p.agrupamiento = 0.8f; p.frecuenciaManchas = 0.025f; p.alinearAlSuelo = true;
        protos.Add(p);
        p = P("Maleza", Variantes("weed_plant_02"), new[] { MatFollaje("weed_plant_02", 0.04f) }, 0.7f, new Vector2(22f, 34f), 45f, 115f);
        p.densidadLejos = 0.6f; p.agrupamiento = 0.4f; p.alinearAlSuelo = true;
        protos.Add(p);

        // --- flores (en manchas)
        p = P("DienteDeLeon", Variantes("dandelion_01"), new[] { MatFollaje("dandelion_01", 0.06f) }, 1.7f, new Vector2(28f, 42f), 30f, 140f);
        p.densidadLejos = 0.6f; p.agrupamiento = 0.6f; p.frecuenciaManchas = 0.02f;
        protos.Add(p);
        p = P("Gazania", Variantes("flower_gazania"), new[] { MatFollaje("flower_gazania", 0.06f) }, 1.3f, new Vector2(30f, 45f), 30f, 140f);
        p.densidadLejos = 0.6f; p.agrupamiento = 0.8f; p.frecuenciaManchas = 0.017f;
        protos.Add(p);
        p = P("Ursinia", Variantes("flower_ursinia"), new[] { MatFollaje("flower_ursinia", 0.06f) }, 1.0f, new Vector2(28f, 40f), 30f, 140f);
        p.densidadLejos = 0.6f; p.agrupamiento = 0.8f; p.frecuenciaManchas = 0.015f;
        protos.Add(p);
        p = P("Empodium", Variantes("flower_empodium"), new[] { MatFollaje("flower_empodium", 0.06f) }, 0.9f, new Vector2(30f, 45f), 30f, 140f);
        p.densidadLejos = 0.6f; p.agrupamiento = 0.8f; p.frecuenciaManchas = 0.022f;
        protos.Add(p);

        // --- plantas medianas
        p = P("Helecho", Variantes("fern_02"), new[] { MatFollaje("fern_02", 0.03f) }, 0.9f, new Vector2(14f, 24f), 45f, 210f);
        p.densidadLejos = 0.8f; p.agrupamiento = 0.5f;
        protos.Add(p);
        p = P("Ortiga", Variantes("nettle_plant"), new[] { MatFollaje("nettle_plant", 0.05f) }, 0.6f, new Vector2(38f, 62f), 50f, 150f);
        p.densidadLejos = 0.7f; p.agrupamiento = 0.6f;
        protos.Add(p);
        p = P("ArbustoBajo", Variantes("shrub_03"), new[] { MatFollaje("shrub_03", 0.035f) }, 0.55f, new Vector2(30f, 45f), 45f, 230f);
        p.agrupamiento = 0.3f;
        protos.Add(p);
        p = P("ArbustoAncho", Variantes("shrub_04"), new[] { MatFollaje("shrub_04", 0.03f) }, 0.3f, new Vector2(35f, 55f), 45f, 230f);
        p.agrupamiento = 0.3f;
        protos.Add(p);
        p = P("Arbusto", Variantes("shrub_02"), new[] { MatFollaje("shrub_02", 0.02f) }, 0.22f, new Vector2(12f, 18f), 50f, 380f);
        p.sombras = true; p.exclusion = 0.6f; p.margenEdificio = 12f;
        protos.Add(p);
        p = P("RamasSecas", Variantes("dry_branches_medium_01"), new[] { MatFollaje("dry_branches_medium_01", 0f, 0.1f, 0.15f, false) }, 0.25f, new Vector2(18f, 26f), 40f, 170f);
        p.hundir = 0.05f;
        protos.Add(p);

        // --- abetos (los "arboles" de un mundo diminuto), con impostor a lo lejos
        p = P("Abeto", Variantes("fir_sapling"),
              new[] { MatFollaje("fir_sapling_branches", 0.012f, 0.2f, 0.15f, false), MatFollaje("fir_sapling_twigs", 0.012f, 0.35f, 0.15f) },
              0.3f, new Vector2(28f, 46f), 40f, 150f);
        p.sombras = true; p.colision = VegetacionExterior.Colision.Capsula; p.exclusion = 0.25f; p.margenEdificio = 30f;
        p.impostor = MatLejano("ImpostorAbeto", Cargar<Texture2D>(Raiz + "/Impostores/fir_0.png"), true, false, 1f, Color.white);
        p.distanciaImpostor = 700f; p.tamImpostor = new Vector2(0.86f, 1.3f);
        protos.Add(p);

        // --- rocas, tocones y troncos (solidos)
        p = P("Roca", Variantes("rock_07"), new[] { MatSolido("rock_07") }, 0.16f, new Vector2(30f, 70f), 60f, 320f);
        p.sombras = true; p.rotacionLibre = true; p.hundir = 0.25f; p.colision = VegetacionExterior.Colision.Caja; p.exclusion = 0.9f; p.margenEdificio = 10f;
        protos.Add(p);
        p = P("Laja", Variantes("rock_09"), new[] { MatSolido("rock_09") }, 0.1f, new Vector2(60f, 120f), 50f, 220f);
        p.sombras = true; p.hundir = 0.3f; p.alinearAlSuelo = true; p.colision = VegetacionExterior.Colision.Caja; p.exclusion = 0.8f;
        protos.Add(p);
        p = P("Piedra", Variantes("stone_01"), new[] { MatSolido("stone_01") }, 0.18f, new Vector2(40f, 90f), 50f, 220f);
        p.sombras = true; p.rotacionLibre = true; p.hundir = 0.2f; p.colision = VegetacionExterior.Colision.Caja; p.exclusion = 0.9f;
        protos.Add(p);
        p = P("Peñasco", Variantes("namaqualand_boulder_05"), new[] { MatSolido("namaqualand_boulder_05") }, 0.08f, new Vector2(20f, 35f), 90f, 450f);
        p.sombras = true; p.hundir = 0.2f; p.colision = VegetacionExterior.Colision.Caja; p.exclusion = 0.9f; p.margenEdificio = 25f;
        protos.Add(p);
        p = P("Tocon", Variantes("tree_stump_01"), new[] { MatSolido("tree_stump_01") }, 0.06f, new Vector2(16f, 24f), 90f, 450f);
        p.sombras = true; p.hundir = 0.05f; p.colision = VegetacionExterior.Colision.Capsula; p.exclusion = 0.9f; p.margenEdificio = 25f;
        protos.Add(p);
        p = P("Tocon2", Variantes("tree_stump_02"), new[] { MatSolido("tree_stump_02") }, 0.06f, new Vector2(16f, 24f), 90f, 450f);
        p.sombras = true; p.hundir = 0.05f; p.colision = VegetacionExterior.Colision.Capsula; p.exclusion = 0.9f; p.margenEdificio = 25f;
        protos.Add(p);
        p = P("TroncoCaido", Variantes("dead_tree_trunk"), new[] { MatSolido("dead_tree_trunk") }, 0.06f, new Vector2(16f, 22f), 90f, 450f);
        p.sombras = true; p.hundir = 0.12f; p.colision = VegetacionExterior.Colision.Caja; p.exclusion = 0.8f; p.margenEdificio = 25f;
        protos.Add(p);

        veg.prototipos = protos.ToArray();
        veg.Generar();
        return veg;
    }

    static Mesh Quad()
    {
        var m = new Mesh { name = "QuadImpostor" };
        m.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 1, 0), new Vector3(-0.5f, 1, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(1, 1, 1));
        return GuardarMalla(m, "QuadImpostor");
    }

    static void CrearBosqueLejano(Transform padre)
    {
        var go = new GameObject("BosqueLejano");
        go.transform.SetParent(padre, false);
        var b = go.AddComponent<BosqueLejano>();
        b.quad = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "QuadImpostor.asset");
        string I = Raiz + "/Impostores/";
        b.tipos = new[]
        {
            new BosqueLejano.TipoArbol { material = MatLejano("Jacaranda0", Cargar<Texture2D>(I + "jacaranda_0.png"), true, false, 1f, Color.white), proporcion = 24.42f / 19.47f, altura = new Vector2(150f, 260f), frecuencia = 1f },
            new BosqueLejano.TipoArbol { material = MatLejano("Jacaranda90", Cargar<Texture2D>(I + "jacaranda_90.png"), true, false, 1f, Color.white), proporcion = 19.15f / 19.47f, altura = new Vector2(140f, 250f), frecuencia = 1f },
            new BosqueLejano.TipoArbol { material = MatLejano("Abeto0", Cargar<Texture2D>(I + "fir_0.png"), true, false, 1f, Color.white), proporcion = 0.86f / 1.3f, altura = new Vector2(120f, 210f), frecuencia = 0.8f },
            new BosqueLejano.TipoArbol { material = MatLejano("Abeto90", Cargar<Texture2D>(I + "fir_90.png"), true, false, 1f, Color.white), proporcion = 0.82f / 1.3f, altura = new Vector2(110f, 200f), frecuencia = 0.8f },
        };
        b.cantidad = 520;
        b.radioMin = 455f; b.radioMax = 1500f; b.concentracion = 0.85f; b.claros = 0.75f;
        b.Generar();
    }

    // ------------------------------------------------------------------ bichos
    static void CrearBichos(Transform padre, VegetacionExterior veg)
    {
        var raiz = new GameObject("Bichos").transform; raiz.SetParent(padre, false);
        var solidos = veg.PosicionesSolidas();
        var usados = new List<Vector3>();
        var rnd = new System.Random(99);
        var cuantos = new Dictionary<string, int>
        {
            { "Enemy_Abeja", 5 }, { "Enemy_Mariposa", 6 }, { "Enemy_Escarabajo", 4 }, { "Enemy_Hormigas", 5 },
            { "Enemy_Ciempies", 4 }, { "Enemy_Cochinilla", 4 }, { "Enemy_Lombriz", 4 }, { "Enemy_Oruga", 4 },
        };
        var sonidos = Object.FindObjectsByType<AnimalSound>(FindObjectsInactive.Include);

        foreach (var kv in cuantos)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemies/Prefabs/" + kv.Key + ".prefab");
            if (prefab == null) { Debug.LogWarning("[Exterior] Falta prefab " + kv.Key); continue; }
            var kind = prefab.GetComponent<CreatureAI>().kind;
            var molde = sonidos.FirstOrDefault(s => s.GetComponent<CreatureAI>() != null && s.GetComponent<CreatureAI>().kind == kind);
            for (int i = 0; i < kv.Value; i++)
            {
                if (!PosicionLibre(rnd, solidos, usados, out var pos)) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, raiz);
                go.name = kv.Key.Replace("Enemy_", "Exterior_") + "_" + (i + 1);
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0));
                if (molde != null)
                {
                    var s = go.GetComponent<AnimalSound>();
                    if (s == null) s = go.AddComponent<AnimalSound>();
                    EditorUtility.CopySerialized(molde, s);
                }
                usados.Add(pos);
            }
        }

        // arañas: copias de la del salon
        var arana = Object.FindObjectsByType<SpiderAI>(FindObjectsInactive.Include).FirstOrDefault(a => !a.transform.IsChildOf(padre));
        if (arana != null)
            for (int i = 0; i < 4; i++)
            {
                if (!PosicionLibre(rnd, solidos, usados, out var pos)) continue;
                // Instantiate copia la araña con sus referencias internas (animator, sonido...) ya reasignadas
                var go = Object.Instantiate(arana.gameObject, raiz);
                go.name = "Exterior_Arana_" + (i + 1);
                pos.y += arana.transform.position.y - ZonaJuego.alturaPiso;
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0));
                usados.Add(pos);
            }
    }

    static bool PosicionLibre(System.Random rnd, List<Vector3> solidos, List<Vector3> usados, out Vector3 pos)
    {
        Vector3 c = ZonaJuego.centro;
        for (int intento = 0; intento < 400; intento++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Lerp(60f, ZonaJuego.radioCerca - 30f, Mathf.Sqrt((float)rnd.NextDouble()));
            float x = c.x + Mathf.Cos(a) * r, z = c.z + Mathf.Sin(a) * r;
            if (ZonaJuego.DistanciaEdificio(x, z) < 35f) continue;
            if (ZonaJuego.DistanciaHormiguero(x, z) < ZonaJuego.hormigueroRadio + 25f) continue;
            if (ZonaJuego.DistanciaArbol(x, z) < ZonaJuego.arbolRadio + 30f) continue;
            bool ok = true;
            foreach (var s in solidos) { float dx = s.x - x, dz = s.z - z, rr = s.y + 6f; if (dx * dx + dz * dz < rr * rr) { ok = false; break; } }
            if (!ok) continue;
            // repartidos: lejos de los demas bichos
            float sep = intento < 200 ? 55f : 25f;
            foreach (var u in usados) if ((u.x - x) * (u.x - x) + (u.z - z) * (u.z - z) < sep * sep) { ok = false; break; }
            if (!ok) continue;
            pos = new Vector3(x, ZonaJuego.Altura(x, z), z);
            return true;
        }
        pos = Vector3.zero;
        return false;
    }

    // ------------------------------------------------------------------ salon, niebla y camara
    static void AjustarSalonYCamara()
    {
        // las puertas no tenian colision: la cerrada bloquea, la abierta deja el paso libre a su lado
        foreach (var n in new[] { "class/Door_1", "class/Door_2" })
        {
            var d = GameObject.Find(n);
            if (d != null && d.GetComponent<Collider>() == null) d.AddComponent<BoxCollider>();
        }

        var dn = Object.FindAnyObjectByType<DayNightCycle>();
        if (dn != null)
        {
            Undo.RecordObject(dn, "Niebla exterior");
            dn.densidadNieblaDia = 0.0018f;     // menos niebla de dia para ver el bosque y las montañas
            dn.densidadNieblaNoche = 0.0065f;
            EditorUtility.SetDirty(dn);
        }

        // la oclusion horneada solo cubre el salon: fuera de ese volumen ocultaba el paisaje lejano
        var cam = Camera.main;
        if (cam != null) { cam.farClipPlane = 6000f; cam.useOcclusionCulling = false; EditorUtility.SetDirty(cam); }
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb.GetType().FullName != "Unity.Cinemachine.CinemachineCamera") continue;
            var so = new SerializedObject(mb);
            var far = so.FindProperty("Lens.FarClipPlane");
            if (far != null) { far.floatValue = 6000f; so.ApplyModifiedProperties(); }
        }
    }
}
