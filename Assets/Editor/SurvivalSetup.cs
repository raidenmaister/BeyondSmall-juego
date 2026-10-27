using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Instala la supervivencia en la escena abierta: velocidades de los bichos, bloqueo de
/// colisiones, barra de comida, icono de carne cocinada y la fogata con fuego.
/// Menu: Tools > Supervivencia > Instalar en la escena actual.
/// </summary>
[InitializeOnLoad]
public static class SurvivalSetup
{
    const string Dir = "Assets/Survival";
    static double next;

    static SurvivalSetup() { EditorApplication.update += PlayTestUpdate; }

    [MenuItem("Tools/Supervivencia/Instalar en la escena actual")]
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

    static T Obtener<T>(GameObject g) where T : Component
    {
        var c = g.GetComponent<T>();
        return c != null ? c : g.AddComponent<T>();
    }

    static Material Mat(string path, string shader)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var sh = Shader.Find(shader);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else if (m.shader != sh) m.shader = sh;
        return m;
    }

    public static void Install(StringBuilder log)
    {
        Carpeta(Dir + "/Materials"); Carpeta(Dir + "/Textures"); Carpeta(Dir + "/Prefabs");

        // ---------- 1. velocidad de los bichos
        foreach (var rat in Object.FindObjectsByType<RatAI>(FindObjectsInactive.Include))
        {
            rat.runSpeed = 4.5f; rat.walkSpeed = 3.2f; EditorUtility.SetDirty(rat);
        }
        foreach (var sp in Object.FindObjectsByType<SpiderAI>(FindObjectsInactive.Include))
        {
            sp.runSpeed = 3.3f; sp.returnSpeed = 2.5f; EditorUtility.SetDirty(sp);
        }
        var spiderPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spider/Spider_Enemy.prefab");
        if (spiderPrefab != null)
        {
            var spAi = spiderPrefab.GetComponent<SpiderAI>();
            if (spAi != null) { spAi.runSpeed = 3.3f; spAi.returnSpeed = 2.5f; EditorUtility.SetDirty(spAi); PrefabUtility.SavePrefabAsset(spiderPrefab); }
        }
        var bichos = Object.FindObjectsByType<CreatureAI>(FindObjectsInactive.Include);
        log.AppendLine($"velocidades: ratas/arañas ajustadas, {bichos.Length} bichos con speedMultiplier={(bichos.Length > 0 ? bichos[0].speedMultiplier : 0f)}");

        // ---------- 2/3. jugador: bloqueo de colisiones y hambre
        var jugador = GameObject.Find("Jugador");
        if (jugador != null)
        {
            Obtener<CreatureBlocker>(jugador);
            Obtener<PlayerHunger>(jugador);
            EditorUtility.SetDirty(jugador);
        }
        log.AppendLine("jugador: " + (jugador != null ? "CreatureBlocker + PlayerHunger" : "NO ENCONTRADO"));

        // ---------- icono de carne cocinada
        var icono = CrearIconoCocinado(log);
        var inv = Object.FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (inv != null && icono != null)
        {
            inv.iconDatabase.RemoveAll(s => s == null || s.name == icono.name);
            inv.iconDatabase.Add(icono);
            EditorUtility.SetDirty(inv);
        }

        // ---------- barra de comida en el HUD
        CrearBarraComida(log);

        // ---------- 4. fogata
        var fogata = CrearFogata(log);
        if (fogata != null) fogata.iconoCarneCocinada = icono;

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        log.AppendLine("instalado en " + EditorSceneManager.GetActiveScene().path);
    }

    // ------------------------------------------------------------------ icono
    static Sprite CrearIconoCocinado(StringBuilder log)
    {
        string src = "Assets/iconos/Carne.png", dst = "Assets/iconos/Carne_Cocinada.png";
        if (!File.Exists(dst))
        {
            var tex = new Texture2D(2, 2);
            tex.LoadImage(File.ReadAllBytes(src));
            var px = tex.GetPixels();
            int w = tex.width, h = tex.height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    if (c.a < 0.02f) continue;
                    Color.RGBToHSV(c, out float hu, out float s, out float v);
                    if (s > 0.25f)
                    {
                        // carne roja -> dorada/marron, con marcas de parrilla en diagonal
                        float grill = Mathf.Repeat((x + y) / (float)w * 7f, 1f);
                        float marca = grill < 0.12f ? 0.45f : 1f;
                        var cocida = Color.Lerp(new Color(0.32f, 0.15f, 0.06f), new Color(0.78f, 0.48f, 0.22f), v) * marca;
                        cocida.a = c.a;
                        px[y * w + x] = cocida;
                    }
                    else px[y * w + x] = Color.Lerp(c, new Color(0.95f, 0.88f, 0.75f, c.a), 0.3f);  // hueso tostado
                }
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(dst, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(dst);
            var srcImp = (TextureImporter)AssetImporter.GetAtPath(src);
            var dstImp = (TextureImporter)AssetImporter.GetAtPath(dst);
            var settings = new TextureImporterSettings();
            srcImp.ReadTextureSettings(settings);
            dstImp.SetTextureSettings(settings);
            dstImp.textureType = TextureImporterType.Sprite;
            dstImp.spriteImportMode = SpriteImportMode.Single;
            dstImp.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(dst);
        log.AppendLine("icono carne cocinada: " + (sprite != null ? "ok" : "FALLO"));
        return sprite;
    }

    // ------------------------------------------------------------------ HUD
    static void CrearBarraComida(StringBuilder log)
    {
        var hud = GameObject.Find("PlayerHUD_Canvas");
        if (hud == null) { log.AppendLine("sin HUD"); return; }
        var stamina = hud.transform.Find("StaminaBar_Container");
        var barra = hud.transform.Find("HungerBar_Container");
        if (barra == null)
        {
            barra = Object.Instantiate(stamina.gameObject, hud.transform).transform;
            barra.name = "HungerBar_Container";
            barra.SetSiblingIndex(stamina.GetSiblingIndex() + 1);
            barra.Find("StaminaBar_Frame").name = "HungerBar_Frame";
            barra.Find("HungerBar_Frame/ST_Text").name = "Food_Text";
        }
        var rt = (RectTransform)barra;
        rt.anchoredPosition = new Vector2(0f, 108f);
        rt.sizeDelta = new Vector2(400f, 22f);
        barra.Find("HungerBar_Frame/Track").GetComponent<Image>().color = new Color(0.25f, 0.13f, 0.04f, 0.95f);
        var fill = barra.Find("HungerBar_Frame/Track/Fill").GetComponent<Image>();
        fill.color = new Color(1f, 0.62f, 0.15f); fill.fillAmount = 1f;
        var txt = barra.Find("HungerBar_Frame/Food_Text").GetComponent<TextMeshProUGUI>();
        txt.text = "100/100 COMIDA"; txt.fontSize = 13;

        // icono de carne a la izquierda de la barra
        var iconT = barra.Find("Food_Icon");
        if (iconT == null)
        {
            var go = new GameObject("Food_Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(barra, false);
            iconT = go.transform;
        }
        var irt = (RectTransform)iconT;
        irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f); irt.pivot = new Vector2(1f, 0.5f);
        irt.anchoredPosition = new Vector2(-6f, 0f); irt.sizeDelta = new Vector2(30f, 30f);
        var img = iconT.GetComponent<Image>();
        img.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/iconos/Carne.png"); img.preserveAspect = true; img.raycastTarget = false;

        // la barra de acceso rapido sube un poco para dejar sitio
        var quick = hud.transform.Find("QuickAccessBar") as RectTransform;
        if (quick != null) quick.anchoredPosition = new Vector2(quick.anchoredPosition.x, 142f);

        var hunger = Object.FindAnyObjectByType<PlayerHunger>();
        if (hunger != null) { hunger.fillImage = fill; hunger.foodText = txt; EditorUtility.SetDirty(hunger); }
        EditorUtility.SetDirty(hud);
        log.AppendLine("barra de comida lista");
    }

    // ------------------------------------------------------------------ texturas de particulas
    static Texture2D TexturaParticula(string nombre, bool humo)
    {
        string path = $"{Dir}/Textures/{nombre}.png";
        if (!File.Exists(path))
        {
            int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = x / (n - 1f) * 2f - 1f, v = y / (n - 1f) * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a;
                    if (humo)
                    {
                        float nz = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.15f + 9f, y * 0.15f) * 0.4f;
                        a = Mathf.Clamp01((1f - r) * 1.4f) * Mathf.Lerp(0.45f, 1f, nz);
                        a = Mathf.SmoothStep(0f, 1f, a);
                    }
                    else a = Mathf.Exp(-r * r * 6f) * Mathf.Clamp01(1f - r);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------------ fogata
    static Campfire CrearFogata(StringBuilder log)
    {
        var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/Models/Bonfire.glb");
        if (modelo == null) { log.AppendLine("NO se encontro Bonfire.glb"); return null; }

        var viejo = GameObject.Find("Fogata");
        if (viejo != null) Object.DestroyImmediate(viejo);

        // junto al punto de inicio del jugador, apoyada en el suelo
        Vector3 pos = new Vector3(-29f, 5f, -44f);
        var jugador = GameObject.Find("Jugador");
        if (jugador != null) pos = jugador.transform.position + new Vector3(-3.5f, 5f, 6.5f);
        Physics.SyncTransforms();
        var hits = Physics.RaycastAll(pos, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => jugador == null || !h.transform.IsChildOf(jugador.transform)).OrderBy(h => h.distance).ToArray();
        pos.y = hits.Length > 0 ? hits[0].point.y : 0f;

        var root = new GameObject("Fogata");
        root.transform.position = pos;
        var fogata = root.AddComponent<Campfire>();

        var m = (GameObject)PrefabUtility.InstantiatePrefab(modelo);
        m.name = "Modelo";
        m.transform.SetParent(root.transform, false);
        m.transform.localScale = Vector3.one * 6f;
        m.transform.localPosition = new Vector3(0f, 0.2f, 0f);   // el modelo tiene el origen algo por encima de la base

        var col = root.AddComponent<CapsuleCollider>();
        col.radius = 1.15f; col.height = 1.6f; col.center = new Vector3(0f, 0.8f, 0f);

        // luz calida
        var luzGo = new GameObject("Luz"); luzGo.transform.SetParent(root.transform, false);
        luzGo.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        var luz = luzGo.AddComponent<Light>();
        luz.type = LightType.Point; luz.color = new Color(1f, 0.55f, 0.22f); luz.range = 20f; luz.intensity = 9f;
        luz.shadows = LightShadows.Soft; luz.shadowStrength = 0.85f;
        luzGo.AddComponent<UniversalAdditionalLightData>();
        fogata.luz = luz;

        // sonido
        var audio = root.AddComponent<AudioSource>();
        audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Dir + "/Audio/FuegoCrepitando.wav");
        audio.loop = true; audio.playOnAwake = true; audio.spatialBlend = 1f; audio.volume = 0.8f;
        audio.rolloffMode = AudioRolloffMode.Linear; audio.minDistance = 3f; audio.maxDistance = 35f;
        fogata.sonido = audio;

        // brillo de las brasas en la base (halo aditivo)
        var halo = Mat(Dir + "/Materials/Brasas_Halo.mat", "Custom/FireflyGlow");
        halo.SetColor("_Color", new Color(2.2f, 0.75f, 0.18f)); halo.SetFloat("_Size", 2.6f); halo.SetFloat("_Core", 0.6f); halo.SetFloat("_Intensity", 0.9f);
        var haloGo = new GameObject("BrilloBrasas"); haloGo.transform.SetParent(root.transform, false);
        haloGo.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        haloGo.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        var hr = haloGo.AddComponent<MeshRenderer>(); hr.sharedMaterial = halo; hr.shadowCastingMode = ShadowCastingMode.Off;

        // particulas
        var matLlama = Mat(Dir + "/Materials/Llama.mat", "Custom/FireFlame");
        var matChispa = Mat(Dir + "/Materials/Chispas.mat", "Custom/ParticleSoft");
        matChispa.SetTexture("_MainTex", TexturaParticula("Particula_Suave", false));
        matChispa.SetColor("_Tint", new Color(4f, 1.6f, 0.4f)); matChispa.SetFloat("_DstBlend", (float)BlendMode.One);
        var matHumo = Mat(Dir + "/Materials/Humo.mat", "Custom/ParticleSoft");
        matHumo.SetTexture("_MainTex", TexturaParticula("Particula_Humo", true));
        matHumo.SetColor("_Tint", Color.white); matHumo.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        EditorUtility.SetDirty(matChispa); EditorUtility.SetDirty(matHumo); EditorUtility.SetDirty(halo);

        Llamas(root.transform, matLlama);
        Chispas(root.transform, matChispa);
        Humo(root.transform, matHumo);

        // asador con carne (visible solo mientras cocinas)
        fogata.carneAsando = Asador(root.transform, log);

        log.AppendLine("fogata en " + pos);
        return fogata;
    }

    static ParticleSystem Sistema(Transform padre, string nombre, Vector3 pos, Material mat)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // el cono de emision apunta hacia arriba
        var ps = go.AddComponent<ParticleSystem>();
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = true; main.loop = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        return ps;
    }

    static Gradient Grad(params (float t, Color c, float a)[] k)
    {
        var g = new Gradient();
        g.SetKeys(k.Select(x => new GradientColorKey(x.c, x.t)).ToArray(), k.Select(x => new GradientAlphaKey(x.a, x.t)).ToArray());
        return g;
    }

    static void Llamas(Transform padre, Material mat)
    {
        var ps = Sistema(padre, "Llamas", new Vector3(0f, 0.35f, 0f), mat);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.05f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.8f, 1.25f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(1.3f, 2.1f);
        main.startSizeZ = 1f;
        main.maxParticles = 150;
        var em = ps.emission; em.rateOverTime = 38f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 6f; sh.radius = 0.5f;
        var col = ps.colorOverLifetime; col.enabled = true;
        col.color = Grad((0f, Color.white, 1f), (1f, Color.white, 0f));
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.35f)));
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 0.9f; noise.scrollSpeed = 1.2f; noise.quality = ParticleSystemNoiseQuality.Medium;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.VerticalBillboard;
        r.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV, ParticleSystemVertexStream.StableRandomX });
    }

    static void Chispas(Transform padre, Material mat)
    {
        var ps = Sistema(padre, "Chispas", new Vector3(0f, 0.6f, 0f), mat);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
        main.gravityModifier = -0.04f;
        main.maxParticles = 80;
        var em = ps.emission; em.rateOverTime = 11f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 3, 6, 0, 1.7f) });   // chasquidos ocasionales
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 22f; sh.radius = 0.45f;
        var col = ps.colorOverLifetime; col.enabled = true;
        col.color = Grad((0f, new Color(1f, 0.9f, 0.6f), 1f), (0.5f, new Color(1f, 0.55f, 0.2f), 0.9f), (1f, new Color(0.8f, 0.2f, 0.05f), 0f));
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.9f; noise.frequency = 1.3f; noise.scrollSpeed = 0.8f;
    }

    static void Humo(Transform padre, Material mat)
    {
        var ps = Sistema(padre, "Humo", new Vector3(0f, 1.7f, 0f), mat);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.16f, 0.15f, 0.14f);
        main.maxParticles = 60;
        var em = ps.emission; em.rateOverTime = 5f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 10f; sh.radius = 0.3f;
        var col = ps.colorOverLifetime; col.enabled = true;
        col.color = Grad((0f, Color.white, 0f), (0.15f, Color.white, 0.38f), (1f, new Color(0.7f, 0.7f, 0.75f), 0f));
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(1f, 2.8f)));
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        var noise = ps.noise; noise.enabled = true; noise.strength = 0.4f; noise.frequency = 0.4f;
    }

    static Renderer Asador(Transform padre, StringBuilder log)
    {
        var madera = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Materials/Palo.mat");
        if (madera == null)
        {
            madera = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            madera.SetColor("_BaseColor", new Color(0.32f, 0.2f, 0.11f)); madera.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(madera, Dir + "/Materials/Palo.mat");
        }
        GameObject Palo(string n, Vector3 p, Vector3 rot, float largo, Transform parent)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = n;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = p; g.transform.localRotation = Quaternion.Euler(rot);
            g.transform.localScale = new Vector3(0.07f, largo * 0.5f, 0.07f);
            g.GetComponent<Renderer>().sharedMaterial = madera;
            return g;
        }
        var soportes = new GameObject("Soportes").transform; soportes.SetParent(padre, false);
        Palo("SoporteIzq", new Vector3(-1.3f, 0.95f, 0f), Vector3.zero, 1.9f, soportes);
        Palo("SoporteDer", new Vector3(1.3f, 0.95f, 0f), Vector3.zero, 1.9f, soportes);

        var giro = new GameObject("Asador").transform; giro.SetParent(padre, false);
        giro.localPosition = new Vector3(0f, 1.85f, 0f);
        giro.localRotation = Quaternion.Euler(0f, 0f, 90f);   // el cilindro (eje Y local) queda horizontal
        Palo("Espeton", Vector3.zero, Vector3.zero, 2.9f, giro);

        Renderer carne = null;
        var carnePf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Drops/Carne.prefab");
        var mf = carnePf != null ? carnePf.GetComponentInChildren<MeshFilter>() : null;
        var mr = carnePf != null ? carnePf.GetComponentInChildren<MeshRenderer>() : null;
        var go = new GameObject("CarneAsando"); go.transform.SetParent(giro, false);
        if (mf != null && mf.sharedMesh != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = mr.sharedMaterials;
            float s = 0.75f / Mathf.Max(0.001f, mf.sharedMesh.bounds.size.magnitude);
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = -mf.sharedMesh.bounds.center * s;
            carne = r;
        }
        else
        {
            Object.DestroyImmediate(go);
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.name = "CarneAsando";
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(giro, false); g.transform.localScale = new Vector3(0.55f, 0.4f, 0.4f);
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, Dir + "/Materials/CarneAsando.mat");
            g.GetComponent<Renderer>().sharedMaterial = mat;
            carne = g.GetComponent<Renderer>();
        }
        log.AppendLine("asador: carne " + (mf != null ? "con modelo de Carne" : "con esfera"));
        return carne;
    }

    // ------------------------------------------------------------------ prueba en modo juego
    // "survplay": da carne al jugador, lo lleva a la fogata, cocina, come y prueba el bloqueo con un bicho.
    public static void BeginPlayTest()
    {
        File.WriteAllText("Temp/sv_log.txt", "");
        if (File.Exists("Temp/sv_done.txt")) File.Delete("Temp/sv_done.txt");
        SessionState.SetInt("sv_stage", 1);
        SessionState.SetFloat("sv_t0", 0f);
        EditorApplication.isPlaying = true;
    }

    static void Log(string s) { File.AppendAllText("Temp/sv_log.txt", s + "\n"); }

    static void Teleport(GameObject g, Vector3 p)
    {
        var cc = g.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        g.transform.position = p;
        if (cc != null) cc.enabled = true;
    }

    static void PlayTestUpdate()
    {
        int stage = SessionState.GetInt("sv_stage", 0);
        if (stage == 0 || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 0.25;
        float now = (float)EditorApplication.timeSinceStartup;
        if (SessionState.GetFloat("sv_t0", 0f) == 0f) { SessionState.SetFloat("sv_t0", now); return; }
        float t = now - SessionState.GetFloat("sv_t0", 0f);

        var jugador = GameObject.Find("Jugador");
        var hunger = Object.FindAnyObjectByType<PlayerHunger>();
        var health = Object.FindAnyObjectByType<PlayerHealth>();
        var inv = ControladorInventario.Instance;
        var fogata = Object.FindAnyObjectByType<Campfire>();
        var ciclo = DayNightCycle.Instance;
        string Estado() => $"comida={hunger?.CurrentFood:F1} vida={health?.CurrentHealth:F1} carne={inv?.GetTotalCount("Carne")} cocinada={inv?.GetTotalCount("Carne_Cocinada")}";

        if (stage == 1 && t > 2f)
        {
            if (ciclo != null) { ciclo.pausado = true; ciclo.hora = 21.5f; }
            hunger.SetFood(40f);
            health.TakeDamage(80f);
            inv.AddItem("Carne", null, 3);
            Vector3 p = fogata.transform.position + new Vector3(0f, 0.3f, -3.2f);
            Teleport(jugador, p);
            jugador.transform.rotation = Quaternion.LookRotation(fogata.transform.position - p);
            Log("inicio: " + Estado());
            fogata.Empezar();
            SessionState.SetInt("sv_stage", 2);
        }
        else if (stage == 2 && t > 5f)
        {
            Captura(Camera.main, "Temp/sv_cocinando.png");
            Log("cocinando=" + fogata.Cocinando + " " + Estado());
            SessionState.SetInt("sv_stage", 3);
        }
        else if (stage == 3 && t > 15f)
        {
            Log("tras cocinar: " + Estado());
            hunger.Comer("Carne_Cocinada");
            Log("tras comer cocinada: " + Estado());
            inv.AddItem("Carne", null, 1);
            hunger.Comer("Carne");
            Log("tras comer cruda: " + Estado());
            // bloqueo: meter al jugador dentro de un escarabajo
            var bicho = Object.FindObjectsByType<CreatureAI>().FirstOrDefault(c => c.kind == CreatureAI.Kind.Beetle) ?? Object.FindAnyObjectByType<CreatureAI>();
            bicho.enabled = false;
            SessionState.SetString("sv_bicho", bicho.name);
            Teleport(jugador, bicho.transform.position + Vector3.up * 0.2f + bicho.transform.right * 0.3f);
            SessionState.SetInt("sv_stage", 4);
        }
        else if (stage == 4 && t > 17f)
        {
            var bicho = GameObject.Find(SessionState.GetString("sv_bicho", ""));
            var hit = bicho.GetComponentsInChildren<Collider>().First();
            Vector3 d = jugador.transform.position - bicho.transform.position; d.y = 0f;
            var cc = jugador.GetComponent<CharacterController>();
            Vector3 c = jugador.transform.TransformPoint(cc.center);
            bool dentro = hit.bounds.Contains(new Vector3(c.x, hit.bounds.center.y, c.z));
            Log($"bloqueo: distancia al {bicho.name}={d.magnitude:F2} jugadorDentroDelBicho={dentro} colliderSize={hit.bounds.size}");
            SessionState.SetInt("sv_stage", 5);
        }
        else if (stage == 5)
        {
            // vista de la fogata de noche
            if (ciclo != null) ciclo.hora = 22f;
            Vector3 p = fogata.transform.position + new Vector3(2.5f, 0.3f, -6f);
            Teleport(jugador, p);
            jugador.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(fogata.transform.position - p, Vector3.up));
            fogata.Empezar();
            inv.AddItem("Carne", null, 2);
            SessionState.SetInt("sv_stage", 6);
        }
        else if (stage == 6 && t > 21f)
        {
            Captura(Camera.main, "Temp/sv_noche.png");
            if (ciclo != null) ciclo.hora = 13f;
            SessionState.SetInt("sv_stage", 7);
        }
        else if (stage == 7 && t > 23f)
        {
            Captura(Camera.main, "Temp/sv_dia.png");
            Log("FIN " + Estado());
            File.WriteAllText("Temp/sv_done.txt", "ok");
            SessionState.SetInt("sv_stage", 0);
            EditorApplication.isPlaying = false;
        }
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

    public static void DumpHud(StringBuilder log)
    {
        var hud = GameObject.Find("PlayerHUD_Canvas");
        if (hud == null) { log.AppendLine("sin HUD"); return; }
        void Rec(Transform t, int d)
        {
            var rt = t as RectTransform;
            log.AppendLine($"{new string(' ', d * 2)}{t.name} active={t.gameObject.activeSelf} pos={rt?.anchoredPosition} size={rt?.sizeDelta}");
            if (d < 3) foreach (Transform c in t) Rec(c, d + 1);
        }
        Rec(hud.transform, 0);
    }
}
