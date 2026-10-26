using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CollisionFix
{
    const string Flag = "Assets/Editor/.collisionfix2_done";

    [InitializeOnLoadMethod]
    static void AutoRun()
    {
        if (File.Exists(Flag)) return;
        EditorApplication.delayCall += () => { if (!File.Exists(Flag)) Run(); };
    }

    [MenuItem("Tools/Fix Collisions")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.name.StartsWith("Class_rom")) return;
        int added = 0, players = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool plank = t.name == "Plank" || t.name == "LeaningPlank";
                bool trash = InTrash(t) && t.parent != null && t.parent.name == "Trash" && !t.name.ToLower().Contains("branch");
                if (!(plank || trash) || t.GetComponentInChildren<Collider>() != null) continue;
                if (plank) t.gameObject.AddComponent<BoxCollider>();
                else AddBox(t.gameObject);
                added++;
            }
        foreach (var cc in Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { cc.stepOffset = 0.6f; players++; }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Flag, "ok");
        Debug.Log($"[CollisionFix] colliders: {added}, controllers: {players}");
    }

    static bool InTrash(Transform t) { for (var p = t; p != null; p = p.parent) if (p.name == "Trash") return true; return false; }

    static void AddBox(GameObject go)
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
        var bc = go.AddComponent<BoxCollider>(); bc.center = lb.center; bc.size = lb.size;
    }
}
