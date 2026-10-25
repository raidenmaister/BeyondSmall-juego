using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Importa los modelos de "Assets/nuevos items 25-10-26", genera iconos de inventario para los que no tienen
/// y los esparce por el suelo del salón como objetos recogibles (WorldItem).
/// </summary>
public static class NuevosItemsSetup
{
    const string Dir = "Assets/nuevos items 25-10-26";
    const string IconDir = "Assets/iconos";

    class Def
    {
        public string file, id, display, icon;
        public float scale;      // escala sobre el modelo (los FBX vienen normalizados a ~2 unidades)
        public int count;
        public Def(string file, string id, string display, string icon, float scale, int count)
        { this.file = file; this.id = id; this.display = display; this.icon = icon; this.scale = scale; this.count = count; }
    }

    static readonly Def[] Defs =
    {
        new Def("clip_completo_pintado",      "Clips",             "Clip",              "Clips",             0.50f, 4),
        new Def("chincheta_01_pintado",       "Chincheta",         "Chincheta",         "Chincheta",         0.48f, 4),
        new Def("chincheta_doblada_pintado",  "Chincheta_Doblada", "Chincheta doblada", null,                0.48f, 3),
        new Def("liga_pintada",               "Ligas",             "Liga",              "Ligas",             0.85f, 4),
        new Def("broche_papel_pintado",       "broche",            "Broche",            "broche",            0.60f, 3),
        new Def("alambre_coloreado",          "Alambre",           "Alambre",           "Alambre",           0.35f, 0),   // 368k vertices: fuera de la escena
        new Def("rollo_alambre_pintado",      "Alambre",           "Rollo de alambre",  "Alambre",           0.60f, 0),   // 368k vertices: fuera de la escena
        new Def("tela_pintado",               "Tela",              "Tela",              null,                0.80f, 4),
        new Def("tornillo_pintado",           "Tornillo",          "Tornillo",          null,                0.50f, 5),
        new Def("tuerca_pintado",             "Tuerca",            "Tuerca",            null,                0.50f, 5),
    };

    // ------------------------------------------------------------------
    //  1) Iconos renderizados desde el propio modelo
    // ------------------------------------------------------------------
    [MenuItem("Tools/NuevosItems/Crear iconos")]
    public static void CreateIcons()
    {
        var log = new StringBuilder();
        foreach (var d in Defs.Where(x => x.icon == null))
        {
            string name = d.id;
            string pngPath = $"{IconDir}/{name}.png";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{d.file}.fbx");
            if (model == null) { log.AppendLine("sin modelo " + d.file); continue; }
            var tex = RenderIcon(model, 256);
            File.WriteAllBytes(pngPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(pngPath, ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (ti != null)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
            log.AppendLine("icono creado: " + pngPath);
        }

        // registrar iconos en iconDatabase
        var ctrl = Object.FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (ctrl != null)
        {
            var so = new SerializedObject(ctrl);
            var db = so.FindProperty("iconDatabase");
            foreach (var name in Defs.Select(x => x.id).Distinct())
            {
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconDir}/{name}.png");
                if (sp == null) continue;
                bool has = false;
                for (int i = 0; i < db.arraySize; i++)
                    if (db.GetArrayElementAtIndex(i).objectReferenceValue != null && db.GetArrayElementAtIndex(i).objectReferenceValue.name == name) has = true;
                if (!has) { db.arraySize++; db.GetArrayElementAtIndex(db.arraySize - 1).objectReferenceValue = sp; log.AppendLine("BD += " + name); }
            }
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(ctrl.gameObject.scene);
        }
        File.WriteAllText("Temp/nuevos_iconos.txt", log.ToString());
        Debug.Log("[NuevosItems] " + log);
    }

    static Texture2D RenderIcon(GameObject model, int size)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            var inst = Object.Instantiate(model);
            inst.hideFlags = HideFlags.HideAndDontSave;
            pru.AddSingleGO(inst);

            var rends = inst.GetComponentsInChildren<Renderer>();
            Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);

