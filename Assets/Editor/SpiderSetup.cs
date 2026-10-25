using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Crea el controlador, el prefab y coloca la araña en el salón. Se ejecuta una vez al compilar
/// (o manualmente desde Tools > Spider > Setup).
/// </summary>
public static class SpiderSetup
{
    const string FbxPath = "Assets/Spider/Spider.fbx";
    const string ControllerPath = "Assets/Spider/Spider_Controller.controller";
    const string PrefabPath = "Assets/Spider/Spider_Enemy.prefab";
    const string MatDir = "Assets/Spider/Materials";
    const string LogPath = "Assets/Spider/setup_log.txt";
    const float TargetWidth = 1.3f;

    static readonly List<string> log = new List<string>();

    [InitializeOnLoadMethod]
    static void AutoRun()
    {
        if (SessionState.GetBool("SpiderSetupRan2", false)) return;
        SessionState.SetBool("SpiderSetupRan2", true);
        EditorApplication.delayCall += () =>
        {
            if (File.Exists(FbxPath) && !File.Exists(PrefabPath)) Run();
        };
    }

    [MenuItem("Tools/Spider/Setup")]
    public static void Run()
    {
        log.Clear();
        try { RunInternal(); }
        catch (System.Exception e) { Log("ERROR: " + e); }
        File.WriteAllText(LogPath, string.Join("\n", log));
        AssetDatabase.Refresh();
    }

    static void Log(string s) { log.Add(s); Debug.Log("[SpiderSetup] " + s); }

    static void RunInternal()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (model == null) { Log("FBX no importado todavia."); return; }

