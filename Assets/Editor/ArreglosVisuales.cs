using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Herramientas > Arreglos > Corregir texturas encimadas.
/// Superficies coplanares o casi (a menos de 2 mm) que parpadean una sobre otra ("z-fighting"):
///   1. Anillo de suelo exterior duplicado alrededor de la alcantarilla (la cueva de la cloaca lo trae y el exterior tambien).
///   2. Anillo de suelo duplicado alrededor del arbol de la colmena.
///   3. Manchas, hojas y musgo del salon (DECAY): parches transparentes a 0.2 mm unos de otros -> separados y con orden fijo.
///   4. Agua de la cloaca: estanques y canales que se cruzan a la misma altura.
///   5. Hamsterlandia: el disco de viruta de los conectores contra el piso de los tubos; la tapa de la caja de pañuelos.
/// Se puede repetir sin problema. Hay que ejecutarla en modo edicion (en Play las mallas estan combinadas).
/// </summary>
public static class ArreglosVisuales
{
    [MenuItem("Herramientas/Arreglos/Corregir texturas encimadas")]
    public static void Corregir()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Arreglos] Sal de Play primero"); return; }
        var log = new StringBuilder();
        var suelo = AssetDatabase.LoadAssetAtPath<Material>("Assets/Exterior/Materiales/Suelo.mat");
        int a = RecortarSuelo("CLOACA", suelo, c => ZonaJuego.DistanciaCloaca(c.x, c.z) > 0.6f && c.y > -6f);
        log.AppendLine("cloaca: triangulos de suelo duplicado quitados " + a);
        int b = RecortarSuelo("ARBOL_COLMENA", suelo, c =>
        {
            float dx = c.x - ZonaJuego.arbolCentro.x, dz = c.y > 30f ? 0f : c.z - ZonaJuego.arbolCentro.y;
            return c.y < 30f && Mathf.Sqrt(dx * dx + dz * dz) > ZonaJuego.arbolRadio - 0.7f;
        });
        log.AppendLine("arbol: triangulos de suelo duplicado quitados " + b);
        log.AppendLine("arbol: suelo de dentro tapado por el de fuera quitado " + SueloTapado("ARBOL_COLMENA"));
        log.AppendLine("hormiguero: suelo de dentro tapado por el de fuera quitado " + SueloTapado("HORMIGUERO"));
        log.AppendLine("aula: triangulos de suelo exterior bajo la losa del aula quitados " + SueloBajoAula());
        log.AppendLine("decay: " + Decay());
        log.AppendLine("agua: " + Agua());
        log.AppendLine("hamster: " + Hamster());
        AssetDatabase.SaveAssets();
        var escena = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        System.IO.File.WriteAllText("Temp/arreglos_visuales.txt", log.ToString());
        Debug.Log("[Arreglos] " + log);
    }

    /// <summary>Quita de las mallas de una zona los triangulos de suelo exterior que ya cubre el suelo del exterior.</summary>
    static int RecortarSuelo(string raiz, Material suelo, System.Func<Vector3, bool> sobra)
    {
        var go = GameObject.Find(raiz);
        if (go == null || suelo == null) return 0;
        int total = 0;
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mats = mr.sharedMaterials;
            int sub = System.Array.IndexOf(mats, suelo);
            if (sub < 0) continue;
            var mf = mr.GetComponent<MeshFilter>();
            var m = mf != null ? mf.sharedMesh : null;
            if (m == null || sub >= m.subMeshCount || !AssetDatabase.Contains(m)) continue;
            var v = m.vertices;
            var tris = m.GetTriangles(sub);
            var quedan = new List<int>(tris.Length);
            var tr = mr.transform;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 c = tr.TransformPoint((v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f);
                if (sobra(c)) { total++; continue; }
                quedan.Add(tris[i]); quedan.Add(tris[i + 1]); quedan.Add(tris[i + 2]);
            }
            if (quedan.Count == tris.Length) continue;
            m.SetTriangles(quedan, sub);
            EditorUtility.SetDirty(m);
        }
        return total;
    }

    /// <summary>
    /// Suelo de una zona (cualquier material) que queda unos centimetros por debajo del suelo exterior: en la boca del
    /// arbol y del hormiguero los dos se pisaban y parpadeaban de lejos. Se quitan los triangulos que mira hacia arriba,
    /// estan a menos de 25 cm por debajo del suelo exterior y tienen ese suelo exterior justo encima (no en el hueco).
    /// </summary>
    static int SueloTapado(string raiz)
    {
        var go = GameObject.Find(raiz);
        if (go == null) return 0;
        Physics.SyncTransforms();
        int total = 0;
        foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mf = mr.GetComponent<MeshFilter>();
            var m = mf != null ? mf.sharedMesh : null;
            if (m == null || !AssetDatabase.Contains(m)) continue;
            var v = m.vertices; var tr = mr.transform; bool cambio = false;
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                var tris = m.GetTriangles(sub);
                var quedan = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = tr.TransformPoint(v[tris[i]]), b = tr.TransformPoint(v[tris[i + 1]]), c = tr.TransformPoint(v[tris[i + 2]]);
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    Vector3 cen = (a + b + c) / 3f;
                    bool tapado = false;
                    if (n.sqrMagnitude > 1e-10f && Mathf.Abs(n.normalized.y) > 0.7f)
                    {
                        float alt = ZonaJuego.Altura(cen.x, cen.z);
                        if (cen.y < alt + 0.005f && cen.y > alt - 0.25f)
                            foreach (var h in Physics.RaycastAll(new Vector3(cen.x, alt + 2f, cen.z), Vector3.down, 2.3f))
                                if (h.collider.name == "Suelo" && !h.collider.transform.IsChildOf(go.transform) && h.point.y >= cen.y) { tapado = true; break; }
                    }
                    if (tapado) { total++; continue; }
                    quedan.Add(tris[i]); quedan.Add(tris[i + 1]); quedan.Add(tris[i + 2]);
                }
                if (quedan.Count == tris.Length) continue;
                m.SetTriangles(quedan, sub); cambio = true;
            }
            if (cambio) EditorUtility.SetDirty(m);
        }
        return total;
    }

    [MenuItem("Herramientas/Arreglos/Apoyar objetos flotantes")]
    public static void ApoyarFlotantes()
    {
        if (Application.isPlaying) { Debug.LogWarning("[Arreglos] Sal de Play primero"); return; }
        var log = new StringBuilder();
        int bajados = 0, subidos = 0, dejados = 0;
        var movidos = new HashSet<Transform>();
        foreach (var g in AuditoriaVisual.GruposFlotantes())
        {
            string ruta = Ruta(g.piezas[0].transform);
            if (ruta.StartsWith("HAMSTERLANDIA")) { dejados++; continue; }          // se arregla en su builder
            if (ruta.Contains("/Patches/")) { dejados++; continue; }                 // calcomanias: ver PegarManchas()
            if (ruta.Contains("DECAY_ROOT/Ceiling")) { dejados++; continue; }        // tejado roto: ver AsentarTejado (solo plantas, solo hacia abajo)
            // tablas "apoyadas" en el aire (y con la otra punta metida en el suelo): se tumban en el suelo
            if (ruta.EndsWith("/Planks/Plank") && g.piezas.Count == 1) { Tumbar(g.piezas[0].transform); bajados++; continue; }
            // lo del techo se cuelga de lo que tenga encima; si encima solo hay cielo (techo roto) se apoya en la viga de abajo
            bool techo = ruta.Contains("/Ceiling") && g.subir <= 10f;
            float d = techo ? g.subir : g.bajar;
            if (d > (techo ? 10f : 6f) || d <= 0.005f) { dejados++; log.AppendLine("sin tocar (" + d.ToString("F2") + "): " + ruta); continue; }
            Vector3 mover = (techo ? Vector3.up : Vector3.down) * (d - 0.004f);
            // las piezas de mas arriba del grupo (si su padre tiene LODGroup, el padre: que no se separen los LOD)
            var raices = new HashSet<Transform>();
            foreach (var r in g.piezas)
            {
                var t = r.transform;
                if (t.parent != null && t.parent.GetComponent<LODGroup>() != null) t = t.parent;
                raices.Add(t);
            }
            foreach (var t in raices)
            {
                bool hijoDeOtra = false;
                for (var p = t.parent; p != null; p = p.parent) if (raices.Contains(p)) { hijoDeOtra = true; break; }
                if (hijoDeOtra || movidos.Contains(t)) continue;
                Undo.RecordObject(t, "Apoyar");
                t.position += mover;
                movidos.Add(t);
            }
            if (techo) subidos++; else bajados++;
        }
        var escena = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        log.Insert(0, "bajados " + bajados + ", subidos al techo " + subidos + ", sin tocar " + dejados + "\n");
        System.IO.File.WriteAllText("Temp/apoyar_flotantes.txt", log.ToString());
        Debug.Log("[Arreglos] " + log.ToString().Split('\n')[0]);
    }

    /// <summary>
    /// Plantas del tejado roto que quedaron sobre un hueco (solo cielo encima y 50 m de vacio debajo): se buscan las
    /// vigas, losas y paneles del tejado alrededor (hasta 8 m) y se asientan encima del mas cercano.
    /// </summary>
    [MenuItem("Herramientas/Arreglos/Asentar plantas del tejado")]
    public static void AsentarTejado()
    {
        if (Application.isPlaying) return;
        var raizDecay = GameObject.Find("DECAY_ROOT");
        if (raizDecay == null) return;
        // colisiones temporales de la estructura del tejado (todo lo de DECAY_ROOT por encima de 40 m que no sea planta)
        var tmp = new GameObject("__Tejado") { hideFlags = HideFlags.HideAndDontSave };
        int capa = 30, n = 0, sinApoyo = 0;
        try
        {
            var fuentesTejado = new List<MeshRenderer>(raizDecay.GetComponentsInChildren<MeshRenderer>());
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude)) if (r.name == "Room") fuentesTejado.Add(r);   // el techo de la sala
            foreach (var r in fuentesTejado)
            {
                if (r.bounds.max.y < 40f || r.name.Contains("plant") || r.name.Contains("Patch")) continue;
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var go = new GameObject("c") { hideFlags = HideFlags.HideAndDontSave, layer = capa };
                go.transform.SetParent(tmp.transform, false);
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation); go.transform.localScale = r.transform.lossyScale;
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            var room = GameObject.Find("class/Room");
            Physics.SyncTransforms();
            var techo = raizDecay.transform.Find("Ceiling");
            if (techo == null) return;
            foreach (var r in techo.GetComponentsInChildren<MeshRenderer>())
            {
                // solo las plantas: los paneles y losas del tejado roto se quedan como estan
                if (!r.name.Contains("plant")) continue;
                var t = r.transform;
                if (t.parent != null && t.parent.GetComponent<LODGroup>() != null) t = t.parent;
                var b = r.bounds;
                bool hecho = false;
                // desde un poco por encima de su base hacia abajo (hasta 3 m), primero justo debajo y luego alrededor (4 m)
                for (float rad = 0f; rad <= 4f && !hecho; rad += 0.5f)
                    for (int k = 0; k < (rad == 0f ? 1 : 12) && !hecho; k++)
                    {
                        float a = k * Mathf.PI * 2f / 12f;
                        var o = new Vector3(Mathf.Cos(a) * rad, 0f, Mathf.Sin(a) * rad);
                        if (!Physics.Raycast(new Vector3(b.center.x, b.min.y + 0.3f, b.center.z) + o, Vector3.down, out var h, 3.3f, 1 << capa) || h.normal.y < 0.6f) continue;
                        float gap = b.min.y - h.point.y;          // >0 flota, <0 metida (como mucho 30 cm)
                        if (Mathf.Abs(gap) < 0.02f && rad == 0f) { hecho = true; break; }   // ya apoyada
                        Undo.RecordObject(t, "Tejado");
                        t.position += o - Vector3.up * (gap + 0.01f);
                        hecho = true; n++;
                    }
                // si no hay nada debajo, es una enredadera que cuelga: se sube hasta tocar lo que tenga encima (hasta 3 m)
                if (!hecho && Physics.Raycast(new Vector3(b.center.x, b.max.y - 0.3f, b.center.z), Vector3.up, out var arriba, 3.3f, 1 << capa))
                {
                    float gap = arriba.point.y - b.max.y;
                    if (Mathf.Abs(gap) >= 0.02f) { Undo.RecordObject(t, "Tejado"); t.position += Vector3.up * (gap + 0.05f); n++; }
                    hecho = true;
                }
                if (!hecho)
                {
                    // ni viga, ni panel, ni techo a 3 m: una flor flotando en el cielo sobre un hueco del tejado se quita
                    Undo.RecordObject(t.gameObject, "Tejado");
                    t.gameObject.SetActive(false);
                    sinApoyo++;
                }
            }
        }
        finally { Object.DestroyImmediate(tmp); }
        var escena = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        System.IO.File.WriteAllText("Temp/asentar_tejado.txt", "asentadas " + n + ", quitadas (flotaban sobre un hueco, sin nada a 3 m) " + sinApoyo);
        Debug.Log("[Arreglos] tejado: asentadas " + n + ", quitadas " + sinApoyo);
    }

    /// <summary>
    /// Manchas de pared (DECAY): un paso anterior las separaba de la pared de forma acumulativa (hasta 30 cm).
    /// Ahora cada una se pega a su pared a 1 cm (+0,5 mm por orden de dibujo para que no se pisen) y las que no tienen
    /// pared detras (sobre un hueco del muro roto) se quitan.
    /// </summary>
    [MenuItem("Herramientas/Arreglos/Pegar manchas de pared")]
    public static void PegarManchas()
    {
        if (Application.isPlaying) return;
        var raizDecay = GameObject.Find("DECAY_ROOT");
        var room = GameObject.Find("class/Room");
        if (raizDecay == null) return;
        var tmp = new GameObject("__Muros") { hideFlags = HideFlags.HideAndDontSave };
        int capa = 30, pegadas = 0, quitadas = 0;
        try
        {
            // muros: la sala y todo lo que no sea mancha ni planta
            var fuentes = new List<MeshRenderer>();
            if (room != null) fuentes.AddRange(room.GetComponentsInChildren<MeshRenderer>());
            foreach (var r in raizDecay.GetComponentsInChildren<MeshRenderer>()) if (!r.name.Contains("Patch") && !r.name.Contains("plant")) fuentes.Add(r);
            foreach (var r in fuentes)
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var go = new GameObject("c") { hideFlags = HideFlags.HideAndDontSave, layer = capa };
                go.transform.SetParent(tmp.transform, false);
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation); go.transform.localScale = r.transform.lossyScale;
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            }
            Physics.SyncTransforms();
            int k = 0;
            foreach (var r in raizDecay.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.StartsWith("WallPatch")) continue;
                var t = r.transform;
                // la normal del parche: su eje mas fino
                var mf = r.GetComponent<MeshFilter>(); if (mf == null) continue;
                var sz = Vector3.Scale(mf.sharedMesh.bounds.size, t.lossyScale);
                Vector3 n = sz.x <= sz.y && sz.x <= sz.z ? t.right : (sz.y <= sz.z ? t.up : t.forward);
                Vector3 c = r.bounds.center;
                float mejor = float.MaxValue; Vector3 punto = Vector3.zero, dir = Vector3.zero;
                foreach (var d in new[] { n, -n })
                    if (Physics.Raycast(c - d * 0.02f, d, out var h, 0.6f, 1 << capa) && h.distance < mejor) { mejor = h.distance; punto = h.point; dir = d; }
                Undo.RecordObject(t, "Manchas");
                Undo.RecordObject(r.gameObject, "Manchas");
                if (mejor == float.MaxValue) { r.gameObject.SetActive(false); quitadas++; continue; }
                float sep = 0.01f + 0.0005f * (k % 24);
                // el centro del parche queda a "sep" de la pared, del lado de donde viene el rayo
                Vector3 objetivo = punto - dir * sep;
                t.position += objetivo - c;
                r.sortingOrder = k % 24;
                k++; pegadas++;
            }
        }
        finally { Object.DestroyImmediate(tmp); }
        var escena = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        System.IO.File.WriteAllText("Temp/pegar_manchas.txt", "pegadas " + pegadas + ", quitadas (sin pared) " + quitadas);
    }

    /// <summary>
    /// Caras coplanares de dos objetos que miran al mismo lado y se solapan (parpadean): la pieza mas pequeña se separa
    /// 4 mm hacia fuera. Si una es hija de la otra se mueve la hija; si las dos son escenario grande (sala y suelo
    /// exterior), la mas grande se hunde 1 cm.
    /// </summary>
    [MenuItem("Herramientas/Arreglos/Separar caras encimadas")]
    public static void SepararEncimadas()
    {
        if (Application.isPlaying) return;
        var log = new StringBuilder();
        var hechos = new HashSet<(Transform, Vector3Int)>();
        int n = 0;
        foreach (var p in AuditoriaVisual.ParesEncimados())
        {
            Renderer mover; Vector3 desp;
            float va = Volumen(p.a.bounds), vb = Volumen(p.b.bounds);
            bool grandeA = p.a.bounds.size.magnitude > 30f, grandeB = p.b.bounds.size.magnitude > 30f;
            if (p.b.transform.IsChildOf(p.a.transform)) { mover = p.b; desp = p.normal * 0.004f; }
            else if (p.a.transform.IsChildOf(p.b.transform)) { mover = p.a; desp = p.normal * 0.004f; }
            else if (grandeA && grandeB) { mover = va > vb ? p.a : p.b; desp = -p.normal * 0.01f; }
            else { mover = va < vb ? p.a : p.b; desp = p.normal * 0.004f; }
            var clave = (mover.transform, Vector3Int.RoundToInt(p.normal * 10f));
            if (hechos.Contains(clave)) continue;
            hechos.Add(clave);
            Undo.RecordObject(mover.transform, "Separar");
            mover.transform.position += desp;
            n++;
            log.AppendLine(p.area.ToString("F3") + " m2: movido " + Ruta(mover.transform) + " " + desp.ToString("F3"));
        }
        var escena = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        log.Insert(0, "separados " + n + "\n");
        System.IO.File.WriteAllText("Temp/separar_encimadas.txt", log.ToString());
    }

    static float Volumen(Bounds b) => Mathf.Max(b.size.x, 0.01f) * Mathf.Max(b.size.y, 0.01f) * Mathf.Max(b.size.z, 0.01f);

    /// <summary>Gira una tabla para que su cara ancha mire arriba (conserva el rumbo) y la deja en el suelo del salon.</summary>
    static void Tumbar(Transform t)
    {
        var mf = t.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        var lb = mf.sharedMesh.bounds;
        var s = Vector3.Scale(lb.size, t.lossyScale);
        int fino = s.x <= s.y && s.x <= s.z ? 0 : (s.y <= s.z ? 1 : 2);
        Vector3 eje = fino == 0 ? t.right : fino == 1 ? t.up : t.forward;
        if (Vector3.Dot(eje, Vector3.up) < 0f) eje = -eje;
        Undo.RecordObject(t, "Tumbar");
        t.rotation = Quaternion.FromToRotation(eje, Vector3.up) * t.rotation;
        // apoyada: la base de la tabla justo encima del suelo (y de las manchas, que estan a 2-8 cm)
        var r = t.GetComponent<Renderer>();
        float suelo = 0.09f;
        var hits = Physics.RaycastAll(new Vector3(r.bounds.center.x, 30f, r.bounds.center.z), Vector3.down, 60f);
        foreach (var h in hits) if (h.collider.name == "Room") suelo = Mathf.Max(suelo, h.point.y + 0.09f);
        t.position += Vector3.up * (suelo - r.bounds.min.y);
    }

    static string Ruta(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    /// <summary>La losa del suelo del aula sobresale de los muros como una acera y coincidia con el suelo exterior.</summary>
    static int SueloBajoAula()
    {
        var s = GameObject.Find("EXTERIOR/Suelo");
        Renderer room = null;
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude)) if (r.name == "Room") room = r;
        if (s == null || room == null) return 0;
        var m = s.GetComponent<MeshFilter>().sharedMesh;
        if (m == null || !AssetDatabase.Contains(m)) return 0;
        var rb = room.bounds; var v = m.vertices; int quit = 0;
        for (int sub = 0; sub < m.subMeshCount; sub++)
        {
            var t = m.GetTriangles(sub); var q = new List<int>(t.Length);
            for (int i = 0; i < t.Length; i += 3)
            {
                bool dentro = true; float ymax = -99f;
                for (int k = 0; k < 3; k++)
                {
                    var w = s.transform.TransformPoint(v[t[i + k]]); ymax = Mathf.Max(ymax, w.y);
                    if (w.x < rb.min.x + 0.05f || w.x > rb.max.x - 0.05f || w.z < rb.min.z + 0.05f || w.z > rb.max.z - 0.05f) dentro = false;
                }
                if (dentro && ymax < 0.2f) { quit++; continue; }
                q.Add(t[i]); q.Add(t[i + 1]); q.Add(t[i + 2]);
            }
            if (q.Count != t.Length) m.SetTriangles(q, sub);
        }
        // los triangulos que cruzan el borde de la losa: sus vertices de dentro, 3 cm por debajo (quedan bajo la losa)
        var vs = m.vertices; bool bajados = false;
        for (int i = 0; i < vs.Length; i++)
        {
            var w = s.transform.TransformPoint(vs[i]);
            if (w.x > rb.min.x - 4.1f && w.x < rb.max.x + 4.1f && w.z > rb.min.z - 4.1f && w.z < rb.max.z + 4.1f && w.y < 0.2f && w.y > -0.085f)   // tambien la fila de fuera (rejilla de 4 m)
            { vs[i].y -= 0.03f / Mathf.Max(s.transform.lossyScale.y, 1e-4f); bajados = true; }
        }
        if (bajados) { m.vertices = vs; m.RecalculateBounds(); }
        if (quit > 0 || bajados) EditorUtility.SetDirty(m);
        return quit;
    }

    static string Decay()
    {
        var root = GameObject.Find("DECAY_ROOT");
        if (root == null) return "no hay DECAY_ROOT";
        int suelo = 0, pared = 0;
        // parches del suelo: separacion de 2 mm entre capas y orden de dibujo fijo (transparentes sin ZWrite)
        var parches = new List<Renderer>();
        // solo los parches del SUELO (los de pared tambien tienen el eje Y hacia arriba: no se pueden distinguir por giro)
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.name.StartsWith("FloorPatch") && r.transform.position.y < 0.5f) parches.Add(r);
        parches.Sort((x, y) => x.transform.position.y.CompareTo(y.transform.position.y));
        for (int i = 0; i < parches.Count; i++)
        {
            var t = parches[i].transform;
            Undo.RecordObject(t, "Decay");
            var p = t.position; p.y = 0.02f + i * 0.0004f; t.position = p;   // el orden fijo evita el parpadeo; basta un escalon minimo
            parches[i].sortingOrder = i;
            suelo++;
        }
        // parches de pared: cada uno un poco mas separado de la pared que el anterior (una sola vez: es acumulativo)
        if (YaHecho(root.transform)) return "parches de suelo " + suelo + ", de pared ya separados";
        int k = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.name.StartsWith("WallPatch")) continue;
            var t = r.transform;
            Undo.RecordObject(t, "Decay");
            t.position += t.forward * (-0.002f * k) ;
            r.sortingOrder = k++;
            pared++;
        }
        return "parches de suelo " + suelo + ", de pared " + pared;
    }

    /// <summary>Marca (hijo vacio "_ArreglosVisuales") para no repetir pasos acumulativos. Devuelve si ya estaba.</summary>
    static bool YaHecho(Transform raiz)
    {
        if (raiz.Find("_ArreglosVisuales") != null) return true;
        var m = new GameObject("_ArreglosVisuales");
        m.transform.SetParent(raiz, false);
        return false;
    }

    /// <summary>
    /// La caja de pañuelos del castillo, en la escena, con las mismas medidas que ahora genera HamsterBuilder: paredes
    /// sin solaparse en las esquinas y hasta donde empieza la tapa (se escalan las cajas ya generadas).
    /// </summary>
    [MenuItem("Herramientas/Arreglos/Caja de panuelos sin solapes")]
    public static void CajaPanuelos()
    {
        var caja = GameObject.Find("HAMSTERLANDIA/Castillo/CajaPanuelos");
        if (caja == null) { Debug.LogWarning("[Arreglos] no hay CajaPanuelos"); return; }
        float x0 = 56f, x1 = 72f, z0 = -95f, z1 = -79f, y0 = 15f, y1 = 25f, e = 0.5f;
        float tx0 = 61.6f, tx1 = 64.8f, tz0 = -90.6f, tz1 = -87.4f, ty = y1 - 0.4f;
        float hx0 = 61.8f, hx1 = 64.6f, hy = y0 + 3.5f;
        var piezas = new Dictionary<string, (Vector3 min, Vector3 max)>
        {
            { "Atras", (new Vector3(x0, y0, z0), new Vector3(x1, ty, z0 + e)) },
            { "Izq", (new Vector3(x0, y0, z0 + e), new Vector3(x0 + e, ty, z1 - e)) },
            { "Der", (new Vector3(x1 - e, y0, z0 + e), new Vector3(x1, ty, z1 - e)) },
            { "FrenteA", (new Vector3(x0, y0, z1 - e), new Vector3(hx0, ty, z1)) },
            { "FrenteB", (new Vector3(hx1, y0, z1 - e), new Vector3(x1, ty, z1)) },
            { "FrenteC", (new Vector3(hx0, hy, z1 - e), new Vector3(hx1, ty, z1)) },
            { "TapaA", (new Vector3(x0, ty, z0), new Vector3(x1, y1, tz0)) },
            { "TapaB", (new Vector3(x0, ty, tz1), new Vector3(x1, y1, z1)) },
            { "TapaC", (new Vector3(x0, ty, tz0), new Vector3(tx0, y1, tz1)) },
            { "TapaD", (new Vector3(tx1, ty, tz0), new Vector3(x1, y1, tz1)) },
        };
        foreach (var kv in piezas)
        {
            var t = caja.transform.Find(kv.Key);
            if (t == null) continue;
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Vector3 tam = kv.Value.max - kv.Value.min, malla = mf.sharedMesh.bounds.size;
            Undo.RecordObject(t, "Caja");
            t.rotation = Quaternion.identity;
            t.localScale = new Vector3(tam.x / malla.x, tam.y / malla.y, tam.z / malla.z);
            t.position = (kv.Value.min + kv.Value.max) * 0.5f;
            var bc = t.GetComponent<BoxCollider>();
            if (bc != null) { Undo.RecordObject(bc, "Caja"); bc.size = malla; bc.center = Vector3.zero; }
        }
        var escena = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
    }

    /// <summary>
    /// Feria de Hamsterlandia, con las mismas medidas que ahora genera HamsterBuilder: la manivela de la caja de musica
    /// pegada a la caja y las patas de la rueda de Bolita apoyadas en la mesa (se alargan por abajo, el extremo de
    /// arriba no se mueve).
    /// </summary>
    [MenuItem("Herramientas/Arreglos/Feria apoyada")]
    public static void FeriaApoyada()
    {
        var caja = GameObject.Find("HAMSTERLANDIA/Feria/CajaMusical/Caja");
        var man = GameObject.Find("HAMSTERLANDIA/Feria/CajaMusical/Manivela");
        if (caja != null && man != null)
        {
            var eje = man.transform.Find("Eje");
            float hueco = eje.GetComponent<Renderer>().bounds.min.x - caja.GetComponent<Renderer>().bounds.max.x;
            if (hueco > 0f) { Undo.RecordObject(man.transform, "Feria"); man.transform.position -= Vector3.right * (hueco + 0.05f); }
        }
        var rueda = GameObject.Find("HAMSTERLANDIA/Feria/GranCarpa/RuedaBolita");
        if (rueda != null)
            foreach (Transform t in rueda.transform)
            {
                if (t.name != "Pata") continue;
                var mf = t.GetComponent<MeshFilter>(); if (mf == null) continue;
                float largo = mf.sharedMesh.bounds.size.y * t.localScale.y;
                Vector3 u = t.up;                                   // a lo largo de la pata, hacia arriba
                Vector3 arriba = t.position + u * largo * 0.5f, abajo = t.position - u * largo * 0.5f;
                float falta = abajo.y - 14.95f;                     // la mesa esta a 15
                if (falta <= 0.01f || u.y < 0.2f) continue;
                abajo -= u * (falta / u.y);
                Undo.RecordObject(t, "Feria");
                t.localScale = new Vector3(t.localScale.x, t.localScale.y * Vector3.Distance(arriba, abajo) / largo, t.localScale.z);
                t.position = (arriba + abajo) * 0.5f;
            }
        var escena = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
    }

    static string Agua()
    {
        int n = 0;
        foreach (var mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            if (mr.sharedMaterial == null || mr.sharedMaterial.shader.name != "Cloaca/Agua") continue;
            float dy = 0f;
            if (mr.name.StartsWith("Estanque")) dy = 0.05f;          // los estanques por encima de los canales que llegan
            else if (mr.name == "Canal_Nido") dy = 0.012f;            // en el cruce tres canales coinciden exactos
            else if (mr.name == "Canal_Bajada") dy = 0.024f;
            if (dy == 0f) continue;
            var t = mr.transform;
            // no repetir si ya se movio (marca en el nombre del objeto padre no hace falta: se compara con la altura base)
            if (t.localPosition.y > 0.001f) continue;
            Undo.RecordObject(t, "Agua");
            t.localPosition += Vector3.up * dy;
            n++;
        }
        return "superficies de agua separadas " + n;
    }

    static string Hamster()
    {
        var root = GameObject.Find("HAMSTERLANDIA");
        if (root == null) return "no hay HAMSTERLANDIA";
        if (YaHecho(root.transform)) return "ya estaba";
        int discos = 0, tapas = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            // disco de viruta del conector: 5 cm mas abajo (el piso del tubo queda encima donde se tocan)
            if (t.name.StartsWith("Piso") && t.parent != null && t.parent.name.StartsWith("Conector") && t.localPosition.y > -0.5f)
            {
                Undo.RecordObject(t, "Hamster"); t.position += Vector3.down * 0.05f; discos++;
            }
            // (la caja de pañuelos ya no se toca aqui: ver CajaPanuelos, que la deja sin solapes)
        }
        return "discos de conector bajados " + discos + ", tapas subidas " + tapas;
    }
}
