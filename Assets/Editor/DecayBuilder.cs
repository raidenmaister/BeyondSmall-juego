using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Convierte el salón en una ruina abandonada y cubierta de vegetación (50 años después).
/// Todo lo generado cuelga de "DECAY_ROOT" y se reconstruye desde cero en cada ejecución.
/// </summary>
public static class DecayBuilder
{
    const string Dir = "Assets/Decay";
    const float FloorY = 0f;
    // zona transitable del salón (mundo)
    const float X0 = -71f, X1 = 71f, Z0 = -94f, Z1 = 82f;
    const float WallL = -73.2f, WallR = 74.2f, WallB = -96.8f, WallF = 85.2f;
    const float CeilY = 53.6f;

    static System.Random rng;
    static Transform root;
    static StringBuilder log;
    static List<Vector3> keepClear;
    static readonly Dictionary<string, GameObject> modelCache = new Dictionary<string, GameObject>();

    [MenuItem("Tools/Decay/Reconstruir salon en ruinas")]
    public static void Run()
    {
        var sb = new StringBuilder();
        Build(sb);
        Debug.Log("[DecayBuilder] " + sb);
    }

    static float R() { return (float)rng.NextDouble(); }
    static float R(float a, float b) { return a + (b - a) * R(); }
    static T Pick<T>(IList<T> l) { return l[rng.Next(l.Count)]; }

