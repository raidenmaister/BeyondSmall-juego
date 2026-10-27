using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Instala el ciclo dia/noche y las luciernagas en la escena abierta:
/// materiales, prefab de la luciernaga, sol/luna, sonido de grillos y post-proceso.
/// Menu: Tools > Dia y Noche > Instalar en la escena actual.
/// </summary>
[InitializeOnLoad]
public static class DayNightSetup
{
    const string Dir = "Assets/DayNight";
    static double next;

    static DayNightSetup() { EditorApplication.update += PlayTestUpdate; }

    [MenuItem("Tools/Dia y Noche/Instalar en la escena actual")]
    static void Menu()
    {
        var log = new StringBuilder();
        Install(log);
        Debug.Log(log.ToString());
    }

    static void Carpeta(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path));
    }

    static Material Mat(string path, string shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find(shader);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != sh) m.shader = sh;
        return m;
    }

    static T Componente<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (p.TryGet<T>(out var c)) return c;
        c = p.Add<T>(true);
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, p);
        return c;
    }

    static VolumeProfile Perfil(string path)
    {
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (p == null) { p = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(p, path); }
        return p;
    }

    static T Obtener<T>(GameObject g) where T : Component
    {
        var c = g.GetComponent<T>();
        return c != null ? c : g.AddComponent<T>();
    }

    static Transform Buscar(Transform t, string nombre)
    {
        if (t.name == nombre) return t;
        foreach (Transform c in t) { var r = Buscar(c, nombre); if (r != null) return r; }
        return null;
    }

    public static void Install(StringBuilder log, bool restablecerValores = false)
    {
        Carpeta(Dir + "/Materials"); Carpeta(Dir + "/Prefabs"); Carpeta(Dir + "/Settings");

        // --- Audio de grillos
        var ai = AssetImporter.GetAtPath(Dir + "/Audio/Crickets.mp3") as AudioImporter;
        if (ai != null && ai.defaultSampleSettings.loadType != AudioClipLoadType.CompressedInMemory)
        {
            var s = ai.defaultSampleSettings; s.loadType = AudioClipLoadType.CompressedInMemory; ai.defaultSampleSettings = s;
            ai.SaveAndReimport();
        }
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "/Audio/Crickets.mp3");
        log.AppendLine("grillos: " + (clip != null ? clip.length.ToString("F1") + "s" : "NO ENCONTRADO"));

        // --- Materiales
        var cielo = Mat(Dir + "/Materials/Cielo_DiaNoche.mat", "Custom/DayNightSky");
        var halo = Mat(Dir + "/Materials/Luciernaga_Halo.mat", "Custom/FireflyGlow");
        var linterna = Mat(Dir + "/Materials/Luciernaga_Linterna.mat", "Custom/FireflyLantern");

        // --- Prefab de la luciernaga
        var prefab = CrearPrefab(halo, linterna, log);

        // --- Escena
        var solGo = GameObject.Find("Sol") ?? GameObject.Find("Directional Light");
        if (solGo == null) { solGo = new GameObject("Sol"); solGo.AddComponent<Light>().type = LightType.Directional; }
        solGo.name = "Sol";
        var sol = solGo.GetComponent<Light>();
        sol.shadows = LightShadows.Soft;

        var lunaGo = GameObject.Find("Luna");
        if (lunaGo == null) { lunaGo = new GameObject("Luna"); lunaGo.AddComponent<Light>().type = LightType.Directional; }
        var luna = lunaGo.GetComponent<Light>();
        luna.shadows = LightShadows.Soft; luna.shadowStrength = 0.75f; luna.intensity = 0f;
        if (lunaGo.GetComponent<UniversalAdditionalLightData>() == null) lunaGo.AddComponent<UniversalAdditionalLightData>();

        var cicloGo = GameObject.Find("CicloDiaNoche") ?? new GameObject("CicloDiaNoche");
        var ciclo = Obtener<DayNightCycle>(cicloGo);
        if (restablecerValores)
        {
            // vuelve a los valores por defecto del script (colores, intensidades...)
            var tmp = new GameObject("__tmp").AddComponent<DayNightCycle>();
            EditorUtility.CopySerialized(tmp, ciclo);
            Object.DestroyImmediate(tmp.gameObject);
        }
        ciclo.sol = sol; ciclo.luna = luna; ciclo.cielo = cielo;
        ciclo.lucesDeDia = Object.FindObjectsByType<Light>(FindObjectsInactive.Include)
            .Where(l => l.type != LightType.Directional && (l.name == "Shaft" || l.name == "GreenFill")).ToList();
        log.AppendLine("luces de dia: " + ciclo.lucesDeDia.Count);

        ciclo.grillos = Fuente(cicloGo.transform, "Grillos", clip);
        ciclo.grillosLejanos = Fuente(cicloGo.transform, "GrillosLejanos", clip);

        // --- Post-proceso: bloom siempre (hace brillar a las luciernagas) + look nocturno con peso variable
        var perfilBase = Perfil(Dir + "/Settings/Post_Brillo.asset");
        var bloom = Componente<Bloom>(perfilBase);
        bloom.threshold.Override(1.0f); bloom.intensity.Override(0.9f); bloom.scatter.Override(0.68f); bloom.highQualityFiltering.Override(true);
        var perfilNoche = Perfil(Dir + "/Settings/Post_Noche.asset");
        var ca = Componente<ColorAdjustments>(perfilNoche);
        ca.postExposure.Override(0.2f); ca.saturation.Override(-22f); ca.contrast.Override(10f); ca.colorFilter.Override(new Color(0.8f, 0.9f, 1.1f));
        var vig = Componente<Vignette>(perfilNoche);
        vig.intensity.Override(0.33f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0f, 0.01f, 0.03f));
        var bloomNoche = Componente<Bloom>(perfilNoche);
        bloomNoche.intensity.Override(1.4f); bloomNoche.threshold.Override(0.85f);
        EditorUtility.SetDirty(perfilBase); EditorUtility.SetDirty(perfilNoche);

        Volumen(cicloGo.transform, "PostBrillo", perfilBase, 1, 1f);
        ciclo.volumenNoche = Volumen(cicloGo.transform, "PostNoche", perfilNoche, 2, 0f);

        var cam = Camera.main;
        if (cam != null)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            EditorUtility.SetDirty(data);
        }

        // --- Enjambre
        var swarmGo = GameObject.Find("Luciernagas") ?? new GameObject("Luciernagas");
        var swarm = Obtener<FireflySwarm>(swarmGo);
        swarm.prefab = prefab;
        var jug = GameObject.Find("Jugador");
        if (jug != null) swarm.jugador = jug.transform;
        var aula = GameObject.Find("class");
        if (aula != null)
        {
            var rs = aula.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            swarm.centroZona = new Vector3(b.center.x, 0f, b.center.z);
            swarm.tamanoZona = new Vector3(b.size.x * 0.85f, 10f, b.size.z * 0.85f);
        }

        RenderSettings.skybox = cielo;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        ciclo.AplicarAhora();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        log.AppendLine("instalado en " + EditorSceneManager.GetActiveScene().path);
    }

    static AudioSource Fuente(Transform padre, string nombre, AudioClip clip)
    {
        var t = padre.Find(nombre);
        if (t == null) { t = new GameObject(nombre).transform; t.SetParent(padre, false); }
        var a = Obtener<AudioSource>(t.gameObject);
        a.clip = clip; a.loop = true; a.playOnAwake = true; a.spatialBlend = 0f; a.volume = 0f;
        return a;
    }

    static Volume Volumen(Transform padre, string nombre, VolumeProfile perfil, int prioridad, float peso)
    {
        var t = padre.Find(nombre);
        if (t == null) { t = new GameObject(nombre).transform; t.SetParent(padre, false); }
        var v = Obtener<Volume>(t.gameObject);
        v.isGlobal = true; v.sharedProfile = perfil; v.priority = prioridad; v.weight = peso;
        return v;
    }

    static Firefly CrearPrefab(Material halo, Material linterna, StringBuilder log)
    {
        string path = Dir + "/Prefabs/Luciernaga.prefab";
        var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Models/Firefly.glb");
        if (modelo == null) { log.AppendLine("NO se encontro Firefly.glb"); return null; }

        var root = new GameObject("Luciernaga");
        var ff = root.AddComponent<Firefly>();
        var m = (GameObject)PrefabUtility.InstantiatePrefab(modelo);
        m.name = "Modelo";
        m.transform.SetParent(root.transform, false);
        m.transform.localScale = Vector3.one * 0.6f;

        Renderer rLinterna = null;
        foreach (var r in m.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            if (r.name.Contains("Lantern"))
            {
                rLinterna = r;
                r.sharedMaterials = Enumerable.Repeat(linterna, r.sharedMaterials.Length).ToArray();
            }
        }
        ff.linterna = rLinterna;
        ff.alaIzq = Buscar(m.transform, "Wing_L");
        ff.alaDer = Buscar(m.transform, "Wing_R");
        log.AppendLine($"modelo: linterna={(rLinterna ? rLinterna.name : "null")} alaIzq={(ff.alaIzq ? "ok" : "null")} alaDer={(ff.alaDer ? "ok" : "null")} renderers={m.GetComponentsInChildren<Renderer>().Length}");

        Vector3 centroLinterna = rLinterna != null ? root.transform.InverseTransformPoint(rLinterna.bounds.center) : Vector3.zero;

        var luzGo = new GameObject("Luz");
        luzGo.transform.SetParent(root.transform, false);
        luzGo.transform.localPosition = centroLinterna + Vector3.down * 0.12f;
        var luz = luzGo.AddComponent<Light>();
        luz.type = LightType.Point; luz.color = new Color(0.52f, 1f, 0.28f); luz.range = 9f; luz.intensity = 6f;
        luz.shadows = LightShadows.None; luz.renderMode = LightRenderMode.ForcePixel;
        luzGo.AddComponent<UniversalAdditionalLightData>();
        ff.luz = luz;

        var haloGo = new GameObject("Halo");
        haloGo.transform.SetParent(root.transform, false);
        haloGo.transform.localPosition = centroLinterna;
        haloGo.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var hr = haloGo.AddComponent<MeshRenderer>();
        hr.sharedMaterial = halo; hr.shadowCastingMode = ShadowCastingMode.Off; hr.receiveShadows = false;
        ff.halo = hr;

        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return saved.GetComponent<Firefly>();
    }

    // Render en modo edicion mirando al cielo a varias horas: Temp/dn_sky.png
    public static void SkyShot(StringBuilder log, string horas)
    {
        var ciclo = Object.FindAnyObjectByType<DayNightCycle>();
        float horaOriginal = ciclo.hora;
        var hs = horas.Split(' ').Where(x => x.Length > 0).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        int cw = 640, ch = 400;
        var hoja = new Texture2D(cw * 2, ch * ((hs.Length + 1) / 2), TextureFormat.RGB24, false);
        var go = new GameObject("__SkyCam"); var cam = go.AddComponent<Camera>();
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        cam.clearFlags = CameraClearFlags.Skybox; cam.fieldOfView = 75; cam.farClipPlane = 1500f;
        cam.transform.position = new Vector3(-31.5f, 3f, -53f);
        for (int i = 0; i < hs.Length; i++)
        {
            ciclo.hora = hs[i]; ciclo.AplicarAhora();
            cam.transform.rotation = Quaternion.Euler(-28f, ciclo.azimut + 90f, 0f);
            var rt = new RenderTexture(cw, ch, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(cw, ch, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, cw, ch), 0, 0); tex.Apply();
            hoja.SetPixels((i % 2) * cw, hoja.height - ch * (i / 2 + 1), cw, ch, tex.GetPixels());
            RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        Object.DestroyImmediate(go); hoja.Apply();
        File.WriteAllBytes("Temp/dn_sky.png", hoja.EncodeToPNG());
        ciclo.hora = horaOriginal; ciclo.AplicarAhora();
        log.AppendLine("dn_sky.png listo");
    }

    // ---------------- Prueba automatica en modo juego ----------------
    // "dnplay 12 18.5 22 5.6": entra a Play, pone cada hora, espera y guarda Temp/dn_<i>.png
    public static void BeginPlayTest(string horas)
    {
        File.WriteAllText("Temp/dn_log.txt", "");
        if (File.Exists("Temp/dn_done.txt")) File.Delete("Temp/dn_done.txt");
        SessionState.SetString("dn_horas", horas);
        SessionState.SetInt("dn_i", 0);
        SessionState.SetFloat("dn_t0", 0f);
        SessionState.SetInt("dn_stage", 1);
        EditorApplication.isPlaying = true;
    }

    static void DnLog(string s) { File.AppendAllText("Temp/dn_log.txt", s + "\n"); }

    static void PlayTestUpdate()
    {
        int stage = SessionState.GetInt("dn_stage", 0);
        if (stage == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 0.25;
        float now = (float)EditorApplication.timeSinceStartup;
        var horas = SessionState.GetString("dn_horas", "").Split(' ').Where(x => x.Length > 0)
            .Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        int i = SessionState.GetInt("dn_i", 0);
        var ciclo = DayNightCycle.Instance;
        if (ciclo == null) return;

        if (i >= horas.Length)
        {
            DnLog("FIN");
            File.WriteAllText("Temp/dn_done.txt", "ok");
            SessionState.SetInt("dn_stage", 0);
            EditorApplication.isPlaying = false;
            return;
        }
        if (SessionState.GetFloat("dn_t0", 0f) == 0f)
        {
            SessionState.SetFloat("dn_t0", now);
            ciclo.pausado = true;
            ciclo.hora = horas[i];
            return;
        }
        // de noche espera a que lleguen las luciernagas, al amanecer a que se vayan
        float espera = (ciclo.Noche > 0.5f || ciclo.EsMadrugadaOManana) ? 16f : 3f;
        if (now - SessionState.GetFloat("dn_t0", 0f) < espera) return;

        var activas = Object.FindObjectsByType<Firefly>().Count(f => f.gameObject.activeInHierarchy);
        var gr = ciclo.grillos;
        DnLog($"hora={horas[i]:F2} noche={ciclo.Noche:F2} luciernagasActivas={activas} grillosVol={(gr ? gr.volume : -1):F2} grillosSonando={(gr && gr.isPlaying)} sol={(ciclo.sol ? ciclo.sol.intensity : 0):F2} luna={(ciclo.luna ? ciclo.luna.intensity : 0):F2}");
        Captura(Camera.main, "Temp/dn_" + i + ".png");
        SessionState.SetInt("dn_i", i + 1);
        SessionState.SetFloat("dn_t0", 0f);
    }

    static void Captura(Camera cam, string file)
    {
        if (cam == null) return;
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var prev = cam.targetTexture;
        cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(file, tex.EncodeToPNG());
        Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
    }
}
