using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class DecayTmp
{
    static double next;
    static DecayTmp()
    {
        File.WriteAllText("Temp/tmp_loaded.txt", DateTime.UtcNow.ToString("o"));
        EditorApplication.update += Update;
    }
    static void Update()
    {
        int stage = SessionState.GetInt("tmp_stage", 0);
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        if (stage == 0)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists("Temp/tmp_cmd.txt")) return;
            string cmd = File.ReadAllText("Temp/tmp_cmd.txt").Trim(); File.Delete("Temp/tmp_cmd.txt");
            var log = new StringBuilder();
            try
            {
                if (cmd == "build") { DecayBuilder.Build(log); Info(log); }
                else if (cmd == "fill") DecayBuilder.FillEmpty(log);
                else if (cmd == "topdown") TopDown(log);
                else if (cmd == "ceil") Ceil(log);
                else if (cmd == "meshtest")
                {
                    var spawned = new System.Collections.Generic.List<GameObject>();
                    Vector3 origin = new Vector3(-30f, 25f, -40f);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Enemies/DropTextures/Alas.png");
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Drops/Alas.prefab");
                    for (int i = 0; i < 4; i++)
                    {
                        var w = UnityEngine.Object.Instantiate(pf, origin + new Vector3(i * 3f, 0f, 0f), Quaternion.identity);
                        foreach (var c in w.GetComponentsInChildren<MonoBehaviour>()) c.enabled = false;
                        Material mm;
                        if (i == 0)
                        {
                            var dbg = new Texture2D(64, 64, TextureFormat.RGB24, false); for (int yy = 0; yy < 64; yy++) for (int xx = 0; xx < 64; xx++) dbg.SetPixel(xx, yy, new Color(xx / 63f, yy / 63f, 0f)); dbg.Apply();
                            mm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); mm.SetTexture("_BaseMap", dbg);
                            var srcM = w.GetComponent<MeshFilter>().sharedMesh; var cp = new Mesh(); cp.vertices = srcM.vertices; cp.normals = srcM.normals; cp.uv = srcM.uv; cp.triangles = srcM.triangles; cp.RecalculateBounds();
                            w.GetComponent<MeshFilter>().sharedMesh = cp;
                            var smesh = w.GetComponent<MeshFilter>().sharedMesh; log.AppendLine("mesh " + smesh.name + " uvcount=" + smesh.uv.Length + " v0uv=" + smesh.uv[0] + " readable=" + smesh.isReadable + " sub=" + smesh.subMeshCount);
                        }
                        else if (i == 1)
                        {
                            var dbg2 = new Texture2D(64, 64, TextureFormat.RGB24, false); for (int yy = 0; yy < 64; yy++) for (int xx = 0; xx < 64; xx++) dbg2.SetPixel(xx, yy, new Color(xx / 63f, yy / 63f, 0f)); dbg2.Apply();
                            mm = new Material(Shader.Find("Universal Render Pipeline/Unlit")); mm.SetTexture("_BaseMap", dbg2);
                            w.GetComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Enemies/DropMeshes/Hueso.asset");
                        }
                        else if (i == 2) { mm = new Material(Shader.Find("Universal Render Pipeline/Lit")); mm.SetColor("_BaseColor", Color.red); }
                        else { mm = new Material(Shader.Find("Sprites/Default")); mm.mainTexture = tex; }
                        w.GetComponent<MeshRenderer>().sharedMaterial = mm; spawned.Add(w);
                    }
                    var cg = new GameObject("__DropCam"); var cam = cg.AddComponent<Camera>();
                    cam.fieldOfView = 40; cam.nearClipPlane = 0.2f; cam.farClipPlane = 100f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.5f, 0.55f);
                    cam.transform.position = origin + new Vector3(4.5f, 6.5f, 0f); cam.transform.rotation = Quaternion.Euler(90, 0, 0);
                    var rt = new RenderTexture(1200, 400, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                    var tx = new Texture2D(1200, 400, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, 1200, 400), 0, 0); tx.Apply();
                    File.WriteAllBytes("Temp/meshtest.png", tx.EncodeToPNG());
                    RenderTexture.active = null; cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tx); UnityEngine.Object.DestroyImmediate(cg);
                    foreach (var g in spawned) UnityEngine.Object.DestroyImmediate(g);
                    log.AppendLine("ok");
                }
                else if (cmd == "quadtest")
                {
                    var spawned = new System.Collections.Generic.List<GameObject>();
                    Vector3 origin = new Vector3(-30f, 8f, -40f);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Enemies/DropTextures/Alas.png");
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.transform.position = origin; q.transform.rotation = Quaternion.Euler(90, 0, 0); q.transform.localScale = Vector3.one * 2.5f;
                    var ml = new Material(Shader.Find("Universal Render Pipeline/Lit")); ml.SetTexture("_BaseMap", tex); q.GetComponent<Renderer>().sharedMaterial = ml; spawned.Add(q);
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Drops/Alas.prefab");
                    var w = UnityEngine.Object.Instantiate(pf, origin + new Vector3(3f, 0f, 0f), Quaternion.identity);
                    foreach (var c in w.GetComponentsInChildren<MonoBehaviour>()) c.enabled = false;
                    var m2 = new Material(Shader.Find("Universal Render Pipeline/Lit")); m2.SetTexture("_BaseMap", tex); m2.SetFloat("_Cull", 0f);
                    w.GetComponent<MeshRenderer>().sharedMaterial = m2; spawned.Add(w);
                    var cg = new GameObject("__DropCam"); var cam = cg.AddComponent<Camera>();
                    cam.fieldOfView = 40; cam.nearClipPlane = 0.2f; cam.farClipPlane = 100f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.5f, 0.55f);
                    cam.transform.position = origin + new Vector3(1.5f, 5f, 0f); cam.transform.rotation = Quaternion.Euler(90, 0, 0);
                    var rt = new RenderTexture(1000, 500, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                    var tx = new Texture2D(1000, 500, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, 1000, 500), 0, 0); tx.Apply();
                    File.WriteAllBytes("Temp/quadtest.png", tx.EncodeToPNG());
                    RenderTexture.active = null; cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tx); UnityEngine.Object.DestroyImmediate(cg);
                    foreach (var g in spawned) UnityEngine.Object.DestroyImmediate(g);
                    log.AppendLine("ok");
                }
                else if (cmd == "alasunlit")
                {
                    var spawned = new System.Collections.Generic.List<GameObject>();
                    Vector3 origin = new Vector3(-30f, 8f, -40f);
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Enemies/DropTextures/Alas.png");
                    for (int i = 0; i < 3; i++)
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Drops/Alas.prefab");
                        var go = UnityEngine.Object.Instantiate(pf, origin + new Vector3(i * 3f, 0f, 0f), Quaternion.Euler(0, 20, 0));
                        foreach (var c in go.GetComponentsInChildren<MonoBehaviour>()) c.enabled = false;
                        var mr = go.GetComponent<MeshRenderer>();
                        if (i == 1) { var mu = new Material(Shader.Find("Universal Render Pipeline/Unlit")); mu.SetTexture("_BaseMap", tex); mr.sharedMaterial = mu; }
                        if (i == 2) { var ml = new Material(Shader.Find("Universal Render Pipeline/Lit")); ml.SetTexture("_BaseMap", tex); mr.sharedMaterial = ml; }
                        spawned.Add(go);
                    }
                    var cg = new GameObject("__DropCam"); var cam = cg.AddComponent<Camera>();
                    cam.fieldOfView = 40; cam.nearClipPlane = 0.2f; cam.farClipPlane = 100f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.5f, 0.55f);
                    cam.transform.position = origin + new Vector3(3f, 5f, -2.5f); cam.transform.LookAt(origin + new Vector3(3f, 0f, 0f));
                    var rt = new RenderTexture(1200, 500, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                    var tx = new Texture2D(1200, 500, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, 1200, 500), 0, 0); tx.Apply();
                    File.WriteAllBytes("Temp/alasunlit.png", tx.EncodeToPNG());
                    RenderTexture.active = null; cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tx); UnityEngine.Object.DestroyImmediate(cg);
                    foreach (var g in spawned) UnityEngine.Object.DestroyImmediate(g);
                    log.AppendLine("ok");
                }
                else if (cmd == "normals")
                {
                    foreach (var g in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Enemies/DropMeshes" }))
                    {
                        var p = AssetDatabase.GUIDToAssetPath(g); var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
                        int nan = 0, up = 0, down = 0; var ns = m.normals; var vs = m.vertices;
                        for (int i = 0; i < ns.Length; i++) { if (float.IsNaN(ns[i].x) || ns[i].sqrMagnitude < 0.5f) nan++; else if (ns[i].y > 0.5f) up++; else if (ns[i].y < -0.5f) down++; }
                        // las normales deben apuntar hacia fuera: comparar con la direccion desde el centro
                        int outward = 0, inward = 0; var c = m.bounds.center;
                        for (int i = 0; i < ns.Length; i++) { var d = vs[i] - c; if (d.sqrMagnitude < 1e-6f) continue; if (Vector3.Dot(ns[i], d) > 0) outward++; else inward++; }
                        log.AppendLine(System.IO.Path.GetFileNameWithoutExtension(p) + ": v=" + ns.Length + " nan/zero=" + nan + " up=" + up + " down=" + down + " outward=" + outward + " inward=" + inward);
                    }
                }
                else if (cmd.StartsWith("cull "))
                {
                    float c = float.Parse(cmd.Substring(5));
                    foreach (var n in new[] { "Alas", "Exoesqueleto" })
                    {
                        var mm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Enemies/DropMaterials/" + n + ".mat");
                        mm.SetFloat("_Cull", c); EditorUtility.SetDirty(mm);
                    }
                    AssetDatabase.SaveAssets();
                    log.AppendLine("cull=" + c);
                }
                else if (cmd == "alasuv")
                {
                    var m = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Enemies/DropMeshes/Alas.asset");
                    var v = m.vertices; var uv = m.uv; log.AppendLine("bounds " + m.bounds);
                    for (int i = 0; i < v.Length; i += 23) log.AppendLine(v[i].ToString("F2") + " -> " + uv[i].ToString("F2"));
                    var tx = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Enemies/DropTextures/Alas.png");
                    log.AppendLine("tex " + tx.width + "x" + tx.height + " mips=" + tx.mipmapCount + " fmt=" + tx.format + " wrap=" + tx.wrapMode);
                    var imp = (TextureImporter)AssetImporter.GetAtPath("Assets/Enemies/DropTextures/Alas.png");
                    log.AppendLine("importer: type=" + imp.textureType + " srgb=" + imp.sRGBTexture + " alpha=" + imp.alphaSource + " max=" + imp.maxTextureSize + " readable=" + imp.isReadable);
                }
                else if (cmd == "dropshot")
                {
                    var ids = new[] { "Hueso", "Piel", "Colmillo", "PataCiempies", "Cuerno", "Exoesqueleto", "Aguijon", "Alas", "Carne" };
                    var spawned = new System.Collections.Generic.List<GameObject>();
                    Vector3 origin = new Vector3(-30f, 8f, -40f);
                    for (int i = 0; i < ids.Length; i++)
                    {
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Drops/" + ids[i] + ".prefab");
                        var go = UnityEngine.Object.Instantiate(pf, origin + new Vector3((i % 5) * 3.2f, 0f, -(i / 5) * 3f), Quaternion.Euler(0, 20, 0));
                        foreach (var c in go.GetComponentsInChildren<MonoBehaviour>()) c.enabled = false;
                        spawned.Add(go);
                    }
                    var cg = new GameObject("__DropCam"); var cam = cg.AddComponent<Camera>();
                    cam.fieldOfView = 40; cam.nearClipPlane = 0.2f; cam.farClipPlane = 100f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.45f, 0.5f, 0.55f);
                    cam.transform.position = origin + new Vector3(6.4f, 9f, -6f); cam.transform.LookAt(origin + new Vector3(6.4f, 0f, -1.5f));
                    var rt = new RenderTexture(1400, 700, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                    var tx = new Texture2D(1400, 700, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, 1400, 700), 0, 0); tx.Apply();
                    File.WriteAllBytes("Temp/dropshot.png", tx.EncodeToPNG());
                    RenderTexture.active = null; cam.targetTexture = null;
                    UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tx); UnityEngine.Object.DestroyImmediate(cg);
                    foreach (var g in spawned) UnityEngine.Object.DestroyImmediate(g);
                    log.AppendLine("dropshot ok");
                }
                else if (cmd == "uvs")
                {
                    foreach (var g in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Enemies/DropMeshes" }))
                    {
                        var p = AssetDatabase.GUIDToAssetPath(g); var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
                        var uv = m.uv; Vector2 mn = new Vector2(9, 9), mx = new Vector2(-9, -9);
                        foreach (var q in uv) { mn = Vector2.Min(mn, q); mx = Vector2.Max(mx, q); }
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Drops/" + System.IO.Path.GetFileNameWithoutExtension(p) + ".prefab");
                        var mr = pf != null ? pf.GetComponent<MeshRenderer>() : null; var mat = mr != null ? mr.sharedMaterial : null;
                        log.AppendLine(System.IO.Path.GetFileNameWithoutExtension(p) + ": uv " + uv.Length + " min " + mn + " max " + mx + " mat=" + (mat != null ? mat.name + " tex=" + (mat.GetTexture("_BaseMap") != null ? mat.GetTexture("_BaseMap").name : "NULL") + " col=" + mat.GetColor("_BaseColor") + " shader=" + mat.shader.name : "null") + " meshSame=" + (pf != null && pf.GetComponent<MeshFilter>().sharedMesh == m));
                    }
                }
                else if (cmd == "playtest") EnemyPlayTest.Begin();
                else if (cmd.StartsWith("shots")) Shots(log, cmd);
                else if (cmd == "reimport")
                {
                    AssetDatabase.Refresh();
                    foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Enemies/Prefabs" })) AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
                    log.AppendLine("prefabs reimportados");
                }
                else if (cmd == "meshes")
                {
                    foreach (var g in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Enemies/Meshes" }))
                    {
                        var p = AssetDatabase.GUIDToAssetPath(g); var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
                        log.AppendLine(p + " -> " + (m == null ? "NULL" : m.vertexCount + " v, bounds " + m.bounds.size));
                    }
                    foreach (var pf in new[] { "Oruga", "Ciempies", "Mariposa" })
                    {
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemies/Prefabs/Enemy_" + pf + ".prefab");
                        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) log.AppendLine("prefab " + pf + "/" + mf.name + " mesh=" + (mf.sharedMesh == null ? "NULL" : mf.sharedMesh.name));
                    }
                }
                else if (cmd.StartsWith("info ")) Info2(log, cmd.Substring(5));
                else if (cmd == "drops" || cmd == "icons" || cmd == "prefabs" || cmd == "place" || cmd == "enemies") EnemiesBuilder.Step(cmd, log);
                else if (cmd == "roots") Roots(log);
                else if (cmd.StartsWith("sheet")) Sheet(log, cmd);
                else if (cmd == "slab") DecayBuilder.AddCeilingSlab(log);
                else if (cmd.StartsWith("view")) View(log, cmd);
                else if (cmd == "report") Report(log);
                else if (cmd == "fixspider")
                {
                    foreach (var s in UnityEngine.Object.FindObjectsByType<SpiderAI>())
                    {
                        var bc = s.GetComponent<BoxCollider>();
                        if (bc == null) continue;
                        PrefabUtility.RevertObjectOverride(bc, InteractionMode.AutomatedAction);
                        log.AppendLine("spider " + s.name + " collider ahora size=" + bc.size + " center=" + bc.center + " world=" + bc.bounds.size);
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
                    UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                }
                else if (cmd == "play") { SessionState.SetInt("tmp_stage", 1); SessionState.SetString("tmp_t0", ""); EditorApplication.isPlaying = true; return; }
            }
            catch (Exception e) { log.AppendLine("ERROR " + e); }
            File.WriteAllText("Temp/tmp_out.txt", log + "FIN\n");
        }
        else if (stage == 1)
        {
            if (!EditorApplication.isPlaying) return;
            if (SessionState.GetString("tmp_t0", "") == "") SessionState.SetString("tmp_t0", EditorApplication.timeSinceStartup.ToString("R"));
            double t = EditorApplication.timeSinceStartup - double.Parse(SessionState.GetString("tmp_t0", "0"));
            if (t > 5 && SessionState.GetInt("tmp_shot", 0) == 0) { SessionState.SetInt("tmp_shot", 1); ScreenCapture.CaptureScreenshot(Path.GetFullPath("Temp/tmp_shot.png")); }
            if (t > 8) { SessionState.SetInt("tmp_stage", 2); EditorApplication.isPlaying = false; }
        }
        else if (stage == 2 && !EditorApplication.isPlaying)
        {
            SessionState.SetInt("tmp_stage", 0); SessionState.SetInt("tmp_shot", 0);
            File.WriteAllText("Temp/tmp_out.txt", "FIN play\n");
        }
    }
    static void TopDown(StringBuilder log)
    {
        var go = new GameObject("__TopCam");
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true; cam.orthographicSize = 96f;
        cam.transform.position = new Vector3(0, 46, -6); cam.transform.rotation = Quaternion.Euler(90, 0, 0);
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 80f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.magenta;
        var rt = new RenderTexture(1800, 1800, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1800, 1800, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1800, 1800), 0, 0); tex.Apply();
        File.WriteAllBytes("Temp/topdown.png", tex.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(go);
        log.AppendLine("topdown.png listo");
    }
    static void View(StringBuilder log, string cmd)
    {
        var a = cmd.Split(' ').Skip(1).Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var go = new GameObject("__ViewCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = new Vector3(a[0], a[1], a[2]); cam.transform.LookAt(new Vector3(a[3], a[4], a[5]));
        cam.fieldOfView = 60; cam.nearClipPlane = 0.5f; cam.farClipPlane = 1500f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.3f, 0.3f, 0.35f);
        var rt = new RenderTexture(1400, 900, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        var tex = new Texture2D(1400, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1400, 900), 0, 0); tex.Apply();
        File.WriteAllBytes("Temp/view.png", tex.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(go);
        log.AppendLine("view.png listo");
    }
    static void Sheet(StringBuilder log, string cmd)
    {
        var guids = AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Enemies/Models" });
        var paths = guids.Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".glb")).OrderBy(p => p).ToList();
        int n = paths.Count; int cell = 320;
        var sheet = new Texture2D(cell * 3, cell * ((n + 2) / 3), TextureFormat.RGB24, false);
        var pru = new PreviewRenderUtility();
        for (int i = 0; i < n; i++)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            var inst = UnityEngine.Object.Instantiate(model); inst.hideFlags = HideFlags.HideAndDontSave; pru.AddSingleGO(inst);
            var rs = inst.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) { log.AppendLine("sin renderers: " + paths[i]); continue; }
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            log.AppendLine($"{System.IO.Path.GetFileName(paths[i])}: size={b.size} center={b.center} renderers={rs.Length} skinned={inst.GetComponentsInChildren<SkinnedMeshRenderer>().Length} legacyAnim={inst.GetComponent<Animation>() != null} animator={inst.GetComponent<Animator>() != null}");
            var cam = pru.camera; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.55f, 0.6f, 0.65f);
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 500f; cam.fieldOfView = 30f;
            float d = b.extents.magnitude / Mathf.Sin(15f * Mathf.Deg2Rad) * 1.05f;
            bool top = cmd.Contains("top"); cam.transform.position = b.center + (top ? new Vector3(0f, 1f, 0.001f) : new Vector3(0.9f, 0.45f, 0f).normalized) * d; cam.transform.LookAt(b.center, top ? Vector3.forward : Vector3.up);
            pru.lights[0].intensity = 1f; pru.lights[0].transform.rotation = Quaternion.Euler(40, 60, 0); pru.ambientColor = new Color(0.45f, 0.45f, 0.45f);
            pru.BeginStaticPreview(new Rect(0, 0, cell, cell)); cam.Render(); var tex = pru.EndStaticPreview();
            sheet.SetPixels((i % 3) * cell, sheet.height - cell * (i / 3 + 1), cell, cell, tex.GetPixels());
            UnityEngine.Object.DestroyImmediate(inst);
        }
        pru.Cleanup(); sheet.Apply();
        File.WriteAllBytes("Temp/sheet.png", sheet.EncodeToPNG());
        log.AppendLine("orden: " + string.Join(", ", paths.Select(System.IO.Path.GetFileName)));
    }
    static void Roots(StringBuilder log)
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        foreach (var r in scene.GetRootGameObjects())
        {
            var comps = string.Join(",", r.GetComponents<Component>().Select(c => c == null ? "null" : c.GetType().Name).Where(n => n != "Transform"));
            var rs = r.GetComponentsInChildren<Renderer>(); string bs = "";
            if (rs.Length > 0) { var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds); bs = " size=" + b.size.ToString("F1") + " min.y=" + b.min.y.ToString("F2"); }
            bool prefab = PrefabUtility.IsPartOfPrefabInstance(r);
            log.AppendLine($"{r.name} pos={r.transform.position.ToString("F1")} prefab={prefab} [{comps}]{bs}");
        }
    }
    static void Info2(StringBuilder log, string name)
    {
        var go = GameObject.Find(name);
        if (go == null) { log.AppendLine("no existe " + name); return; }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>();
            log.AppendLine($"{r.name} [{r.GetType().Name}] enabled={r.enabled} active={r.gameObject.activeInHierarchy} bounds={r.bounds} mesh={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name + " v=" + mf.sharedMesh.vertexCount + " b=" + mf.sharedMesh.bounds : "null")} mats={string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "/" + m.shader.name : "null"))} scale={r.transform.lossyScale}");
        }
    }
    static void Shots(StringBuilder log, string cmd)
    {
        var names = cmd.Split(' ').Skip(1).ToArray();
        int cw = 560, ch = 360, cols = 2, rows = (names.Length + 1) / 2;
        var sheet = new Texture2D(cw * cols, ch * rows, TextureFormat.RGB24, false);
        var go = new GameObject("__ShotCam"); var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 50; cam.nearClipPlane = 0.2f; cam.farClipPlane = 400f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.3f, 0.3f, 0.35f);
        for (int i = 0; i < names.Length; i++)
        {
            var target = GameObject.Find(names[i]);
            if (target == null) { log.AppendLine("no existe " + names[i]); continue; }
            var rs = target.GetComponentsInChildren<Renderer>(); Bounds b = new Bounds(target.transform.position + Vector3.up * 0.5f, Vector3.one);
            if (rs.Length > 0 && rs[0].bounds.size.sqrMagnitude > 0.001f) { b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); }
            float d = Mathf.Max(5f, b.extents.magnitude * 3.2f);
            var dir = new Vector3(0.45f, 0.62f, -0.75f).normalized;
            cam.transform.position = b.center + dir * d; cam.transform.LookAt(b.center);
            var rt = new RenderTexture(cw, ch, 24); cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            var tex = new Texture2D(cw, ch, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, cw, ch), 0, 0); tex.Apply();
            sheet.SetPixels((i % cols) * cw, sheet.height - ch * (i / cols + 1), cw, ch, tex.GetPixels());
            RenderTexture.active = null; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
            log.AppendLine(names[i] + " bounds=" + b.size);
        }
        UnityEngine.Object.DestroyImmediate(go); sheet.Apply();
        File.WriteAllBytes("Temp/shots.png", sheet.EncodeToPNG());
    }
    static void Ceil(StringBuilder log)
    {
        var root = GameObject.Find("DECAY_ROOT");
        log.AppendLine("DECAY_ROOT activo=" + (root != null && root.activeInHierarchy));
        foreach (Transform g in root.transform)
        {
            int rc = g.GetComponentsInChildren<Renderer>(true).Length, en = g.GetComponentsInChildren<Renderer>(false).Length;
            log.AppendLine($"grupo {g.name}: activo={g.gameObject.activeSelf} hijos={g.childCount} renderers={rc} activos={en}");
        }
        var cls = GameObject.Find("class");
        foreach (Transform t in cls.transform)
        {
            var r = t.GetComponent<Renderer>();
            if (r != null && (t.name == "Cube" || t.name == "Room")) log.AppendLine($"class/{t.name} activo={t.gameObject.activeSelf} renderer={r.enabled} bounds={r.bounds} mats={string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "null"))}");
        }
        var ceil = root.transform.Find("Ceiling");
        if (ceil != null) { var b = new Bounds(); bool f = true; foreach (var r in ceil.GetComponentsInChildren<Renderer>()) { if (f) { b = r.bounds; f = false; } else b.Encapsulate(r.bounds); } log.AppendLine("Ceiling bounds=" + b); }
    }
    static void Report(StringBuilder log)
    {
        string P(Transform t) { string s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
        foreach (var c in UnityEngine.Object.FindObjectsByType<Collider>())
        {
            if (!c.enabled || !c.gameObject.activeInHierarchy) continue;
            var r = c.GetComponent<Renderer>();
            var rs = c.GetComponentsInChildren<Renderer>();
            bool visible = rs.Any(x => x.enabled);
            var b = c.bounds;
            string path = P(c.transform);
            if (visible)
            {
                var rb = rs.First(x => x.enabled).bounds; foreach (var x in rs) if (x.enabled) rb.Encapsulate(x.bounds);
                float ratio = (b.size.x * b.size.y * b.size.z) / Mathf.Max(1e-4f, rb.size.x * rb.size.y * rb.size.z);
                if (ratio > 1.8f || path.StartsWith("Spider")) log.AppendLine($"RATIO {ratio:F1} {path} coll={b.size} rend={rb.size} rendCenter={rb.center}");
            }
            if (path.StartsWith("DECAY_ROOT") && visible && !(c is MeshCollider)) { if (b.size.y < 0.01f) log.AppendLine("FLAT " + path); continue; }
            log.AppendLine($"{path} [{c.GetType().Name}] trigger={c.isTrigger} visible={visible} layer={LayerMask.LayerToName(c.gameObject.layer)} center={b.center} size={b.size}");
        }
    }
    static void Info(StringBuilder log)
    {
        var j = GameObject.Find("Jugador");
        log.AppendLine("Jugador componentes: " + string.Join(", ", j.GetComponents<Component>().Select(c => c.GetType().Name)));
        foreach (var c in j.GetComponentsInChildren<Collider>()) log.AppendLine("  collider " + c.GetType().Name + " en " + c.name);
        var cls = GameObject.Find("class");
        log.AppendLine("colliders en class: " + string.Join(", ", cls.GetComponentsInChildren<Collider>().Select(c => c.name + ":" + c.GetType().Name)));
        var root = GameObject.Find("DECAY_ROOT");
        log.AppendLine("colliders en DECAY_ROOT: " + root.GetComponentsInChildren<Collider>().Length);
        Physics.SyncTransforms();
        int hit = 0, tot = 0;
        foreach (var bc in root.GetComponentsInChildren<BoxCollider>())
        {
            tot++;
            var p = bc.bounds.center + Vector3.up * (bc.bounds.extents.y + 5);
            if (Physics.Raycast(p, Vector3.down, out var h, 200) && h.collider == bc) hit++;
        }
        log.AppendLine($"raycast desde arriba acierta su collider: {hit}/{tot}");
        var rs = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        log.AppendLine("renderers en escena: " + rs.Length);
    }
}
