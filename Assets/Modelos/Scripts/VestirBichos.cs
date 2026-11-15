using UnityEngine;

/// <summary>
/// Al cargar la escena viste a todos los bichos con los modelos descargados (ModeloVivo).
/// Cada tipo se viste en la pieza que su IA ya mueve, para heredar sus movimientos (embestidas, botes, volteretas):
///   CreatureAI   cada hijo con CreatureVisual (las hormigas son 3 por grupo y desaparecen de una en una)
///   HormigaNido  la raiz (al morir la propia IA la pone patas arriba)
///   Reinas, ratas de la cloaca, Rey Rata, abejas de la colmena y Bolita: su hijo "Visual"
///   RatAI y SpiderAI: la raiz
///   LarvaAbeja: la raiz (su script la retuerce y la aplasta al morir)
/// La lombriz no trae animacion: ModeloVivo la hace ondular deformando su malla.
/// </summary>
public static class VestirBichos
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= AlCargar;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += AlCargar;
        Todo();
    }

    static void AlCargar(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) { Todo(); }

    public static void Todo()
    {
        if (Object.FindAnyObjectByType<PlayerController>() == null) return;
        int n = 0;
        foreach (var ai in Object.FindObjectsByType<CreatureAI>(FindObjectsInactive.Include))
        {
            string clave = Clave(ai.kind);
            if (clave == null) continue;
            foreach (var cv in ai.GetComponentsInChildren<CreatureVisual>(true))
                if (ModeloVivo.Vestir(cv.gameObject, clave, 1f, false) != null) n++;
        }
        foreach (var h in Object.FindObjectsByType<HormigaNido>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(h.gameObject, "Hormiga", 1f, false, 70f) != null) n++;
        foreach (var r in Object.FindObjectsByType<ReinaHormiga>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(Visual(r), "ReinaHormiga", 1f, false, 250f) != null) n++;
        foreach (var r in Object.FindObjectsByType<ReinaAbeja>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(Visual(r), "Abeja", 1f, true, 250f) != null) n++;
        foreach (var a in Object.FindObjectsByType<AbejaColmena>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(Visual(a), "Abeja", 1f, true, 70f) != null) n++;
        foreach (var r in Object.FindObjectsByType<RataCloaca>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(Visual(r), "Rata", 1f, true) != null) n++;
        foreach (var r in Object.FindObjectsByType<ReyRata>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(Visual(r), "Rata", 1f, true, 250f) != null) n++;
        foreach (var r in Object.FindObjectsByType<RatAI>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(r.gameObject, "Rata", 1f, true) != null) n++;
        foreach (var s in Object.FindObjectsByType<SpiderAI>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(s.gameObject, "Arana", 1f, false) != null) n++;
        foreach (var l in Object.FindObjectsByType<LarvaAbeja>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(l.gameObject, "Larva", 1f, false, 60f) != null) n++;
        foreach (var b in Object.FindObjectsByType<Bolita>(FindObjectsInactive.Include))
            if (ModeloVivo.Vestir(Visual(b), "Bolita", 1f, false, 300f) != null) n++;
        Debug.Log("[VestirBichos] " + n + " bichos con modelo nuevo");
    }

    /// <summary>
    /// Material del suelo del exterior (proyectado en XZ del mundo): el monticulo de la madriguera de la lombriz lo usa
    /// para parecer tierra removida del propio suelo (antes era una esfera de plastico marron brillante).
    /// </summary>
    static Material tierra;
    public static Material MaterialTierra()
    {
        if (tierra != null) return tierra;
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            if (mr.sharedMaterial != null && mr.sharedMaterial.shader.name == "Exterior/Suelo") { tierra = mr.sharedMaterial; break; }
        return tierra;
    }

    static GameObject Visual(Component c)
    {
        var v = c.transform.Find("Visual");
        return v != null ? v.gameObject : c.gameObject;
    }

    static string Clave(CreatureAI.Kind k)
    {
        switch (k)
        {
            case CreatureAI.Kind.Ants: return "Hormiga";
            case CreatureAI.Kind.Bee: return "Abeja";
            case CreatureAI.Kind.Caterpillar: return "Oruga";
            case CreatureAI.Kind.Butterfly: return "Mariposa";
            case CreatureAI.Kind.Centipede: return "Ciempies";
            case CreatureAI.Kind.Woodlouse: return "Cochinilla";
            case CreatureAI.Kind.Beetle: return "Escarabajo";
            case CreatureAI.Kind.Worm: return "Lombriz";
            default: return null;
        }
    }
}