            // orientar para que la dimension mas larga quede en pantalla
            inst.transform.rotation = Quaternion.Euler(0, 0, 0);
            var cam = pru.camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 200f;
            cam.fieldOfView = 25f;
            float dist = b.extents.magnitude / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
            cam.transform.position = b.center + (Quaternion.Euler(48f, -35f, 0f) * Vector3.back) * dist;
            cam.transform.LookAt(b.center);

            pru.lights[0].intensity = 0.35f; pru.lights[0].transform.rotation = Quaternion.Euler(40f, 20f, 0f);
            pru.lights[1].intensity = 0.15f; pru.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
            pru.ambientColor = new Color(0.3f, 0.3f, 0.3f, 1f);

            // Se renderiza sobre fondo negro y sobre fondo blanco para obtener un alfa real
            Color[] onBlack, onWhite; int w, h;
            cam.backgroundColor = Color.black;
            pru.BeginStaticPreview(new Rect(0, 0, size, size)); cam.Render();
            var t1 = pru.EndStaticPreview(); onBlack = t1.GetPixels(); w = t1.width; h = t1.height;
            cam.backgroundColor = Color.white;
            pru.BeginStaticPreview(new Rect(0, 0, size, size)); cam.Render();
            var t2 = pru.EndStaticPreview(); onWhite = t2.GetPixels();

