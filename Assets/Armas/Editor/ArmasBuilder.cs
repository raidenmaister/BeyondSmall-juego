using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Herramientas > Armas > Construir:
///  1. Fuentes del cuaderno (Patrick Hand y Permanent Marker) como fuentes TMP dinamicas en Resources/Fuentes.
///  2. Iconos de las armas, la venda y las piezas nuevas renderizados desde sus modelos (Resources/Armas/Iconos).
///  3. Las piezas nuevas (pedazos de lapiz y navajas de sacapuntas) repartidas por el salon como objetos recogibles,
///     con sus mallas y materiales guardados en Assets/Armas/Generado.
/// Se puede repetir: borra y regenera lo suyo.
/// </summary>
public static class ArmasBuilder
{
    const string Gen = "Assets/Armas/Generado";
    const string Iconos = "Assets/Armas/Resources/Armas/Iconos";
    const string Fuentes = "Assets/Progresion/Resources/Fuentes";

    static readonly (string id, string nombre, int cantidad)[] Piezas =
    {
        ("Lapiz", "Pedazo de lapiz", 4),
        ("Navaja", "Navaja de sacapuntas", 4),
    };

    [MenuItem("Herramientas/Armas/Construir")]
    public static void Construir()
    {
        var log = new StringBuilder();
        Carpetas();
        CrearFuentes(log);
        CrearIconos(log);
        ColocarPiezas(log);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        File.WriteAllText("Temp/armas_builder.txt", log.ToString());
        Debug.Log("[Armas] " + log);
    }

    static void Carpetas()
    {
        foreach (var p in new[] { Gen, Iconos, Fuentes })
            if (!AssetDatabase.IsValidFolder(p)) Directory.CreateDirectory(p);
        AssetDatabase.Refresh();
    }

    // ------------------------------------------------------------------ 1. fuentes
    static void CrearFuentes(StringBuilder log)
    {
        Fuente("Assets/Progresion/Fuentes/PatrickHand-Regular.ttf", Fuentes + "/PatrickHand SDF.asset", log);
        Fuente("Assets/Progresion/Fuentes/PermanentMarker-Regular.ttf", Fuentes + "/PermanentMarker SDF.asset", log);
    }

    static void Fuente(string ttf, string destino, StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(destino) != null) { log.AppendLine("fuente ya existe " + destino); return; }
        var f = AssetDatabase.LoadAssetAtPath<Font>(ttf);
        if (f == null) { log.AppendLine("falta " + ttf); return; }
        var fa = TMP_FontAsset.CreateFontAsset(f, 72, 8, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        fa.name = Path.GetFileNameWithoutExtension(destino);
        AssetDatabase.CreateAsset(fa, destino);
        // el atlas y el material van dentro del asset
        if (fa.atlasTextures != null) foreach (var t in fa.atlasTextures) if (t != null) { t.name = fa.name + " Atlas"; AssetDatabase.AddObjectToAsset(t, fa); }
        if (fa.material != null) { fa.material.name = fa.name + " Material"; AssetDatabase.AddObjectToAsset(fa.material, fa); }
        // caracteres del español por adelantado
        fa.TryAddCharacters(" abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789áéíóúÁÉÍÓÚñÑüÜ¿?¡!.,;:-+=()/%*·→★\"'<>[]_");
        EditorUtility.SetDirty(fa);
        log.AppendLine("fuente " + destino);
    }

