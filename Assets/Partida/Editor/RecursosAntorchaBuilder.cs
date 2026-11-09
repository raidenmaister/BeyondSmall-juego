using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Herramientas > Antorchas > Colocar carbon y palos: crea los objetos recogibles "Carbon" y "Palo"
/// (ingredientes de la antorcha: Carbon + Palo en CREAR ARMA) y los reparte por el salon y el exterior.
/// Borra y vuelve a crear los que hubiera. Tambien registra los iconos de Carbon, Palo y Antorcha.
/// </summary>
public static class RecursosAntorchaBuilder
{
    const string Raiz = "Assets/Partida/";
    const string Gen = Raiz + "Generado/";
    const string Spr = Raiz + "Sprites/";

    [MenuItem("Herramientas/Antorchas/Colocar carbon y palos")]
    public static void Construir()
    {
        foreach (var c in new[] { "Generado", "Sprites" }) if (!AssetDatabase.IsValidFolder(Raiz + c)) AssetDatabase.CreateFolder(Raiz.TrimEnd('/'), c);
        foreach (var wi in Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include))
            if (wi != null && (wi.itemId == "Carbon" || wi.itemId == "Palo")) Object.DestroyImmediate(wi.gameObject);

        var mCarbon = Guardar(MallaCarbon(), "Carbon");
        var mPalo = Guardar(MallaPalo(), "Palo");
        var matCarbon = Mat("Carbon", new Color(0.07f, 0.065f, 0.065f), 0.45f);
        var matPalo = Mat("Palo", new Color(0.38f, 0.26f, 0.15f), 0.12f);
        var iCarbon = Icono("Carbon", PintarCarbon);
        var iPalo = Icono("Palo", PintarPalo);
        // el icono de la antorcha estaba importado como "Multiple" sin sprites: ningun objeto tenia icono
        const string rutaAntorcha = "Assets/Colmena/Sprites/Antorcha.png";
        var tiA = AssetImporter.GetAtPath(rutaAntorcha) as TextureImporter;
        if (tiA != null && tiA.spriteImportMode != SpriteImportMode.Single)
        {
            tiA.textureType = TextureImporterType.Sprite; tiA.spriteImportMode = SpriteImportMode.Single; tiA.alphaIsTransparency = true; tiA.SaveAndReimport();
        }
        var iAntorcha = AssetDatabase.LoadAssetAtPath<Sprite>(rutaAntorcha);
        foreach (var wi in Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include))
            if (wi.itemId == "Antorcha" && wi.itemIcon == null) { wi.itemIcon = iAntorcha; EditorUtility.SetDirty(wi); }

        var rnd = new System.Random(2026);
        var usados = new List<Vector3>();
        int nc = 0, np = 0;
        // carbon: junto a la fogata del salon (de ahi sale), alguno mas en el salon y afuera
        var fog = GameObject.Find("Fogata");
        if (fog != null)
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                Vector3 p = fog.transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (5f + i);
                Crear("Carbon", nc++, Piso(p), mCarbon, matCarbon, iCarbon, rnd, usados);
            }
        for (int i = 0; i < 2; i++) { var p = PuntoSalon(rnd, usados); if (p.HasValue) Crear("Carbon", nc++, p.Value, mCarbon, matCarbon, iCarbon, rnd, usados); }
        for (int i = 0; i < 6; i++) { var p = PuntoExterior(rnd, usados); if (p.HasValue) Crear("Carbon", nc++, p.Value, mCarbon, matCarbon, iCarbon, rnd, usados); }
        // palos: ramitas en el salon (el techo roto deja caer ramas) y muchas afuera
        for (int i = 0; i < 4; i++) { var p = PuntoSalon(rnd, usados); if (p.HasValue) Crear("Palo", np++, p.Value, mPalo, matPalo, iPalo, rnd, usados); }
        for (int i = 0; i < 10; i++) { var p = PuntoExterior(rnd, usados); if (p.HasValue) Crear("Palo", np++, p.Value, mPalo, matPalo, iPalo, rnd, usados); }

        // iconos en la base de datos del inventario
        var inv = Object.FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (inv != null)
        {
            if (inv.iconDatabase == null) inv.iconDatabase = new List<Sprite>();
            foreach (var s in new[] { iCarbon, iPalo, iAntorcha })
            {
                if (s == null) continue;
                inv.iconDatabase.RemoveAll(x => x == null || x.name == s.name);
                inv.iconDatabase.Add(s);
            }
            EditorUtility.SetDirty(inv);
        }
        PrepararEscudo();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log($"[RecursosAntorcha] {nc} carbones y {np} palos colocados");
    }

    /// <summary>Icono del escudo (Exoesqueleto + Ligas o Borrador + Ligas) y el componente Escudo en el jugador.</summary>
    [MenuItem("Herramientas/Antorchas/Preparar escudo")]
    public static void PrepararEscudo()
    {
        if (!AssetDatabase.IsValidFolder(Raiz + "Sprites")) AssetDatabase.CreateFolder(Raiz.TrimEnd('/'), "Sprites");
        var icono = Icono("Escudo", PintarEscudo);
        var inv = Object.FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (inv != null && icono != null)
        {
            inv.iconDatabase.RemoveAll(x => x == null || x.name == icono.name);
            inv.iconDatabase.Add(icono);
            EditorUtility.SetDirty(inv);
        }
        var pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (pc != null && pc.GetComponent<Escudo>() == null) { pc.gameObject.AddComponent<Escudo>(); EditorUtility.SetDirty(pc.gameObject); }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[RecursosAntorcha] escudo listo");
    }

    static Color PintarEscudo(float u, float v)
    {
        // caparazon redondo visto de frente, con surco central, brillo y una liga negra
        float x = (u - 0.5f) / 0.42f, y = (v - 0.5f) / 0.42f;
        float d = Mathf.Sqrt(x * x + y * y);
        if (d > 1f) return Color.clear;
        float z = Mathf.Sqrt(Mathf.Max(0f, 1f - d * d));
        float luz = Mathf.Clamp01(0.35f + 0.65f * (z * 0.6f + (-x * 0.3f + y * 0.4f)));
        Color c = Color.Lerp(new Color(0.08f, 0.06f, 0.04f), new Color(0.45f, 0.33f, 0.2f), luz);
        if (d > 0.88f) c = new Color(0.5f, 0.36f, 0.2f);
        if (Mathf.Abs(x) < 0.05f && d < 0.88f) c *= 0.55f;
        float brillo = Mathf.Clamp01(1f - Vector2.Distance(new Vector2(x, y), new Vector2(-0.35f, 0.4f)) / 0.25f);
        c = Color.Lerp(c, new Color(1f, 0.95f, 0.85f), brillo * 0.7f);
        if (Mathf.Abs(y + 0.15f) < 0.07f && d < 0.96f) c = new Color(0.05f, 0.05f, 0.05f);
        c.a = 1f;
        return c;
    }

    // ------------------------------------------------------------------ lugares
    static Vector3? PuntoExterior(System.Random rnd, List<Vector3> usados)
    {
        for (int k = 0; k < 200; k++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f, r = Mathf.Lerp(70f, 370f, Mathf.Sqrt((float)rnd.NextDouble()));
            float x = ZonaJuego.centro.x + Mathf.Cos(a) * r, z = ZonaJuego.centro.z + Mathf.Sin(a) * r;
            var p = new Vector3(x, 0f, z);
            if (ZonaJuego.EnEdificio(p, 10f)) continue;
            if (ZonaJuego.DistanciaHormiguero(x, z) < ZonaJuego.hormigueroRadio + 15f) continue;
            if (ZonaJuego.DistanciaArbol(x, z) < ZonaJuego.arbolRadio + 15f) continue;
            if (Lejos(p, usados, 45f)) { p.y = ZonaJuego.Altura(x, z); return p; }
        }
        return null;
    }

    static Vector3? PuntoSalon(System.Random rnd, List<Vector3> usados)
    {
        var maq = GameObject.Find("MAQUINA");
        for (int k = 0; k < 200; k++)
        {
            float x = Mathf.Lerp(ZonaJuego.salonMin.x + 8f, ZonaJuego.salonMax.x - 8f, (float)rnd.NextDouble());
            float z = Mathf.Lerp(ZonaJuego.salonMin.y + 8f, ZonaJuego.salonMax.y - 8f, (float)rnd.NextDouble());
            var p = new Vector3(x, 0f, z);
            if (maq != null && Vector3.Distance(new Vector3(maq.transform.position.x, 0, maq.transform.position.z), p) < 34f) continue;
            if (!Lejos(p, usados, 25f)) continue;
            return Piso(p);
        }
        return null;
    }

    static bool Lejos(Vector3 p, List<Vector3> usados, float d)
    {
        foreach (var u in usados) if (new Vector2(u.x - p.x, u.z - p.z).magnitude < d) return false;
        return true;
    }

    static Vector3 Piso(Vector3 p)
    {
        if (Physics.Raycast(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return new Vector3(p.x, ZonaJuego.alturaPiso, p.z);
    }

    static void Crear(string id, int n, Vector3 pos, Mesh malla, Material mat, Sprite icono, System.Random rnd, List<Vector3> usados)
    {
        var go = new GameObject(id + "_" + n);
        go.transform.SetPositionAndRotation(pos + Vector3.up * 0.02f, Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
        var vis = new GameObject("Modelo");
        vis.transform.SetParent(go.transform, false);
        vis.AddComponent<MeshFilter>().sharedMesh = malla;
        vis.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var wi = go.AddComponent<WorldItem>();
        wi.itemId = id; wi.displayName = id == "Carbon" ? "Carbon" : "Palo"; wi.itemIcon = icono; wi.quantity = 1;
        var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true;
        bc.center = new Vector3(0f, 0.4f, 0f); bc.size = id == "Carbon" ? new Vector3(1.1f, 1f, 1.1f) : new Vector3(2.4f, 0.8f, 0.8f);
        var sp = go.AddComponent<ItemSparkle>(); sp.color = id == "Carbon" ? new Color(1f, 0.6f, 0.3f, 1f) : new Color(1f, 0.95f, 0.6f, 1f);
        usados.Add(pos);
    }

    // ------------------------------------------------------------------ mallas
    static Mesh MallaCarbon()
    {
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var m = Object.Instantiate(tmp.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(tmp);
        var v = m.vertices;
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = v[i];
            // terrones: ruido por posicion (las costuras quedan iguales) y caras planas como carbon partido
            float n = Mathf.PerlinNoise(p.x * 3.1f + 5f, p.z * 3.1f + p.y * 2.3f) * 0.35f + Mathf.PerlinNoise(p.y * 7f + 11f, p.x * 7f) * 0.12f;
            p *= 0.85f + n;
            p.x *= 0.78f; p.z *= 0.62f; p.y *= 0.5f;
            p = new Vector3(Mathf.Round(p.x * 9f) / 9f * 0.3f + p.x * 0.7f, p.y, Mathf.Round(p.z * 9f) / 9f * 0.3f + p.z * 0.7f);
            if (p.y < -0.12f) p.y = -0.12f;      // base plana
            p.y += 0.12f;
            v[i] = p;
        }
        m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds(); m.name = "Carbon";
        return m;
    }

    static Mesh MallaPalo()
    {
        var v = new List<Vector3>(); var t = new List<int>();
        void Rama(Vector3 a, Vector3 b, Vector3 curva, float r0, float r1, int seg)
        {
            int lados = 7, b0 = v.Count;
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg;
                Vector3 c = Vector3.Lerp(a, b, u) + curva * Mathf.Sin(u * Mathf.PI);
                Vector3 dir = (b - a).normalized;
                Vector3 n1 = Vector3.Cross(dir, Vector3.up).normalized; if (n1.sqrMagnitude < 0.1f) n1 = Vector3.right;
                Vector3 n2 = Vector3.Cross(dir, n1);
                float r = Mathf.Lerp(r0, r1, u) * (1f + 0.15f * Mathf.Sin(u * 23f));
                for (int k = 0; k < lados; k++) { float ang = k * Mathf.PI * 2f / lados; v.Add(c + (n1 * Mathf.Cos(ang) + n2 * Mathf.Sin(ang)) * r); }
            }
            for (int i = 0; i < seg; i++)
                for (int k = 0; k < lados; k++)
                {
                    int p0 = b0 + i * lados + k, p1 = b0 + i * lados + (k + 1) % lados, p2 = p0 + lados, p3 = p1 + lados;
                    t.AddRange(new[] { p0, p2, p1, p1, p2, p3 });
                }
            // tapas
            int ca = v.Count; v.Add(a); int cb = v.Count; v.Add(b);
            for (int k = 0; k < lados; k++) { t.AddRange(new[] { ca, b0 + (k + 1) % lados, b0 + k }); int e = b0 + seg * lados; t.AddRange(new[] { cb, e + k, e + (k + 1) % lados }); }
        }
        float y = 0.11f;
        Rama(new Vector3(-1.1f, y, 0f), new Vector3(1.1f, y, 0.05f), new Vector3(0f, 0.04f, 0.12f), 0.11f, 0.07f, 10);
        Rama(new Vector3(0.25f, y, 0.08f), new Vector3(0.75f, y + 0.05f, 0.42f), new Vector3(0.03f, 0.02f, 0f), 0.05f, 0.025f, 4);
        var m = new Mesh { name = "Palo" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh Guardar(Mesh m, string n)
    {
        string p = Gen + n + ".asset";
        NormalesSeguras.Sanear(m);   // normales nulas = NaN = destellos del Bloom
        if (AssetDatabase.LoadAssetAtPath<Mesh>(p) != null) AssetDatabase.DeleteAsset(p);
        AssetDatabase.CreateAsset(m, p);
        return m;
    }

    static Material Mat(string n, Color c, float suave)
    {
        string p = Gen + n + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", suave); m.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ iconos
    static Sprite Icono(string n, System.Func<float, float, Color> pintar)
    {
        string ruta = Spr + n + ".png";
        int s = 128; var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
        var px = new Color[s * s];
        for (int y = 0; y < s; y++) for (int x = 0; x < s; x++) px[y * s + x] = pintar(x / (float)s, y / (float)s);
        t.SetPixels(px); t.Apply();
        File.WriteAllBytes(ruta, t.EncodeToPNG()); Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(ruta);
        ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(ruta);
    }

    static Color PintarCarbon(float u, float v)
    {
        // terron oscuro con facetas y brillos grises
        float cx = (u - 0.5f) / 0.36f, cy = (v - 0.45f) / 0.27f;
        float borde = Mathf.PerlinNoise(u * 9f, v * 9f) * 0.3f;
        float d = cx * cx + cy * cy;
        if (d > 1f + borde) return Color.clear;
        float faceta = Mathf.Floor(Mathf.PerlinNoise(u * 6f + 3f, v * 6f) * 4f) / 4f;
        float luz = Mathf.Clamp01(0.5f - (cx * 0.25f + cy * -0.35f));
        Color c = Color.Lerp(new Color(0.05f, 0.05f, 0.055f), new Color(0.32f, 0.31f, 0.3f), faceta * 0.55f + luz * 0.3f);
        if (d > 0.82f + borde) c = new Color(0.02f, 0.02f, 0.02f);
        // brasas tenues
        if (Mathf.PerlinNoise(u * 22f, v * 22f) > 0.78f) c = Color.Lerp(c, new Color(0.9f, 0.35f, 0.08f), 0.6f);
        c.a = 1f;
        return c;
    }

    static Color PintarPalo(float u, float v)
    {
        // rama diagonal con una ramita
        float d1 = Mathf.Abs((v - u) * 0.7071f);
        float largo = (u + v) * 0.7071f;
        float grosor = Mathf.Lerp(0.07f, 0.045f, u);
        bool tronco = d1 < grosor && largo > 0.15f && largo < 1.27f;
        float d2 = Mathf.Abs((v - 0.55f) - (u - 0.48f) * -1.6f) / 1.9f;
        bool ramita = d2 < 0.025f && u > 0.48f && u < 0.66f && v < 0.56f && v > 0.27f;
        if (!tronco && !ramita) return Color.clear;
        float corteza = Mathf.PerlinNoise(largo * 30f, d1 * 40f);
        Color c = Color.Lerp(new Color(0.3f, 0.2f, 0.11f), new Color(0.55f, 0.4f, 0.24f), corteza);
        if (tronco && d1 > grosor * 0.75f) c *= 0.6f;
        c.a = 1f;
        return c;
    }
}