            var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var outPx = new Color[onBlack.Length];
            for (int i = 0; i < outPx.Length; i++)
            {
                float a = 1f - ((onWhite[i].r - onBlack[i].r) + (onWhite[i].g - onBlack[i].g) + (onWhite[i].b - onBlack[i].b)) / 3f;
                a = Mathf.Clamp01(a);
                Color c = a > 0.003f ? new Color(Mathf.Clamp01(onBlack[i].r / a), Mathf.Clamp01(onBlack[i].g / a), Mathf.Clamp01(onBlack[i].b / a), a) : new Color(0, 0, 0, 0);
                outPx[i] = c;
            }
            result.SetPixels(outPx);
            result.Apply();
            Object.DestroyImmediate(inst);
            return result;
        }
        finally { pru.Cleanup(); }
    }

    // ------------------------------------------------------------------
    //  2) Esparcir en el suelo
    // ------------------------------------------------------------------
    [MenuItem("Tools/NuevosItems/Esparcir en el salon")]
    public static void Scatter()
    {
        var log = new StringBuilder();
        var scene = EditorSceneManager.GetActiveScene();
        Physics.SyncTransforms();

        // limpiar una ejecucion previa
        foreach (var old in scene.GetRootGameObjects().Where(g => g.name.StartsWith("nuevo_")).ToList())
            Object.DestroyImmediate(old);

        var roots = scene.GetRootGameObjects();
        var existing = roots.Where(g => g.GetComponent<WorldItem>() != null).Select(g => g.transform.position).ToList();
        var pc = Object.FindAnyObjectByType<PlayerController>();
        Vector3 playerPos = pc != null ? pc.transform.position : Vector3.zero;

        // zona: bounds del salon encogidos
        var room = GameObject.Find("Room");
        var rr = room.GetComponentsInChildren<Renderer>();
        Bounds rb = rr[0].bounds; foreach (var r in rr) rb.Encapsulate(r.bounds);
        float margin = 7f;
        float x0 = rb.min.x + margin, x1 = rb.max.x - margin, z0 = rb.min.z + margin, z1 = rb.max.z - margin;
        log.AppendLine($"zona x[{x0:F1},{x1:F1}] z[{z0:F1},{z1:F1}]");

        var placed = new List<Vector3>();
        var rng = new System.Random(2610);
        int total = 0, failed = 0;

        foreach (var d in Defs)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/{d.file}.fbx");
            if (model == null) { log.AppendLine("falta " + d.file); continue; }

            Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>($"{IconDir}/{(d.icon ?? d.id)}.png");

            for (int n = 0; n < d.count; n++)
            {
                Vector3 pos = default; bool ok = false;
                for (int tries = 0; tries < 400 && !ok; tries++)
                {
                    float x = Mathf.Lerp(x0, x1, (float)rng.NextDouble());
                    float z = Mathf.Lerp(z0, z1, (float)rng.NextDouble());
                    if (!Physics.Raycast(new Vector3(x, 3f, z), Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.normal.y < 0.95f || Mathf.Abs(hit.point.y) > 0.4f) continue;
                    Vector3 p = hit.point;
                    if (Physics.CheckBox(p + Vector3.up * 0.9f, new Vector3(0.8f, 0.6f, 0.8f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if ((p - playerPos).sqrMagnitude < 25f) continue;
                    if (existing.Any(e => (new Vector3(e.x, 0, e.z) - new Vector3(p.x, 0, p.z)).sqrMagnitude < 2.5f * 2.5f)) continue;
                    if (placed.Any(e => (new Vector3(e.x, 0, e.z) - new Vector3(p.x, 0, p.z)).sqrMagnitude < 4f * 4f)) continue;
                    pos = p; ok = true;
                }
                if (!ok) { failed++; log.AppendLine($"sin lugar para {d.file} #{n + 1}"); continue; }

                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                go.name = $"nuevo_{d.file}_{n + 1:00}";
                go.transform.position = Vector3.zero;
                go.transform.rotation = Quaternion.identity;
                go.transform.localScale = Vector3.one * d.scale;

                OrientFlat(go);
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0) * go.transform.rotation;

                // apoyar sobre el suelo
                var rs = go.GetComponentsInChildren<Renderer>();
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y + 0.01f, pos.z - b.center.z);

                var wi = go.AddComponent<WorldItem>();
                wi.itemId = d.id; wi.displayName = d.display; wi.itemIcon = icon; wi.quantity = 1;

                placed.Add(pos);
                total++;
            }
        }
        log.AppendLine($"colocados {total}, sin lugar {failed}");

        // registrar en FloorItemPromptManager (lista serializada)
        var mgr = Object.FindAnyObjectByType<FloorItemPromptManager>(FindObjectsInactive.Include);
        if (mgr != null)
        {
            var news = scene.GetRootGameObjects().Where(g => g.name.StartsWith("nuevo_")).ToList();
            var so = new SerializedObject(mgr);
            var list = so.FindProperty("floorObjects");
            if (list != null && list.arraySize > 0)
            {
                foreach (var g in news) { list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = g; }
                so.ApplyModifiedProperties();
                log.AppendLine("registrados en FloorItemPromptManager: " + news.Count);
            }
            else log.AppendLine("FloorItemPromptManager sin lista: se detectaran por WorldItem al iniciar");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        log.AppendLine("escena guardada");
        File.WriteAllText("Temp/nuevos_scatter.txt", log.ToString());
        Debug.Log("[NuevosItems] " + log);
    }

    /// <summary>Acuesta el objeto: el eje mas corto (o el largo si esta de pie) pasa a ser el vertical.</summary>
    static void OrientFlat(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        Vector3 s = b.size;
        float max = Mathf.Max(s.x, s.y, s.z), min = Mathf.Min(s.x, s.y, s.z);
        if (min <= 0.55f * max)
        {
            if (Mathf.Approximately(min, s.z)) go.transform.rotation = Quaternion.Euler(90f, 0, 0) * go.transform.rotation;     // eje fino Z -> vertical
            else if (Mathf.Approximately(min, s.x)) go.transform.rotation = Quaternion.Euler(0, 0, 90f) * go.transform.rotation; // eje fino X -> vertical
            // si el fino ya es Y, esta acostado
        }
        else if (s.y >= max * 0.999f && s.y > 1.25f * Mathf.Max(s.x, s.z))
        {
            go.transform.rotation = Quaternion.Euler(90f, 0, 0) * go.transform.rotation;                                       // objeto largo de pie -> acostado
        }
    }
}