        // 1. Ajustar loops de los clips
        var importer = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
        if (importer.animationType != ModelImporterAnimationType.Generic)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.SaveAndReimport();
            Log("animationType -> Generic");
        }
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips)
        {
            string n = c.name.ToLower();
            c.loopTime = n.Contains("idle") && !n.Contains("jumpi") || n.Contains("walk");
            Log($"Clip: {c.name} loop={c.loopTime}");
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();

        var allClips = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToList();
        AnimationClip Find(string key) =>
            allClips.FirstOrDefault(c => c.name.ToLower().EndsWith(key));

        var idle = Find("idle");
        var walk = Find("walk");
        var attack = Find("attack");
        var death = Find("death");
        if (idle == null || walk == null || attack == null || death == null)
        {
            Log("Faltan clips: " + string.Join(", ", allClips.Select(c => c.name)));
            return;
        }

        // 2. Controller
        if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = ctrl.layers[0].stateMachine;
        var sIdle = sm.AddState("Idle"); sIdle.motion = idle;
        var sRun = sm.AddState("Run"); sRun.motion = walk; sRun.speed = 2.2f;
        var sAtk = sm.AddState("Attack"); sAtk.motion = attack;
        var sDeath = sm.AddState("Death"); sDeath.motion = death;
        sm.defaultState = sIdle;
        // Attack vuelve a Idle al terminar (la IA vuelve a disparar el ataque cada intervalo)
        var back = sAtk.AddTransition(sIdle);
        back.hasExitTime = true; back.exitTime = 1f; back.duration = 0.1f;
        EditorUtility.SetDirty(ctrl);

        // 3. Materiales URP
        Directory.CreateDirectory(MatDir);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        model = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        var matMap = new Dictionary<Material, Material>();
        foreach (var r in inst.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                if (!matMap.TryGetValue(src, out var dst))
                {
                    Color col = Color.white;
                    if (src.HasProperty("_BaseColor")) col = src.GetColor("_BaseColor");
                    else if (src.HasProperty("_Color")) col = src.GetColor("_Color");
                    string p = $"{MatDir}/Spider_{src.name}.mat";
                    dst = AssetDatabase.LoadAssetAtPath<Material>(p);
                    if (dst == null)
                    {
                        dst = new Material(shader) { name = "Spider_" + src.name };
                        AssetDatabase.CreateAsset(dst, p);
                    }
                    dst.SetColor("_BaseColor", col);
                    dst.SetFloat("_Smoothness", 0.25f);
                    EditorUtility.SetDirty(dst);
                    matMap[src] = dst;
                    Log($"Material {src.name} color={col}");
                }
                mats[i] = dst;
            }
            r.sharedMaterials = mats;
        }

        // 4. Prefab: raiz + modelo hijo
        var root = new GameObject("Spider_Enemy");
        inst.transform.SetParent(root.transform, false);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;

        var rends = inst.GetComponentsInChildren<Renderer>();
        Bounds b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        float width = Mathf.Max(b.size.x, b.size.z);
        float scale = width > 0.0001f ? TargetWidth / width : 1f;
        inst.transform.localScale = Vector3.one * scale;
        // apoyar sobre el suelo y centrar
        b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        inst.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z) + root.transform.position;
        b = rends[0].bounds;
        foreach (var r in rends) b.Encapsulate(r.bounds);
        Log($"Escala={scale:F3}, bounds size={b.size}");

        var anim = inst.GetComponent<Animator>();
        if (anim == null) anim = inst.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        anim.applyRootMotion = false;

        var box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(b.center);
        box.size = new Vector3(b.size.x, Mathf.Max(b.size.y, 0.6f), b.size.z);

        var health = root.AddComponent<RatHealth>();
        var so = new SerializedObject(health);
        so.FindProperty("enemyName").stringValue = "Araña";
        so.FindProperty("maxHealth").floatValue = 80f;
        so.FindProperty("currentHealth").floatValue = 80f;
        so.ApplyModifiedPropertiesWithoutUndo();

        var ai = root.AddComponent<SpiderAI>();
        ai.animator = anim;
        ai.health = health;
        ai.deathParticlesPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RatDeathParticles.prefab");

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Log("Prefab creado: " + PrefabPath);

        // 5. Colocar en la escena
        PlaceInScene(prefab);
    }

    static void PlaceInScene(GameObject prefab)
    {
        var scene = EditorSceneManager.GetActiveScene();
        Log("Escena activa: " + scene.name);
        if (!scene.name.Contains("Class_rom")) { Log("La escena activa no es el salon; no se coloco."); return; }
        if (Object.FindAnyObjectByType<SpiderAI>() != null) { Log("Ya hay una araña en la escena."); return; }

        var player = Object.FindAnyObjectByType<PlayerController>();
        var rat = Object.FindAnyObjectByType<RatAI>();
        Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;
        Vector3 ratPos = rat != null ? rat.transform.position : playerPos + Vector3.right * 5f;
        Log($"Jugador={playerPos} Rata={ratPos}");

        // Limites del salon a partir de los renderers del objeto "Room"
        Bounds room;
        var roomGo = GameObject.Find("Room");
        var rr = roomGo != null ? roomGo.GetComponentsInChildren<Renderer>() : new Renderer[0];
        if (rr.Length > 0) { room = rr[0].bounds; foreach (var r in rr) room.Encapsulate(r.bounds); }
        else room = new Bounds(playerPos, new Vector3(20, 4, 20));
        Log("Salon bounds: " + room);

        Physics.SyncTransforms();
        Vector3? best = null;
        float bestScore = float.MinValue;
        for (float x = room.min.x + 1.5f; x <= room.max.x - 1.5f; x += 1f)
        {
            for (float z = room.min.z + 1.5f; z <= room.max.z - 1.5f; z += 1f)
            {
                var from = new Vector3(x, room.max.y + 1f, z);
                if (!Physics.Raycast(from, Vector3.down, out var hit, room.size.y + 5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.9f) continue;
                Vector3 p = hit.point;
                if (Physics.CheckBox(p + Vector3.up * 0.9f, new Vector3(0.9f, 0.7f, 0.9f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;
                float dPlayer = Vector3.Distance(p, playerPos);
                float dRat = Vector3.Distance(p, ratPos);
                if (dPlayer < 6f || dRat < 3f) continue;
                float score = -Mathf.Abs(dPlayer - 9f) + Mathf.Min(dRat, 6f) * 0.3f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
        }

        Vector3 pos = best ?? (ratPos + new Vector3(4f, 0f, 0f));
        Log("Posicion araña: " + pos + (best.HasValue ? "" : " (fallback)"));

        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.transform.position = pos;
        Vector3 look = playerPos - pos; look.y = 0f;
        if (look.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(look);
        go.name = "Spider_Enemy";
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = go;
        Log("Araña colocada y escena guardada.");
    }
}
