using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Herramientas > Arreglos > Auditoria visual.
/// Recorre toda la escena (en modo edicion) buscando dos tipos de fallo y escribe el informe en Temp/auditoria_visual.txt:
///   1. FLOTANTES: objetos cuya base no toca nada. Para cada malla se toman los vertices mas bajos y se lanza un rayo hacia
///      abajo (contra los colliders de la escena, sin contar los propios); si ninguno de esos puntos tiene superficie a
///      menos de unos centimetros, el objeto flota.
///   2. ENCIMADAS: triangulos casi coplanares (a menos de 3 mm) de dos objetos distintos que se solapan: parpadean
///      ("z-fighting", texturas que se pisan).
/// No cambia nada: solo informa (los arreglos van en ArreglosVisuales).
/// </summary>
public static class AuditoriaVisual
{
    const string Informe = "Temp/auditoria_visual.txt";

    [MenuItem("Herramientas/Arreglos/Auditoria visual")]
    public static void Ejecutar()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Flotantes());
        sb.AppendLine(Encimadas());
        System.IO.File.WriteAllText(Informe, sb.ToString());
        Debug.Log("[Auditoria] informe en " + Informe);
    }

    static string Ruta(Transform t)
    {
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    static bool Ignorar(Renderer r)
    {
        if (!r.enabled || !r.gameObject.activeInHierarchy) return true;
        if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || r is SkinnedMeshRenderer) return true;
        if (r.GetComponentInParent<Canvas>() != null) return true;
        if (r.GetComponentInParent<RatHealth>() != null) return true;              // bichos: se revisan en Play
        if (r.GetComponentInParent<PlayerController>() != null) return true;
        var mf = r.GetComponent<MeshFilter>();
        return mf == null || mf.sharedMesh == null;
    }

    // ------------------------------------------------------------------ 1. flotantes
    const int CapaAuditoria = 31;

    public class Grupo
    {
        public List<Renderer> piezas = new List<Renderer>();
        public Bounds caja;
        public float bajar = float.MaxValue, subir = float.MaxValue;   // distancia hasta apoyarse (abajo) o colgarse (arriba)
        public string sobre = "", bajo = "";
    }

    /// <summary>
    /// Busca grupos de mallas que no se apoyan en nada. Se ponen MeshColliders temporales a todas las mallas (capa
    /// aparte), se miran los contactos (esferas pequeñas en sus vertices) y se forman grupos conectados. Un grupo que no
    /// toca ninguna pieza "de escenario" (suelo, paredes, cueva, mesa: mallas de mas de 30 m) esta flotando: se mide
    /// cuanto tiene que bajar para apoyarse (o subir, si cuelga de un techo).
    /// </summary>
    public static List<Grupo> GruposFlotantes(float tolerancia = 0.04f)
    {
        var res = new List<Grupo>();
        var raiz = new GameObject("__AuditoriaColisiones") { hideFlags = HideFlags.HideAndDontSave };
        var lista = new List<Renderer>();
        var col = new List<Collider>();
        var idx = new Dictionary<Collider, int>();
        try
        {
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
            {
                if (Ignorar(r)) continue;
                var m = r.GetComponent<MeshFilter>().sharedMesh;
                if (!m.isReadable || m.vertexCount < 3) continue;
                var go = new GameObject("c") { hideFlags = HideFlags.HideAndDontSave, layer = CapaAuditoria };
                go.transform.SetParent(raiz.transform, false);
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                go.transform.localScale = r.transform.lossyScale;
                var mc = go.AddComponent<MeshCollider>();
                try { mc.sharedMesh = m; } catch { Object.DestroyImmediate(go); continue; }
                idx[mc] = lista.Count; lista.Add(r); col.Add(mc);
            }
            Physics.SyncTransforms();
            int mascara = 1 << CapaAuditoria;
            int n = lista.Count;
            var padre = new int[n]; for (int i = 0; i < n; i++) padre[i] = i;
            System.Func<int, int> Raiz = null; Raiz = x => padre[x] == x ? x : (padre[x] = Raiz(padre[x]));
            var ancla = new bool[n];
            for (int i = 0; i < n; i++) ancla[i] = lista[i].bounds.size.magnitude > 30f;
            var buf = new Collider[32];
            var puntosDe = new List<Vector3>[n];
            for (int i = 0; i < n; i++)
            {
                var r = lista[i];
                var pts = Puntos(r, 220);
                puntosDe[i] = pts;
                if (ancla[i]) continue;      // las grandes no hace falta probarlas: las pequeñas las encuentran a ellas
                float tol = tolerancia + r.bounds.size.magnitude * 0.004f;
                foreach (var w in pts)
                {
                    int k = Physics.OverlapSphereNonAlloc(w, tol, buf, mascara, QueryTriggerInteraction.Ignore);
                    for (int q = 0; q < k; q++)
                    {
                        if (!idx.TryGetValue(buf[q], out int j) || j == i) continue;
                        int a = Raiz(i), b = Raiz(j);
                        if (a != b) padre[a] = b;
                    }
                }
            }
            // grupos sin ancla
            var grupos = new Dictionary<int, List<int>>();
            var conAncla = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int g = Raiz(i);
                if (!grupos.TryGetValue(g, out var l)) grupos[g] = l = new List<int>();
                l.Add(i);
                if (ancla[i]) conAncla.Add(g);
            }
            foreach (var kv in grupos)
            {
                if (conAncla.Contains(kv.Key)) continue;
                var G = new Grupo();
                var miembros = new HashSet<Collider>();
                bool primero = true;
                foreach (int i in kv.Value)
                {
                    G.piezas.Add(lista[i]); miembros.Add(col[i]);
                    if (primero) { G.caja = lista[i].bounds; primero = false; } else G.caja.Encapsulate(lista[i].bounds);
                }
                // cuanto falta hasta la superficie de debajo (desde los puntos mas bajos) y hasta la de encima
                var todos = new List<Vector3>();
                foreach (int i in kv.Value) todos.AddRange(puntosDe[i]);
                foreach (var w in todos.OrderBy(q => q.y).Take(80))
                    foreach (var h in Physics.RaycastAll(w + Vector3.up * 0.01f, Vector3.down, 40f, mascara, QueryTriggerInteraction.Ignore))
                        if (!miembros.Contains(h.collider)) { float d = w.y - h.point.y; if (d < G.bajar) { G.bajar = d; G.sobre = lista[idx[h.collider]].name; } }
                foreach (var w in todos.OrderByDescending(q => q.y).Take(80))
                    foreach (var h in Physics.RaycastAll(w - Vector3.up * 0.01f, Vector3.up, 40f, mascara, QueryTriggerInteraction.Ignore))
                        if (!miembros.Contains(h.collider)) { float d = h.point.y - w.y; if (d < G.subir) { G.subir = d; G.bajo = lista[idx[h.collider]].name; } }
                res.Add(G);
            }
        }
        finally { Object.DestroyImmediate(raiz); }
        return res;
    }

    static List<Vector3> Puntos(Renderer r, int max)
    {
        var v = r.GetComponent<MeshFilter>().sharedMesh.vertices;
        var puntos = new List<Vector3>(v.Length);
        foreach (var p in v) puntos.Add(r.transform.TransformPoint(p));
        if (puntos.Count <= max) return puntos;
        var prueba = puntos.OrderBy(q => q.y).Take(50).ToList();
        prueba.AddRange(puntos.OrderByDescending(q => q.y).Take(20));
        prueba.Add(puntos.OrderBy(q => q.x).First()); prueba.Add(puntos.OrderBy(q => q.x).Last());
        prueba.Add(puntos.OrderBy(q => q.z).First()); prueba.Add(puntos.OrderBy(q => q.z).Last());
        int paso = Mathf.Max(1, puntos.Count / max);
        for (int i = 0; i < puntos.Count; i += paso) prueba.Add(puntos[i]);
        return prueba;
    }

    public static string Flotantes()
    {
        var gs = GruposFlotantes();
        var sb = new StringBuilder("== FLOTANTES (grupos que no se apoyan en nada) ==\ngrupos " + gs.Count + "\n");
        foreach (var g in gs.OrderBy(x => Ruta(x.piezas[0].transform)))
            sb.AppendLine("bajar " + (g.bajar < 1e6f ? g.bajar.ToString("F2") : "-") + " (sobre " + g.sobre + ")  subir " + (g.subir < 1e6f ? g.subir.ToString("F2") : "-") + " (bajo " + g.bajo + ")  piezas " + g.piezas.Count + "  " + Ruta(g.piezas[0].transform) + "  en " + g.caja.center.ToString("F1") + " tam " + g.caja.size.ToString("F1"));
        return sb.ToString();
    }

    // ------------------------------------------------------------------ 2. encimadas
    struct Tri { public Vector3 a, b, c, n, nReal; public float d; public int dueno; public float area; }

    public class Par { public Renderer a, b; public float area; public Vector3 normal, donde; }

    public static string Encimadas(float distancia = 0.003f)
    {
        var sb = new StringBuilder("== ENCIMADAS (coplanares que se solapan) ==\n");
        var lista = ParesEncimados(distancia);
        int k = 0;
        foreach (var p in lista)
            if (++k <= 150) sb.AppendLine(p.area.ToString("F3") + " m2  " + Ruta(p.a.transform) + "  <->  " + Ruta(p.b.transform) + "  en " + p.donde.ToString("F2"));
        sb.Insert(0, "pares encimados " + lista.Count + "\n");
        return sb.ToString();
    }

    /// <summary>Parejas de objetos con caras coplanares (a menos de "distancia") que miran al mismo lado y se solapan.</summary>
    public static List<Par> ParesEncimados(float distancia = 0.003f)
    {
        var renderers = new List<Renderer>();
        var tris = new List<Tri>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            if (Ignorar(r)) continue;
            var mat = r.sharedMaterial;
            if (mat != null && mat.renderQueue >= 2450 && mat.renderQueue < 3000) { }   // recortados (hojas): cuentan
            var m = r.GetComponent<MeshFilter>().sharedMesh;
            if (!m.isReadable) continue;
            int dueno = renderers.Count; renderers.Add(r);
            var v = m.vertices; var t = m.triangles; var tr = r.transform;
            var w = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++) w[i] = tr.TransformPoint(v[i]);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = w[t[i]], bb = w[t[i + 1]], c = w[t[i + 2]];
                var cr = Vector3.Cross(bb - a, c - a);
                float area = cr.magnitude * 0.5f;
                if (area < 0.004f) continue;                 // triangulos minusculos: no se notan
                var n = cr.normalized; var nReal = n;
                if (n.y < 0f || (Mathf.Approximately(n.y, 0f) && (n.x < 0f || Mathf.Approximately(n.x, 0f) && n.z < 0f))) n = -n;   // orientacion canonica
                tris.Add(new Tri { a = a, b = bb, c = c, n = n, nReal = nReal, d = Vector3.Dot(n, a), dueno = dueno, area = area });
            }
        }
        // cubetas por normal (cuantizada) y distancia al origen
        var cubetas = new Dictionary<long, List<int>>();
        for (int i = 0; i < tris.Count; i++)
        {
            var k = Clave(tris[i]);
            if (!cubetas.TryGetValue(k, out var l)) cubetas[k] = l = new List<int>();
            l.Add(i);
        }
        var pares = new Dictionary<(int, int), (float area, Vector3 donde, Vector3 normal)>();
        foreach (var kv in cubetas)
        {
            // vecinas en distancia (la cuantizacion puede partir un plano entre dos cubetas)
            var lista = new List<int>(kv.Value);
            if (cubetas.TryGetValue(kv.Key + 1, out var sig)) lista.AddRange(sig);
            var propia = kv.Value;
            foreach (int i in propia)
                foreach (int j in lista)
                {
                    if (j <= i && propia.Contains(j)) continue;
                    var A = tris[i]; var B = tris[j];
                    if (A.dueno == B.dueno) continue;
                    // caras que se tocan mirando en sentidos opuestos (la base de una caja sobre la mesa) no se ven,
                    // ni las que miran hacia abajo (bases apoyadas)
                    if (Vector3.Dot(A.nReal, B.nReal) < 0f || A.nReal.y < -0.9f) continue;
                    // dos transparentes con orden de dibujo fijo (manchas) no parpadean
                    if (Transparente(renderers[A.dueno]) && Transparente(renderers[B.dueno]) && renderers[A.dueno].sortingOrder != renderers[B.dueno].sortingOrder) continue;
                    if (Vector3.Dot(A.n, B.n) < 0.9995f || Mathf.Abs(A.d - B.d) > distancia) continue;
                    float sol = Solape(A, B);
                    if (sol < 0.002f) continue;
                    var key = A.dueno < B.dueno ? (A.dueno, B.dueno) : (B.dueno, A.dueno);
                    pares.TryGetValue(key, out var acc);
                    pares[key] = (acc.area + sol, (A.a + A.b + A.c) / 3f, A.nReal);
                }
        }
        var res = new List<Par>();
        foreach (var kv in pares.OrderByDescending(p => p.Value.area))
            if (kv.Value.area >= 0.01f)
                res.Add(new Par { a = renderers[kv.Key.Item1], b = renderers[kv.Key.Item2], area = kv.Value.area, normal = kv.Value.normal, donde = kv.Value.donde });
        return res;
    }

    static bool Transparente(Renderer r)
    {
        var m = r.sharedMaterial;
        return m != null && m.renderQueue >= 2500;
    }

    static long Clave(Tri t)
    {
        // normal cuantizada a ~1 grado y distancia a 1 cm
        long nx = Mathf.RoundToInt(t.n.x * 60f) + 64, ny = Mathf.RoundToInt(t.n.y * 60f) + 64, nz = Mathf.RoundToInt(t.n.z * 60f) + 64;
        long dd = Mathf.FloorToInt(t.d * 100f) + 1000000;
        return (((nx * 128 + ny) * 128 + nz) << 24) ^ dd;
    }

    /// <summary>Area aproximada del solape de dos triangulos coplanares: se proyectan al plano y se recorta uno con el otro.</summary>
    static float Solape(Tri A, Tri B)
    {
        // base del plano
        var u = Vector3.Cross(A.n, Mathf.Abs(A.n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        var w = Vector3.Cross(A.n, u);
        System.Func<Vector3, Vector2> P = p => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w));
        var pa = new List<Vector2> { P(A.a), P(A.b), P(A.c) };
        var pb = new List<Vector2> { P(B.a), P(B.b), P(B.c) };
        if (Cruz(pa[1] - pa[0], pa[2] - pa[0]) < 0f) pa.Reverse();
        if (Cruz(pb[1] - pb[0], pb[2] - pb[0]) < 0f) pb.Reverse();
        var poli = pa;
        for (int i = 0; i < 3 && poli.Count > 0; i++) poli = Recortar(poli, pb[i], pb[(i + 1) % 3]);
        float area = 0f;
        for (int i = 0; i < poli.Count; i++) area += Cruz(poli[i], poli[(i + 1) % poli.Count]);
        return Mathf.Abs(area) * 0.5f;
    }

    static float Cruz(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    static List<Vector2> Recortar(List<Vector2> poli, Vector2 e0, Vector2 e1)
    {
        var res = new List<Vector2>();
        var e = e1 - e0;
        for (int i = 0; i < poli.Count; i++)
        {
            var p = poli[i]; var q = poli[(i + 1) % poli.Count];
            float sp = Cruz(e, p - e0), sq = Cruz(e, q - e0);
            if (sp >= 0f) res.Add(p);
            if ((sp >= 0f) != (sq >= 0f)) { float t = sp / (sp - sq); res.Add(p + (q - p) * t); }
        }
        return res;
    }
}