    // ------------------------------------------------------------------ materiales
    static Texture2D Tex(string rel, bool normal = false)
    {
        string path = $"{Dir}/Textures/{rel}";
        if (normal)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null && imp.textureType != TextureImporterType.NormalMap)
            {
                imp.textureType = TextureImporterType.NormalMap;
                imp.SaveAndReimport();
            }
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Mat(string name, string folder, Vector2 tile, Color tint, float smooth = 0.15f, bool useNormal = true)
    {
        Directory.CreateDirectory($"{Dir}/Materials");
        string path = $"{Dir}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = Shader.Find("Universal Render Pipeline/Lit");
        var d = Tex($"{folder}/diff.jpg");
        m.SetTexture("_BaseMap", d);
        m.SetTextureScale("_BaseMap", tile);
        m.SetColor("_BaseColor", tint);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", 0f);
        if (useNormal)
        {
            var n = Tex($"{folder}/nor.jpg", true);
            m.SetTexture("_BumpMap", n);
            m.SetTextureScale("_BumpMap", tile);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material PatchMat(string name, Texture2D tex)
    {
        string path = $"{Dir}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = Shader.Find("Universal Render Pipeline/Lit");
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Smoothness", 0.1f);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_BlendModePreserveSpecular", 0f); // evita SrcBlend=One + premultiplicado (cuadros claros)
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetFloat("_SrcBlend", 5f);
        m.SetFloat("_DstBlend", 10f);
        m.SetFloat("_SrcBlendAlpha", 1f);
        m.SetFloat("_DstBlendAlpha", 10f);
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
        return m;
    }

    // textura de parche con borde irregular (alfa) a partir de una textura fuente
    static Texture2D MakePatch(string name, string srcFolder, int seed, Color tint, float coverage)
    {
        Directory.CreateDirectory($"{Dir}/Generated");
        string outPath = $"{Dir}/Generated/{name}.png";
        if (!File.Exists(outPath))
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (srcFolder != null) src.LoadImage(File.ReadAllBytes($"{Dir}/Textures/{srcFolder}/diff.jpg"));
            const int N = 512;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            float ox = seed * 17.3f, oy = seed * 31.7f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f; // 0 centro, 1 borde
                    float n = Mathf.PerlinNoise(ox + u * 5f, oy + v * 5f) * 0.55f
                            + Mathf.PerlinNoise(ox + u * 13f, oy + v * 13f) * 0.30f
                            + Mathf.PerlinNoise(ox + u * 31f, oy + v * 31f) * 0.15f;
                    float a = Mathf.Clamp01((coverage - d + (n - 0.5f) * 0.9f) * 6f);
                    Color c = srcFolder != null ? src.GetPixelBilinear(u * 2f, v * 2f) * tint : tint;
                    c.a = a * tint.a;
                    px[y * N + x] = c;
                }
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(outPath, t.EncodeToPNG());
            Object.DestroyImmediate(t); Object.DestroyImmediate(src);
            AssetDatabase.ImportAsset(outPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(outPath);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = 512;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
    }

    // ------------------------------------------------------------------ helpers de escena
    static GameObject Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material m, Transform parent = null, bool collide = false)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = name;
        if (!collide) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent ? parent : root, false);
        go.transform.position = pos; go.transform.rotation = rot; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    static GameObject Model(string id)
    {
        if (modelCache.TryGetValue(id, out var g)) return g;
        string file = Directory.GetFiles($"{Dir}/Models/{id}", "*.gltf").FirstOrDefault();
        g = file == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\', '/'));
        if (g == null) log.AppendLine("modelo no disponible: " + id);
        modelCache[id] = g;
        return g;
    }

    static bool NearItem(Vector3 p, float r)
    {
        foreach (var k in keepClear) if ((k.x - p.x) * (k.x - p.x) + (k.z - p.z) * (k.z - p.z) < r * r) return true;
        return false;
    }

    // coloca un modelo escalado a una altura (o dimensión mayor) objetivo, apoyado en el suelo
    static GameObject Place(string id, Vector3 pos, float target, float yaw, Vector3 tilt = default, bool byHeight = true, Transform parent = null, bool collide = false)
    {
        var prefab = Model(id);
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(parent ? parent : root, false);
        go.transform.rotation = Quaternion.Euler(tilt.x, yaw, tilt.z);
        go.transform.position = Vector3.zero;
        go.transform.localScale = Vector3.one;
        var b = RendBounds(go);
        float dim = byHeight ? b.size.y : Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (dim < 1e-4f) { Object.DestroyImmediate(go); return null; }
        go.transform.localScale = Vector3.one * (target / dim);
        b = RendBounds(go);
        go.transform.position = new Vector3(pos.x, pos.y - b.min.y, pos.z) - new Vector3(b.center.x, 0, b.center.z) + new Vector3(0, 0, 0);
        foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
        if (collide) AddBoxCollider(go);
        return go;
    }

    // caja de colisión ajustada (en espacio local, así sigue la rotación del objeto)
    static void AddBoxCollider(GameObject go)
    {
        Bounds lb = default; bool first = true;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var l = go.transform.InverseTransformPoint(r.transform.TransformPoint(c));
                if (first) { lb = new Bounds(l, Vector3.zero); first = false; } else lb.Encapsulate(l);
            }
        }
        if (first) return;
        var bc = go.AddComponent<BoxCollider>();
        bc.center = lb.center; bc.size = lb.size;
    }

    static Bounds RendBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Vector3 RandFloor(float margin = 0f)
    {
        return new Vector3(R(X0 + margin, X1 - margin), FloorY, R(Z0 + margin, Z1 - margin));
    }

    // ------------------------------------------------------------------ construcción
    public static void Build(StringBuilder outLog)
    {
        log = outLog;
        rng = new System.Random(5050);
        modelCache.Clear();
        var scene = SceneManager.GetActiveScene();

        var old = GameObject.Find("DECAY_ROOT");
        if (old != null) Object.DestroyImmediate(old);
        root = new GameObject("DECAY_ROOT").transform;

        // objetos pequeños del juego (ítems recogibles) que no debemos tapar
        keepClear = new List<Vector3>();
        foreach (var r in scene.GetRootGameObjects())
        {
            if (r == root.gameObject || r.name == "class" || r.name.Contains("Camera") || r.name.Contains("Light") || r.GetComponentInChildren<Canvas>() != null) continue;
            var rs = r.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            if (Mathf.Max(b.size.x, b.size.z) < 12f) keepClear.Add(b.center);
        }
        log.AppendLine("zonas a respetar: " + keepClear.Count);

        AssetDatabase.StartAssetEditing();
        AssetDatabase.StopAssetEditing();

        // ----- materiales base
        var wallMat = Mat("Decay_Wall", "worn_mossy_plasterwall", new Vector2(0.5f, 0.5f), new Color(0.78f, 0.78f, 0.7f), 0.1f);
        var wallMat2 = Mat("Decay_WallPeel", "damaged_plaster", new Vector2(0.5f, 0.5f), new Color(0.7f, 0.7f, 0.66f), 0.1f);
        var floorMat = Mat("Decay_Floor", "old_wooden_floor_02", new Vector2(0.7f, 0.7f), new Color(0.55f, 0.5f, 0.42f), 0.15f);
        var plankMat = Mat("Decay_Plank", "weathered_planks", new Vector2(1.5f, 0.3f), new Color(0.6f, 0.55f, 0.5f), 0.1f);
        var beamMat = Mat("Decay_Beam", "rusty_metal_02", new Vector2(6f, 0.6f), new Color(0.38f, 0.31f, 0.27f), 0.2f);
        var metalMat = Mat("Decay_RustMetal", "rusty_metal_02", new Vector2(1f, 1f), new Color(0.5f, 0.4f, 0.34f), 0.2f);
        var ceilMat = Mat("Decay_CeilPanel", "damaged_plaster", new Vector2(1f, 1f), new Color(0.65f, 0.62f, 0.55f), 0.05f);

        var glassPath = $"{Dir}/Materials/Decay_Glass.mat";
        var glass = AssetDatabase.LoadAssetAtPath<Material>(glassPath);
        if (glass == null) { glass = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(glass, glassPath); }
        glass.SetFloat("_Surface", 1f); glass.SetFloat("_SrcBlend", 5f); glass.SetFloat("_DstBlend", 10f); glass.SetFloat("_ZWrite", 0f);
        glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glass.SetOverrideTag("RenderType", "Transparent"); glass.renderQueue = 3000;
        glass.SetColor("_BaseColor", new Color(0.7f, 0.8f, 0.75f, 0.06f)); glass.SetFloat("_Smoothness", 0.9f);
        EditorUtility.SetDirty(glass);

        // ----- el salón: paredes, suelo, ventanas, mobiliario existente
        var cls = GameObject.Find("class");
        var room = cls.transform.Find("Room").GetComponent<Renderer>();
        room.sharedMaterials = new[] { wallMat, ceilMat, floorMat };

        foreach (var r in cls.GetComponentsInChildren<Renderer>())
        {
            if (r.name == "Room") continue;
            var ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                if (ms[i] == null) continue;
                string n = ms[i].name;
                if (n == "Glass") ms[i] = glass;
                else if (n == "Metal_Rough" || n == "school_metal_cabinet" || n == "Decay_RustMetal") ms[i] = metalMat;
            }
            r.sharedMaterials = ms;
        }
        var ceiling = cls.transform.Find("Cube");
        if (ceiling) ceiling.GetComponent<Renderer>().enabled = false;

        // luminarias colgando torcidas
        var l1 = cls.transform.Find("Light (1)"); if (l1) { if (l1.Find("__decay") == null) { new GameObject("__decay").transform.SetParent(l1, false); l1.Rotate(0, 0, 14f, Space.World); l1.position += new Vector3(0, -6f, 0); } }

        // ----- atmósfera
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.0042f;
        RenderSettings.fogColor = new Color(0.56f, 0.64f, 0.55f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.62f, 0.72f, 0.64f);
        RenderSettings.ambientEquatorColor = new Color(0.42f, 0.48f, 0.38f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.2f, 0.14f);
        RenderSettings.ambientIntensity = 1.1f;
        RenderSettings.reflectionIntensity = 0.12f;
        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun)
        {
            sun.transform.rotation = Quaternion.Euler(58, -35, 0);
            sun.color = new Color(1f, 0.93f, 0.78f);
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
        }

        RemoveOutside();
        BuildCeiling(beamMat, ceilMat);
        BuildCeilingSlab(ceilMat);
        BuildPatches(wallMat2);
        BuildPlanks(plankMat);
        BuildFurniture();
        BuildVegetation();
        BuildTrash();
        BuildLights();

        Optimize(cls);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine("hijos de DECAY_ROOT: " + root.childCount);
    }

    // optimización: estático para batching, sombras solo donde importan, sin objetos invisibles
    static void Optimize(GameObject cls)
    {
        var batch = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            string n = t.name;
            if (n == "WallPatch" || n == "FloorPatch" || n == "Paper" || n.StartsWith("Plank") || n == "LeaningPlank")
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, batch);
            else if (n == "CeilPanel" || n == "Beam" || n == "Joist" || n == "CeilSlab") // sin occludee: el occlusion bakeado viejo los ocultaba
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
        }
        // plantas pequeñas y papeles no proyectan sombra; el resto sí
        int shadowOff = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            var size = r.bounds.size;
            if (Mathf.Max(size.x, size.y, size.z) < 6f || r.name == "Paper") { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; shadowOff++; }
            r.allowOcclusionWhenDynamic = true;
        }
        foreach (var r in cls.GetComponentsInChildren<Renderer>())
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, batch);
        // el ceiling original está desactivado: se elimina para no cargarlo
        var cube = cls.transform.Find("Cube");
        log.AppendLine("sombras desactivadas en " + shadowOff + " renderers");
    }

    static Transform Group(string n) { var g = new GameObject(n).transform; g.SetParent(root, false); return g; }

    // el personaje no sale del salón: se borra todo lo que quede fuera de él
    static void RemoveOutside()
    {
        const float lx = -78f, hx = 78f, lz = -99f, hz = 88f;
        int n = 0;
        foreach (var r in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (r == root.gameObject || r.name == "class" || r.GetComponentInChildren<Canvas>() != null) continue;
            var rs = r.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            var c = b.center;
            if (c.x < lx || c.x > hx || c.z < lz || c.z > hz || c.y < -5f)
            {
                log.AppendLine("borrado fuera del salón: " + r.name);
                Object.DestroyImmediate(r); n++;
            }
        }
        log.AppendLine("objetos fuera del salón borrados: " + n);
    }

    static void BuildCeiling(Material beamMat, Material ceilMat)
    {
        var g = Group("Ceiling");
        // vigas largas (eje X)
        for (float z = Z0 + 4; z < Z1; z += 17f)
        {
            float roll = R();
            float len = 148f;
            if (roll < 0.28f) // rota: se corta y cuelga un extremo
            {
                float a = R(25, 60) * (R() < 0.5f ? 1 : -1);
                float cut = R(30, 95);
                float xs = R() < 0.5f ? WallL + 1 : WallR - 1;
                float dirx = xs < 0 ? 1 : -1;
                // pieza anclada en la pared que cuelga
                var pivot = new GameObject("BeamHang").transform; pivot.SetParent(g, false);
                pivot.position = new Vector3(xs, CeilY, z);
                var b = Prim(PrimitiveType.Cube, "Beam", Vector3.zero, new Vector3(cut, 1.8f, 1.5f), Quaternion.identity, beamMat, pivot);
                b.transform.localPosition = new Vector3(dirx * cut / 2f, 0, 0);
                pivot.rotation = Quaternion.Euler(0, 0, -dirx * a * 0.5f) * Quaternion.Euler(0, R(-4, 4), 0);
            }
            else if (roll < 0.40f) { /* viga desaparecida */ }
            else
                Prim(PrimitiveType.Cube, "Beam", new Vector3(0.5f, CeilY + R(-0.4f, 0.1f), z), new Vector3(len, 1.8f, 1.5f), Quaternion.Euler(0, R(-0.8f, 0.8f), R(-1.5f, 1.5f)), beamMat, g);
        }
        // viguetas transversales (eje Z)
        for (float x = X0 + 6; x < X1; x += 24f)
            if (R() > 0.35f)
                Prim(PrimitiveType.Cube, "Joist", new Vector3(x, CeilY - 1.4f, R(-4, 4)), new Vector3(1.2f, 1.2f, R(100, 176)), Quaternion.Euler(0, R(-1, 1), 0), beamMat, g);
        // restos de falso techo
        for (float x = WallL + 8; x < WallR; x += 22f)
            for (float z = Z0 + 6; z < Z1; z += 15f)
            {
                bool edge = Mathf.Abs(x) > 42f || z > 55f || z < -82f;
                if (R() > (edge ? 0.38f : 0.07f)) continue;
                float hang = R() < 0.35f ? R(15, 45) : R(0, 4);
                var p = new Vector3(x + R(-3, 3), CeilY - 1.6f - R(0, 1.5f), z + R(-3, 3));
                var panel = Prim(PrimitiveType.Cube, "CeilPanel", p, new Vector3(R(14, 24), 0.45f, R(9, 14)), Quaternion.Euler(hang * (R() < 0.5f ? -1 : 1), R(0, 360), hang * 0.6f), ceilMat, g);
                if (hang > 12f) panel.transform.position += new Vector3(0, -R(2, 6), 0);
            }
        // plantas colgando / entrando por el techo roto
        for (int i = 0; i < 22; i++)
            Place("periwinkle_plant", new Vector3(R(X0, X1), CeilY - 2, R(Z0, Z1)), R(4, 9), R(0, 360), new Vector3(180, 0, R(-10, 10)), true, g);
    }

    // losas del techo con huecos: cubren todo el salón, más cerradas hacia los bordes
    static void BuildCeilingSlab(Material ceilMat)
    {
        var g = Group("CeilingSlab");
        const float tw = 21f, td = 15f;
        int n = 0;
        for (float z = Z0 - 2; z < Z1 + 2; z += td)
            for (float x = X0 - 2; x < X1 + 2; x += tw)
            {
                float cx = x + tw / 2f, cz = z + td / 2f;
                float e = Mathf.Max(Mathf.Abs(cx) / X1, Mathf.Abs(cz + 6f) / 90f);
                if (R() > 0.5f + 0.38f * e * e) continue;
                float hang = R() < 0.2f ? R(10, 38) : R(0, 3);
                float sg = R() < 0.5f ? -1 : 1;
                var p = new Vector3(cx + R(-1.5f, 1.5f), CeilY - 0.3f + R(-0.2f, 0.2f), cz + R(-1.5f, 1.5f));
                if (hang > 10f) p.y -= R(2, 6);
                var q = Prim(PrimitiveType.Cube, "CeilSlab", p, new Vector3(tw * R(0.9f, 1.08f), 0.5f, td * R(0.9f, 1.08f)),
                    Quaternion.Euler(hang * sg, R(-5, 5), hang * 0.5f * sg), ceilMat, g);
                n++;
            }
        log.AppendLine("losas de techo: " + n);
    }

    [MenuItem("Tools/Decay/Agregar techo destruido")]
    public static void AddCeilingMenu()
    {
        var sb = new StringBuilder();
        AddCeilingSlab(sb);
        Debug.Log("[DecayBuilder] " + sb);
    }

    // añade (o rehace) solo el techo de losas, sin tocar el resto
    public static void AddCeilingSlab(StringBuilder outLog)
    {
        log = outLog;
        rng = new System.Random(6060);
        var scene = SceneManager.GetActiveScene();
        var rootGo = GameObject.Find("DECAY_ROOT");
        if (rootGo == null) { log.AppendLine("no hay DECAY_ROOT"); return; }
        root = rootGo.transform;
        var old = root.Find("CeilingSlab");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var mat = AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/Materials/Decay_CeilPanel.mat");
        BuildCeilingSlab(mat);
        // vigas/paneles/losas: solo batching estático (sin occludee) para que ningún occlusion viejo los oculte
        int fixedN = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>())
            if (t.name == "CeilPanel" || t.name == "Beam" || t.name == "Joist" || t.name == "CeilSlab")
            { GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic); fixedN++; }
        log.AppendLine("flags de techo ajustados: " + fixedN);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void BuildPatches(Material peelMat)
    {
        var g = Group("Patches");
        var leaves = new List<Material>(); var moss = new List<Material>(); var stains = new List<Material>();
        var wallPeel = new List<Material>(); var wallMoss = new List<Material>(); var wallStain = new List<Material>();
        for (int i = 0; i < 4; i++)
        {
            leaves.Add(PatchMat($"P_Leaves{i}", MakePatch($"leaves{i}", "brown_mud_leaves_01", 10 + i, new Color(0.75f, 0.68f, 0.55f, 1f), 0.8f)));
            moss.Add(PatchMat($"P_Moss{i}", MakePatch($"moss{i}", "moss_wood", 20 + i, new Color(0.7f, 0.85f, 0.55f, 1f), 0.75f)));
            stains.Add(PatchMat($"P_Stain{i}", MakePatch($"stain{i}", null, 30 + i, new Color(0.08f, 0.07f, 0.05f, 0.55f), 0.7f)));
            wallPeel.Add(PatchMat($"P_WallPeel{i}", MakePatch($"wallpeel{i}", "peeling_painted_wall", 40 + i, new Color(0.85f, 0.85f, 0.8f, 1f), 0.8f)));
            wallMoss.Add(PatchMat($"P_WallMoss{i}", MakePatch($"wallmoss{i}", "worn_mossy_plasterwall", 50 + i, new Color(0.55f, 0.7f, 0.45f, 1f), 0.7f)));
            wallStain.Add(PatchMat($"P_WallStain{i}", MakePatch($"wallstain{i}", null, 60 + i, new Color(0.1f, 0.12f, 0.08f, 0.5f), 0.65f)));
        }
        // suelo
        for (int i = 0; i < 20; i++) FloorPatch(g, stains, R(18, 40), 0.03f + i * 0.0002f);
        for (int i = 0; i < 90; i++) FloorPatch(g, leaves, R(10, 32), 0.06f + i * 0.0003f);
        for (int i = 0; i < 50; i++) FloorPatch(g, moss, R(10, 30), 0.1f + i * 0.0003f);

        // paredes (decals verticales). caras interiores: izquierda, derecha, fondo, frente
        var faces = new[]
        {
            (axis: 0, pos: WallL + 0.25f, yaw: -90f, a0: Z0 - 4, a1: Z1 + 2),
            (axis: 0, pos: WallR - 0.25f, yaw: 90f,  a0: Z0 - 4, a1: Z1 + 2),
            (axis: 1, pos: WallB + 0.25f, yaw: 180f, a0: X0 - 2, a1: X1 + 2),
            (axis: 1, pos: WallF - 0.25f, yaw: 0f,   a0: X0 - 2, a1: X1 + 2),
        };
        foreach (var f in faces)
            for (int i = 0; i < 38; i++)
            {
                var list = R() < 0.4f ? wallPeel : (R() < 0.55f ? wallMoss : wallStain);
                float a = R(f.a0, f.a1), y = R(1, 52), s = R(8, 26);
                if (list == wallMoss) y = R(0.5f, 24);
                Vector3 p = f.axis == 0 ? new Vector3(f.pos, y, a) : new Vector3(a, y, f.pos);
                var q = Prim(PrimitiveType.Quad, "WallPatch", p, new Vector3(s * R(0.8f, 1.5f), s, 1), Quaternion.Euler(0, f.yaw, R(0, 360)), Pick(list), g);
                q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                q.transform.position += q.transform.forward * -0.0f;
            }
    }

    static void FloorPatch(Transform g, List<Material> mats, float size, float y)
    {
        var p = RandFloor();
        var q = Prim(PrimitiveType.Quad, "FloorPatch", new Vector3(p.x, FloorY + y, p.z), new Vector3(size, size, 1), Quaternion.Euler(90, R(0, 360), 0), Pick(mats), g);
        q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static void BuildPlanks(Material plankMat)
    {
        var g = Group("Planks");
        // tablas del suelo levantadas / caídas
        for (int i = 0; i < 70; i++)
        {
            var p = RandFloor(3);
            float len = R(14, 36);
            float lift = R() < 0.4f ? R(8, 38) : R(0, 7);
            var rot = Quaternion.Euler(lift * (R() < 0.5f ? -1 : 1), R(0, 360), R(-6, 6));
            var pl = Prim(PrimitiveType.Cube, "Plank", p + new Vector3(0, 0.3f + lift * 0.12f, 0), new Vector3(R(2f, 3.4f), 0.5f, len), rot, plankMat, g, true);
        }
        // restos del techo caídos: tablones largos apoyados en las paredes
        for (int i = 0; i < 16; i++)
        {
            float x = R() < 0.5f ? WallL + 6 : WallR - 6;
            var pl = Prim(PrimitiveType.Cube, "LeaningPlank", new Vector3(x + R(-3, 3), 14, R(Z0, Z1)), new Vector3(R(2.2f, 3.4f), 0.6f, R(30, 46)),
                Quaternion.Euler(R(52, 72) * (x < 0 ? 1 : 1), 90 + (x < 0 ? 0 : 180) + R(-14, 14), 0) * Quaternion.Euler(0, 0, 0), plankMat, g, true);
            pl.transform.rotation = Quaternion.Euler(0, 90 + R(-15, 15), 0) * Quaternion.Euler(0, 0, (x < 0 ? -1 : 1) * R(60, 75));
            pl.transform.localScale = new Vector3(pl.transform.localScale.x, 0.6f, R(18, 28));
            pl.transform.rotation = Quaternion.LookRotation(new Vector3(x < 0 ? 0.4f : -0.4f, 1f, R(-0.25f, 0.25f)).normalized, Vector3.up);
        }
        // papeles / cartones sueltos
        var paperCols = new[] { new Color(0.78f, 0.75f, 0.62f), new Color(0.65f, 0.7f, 0.74f), new Color(0.8f, 0.72f, 0.5f), new Color(0.6f, 0.58f, 0.5f) };
        for (int i = 0; i < 90; i++)
        {
            var p = RandFloor(1);
            if (NearItem(p, 3f)) continue;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = Pick(paperCols); m.SetFloat("_Smoothness", 0.05f);
            string mp = $"{Dir}/Materials/Paper{i % 4}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (mat == null) { AssetDatabase.CreateAsset(m, mp); mat = m; }
            mat.SetColor("_BaseColor", paperCols[i % 4]);
            Prim(PrimitiveType.Cube, "Paper", p + new Vector3(0, 0.25f + i * 0.0015f, 0), new Vector3(R(3, 6), 0.06f, R(4, 8)), Quaternion.Euler(R(-6, 6), R(0, 360), R(-6, 6)), mat, g);
        }
    }

    static void BuildFurniture()
    {
        var g = Group("Furniture");
        var spots = new[] { new Vector3(-18, 0, 14), new Vector3(22, 0, -12), new Vector3(-30, 0, -20), new Vector3(8, 0, 38), new Vector3(35, 0, 26) };
        for (int i = 0; i < spots.Length; i++)
        {
            bool toppled = i % 3 == 1;
            Place("SchoolDesk_01", spots[i], 15.5f, R(0, 360), toppled ? new Vector3(0, 0, 90) : default, true, g, true);
            if (!toppled) Place("SchoolChair_01", spots[i] + new Vector3(R(-6, 6), 0, R(-14, -8)), 16.5f, R(0, 360), default, true, g, true);
        }
        for (int i = 0; i < 6; i++)
        {
            var p = RandFloor(8);
            if (NearItem(p, 8f)) continue;
            Place("SchoolChair_01", p, 16.5f, R(0, 360), R() < 0.6f ? new Vector3(0, 0, 90) : new Vector3(R(-20, 20), 0, R(-25, 25)), true, g, true);
        }
    }

    static void BuildVegetation()
    {
        var g = Group("Vegetation");
        // focos de vegetación densa
        var centres = new List<Vector3>();
        for (int i = 0; i < 11; i++) centres.Add(RandFloor(6));
        // y bajo las ventanas
        for (float z = Z0 + 8; z < Z1 - 6; z += 34f) { centres.Add(new Vector3(WallL + 5, 0, z)); centres.Add(new Vector3(WallR - 5, 0, z + 12)); }
        string[] big = { "fern_02", "fern_02", "fern_02", "fern_02", "fern_02", "fern_02", "nettle_plant", "weed_plant_02", "periwinkle_plant" };
        string[] low = { "grass_medium_01", "moss_01", "fern_02", "grass_medium_01", "periwinkle_plant" };
        foreach (var c in centres)
        {
            int n = rng.Next(3, 7);
            for (int i = 0; i < n; i++)
            {
                var p = c + new Vector3(R(-16, 16), 0, R(-16, 16));
                p.x = Mathf.Clamp(p.x, X0 - 1, X1 + 1); p.z = Mathf.Clamp(p.z, Z0, Z1);
                if (NearItem(p, 3.5f)) continue;
                bool tall = R() < 0.4f;
                string id = tall ? Pick(big) : Pick(low);
                float h = tall ? R(2.5f, 6f) : R(0.8f, 2.4f);
                if (id == "moss_01") h = R(2, 5);
                var go = Place(id, p, h, R(0, 360), new Vector3(R(-6, 6), 0, R(-6, 6)), id != "moss_01", g);
            }
        }
        // enredaderas trepando por las paredes
        for (int i = 0; i < 30; i++)
        {
            bool left = R() < 0.5f;
            var p = new Vector3(left ? WallL + 1.2f : WallR - 1.2f, 0, R(Z0, Z1));
            Place("periwinkle_plant", p, R(10, 28), left ? 90 : -90, new Vector3(0, 0, R(-8, 8)), true, g);
        }
        for (int i = 0; i < 14; i++)
        {
            bool back = R() < 0.5f;
            var p = new Vector3(R(X0, X1), 0, back ? WallB + 1.2f : WallF - 1.2f);
            Place("periwinkle_plant", p, R(10, 28), back ? 0 : 180, default, true, g);
        }
    }

    static void BuildTrash()
    {
        var g = Group("Trash");
        var defs = new (string id, float size, bool h, int count)[]
        {
            ("can_rusted", 2.6f, true, 16), ("trashbag", 6f, true, 8), ("plastic_bottle_gallon", 6.5f, true, 7),
            ("cardboard_box_01", 6f, true, 6), ("old_tyre", 9f, false, 3), ("wooden_crate_01", 7f, true, 3),
            ("bleach_bottle", 5.5f, true, 6), ("multi_cleaner_bottle", 5f, true, 6), ("dry_branches_medium_01", 14f, false, 12),
        };
        foreach (var d in defs)
            for (int i = 0; i < d.count; i++)
            {
                var p = RandFloor(4);
                if (NearItem(p, 5f)) continue;
                Place(d.id, p, d.size * R(0.85f, 1.2f), R(0, 360), R() < 0.4f ? new Vector3(0, 0, 90) : new Vector3(R(-8, 8), 0, R(-8, 8)), d.h, g, !d.id.Contains("branch"));
            }
    }

    // ------------------------------------------------------------------ relleno de zonas vacías
    [MenuItem("Tools/Decay/Rellenar zonas vacias")]
    public static void FillMenu()
    {
        var sb = new StringBuilder();
        FillEmpty(sb);
        Debug.Log("[DecayBuilder] " + sb);
    }

    // añade plantas, escombros y parches SOLO donde el suelo está libre; no toca lo existente
    public static void FillEmpty(StringBuilder outLog)
    {
        log = outLog;
        rng = new System.Random(8181);
        modelCache.Clear();
        var scene = SceneManager.GetActiveScene();
        var rootGo = GameObject.Find("DECAY_ROOT");
        if (rootGo == null) { log.AppendLine("no hay DECAY_ROOT: ejecuta primero 'Reconstruir salon en ruinas'"); return; }
        root = rootGo.transform;
        var oldFill = root.Find("Fill");
        if (oldFill != null) Object.DestroyImmediate(oldFill.gameObject);

        // zonas ya ocupadas (rectángulos en XZ)
        var occupied = new List<Vector3>(); int nRoot = 0; // x, z, radio
        void AddRect(Bounds b, float m) { occupied.Add(new Vector3(b.center.x, b.center.z, Mathf.Min(0.3f * Mathf.Max(b.size.x, b.size.z) + m, 14f))); }
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            if (r.name == "Paper" || r.name == "FloorPatch" || r.name == "WallPatch" || r.name == "Shaft") continue;
            var b = r.bounds;
            if (b.min.y > 12f || Mathf.Max(b.size.x, b.size.z) > 60f) continue;
            AddRect(b, 1f); nRoot++;
        }
        var cls = GameObject.Find("class");
        if (cls != null)
            foreach (var r in cls.GetComponentsInChildren<Renderer>())
            {
                if (r.name == "Room") continue;
                var b = r.bounds;
                if (b.min.y > 12f || Mathf.Max(b.size.x, b.size.z) > 60f) continue;
                AddRect(b, 2f);
            }
        foreach (var r in scene.GetRootGameObjects())
        {
            if (r == root.gameObject || r.name == "class" || r.GetComponentInChildren<Canvas>() != null || r.name.Contains("Light") || r.name.Contains("Camera")) continue;
            var rs = r.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;
            var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
            if (Mathf.Max(b.size.x, b.size.z) < 14f) AddRect(b, 6f);
        }
        var player = GameObject.Find("Jugador");
        if (player != null) occupied.Add(new Vector3(player.transform.position.x, player.transform.position.z, 10f));
        log.AppendLine("zonas ocupadas: " + occupied.Count + " (root " + nRoot + ")");
        int free = 0; for (int k = 0; k < 2000; k++) { var q = RandFloor(3); var qq = new Vector2(q.x, q.z); bool bl = false; foreach (var o in occupied) if ((o.x - qq.x) * (o.x - qq.x) + (o.y - qq.y) * (o.y - qq.y) < o.z * o.z) { bl = true; break; } if (!bl) free++; }
        log.AppendLine("fraccion libre: " + free / 2000f);

        var g = Group("Fill");
        var plankMat = AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/Materials/Decay_Plank.mat");
        var mossMats = new List<Material>(); var leafMats = new List<Material>(); var paperMats = new List<Material>();
        for (int i = 0; i < 4; i++)
        {
            mossMats.Add(AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/Materials/P_Moss{i}.mat"));
            leafMats.Add(AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/Materials/P_Leaves{i}.mat"));
            paperMats.Add(AssetDatabase.LoadAssetAtPath<Material>($"{Dir}/Materials/Paper{i}.mat"));
        }
        string[] plants = { "fern_02", "fern_02", "nettle_plant", "weed_plant_02", "grass_medium_01", "grass_medium_01", "periwinkle_plant", "moss_01", "shrub_01" };
        var trash = new (string id, float size, bool h)[]
        {
            ("can_rusted", 2.6f, true), ("trashbag", 6f, true), ("plastic_bottle_gallon", 6.5f, true), ("cardboard_box_01", 6f, true),
            ("old_tyre", 9f, false), ("wooden_crate_01", 7f, true), ("bleach_bottle", 5.5f, true), ("multi_cleaner_bottle", 5f, true),
            ("dry_branches_medium_01", 14f, false), ("dry_branches_medium_01", 18f, false),
        };

        bool PlaceGroup(Vector3 p)
        {
            float roll = R();
            Bounds nb;
            if (roll < 0.55f)
            {
                // matorral: parche de musgo/hojas + 2-4 plantas juntas
                var patch = Prim(PrimitiveType.Quad, "FloorPatch", new Vector3(p.x, FloorY + 0.1f + R() * 0.05f, p.z), new Vector3(R(12, 26), R(12, 26), 1),
                    Quaternion.Euler(90, R(0, 360), 0), Pick(R() < 0.6f ? mossMats : leafMats), g);
                patch.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                nb = patch.GetComponent<Renderer>().bounds;
                int n = rng.Next(2, 5);
                for (int i = 0; i < n; i++)
                {
                    string id = Pick(plants);
                    var pp = p + new Vector3(R(-6, 6), 0, R(-6, 6));
                    float h = id == "shrub_01" ? R(9, 14) : (id == "grass_medium_01" ? R(2.5f, 5f) : R(4, 9));
                    if (id == "moss_01") h = R(4, 8);
                    var go = Place(id, pp, h, R(0, 360), new Vector3(R(-6, 6), 0, R(-6, 6)), id != "moss_01" && id != "shrub_01", g);
                    if (go != null) nb.Encapsulate(RendBounds(go));
                }
                AddRect(nb, 2f);
            }
            else if (roll < 0.85f)
            {
                var d = Pick(trash);
                var go = Place(d.id, p, d.size * R(0.85f, 1.25f), R(0, 360), R() < 0.4f ? new Vector3(0, 0, 90) : new Vector3(R(-8, 8), 0, R(-8, 8)), d.h, g, !d.id.Contains("branch"));
                if (go == null) return false;
                AddRect(RendBounds(go), 3f);
            }
            else if (roll < 0.95f && plankMat != null)
            {
                float lift = R() < 0.4f ? R(8, 30) : R(0, 6);
                var pl = Prim(PrimitiveType.Cube, "Plank", p + new Vector3(0, 0.3f + lift * 0.12f, 0), new Vector3(R(2f, 3.4f), 0.5f, R(12, 30)),
                    Quaternion.Euler(lift * (R() < 0.5f ? -1 : 1), R(0, 360), R(-6, 6)), plankMat, g, true);
                AddRect(pl.GetComponent<Renderer>().bounds, 2f);
            }
            else
            {
                // montoncito de papeles
                for (int i = 0; i < 5; i++)
                    Prim(PrimitiveType.Cube, "Paper", p + new Vector3(R(-6, 6), 0.25f + i * 0.002f, R(-6, 6)), new Vector3(R(3, 6), 0.06f, R(4, 8)),
                        Quaternion.Euler(R(-6, 6), R(0, 360), R(-6, 6)), Pick(paperMats), g);
                occupied.Add(new Vector3(p.x, p.z, 8f));
            }
            return true;
        }

        int placed = 0, attempts = 0;
        while (placed < 230 && attempts < 4000)
        {
            attempts++;
            var p = RandFloor(3);
            bool blocked = false;
            var pt = new Vector2(p.x, p.z);
            foreach (var o in occupied) if ((o.x - pt.x) * (o.x - pt.x) + (o.y - pt.y) * (o.y - pt.y) < o.z * o.z) { blocked = true; break; }
            if (blocked) continue;
            if (PlaceGroup(p)) placed++;
        }
        log.AppendLine($"relleno: {placed} grupos nuevos en {attempts} intentos");

        // segundo pase: cualquier hueco donde no haya NINGÚN objeto real cerca (distancia medida a los límites reales)
        var rends = new List<Renderer>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            var rb = r.bounds;
            if (r.name == "Room" || r.name == "FloorPatch" || r.name == "WallPatch" || r.name == "Paper" || rb.min.y > 12f || Mathf.Max(rb.size.x, rb.size.z) >= 60f) continue;
            rends.Add(r);
        }
        int firstNew = occupied.Count, gaps = 0;
        const float cell = 9f, gapDist = 13f;
        for (float z = Z0 + 4; z < Z1 - 2; z += cell)
            for (float x = X0 + 4; x < X1 - 2; x += cell)
            {
                var p = new Vector3(x + R(-3, 3), 0, z + R(-3, 3));
                bool near = false;
                for (int k = firstNew; k < occupied.Count && !near; k++)
                { var o = occupied[k]; near = (o.x - p.x) * (o.x - p.x) + (o.y - p.z) * (o.y - p.z) < (o.z * 0.8f) * (o.z * 0.8f); }
                if (near) continue;
                float gd2 = gapDist * gapDist;
                foreach (var rr in rends) if (DistXZ2(rr, p) < gd2) { near = true; break; }
                if (near) continue;
                if (PlaceGroup(p)) gaps++;
            }
        log.AppendLine($"huecos rellenados: {gaps}");

        // sombras fuera para lo pequeño y batching estático para parches/tablas/papeles
        var batch = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
        foreach (var t in g.GetComponentsInChildren<Transform>())
            if (t.name == "FloorPatch" || t.name == "Paper" || t.name == "Plank") GameObjectUtility.SetStaticEditorFlags(t.gameObject, batch);
        foreach (var r in g.GetComponentsInChildren<Renderer>())
        {
            var s = r.bounds.size;
            if (Mathf.Max(s.x, s.y, s.z) < 6f || r.name == "Paper" || r.name == "FloorPatch") r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.allowOcclusionWhenDynamic = true;
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine("hijos de Fill: " + g.childCount);
    }

    // distancia² en XZ de un punto a la geometría real (caja orientada del mesh) de un renderer
    static float DistXZ2(Renderer r, Vector3 p)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            var b = r.bounds; float dx = Mathf.Max(b.min.x - p.x, 0, p.x - b.max.x), dz = Mathf.Max(b.min.z - p.z, 0, p.z - b.max.z);
            return dx * dx + dz * dz;
        }
        var lb = mf.sharedMesh.bounds;
        var wc = r.bounds.center;
        var lp = r.transform.InverseTransformPoint(new Vector3(p.x, wc.y, p.z));
        var cl = new Vector3(Mathf.Clamp(lp.x, lb.min.x, lb.max.x), Mathf.Clamp(lp.y, lb.min.y, lb.max.y), Mathf.Clamp(lp.z, lb.min.z, lb.max.z));
        var cw = r.transform.TransformPoint(cl);
        return (cw.x - p.x) * (cw.x - p.x) + (cw.z - p.z) * (cw.z - p.z);
    }

    static void BuildLights()
    {
        var g = Group("Lights");
        // haces de luz que entran por el techo roto
        for (int i = 0; i < 6; i++)
        {
            var go = new GameObject("Shaft"); go.transform.SetParent(g, false);
            go.transform.position = new Vector3(R(-45, 45), CeilY + 6, R(-70, 60));
            go.transform.rotation = Quaternion.Euler(R(70, 85), R(0, 360), 0);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.spotAngle = 38; l.innerSpotAngle = 20;
            l.range = 140; l.intensity = 2500; l.color = new Color(1f, 0.95f, 0.8f); l.shadows = LightShadows.None;
        }
        // relleno verdoso general
        var fill = new GameObject("GreenFill"); fill.transform.SetParent(g, false);
        fill.transform.position = new Vector3(0, 30, -5);
        var fl = fill.AddComponent<Light>(); fl.type = LightType.Point; fl.range = 190; fl.intensity = 1500; fl.color = new Color(0.75f, 0.95f, 0.7f); fl.shadows = LightShadows.None;
    }
}