    // ------------------------------------------------------------------ 2. iconos
    static void CrearIconos(StringBuilder log)
    {
        var ids = new List<string> { "Lapiz", "Navaja", "Venda" };
        foreach (var a in Armas.Todas) ids.Add(a.id);
        // el primer render de PreviewRenderUtility sale sin materiales listos: uno de calentamiento
        var calentar = ModelosArmas.Crear("Lanza");
        Object.DestroyImmediate(RenderIcono(calentar, 64));
        Object.DestroyImmediate(calentar);
        foreach (var id in ids)
        {
            var go = ModelosArmas.Crear(id);
            if (go == null) { log.AppendLine("sin modelo " + id); continue; }
            // en diagonal, como los iconos dibujados del inventario
            go.transform.rotation = Quaternion.Euler(0, 0, id == "Navaja" || id == "Venda" ? -20f : -42f);
            var tex = RenderIcono(go, 256);
            Object.DestroyImmediate(go);
            string ruta = Iconos + "/" + id + ".png";
            File.WriteAllBytes(ruta, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(ruta, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(ruta);
            ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single;
            ti.alphaIsTransparency = true; ti.mipmapEnabled = false;
            ti.SaveAndReimport();
            log.AppendLine("icono " + id);
        }
    }

    static Texture2D RenderIcono(GameObject modelo, int size)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            var inst = Object.Instantiate(modelo);
            inst.hideFlags = HideFlags.HideAndDontSave;
            pru.AddSingleGO(inst);
            var rends = inst.GetComponentsInChildren<Renderer>();
            Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
            var cam = pru.camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 100f; cam.fieldOfView = 22f;
            float dist = b.extents.magnitude / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.0f;
            cam.transform.position = b.center + (Quaternion.Euler(18f, -20f, 0f) * Vector3.back) * dist;
            cam.transform.LookAt(b.center);
            pru.lights[0].intensity = 1.1f; pru.lights[0].transform.rotation = Quaternion.Euler(40f, 30f, 0f);
            pru.lights[1].intensity = 0.5f; pru.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
            pru.ambientColor = new Color(0.45f, 0.45f, 0.45f, 1f);

            Color[] negro, blanco; int w, h;
            cam.backgroundColor = Color.black;
            pru.BeginStaticPreview(new Rect(0, 0, size, size)); cam.Render();
            var t1 = pru.EndStaticPreview(); negro = t1.GetPixels(); w = t1.width; h = t1.height;
            cam.backgroundColor = Color.white;
            pru.BeginStaticPreview(new Rect(0, 0, size, size)); cam.Render();
            var t2 = pru.EndStaticPreview(); blanco = t2.GetPixels();

            var res = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[negro.Length];
            for (int i = 0; i < px.Length; i++)
            {
                float a = 1f - ((blanco[i].r - negro[i].r) + (blanco[i].g - negro[i].g) + (blanco[i].b - negro[i].b)) / 3f;
                a = Mathf.Clamp01(a);
                px[i] = a > 0.003f ? new Color(Mathf.Clamp01(negro[i].r / a), Mathf.Clamp01(negro[i].g / a), Mathf.Clamp01(negro[i].b / a), a) : Color.clear;
            }
            // contorno de tinta de 2 px para que encaje con los iconos dibujados del cuaderno
            var conBorde = (Color[])px.Clone();
            for (int y = 2; y < h - 2; y++)
                for (int x = 2; x < w - 2; x++)
                {
                    int i = y * w + x;
                    if (px[i].a > 0.5f) continue;
                    float m = 0f;
                    for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) m = Mathf.Max(m, px[(y + dy) * w + x + dx].a);
                    if (m > 0.5f) conBorde[i] = new Color(0.18f, 0.15f, 0.13f, Mathf.Max(px[i].a, m * 0.9f));
                }
            res.SetPixels(conBorde); res.Apply();
            Object.DestroyImmediate(inst);
            return res;
        }
        finally { pru.Cleanup(); }
    }

    // ------------------------------------------------------------------ 3. piezas en el salon
    static void ColocarPiezas(StringBuilder log)
    {
        var escena = EditorSceneManager.GetActiveScene();
        Physics.SyncTransforms();
        foreach (var viejo in escena.GetRootGameObjects().Where(g => g.name.StartsWith("pieza_")).ToList()) Object.DestroyImmediate(viejo);

        var room = GameObject.Find("Room");
        if (room == null) { log.AppendLine("no hay Room"); return; }
        var rr = room.GetComponentsInChildren<Renderer>();
        Bounds rb = rr[0].bounds; foreach (var r in rr) rb.Encapsulate(r.bounds);
        float margen = 8f;
        var ocupados = escena.GetRootGameObjects().Where(g => g.GetComponent<WorldItem>() != null).Select(g => g.transform.position).ToList();
        var pc = Object.FindAnyObjectByType<PlayerController>();
        Vector3 jugador = pc != null ? pc.transform.position : Vector3.zero;
        var rng = new System.Random(1511);
        var nuevos = new List<GameObject>();

        foreach (var p in Piezas)
        {
            var icono = AssetDatabase.LoadAssetAtPath<Sprite>(Iconos + "/" + p.id + ".png");
            for (int n = 0; n < p.cantidad; n++)
            {
                Vector3 pos = default; bool ok = false;
                for (int intento = 0; intento < 500 && !ok; intento++)
                {
                    float x = Mathf.Lerp(rb.min.x + margen, rb.max.x - margen, (float)rng.NextDouble());
                    float z = Mathf.Lerp(rb.min.z + margen, rb.max.z - margen, (float)rng.NextDouble());
                    RaycastHit hit;
                    if (!Physics.Raycast(new Vector3(x, 3f, z), Vector3.down, out hit, 10f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.normal.y < 0.95f || Mathf.Abs(hit.point.y) > 0.4f) continue;
                    var q = hit.point;
                    if (Physics.CheckBox(q + Vector3.up * 0.9f, new Vector3(0.8f, 0.6f, 0.8f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if ((q - jugador).sqrMagnitude < 36f) continue;
                    if (ocupados.Any(e => (new Vector2(e.x, e.z) - new Vector2(q.x, q.z)).sqrMagnitude < 9f)) continue;
                    pos = q; ok = true;
                }
                if (!ok) { log.AppendLine("sin sitio para " + p.id); continue; }
                ocupados.Add(pos);

                var go = ModelosArmas.Crear(p.id);
                go.name = $"pieza_{p.id}_{n + 1:00}";
                SceneManager_Mover(go, escena);
                GuardarMallas(go);
                // tumbado en el suelo, con un giro al azar
                go.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0) * Quaternion.Euler(0, 0, 90f);
                go.transform.localScale = Vector3.one * 1.1f;
                var rs = go.GetComponentsInChildren<Renderer>();
                Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                go.transform.position += new Vector3(pos.x - b.center.x, pos.y - b.min.y + 0.01f, pos.z - b.center.z);
                // colision para que se pueda ver/apuntar (como los demas objetos del suelo)
                var col = go.AddComponent<BoxCollider>();
                col.center = go.transform.InverseTransformPoint(b.center); col.size = go.transform.InverseTransformVector(b.size);
                col.size = new Vector3(Mathf.Abs(col.size.x), Mathf.Abs(col.size.y), Mathf.Abs(col.size.z));
                var wi = go.AddComponent<WorldItem>();
                wi.itemId = p.id; wi.displayName = p.nombre; wi.itemIcon = icono; wi.quantity = 1;
                nuevos.Add(go);
            }
            log.AppendLine("colocadas piezas " + p.id);
        }

        var mgr = Object.FindAnyObjectByType<FloorItemPromptManager>(FindObjectsInactive.Include);
        if (mgr != null)
        {
            var so = new SerializedObject(mgr);
            var lista = so.FindProperty("floorObjects");
            if (lista != null && lista.arraySize > 0)
            {
                // quitar referencias muertas de una ejecucion anterior
                for (int i = lista.arraySize - 1; i >= 0; i--)
                    if (lista.GetArrayElementAtIndex(i).objectReferenceValue == null) lista.DeleteArrayElementAtIndex(i);
                foreach (var g in nuevos) { lista.arraySize++; lista.GetArrayElementAtIndex(lista.arraySize - 1).objectReferenceValue = g; }
                so.ApplyModifiedProperties();
            }
        }
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        log.AppendLine("piezas colocadas: " + nuevos.Count + "; escena guardada");
    }

    static void SceneManager_Mover(GameObject go, UnityEngine.SceneManagement.Scene escena)
    {
        if (go.scene != escena) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, escena);
    }

    /// <summary>Mallas y materiales del modelo como assets (si no, la escena no los guarda bien).</summary>
    static void GuardarMallas(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            var m = mf.sharedMesh;
            if (m == null || AssetDatabase.Contains(m)) continue;
            string ruta = Gen + "/Malla_" + Limpio(m.name) + ".asset";
            var existente = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
            if (existente != null) { mf.sharedMesh = existente; continue; }
            AssetDatabase.CreateAsset(m, ruta);
        }
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>())
        {
            var mat = mr.sharedMaterial;
            if (mat == null || AssetDatabase.Contains(mat)) continue;
            string ruta = Gen + "/" + Limpio(mat.name) + ".mat";
            var existente = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (existente != null) { mr.sharedMaterial = existente; continue; }
            AssetDatabase.CreateAsset(mat, ruta);
        }
    }

    static string Limpio(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace('.', '_'); }
}
