using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Crea los enemigos del documento "ENEMIGOS": prefabs a partir de los modelos descargados (Assets/Enemies/Models),
/// los objetos que sueltan (modelos 3D + iconos del inventario) y los coloca en el salon con objetos alrededor.
///   Tools > Enemies > 1 Objetos y iconos / 2 Prefabs de criaturas / 3 Colocar en el salon
/// </summary>
public static class EnemiesBuilder
{
    const string Root = "Assets/Enemies";
    const string Models = Root + "/Models";
    const string MeshDir = Root + "/Meshes";
    const string PrefabDir = Root + "/Prefabs";
    const string MatDir = Root + "/Materials";
    const string DropMatDir = Root + "/DropMaterials";
    const string TexDir = Root + "/DropTextures";
    const string DropDir = "Assets/Resources/Drops";
    const string IconDir = "Assets/iconos";

    static StringBuilder log = new StringBuilder();
    static void Log(string s) { log.AppendLine(s); }

    [MenuItem("Tools/Enemies/Construir todo")]
    public static void BuildAll() { RunAll(new StringBuilder()); }

    public static void Step(string what, StringBuilder outLog)
    {
        log = outLog;
        if (what == "drops") BuildDrops();
        else if (what == "prefabs") BuildCreaturePrefabs();
        else if (what == "icons") BuildIcons();
        else if (what == "place") Place();
        else if (what == "enemies") RunAll(outLog);
    }

    public static void RunAll(StringBuilder outLog)
    {
        log = outLog;
        BuildDrops();
        BuildCreaturePrefabs();
        Place();
    }

    // ====================================================================== mallas procedurales
    static Mesh Lathe(float[] r, float[] y, int seg, bool capBottom = true, bool capTop = true)
    {
        var vs = new List<Vector3>(); var ts = new List<int>();
        int rings = r.Length;
        for (int i = 0; i < rings; i++)
            for (int s = 0; s < seg; s++)
            {
                float a = Mathf.PI * 2f * s / seg;
                vs.Add(new Vector3(r[i] * Mathf.Cos(a), y[i], r[i] * Mathf.Sin(a)));
            }
        for (int i = 0; i < rings - 1; i++)
            for (int s = 0; s < seg; s++)
            {
                int a = i * seg + s, b = i * seg + (s + 1) % seg, c = (i + 1) * seg + s, d = (i + 1) * seg + (s + 1) % seg;
                AddTri(ts, vs, a, c, b); AddTri(ts, vs, b, c, d);
            }
        if (capBottom && r[0] > 0.0001f)
        {
            int ci = vs.Count; vs.Add(new Vector3(0f, y[0], 0f));
            for (int s = 0; s < seg; s++) AddTri(ts, vs, ci, s, (s + 1) % seg);
        }
        if (capTop && r[rings - 1] > 0.0001f)
        {
            int ci = vs.Count; vs.Add(new Vector3(0f, y[rings - 1], 0f));
            int o = (rings - 1) * seg;
            for (int s = 0; s < seg; s++) AddTri(ts, vs, ci, o + (s + 1) % seg, o + s);
        }
        var m = new Mesh();
        m.SetVertices(vs); m.SetTriangles(ts, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // el triangulo se orienta para que la normal (Cross) mire hacia fuera del eje Y / del centro
    static void AddTri(List<int> ts, List<Vector3> vs, int a, int b, int c)
    {
        Vector3 n = Vector3.Cross(vs[b] - vs[a], vs[c] - vs[a]);
        Vector3 center = (vs[a] + vs[b] + vs[c]) / 3f;
        Vector3 outward = new Vector3(center.x, 0f, center.z);
        if (outward.sqrMagnitude < 1e-6f) outward = Vector3.up * (center.y >= 0f ? 1f : -1f);
        if (Vector3.Dot(n, outward) < 0f) { ts.Add(a); ts.Add(c); ts.Add(b); }
        else { ts.Add(a); ts.Add(b); ts.Add(c); }
    }

    static Mesh Ellipsoid(Vector3 radii, int seg = 14, int rings = 8)
    {
        var r = new float[rings + 1]; var y = new float[rings + 1];
        for (int i = 0; i <= rings; i++)
        {
            float phi = Mathf.PI * i / rings - Mathf.PI * 0.5f;
            r[i] = Mathf.Cos(phi) * 1f; y[i] = Mathf.Sin(phi) * 1f;
        }
        r[0] = 0f; r[rings] = 0f;
        var m = Lathe(r, y, seg, false, false);
        return Xform(m, Matrix4x4.Scale(radii));
    }

    static Mesh Xform(Mesh m, Matrix4x4 xf)
    {
        var vs = m.vertices.Select(v => xf.MultiplyPoint3x4(v)).ToArray();
        var o = new Mesh(); o.vertices = vs; o.triangles = m.triangles;
        // si la escala invierte la orientacion, se corrige
        if (xf.determinant < 0f) { var t = o.triangles; for (int i = 0; i < t.Length; i += 3) { int tmp = t[i]; t[i] = t[i + 1]; t[i + 1] = tmp; } o.triangles = t; }
        o.RecalculateNormals(); o.RecalculateBounds();
        return o;
    }

    static Mesh Bend(Mesh m, float k)
    {
        var vs = m.vertices.Select(v => new Vector3(v.x + k * v.y * v.y, v.y, v.z)).ToArray();
        var o = new Mesh(); o.vertices = vs; o.triangles = m.triangles;
        o.RecalculateNormals(); o.RecalculateBounds(); return o;
    }

    static Mesh Combine(params KeyValuePair<Mesh, Matrix4x4>[] parts)
    {
        var vs = new List<Vector3>(); var ts = new List<int>();
        foreach (var p in parts)
        {
            int off = vs.Count;
            var src = p.Key.vertices; var tri = p.Key.triangles;
            bool flip = p.Value.determinant < 0f;
            foreach (var v in src) vs.Add(p.Value.MultiplyPoint3x4(v));
            for (int i = 0; i < tri.Length; i += 3)
            {
                if (flip) { ts.Add(off + tri[i]); ts.Add(off + tri[i + 2]); ts.Add(off + tri[i + 1]); }
                else { ts.Add(off + tri[i]); ts.Add(off + tri[i + 1]); ts.Add(off + tri[i + 2]); }
            }
        }
        var m = new Mesh();
        m.indexFormat = vs.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        m.SetVertices(vs); m.SetTriangles(ts, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static KeyValuePair<Mesh, Matrix4x4> P(Mesh m, Vector3 pos, Quaternion rot) { return new KeyValuePair<Mesh, Matrix4x4>(m, Matrix4x4.TRS(pos, rot, Vector3.one)); }
    static KeyValuePair<Mesh, Matrix4x4> P(Mesh m, Matrix4x4 xf) { return new KeyValuePair<Mesh, Matrix4x4>(m, xf); }

    static Mesh Segment(Vector3 a, Vector3 b, float radius)
    {
        float len = Vector3.Distance(a, b);
        var cyl = Lathe(new[] { radius, radius, radius }, new[] { 0f, len * 0.5f, len }, 8);
        var rot = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        return Xform(cyl, Matrix4x4.TRS(a, rot, Vector3.one));
    }

    // ====================================================================== materiales
    static Material MakeMat(string dir, string name, Color c, float smooth = 0.3f, float metallic = 0f, bool doubleSided = false, Color? emission = null, Texture2D tex = null)
    {
        Directory.CreateDirectory(dir);
        string path = $"{dir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("Universal Render Pipeline/Lit");
        m.SetColor("_BaseColor", c); m.color = c;
        m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Cull", doubleSided ? 0f : 2f);
        m.SetTexture("_BaseMap", tex); if (tex != null) { m.SetColor("_BaseColor", Color.white); m.color = Color.white; }
        if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        EditorUtility.SetDirty(m);
        return m;
    }

    // ====================================================================== texturas procedurales de los objetos
    static void AssignUV(Mesh m, bool planar)
    {
        var vs = m.vertices; var b = m.bounds; var uv = new Vector2[vs.Length];
        if (planar)
        {
            for (int i = 0; i < vs.Length; i++)
                uv[i] = new Vector2((vs[i].x - b.min.x) / Mathf.Max(b.size.x, 1e-4f), (vs[i].z - b.min.z) / Mathf.Max(b.size.z, 1e-4f));
        }
        else
        {
            // u a lo largo del eje mas largo; v alrededor del eje (espejado: sin costura)
            int ax = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.y >= b.size.z ? 1 : 2);
            for (int i = 0; i < vs.Length; i++)
            {
                float a = ax == 0 ? vs[i].x : (ax == 1 ? vs[i].y : vs[i].z);
                float p = ax == 0 ? vs[i].y - b.center.y : vs[i].x - b.center.x;
                float q = ax == 0 ? vs[i].z - b.center.z : (ax == 1 ? vs[i].z - b.center.z : vs[i].y - b.center.y);
                float u = (a - b.min[ax]) / Mathf.Max(b.size[ax], 1e-4f);
                float v = Mathf.Abs(Mathf.Atan2(q, p)) / Mathf.PI;
                uv[i] = new Vector2(u, v);
            }
        }
        m.uv = uv;
    }

    // malla nueva con todos los atributos asignados de una vez (evita mallas con el buffer de UV desactualizado)
    static Mesh CleanCopy(Mesh src)
    {
        var m = new Mesh { name = src.name };
        m.indexFormat = src.indexFormat;
        m.vertices = src.vertices; m.normals = src.normals; m.uv = src.uv; m.triangles = src.triangles;
        m.RecalculateBounds();
        return m;
    }

    static float N(float x, float y) { return Mathf.PerlinNoise(x + 37.1f, y + 11.7f); }
    static float Hash(float x, float y, float k) { float f = Mathf.Sin(x * 127.1f + y * 311.7f + k * 74.7f) * 43758.5453f; return f - Mathf.Floor(f); }
    static Color Mul(Color c, float k) { return new Color(c.r * k, c.g * k, c.b * k, 1f); }

    static Texture2D Paint(string id, int w, int h, System.Func<float, float, Color> f)
    {
        Directory.CreateDirectory(TexDir);
        string path = TexDir + "/" + id + ".png";
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) { var c = f((x + 0.5f) / w, (y + 0.5f) / h); c.a = 1f; px[y * w + x] = c; }
        t.SetPixels(px);
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void ApplyTexture(DropDef d)
    {
        Texture2D tex = null;
        bool planar = d.id == "Piel" || d.id == "Exoesqueleto" || d.id == "Alas";
        AssignUV(d.mesh, planar);
        d.mesh = CleanCopy(d.mesh);
        var b = d.mesh.bounds;
        switch (d.id)
        {
            case "Hueso":
                tex = Paint(d.id, 256, 128, (u, v) =>
                {
                    var bone = new Color(0.93f, 0.89f, 0.76f);
                    float k = 0.84f + (N(u * 14f, v * 14f) * 0.5f + N(u * 40f, v * 40f) * 0.25f) * 0.34f;
                    float ends = Mathf.Clamp01(Mathf.Max(1f - u * 6f, (u - 0.78f) * 6f));
                    var c = Color.Lerp(Mul(bone, k), Mul(new Color(0.72f, 0.58f, 0.38f), k), ends * 0.6f * N(u * 30f, v * 30f + 9f));
                    if (N(u * 95f, v * 95f) > 0.8f) c = Mul(c, 0.78f);   // poros
                    return c;
                });
                break;
            case "Piel":
                tex = Paint(d.id, 256, 256, (u, v) =>
                {
                    var baseC = Color.Lerp(new Color(0.96f, 0.93f, 0.89f), new Color(0.84f, 0.78f, 0.72f), N(u * 3f, v * 3f) * 0.9f);
                    float streak = N(u * 12f, v * 140f), fine = N(u * 220f, v * 220f);
                    var c = Mul(baseC, (0.88f + 0.24f * streak) * (0.93f + 0.14f * fine));
                    float patch = N(u * 5f + 40f, v * 5f);
                    if (patch > 0.72f) c = Color.Lerp(c, new Color(0.55f, 0.4f, 0.32f), (patch - 0.72f) * 3f);   // manchas pardas
                    float edge = Mathf.Clamp01((Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) - 0.02f) * 25f);
                    return Mul(c, 0.7f + 0.3f * edge);
                });
                break;
            case "Colmillo":
                tex = Paint(d.id, 256, 128, (u, v) =>
                {
                    float streak = N(u * 6f, v * 40f);
                    var c = Color.Lerp(new Color(0.98f, 0.94f, 0.78f), new Color(0.86f, 0.77f, 0.5f), streak * 0.6f);
                    float root = Mathf.Clamp01((0.3f - u) / 0.3f);
                    c = Color.Lerp(c, new Color(0.5f, 0.3f, 0.26f), root * root);
                    c = Color.Lerp(c, new Color(1f, 0.98f, 0.9f), Mathf.Clamp01((u - 0.8f) / 0.2f) * 0.7f);
                    float groove = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 6f));
                    if (groove < 0.07f) c = Mul(c, 0.82f);
                    return c;
                });
                break;
            case "PataCiempies":
                tex = Paint(d.id, 256, 64, (u, v) =>
                {
                    var c = Color.Lerp(new Color(0.88f, 0.42f, 0.1f), new Color(0.6f, 0.25f, 0.06f), N(u * 18f, v * 18f));
                    float ring = Mathf.Exp(-Mathf.Pow((u - 0.33f) / 0.03f, 2f)) + Mathf.Exp(-Mathf.Pow((u - 0.66f) / 0.03f, 2f));
                    c = Color.Lerp(c, new Color(0.22f, 0.09f, 0.03f), Mathf.Clamp01(ring) * 0.8f);
                    c = Color.Lerp(c, new Color(0.14f, 0.07f, 0.04f), Mathf.Clamp01((u - 0.9f) / 0.08f));
                    if (N(u * 120f, v * 60f) > 0.78f) c = Mul(c, 0.8f);
                    return c;
                });
                break;
            case "Cuerno":
                tex = Paint(d.id, 256, 128, (u, v) =>
                {
                    float ridge = 0.5f + 0.5f * Mathf.Sin(u * 64f + N(u * 6f, v * 6f) * 3f);
                    var c = Color.Lerp(new Color(0.15f, 0.08f, 0.04f), new Color(0.44f, 0.27f, 0.12f), ridge * 0.55f + N(u * 8f, v * 20f) * 0.45f);
                    c = Color.Lerp(c, new Color(0.72f, 0.52f, 0.26f), Mathf.SmoothStep(0.72f, 1f, u) * 0.65f);
                    c = Color.Lerp(c, new Color(0.07f, 0.04f, 0.03f), Mathf.Clamp01((0.08f - u) / 0.08f));
                    return c;
                });
                break;
            case "Exoesqueleto":
                tex = Paint(d.id, 256, 256, (u, v) =>
                {   // escamas tipo celdas de Voronoi con brillo iridiscente
                    float px = u * 11f, py = v * 11f; int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
                    float d1 = 9f, d2 = 9f, id = 0f;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int gx = cx + i, gy = cy + j;
                            float fx = gx + Hash(gx, gy, 1f), fy = gy + Hash(gx, gy, 2f);
                            float dd = Mathf.Sqrt((px - fx) * (px - fx) + (py - fy) * (py - fy));
                            if (dd < d1) { d2 = d1; d1 = dd; id = Hash(gx, gy, 3f); } else if (dd < d2) d2 = dd;
                        }
                    var c = Color.Lerp(new Color(0.04f, 0.3f, 0.3f), new Color(0.24f, 0.1f, 0.42f), id);
                    c = Color.Lerp(c, new Color(0.15f, 0.55f, 0.45f), Mathf.Pow(Mathf.Clamp01(1f - d1 * 1.1f), 2f) * 0.5f);
                    float edge = Mathf.Clamp01((d2 - d1) / 0.16f);
                    c = Mul(c, 0.3f + 0.7f * edge);
                    float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f));
                    return Mul(c, Mathf.Lerp(1f, 0.45f, Mathf.Clamp01((r - 0.38f) / 0.12f)));
                });
                break;
            case "Aguijon":
                tex = Paint(d.id, 256, 64, (u, v) =>
                {
                    var amber = new Color(0.88f, 0.56f, 0.06f); var black = new Color(0.06f, 0.035f, 0.02f);
                    Color c;
                    if (u < 0.32f) c = Mul(amber, 0.82f + 0.18f * (0.5f + 0.5f * Mathf.Sin(u * 60f)));
                    else
                    {
                        float band = Mathf.Sin(u * Mathf.PI * 11f);
                        c = band > 0.15f ? black : amber;
                        c = Color.Lerp(c, black, Mathf.SmoothStep(0.7f, 1f, u));
                    }
                    return Mul(c, 0.9f + 0.2f * N(u * 40f, v * 30f));
                });
                break;
            case "Alas":
                tex = Paint(d.id, 512, 512, (u, v) =>
                {   // dos alas de mariposa monarca (misma geometria que la malla)
                    float wx = b.min.x + u * b.size.x, wz = b.min.z + v * b.size.z;
                    var p = new Vector3(wx, 0f, wz);
                    var dark = new Color(0.1f, 0.06f, 0.04f);
                    for (int k = 0; k < 2; k++)
                    {
                        float sgn = k == 0 ? 1f : -1f;
                        var local = Quaternion.Euler(0f, -22f * sgn, 0f) * (p - new Vector3(0.1f, 0f, 0.45f * sgn));
                        float e = local.x * local.x / 0.81f + local.z * local.z / 0.2025f;
                        if (e >= 1f) continue;
                        float t = Mathf.Sqrt(e);
                        float ang = Mathf.Atan2(local.z * sgn, local.x + 0.9f);
                        var c = Color.Lerp(new Color(1f, 0.66f, 0.14f), new Color(0.9f, 0.38f, 0.05f), t);
                        float vein = Mathf.Abs(Mathf.Sin(ang * 15f + t * 1.2f));
                        if (vein < 0.12f || Mathf.Abs(local.z) < 0.018f) c = dark;    // venas
                        if (local.x < -0.62f) c = Color.Lerp(c, dark, Mathf.Clamp01((-0.62f - local.x) / 0.2f));   // base oscura
                        float border = Mathf.Clamp01((t - 0.78f) / 0.07f);
                        c = Color.Lerp(c, dark, border);
                        float ring = Mathf.Abs(t - 0.89f);
                        float dotAng = Mathf.Atan2(local.z, local.x);
                        if (ring < 0.03f && Mathf.Repeat(dotAng * 11f / (2f * Mathf.PI), 1f) < 0.45f) c = new Color(0.97f, 0.97f, 0.95f);   // puntos blancos
                        return Mul(c, 0.94f + 0.12f * N(u * 60f, v * 60f));
                    }
                    return dark;   // cuerpo del insecto
                });
                break;
        }
        if (tex != null)
        {
            var m = d.mat;
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white); m.color = Color.white;
            if (d.id == "Alas") { m.SetFloat("_Smoothness", 0.35f); m.SetColor("_EmissionColor", Color.black); }
            if (d.id == "Aguijon") { m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m);
            Log("textura " + d.id + ": " + AssetDatabase.GetAssetPath(tex));
        }
    }

    // ====================================================================== 1) OBJETOS QUE SUELTAN + ICONOS
    class DropDef { public string id, display; public Mesh mesh; public Material mat; public Color spark = new Color(1f, 0.95f, 0.55f); }

    static List<DropDef> MakeDropDefs()
    {
        var defs = new List<DropDef>();
        var Q = Quaternion.identity;

        // HUESO: diafisis + dos nudos en cada extremo
        var shaft = Lathe(new[] { 0.14f, 0.12f, 0.12f, 0.14f }, new[] { -0.7f, -0.3f, 0.3f, 0.7f }, 10);
        shaft = Xform(shaft, Matrix4x4.Rotate(Quaternion.Euler(0, 0, 90)));
        var knob = Ellipsoid(new Vector3(0.24f, 0.22f, 0.22f), 10, 6);
        var bone = Combine(P(shaft, Vector3.zero, Q),
            P(knob, new Vector3(0.78f, 0f, 0.15f), Q), P(knob, new Vector3(0.78f, 0f, -0.15f), Q),
            P(knob, new Vector3(-0.78f, 0f, 0.15f), Q), P(knob, new Vector3(-0.78f, 0f, -0.15f), Q));
        defs.Add(new DropDef { id = "Hueso", display = "Hueso", mesh = bone, mat = MakeMat(DropMatDir, "Hueso", new Color(0.93f, 0.89f, 0.77f), 0.25f) });

        // PIEL: pelaje aplastado con patas y cola
        var pelt = Ellipsoid(new Vector3(1.0f, 0.15f, 0.72f), 16, 8);
        var limb = Ellipsoid(new Vector3(0.3f, 0.12f, 0.2f), 10, 6);
        var tail = Segment(new Vector3(-0.85f, 0.02f, 0f), new Vector3(-1.5f, 0.02f, 0.25f), 0.07f);
        var skin = Combine(P(pelt, Vector3.zero, Q),
            P(limb, new Vector3(0.8f, 0f, 0.62f), Quaternion.Euler(0, 25, 0)), P(limb, new Vector3(0.8f, 0f, -0.62f), Quaternion.Euler(0, -25, 0)),
            P(limb, new Vector3(-0.7f, 0f, 0.6f), Quaternion.Euler(0, -25, 0)), P(limb, new Vector3(-0.7f, 0f, -0.6f), Quaternion.Euler(0, 25, 0)),
            P(Ellipsoid(new Vector3(0.28f, 0.14f, 0.24f), 10, 6), new Vector3(1.05f, 0.02f, 0f), Q), P(tail, Vector3.zero, Q));
        defs.Add(new DropDef { id = "Piel", display = "Piel", mesh = skin, mat = MakeMat(DropMatDir, "Piel", new Color(0.92f, 0.88f, 0.86f), 0.1f) });

        // COLMILLO: cono curvo marfil
        var fang = Bend(Lathe(new[] { 0.2f, 0.17f, 0.1f, 0.0f }, new[] { 0f, 0.4f, 0.85f, 1.3f }, 10), 0.35f);
        fang = Xform(fang, Matrix4x4.TRS(new Vector3(-0.55f, 0f, 0f), Quaternion.Euler(0, 0, -90), Vector3.one));
        defs.Add(new DropDef { id = "Colmillo", display = "Colmillo", mesh = fang, mat = MakeMat(DropMatDir, "Colmillo", new Color(0.97f, 0.93f, 0.74f), 0.55f) });

        // PATA DE CIEMPIES: tres segmentos articulados con garra
        var j = new[] { new Vector3(-0.8f, 0f, -0.1f), new Vector3(-0.3f, 0.02f, 0.2f), new Vector3(0.2f, 0.04f, 0.1f), new Vector3(0.7f, 0.04f, 0.4f) };
        var leg = Combine(P(Segment(j[0], j[1], 0.11f), Vector3.zero, Q), P(Segment(j[1], j[2], 0.1f), Vector3.zero, Q), P(Segment(j[2], j[3], 0.08f), Vector3.zero, Q),
            P(Ellipsoid(Vector3.one * 0.14f, 8, 5), j[1], Q), P(Ellipsoid(Vector3.one * 0.12f, 8, 5), j[2], Q),
            P(Xform(Lathe(new[] { 0.09f, 0.0f }, new[] { 0f, 0.3f }, 8), Matrix4x4.identity), j[3], Quaternion.Euler(0, 0, -70)));
        defs.Add(new DropDef { id = "PataCiempies", display = "Pata de ciempies", mesh = leg, mat = MakeMat(DropMatDir, "PataCiempies", new Color(0.82f, 0.38f, 0.12f), 0.35f) });

        // CUERNO: cono curvo oscuro con anillo en la base
        var horn = Bend(Lathe(new[] { 0.34f, 0.3f, 0.2f, 0.0f }, new[] { 0f, 0.5f, 1.0f, 1.7f }, 12), 0.3f);
        horn = Xform(horn, Matrix4x4.TRS(new Vector3(-0.8f, 0f, 0f), Quaternion.Euler(0, 0, -90), Vector3.one));
        defs.Add(new DropDef { id = "Cuerno", display = "Cuerno", mesh = horn, mat = MakeMat(DropMatDir, "Cuerno", new Color(0.3f, 0.17f, 0.09f), 0.7f) });

        // EXOESQUELETO: placa abombada con borde
        int n = 9; var dr = new float[n]; var dy = new float[n];
        for (int i = 0; i < n; i++) { float phi = Mathf.PI * 0.5f * i / (n - 1); dr[i] = Mathf.Cos(phi) * 0.85f; dy[i] = Mathf.Sin(phi) * 0.4f; }
        var dome = Xform(Lathe(dr, dy, 16, false, false), Matrix4x4.Scale(new Vector3(1f, 1f, 0.75f)));
        var shellRim = Xform(Lathe(new[] { 0.9f, 0.9f, 0.85f }, new[] { 0f, 0.05f, 0.05f }, 16, false, false), Matrix4x4.Scale(new Vector3(1f, 1f, 0.75f)));
        var shell = Combine(P(dome, Vector3.zero, Q), P(shellRim, Vector3.zero, Q));
        defs.Add(new DropDef { id = "Exoesqueleto", display = "Exoesqueleto", mesh = shell, mat = MakeMat(DropMatDir, "Exoesqueleto", new Color(0.1f, 0.2f, 0.2f), 0.85f, 0.4f, true) });

        // AGUIJON: bulbo + punta afilada
        var sting = Combine(P(Ellipsoid(new Vector3(0.3f, 0.26f, 0.26f), 12, 7), new Vector3(-0.55f, 0f, 0f), Q),
            P(Xform(Bend(Lathe(new[] { 0.14f, 0.09f, 0.0f }, new[] { 0f, 0.6f, 1.2f }, 10), -0.1f), Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, -90), Vector3.one)), new Vector3(-0.35f, 0f, 0f), Q));
        defs.Add(new DropDef { id = "Aguijon", display = "Aguijon", mesh = sting, mat = MakeMat(DropMatDir, "Aguijon", new Color(0.78f, 0.52f, 0.06f), 0.5f, 0f, false, new Color(0.15f, 0.08f, 0f)) });

        // ALAS: dos laminas finas translucidas
        var wing = Ellipsoid(new Vector3(0.9f, 0.04f, 0.45f), 18, 6);
        var wings = Combine(P(wing, new Vector3(0.1f, 0f, 0.45f), Quaternion.Euler(0, 22, 0)), P(wing, new Vector3(0.1f, 0f, -0.45f), Quaternion.Euler(0, -22, 0)),
            P(Ellipsoid(new Vector3(0.2f, 0.1f, 0.1f), 8, 5), new Vector3(-0.7f, 0f, 0f), Q));
        defs.Add(new DropDef { id = "Alas", display = "Alas", mesh = wings, mat = MakeMat(DropMatDir, "Alas", new Color(0.78f, 0.9f, 1f), 0.85f, 0f, true, new Color(0.12f, 0.2f, 0.3f)), spark = new Color(0.7f, 0.9f, 1f) });

        foreach (var d in defs) ApplyTexture(d);
        return defs;
    }

    [MenuItem("Tools/Enemies/1 Objetos y iconos")]
    public static void BuildDropsMenu() { log = new StringBuilder(); BuildDrops(); Debug.Log("[Enemies] " + log); }

    public static void BuildDrops()
    {
        Directory.CreateDirectory(DropDir); Directory.CreateDirectory(Root + "/DropMeshes");
        var defs = MakeDropDefs();
        var sprites = new List<Sprite>();

        foreach (var d in defs)
        {
            string meshPath = $"{Root}/DropMeshes/{d.id}.asset";
            d.mesh.name = d.id;
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(d.mesh, meshPath);

            var go = new GameObject("Drop_" + d.id);
            go.AddComponent<MeshFilter>().sharedMesh = d.mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = d.mat;
            var b = d.mesh.bounds;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = b.center; bc.size = new Vector3(Mathf.Max(b.size.x, 0.4f), Mathf.Max(b.size.y, 0.35f), Mathf.Max(b.size.z, 0.4f));
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.3f; rb.linearDamping = 1.5f; rb.angularDamping = 2f;
            go.AddComponent<DropItem>();
            var sp = go.AddComponent<ItemSparkle>(); sp.color = d.spark;
            var wi = go.AddComponent<WorldItem>(); wi.itemId = d.id; wi.displayName = d.display; wi.quantity = 1;

            string prefabPath = $"{DropDir}/{d.id}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            // icono
            string png = $"{IconDir}/{d.id}.png";
            var tex = RenderIcon(prefab, 256);
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceUpdate);
            var ti = AssetImporter.GetAtPath(png) as TextureImporter;
            if (ti != null) { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(png);
            sprites.Add(sprite);

            // el prefab recuerda su icono
            var pgo = PrefabUtility.LoadPrefabContents(prefabPath);
            pgo.GetComponent<WorldItem>().itemIcon = sprite;
            PrefabUtility.SaveAsPrefabAsset(pgo, prefabPath);
            PrefabUtility.UnloadPrefabContents(pgo);
            Log($"objeto {d.id}: prefab + icono ({(sprite != null ? "ok" : "SIN SPRITE")})");
        }

        // la carne ya existe: se copia a Resources/Drops y flota/brilla como el resto
        string meat = "Assets/Prefabs/Carne_Drop.prefab", meatCopy = $"{DropDir}/Carne.prefab";
        if (File.Exists(meat))
        {
            foreach (var path in new[] { meat })
            {
                var pgo = PrefabUtility.LoadPrefabContents(path);
                if (pgo.GetComponent<DropItem>() == null) pgo.AddComponent<DropItem>();
                if (pgo.GetComponent<ItemSparkle>() == null) pgo.AddComponent<ItemSparkle>().color = new Color(1f, 0.6f, 0.5f);
                var w = pgo.GetComponent<WorldItem>(); if (w != null) { w.itemId = "Carne"; w.displayName = "Carne"; }
                PrefabUtility.SaveAsPrefabAsset(pgo, path); PrefabUtility.UnloadPrefabContents(pgo);
            }
            AssetDatabase.CopyAsset(meat, meatCopy);
            Log("Carne copiada a Resources/Drops");
        }

        // registrar iconos en la base de datos del inventario
        var ctrl = Object.FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (ctrl != null)
        {
            var so = new SerializedObject(ctrl);
            var db = so.FindProperty("iconDatabase");
            foreach (var s in sprites)
            {
                if (s == null) continue;
                bool has = false;
                for (int i = 0; i < db.arraySize; i++)
                { var o = db.GetArrayElementAtIndex(i).objectReferenceValue; if (o != null && o.name == s.name) { db.GetArrayElementAtIndex(i).objectReferenceValue = s; has = true; } }
                if (!has) { db.arraySize++; db.GetArrayElementAtIndex(db.arraySize - 1).objectReferenceValue = s; Log("iconDatabase += " + s.name); }
            }
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(ctrl.gameObject.scene);
        }
        AssetDatabase.SaveAssets();
        foreach (var d in defs) AssetDatabase.ImportAsset($"{DropDir}/{d.id}.prefab", ImportAssetOptions.ForceUpdate);
    }

    /// <summary>Vuelve a renderizar los iconos de los objetos soltados (con las texturas ya cargadas).</summary>
    public static void BuildIcons()
    {
        foreach (var id in new[] { "Hueso", "Piel", "Colmillo", "PataCiempies", "Cuerno", "Exoesqueleto", "Aguijon", "Alas" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{DropDir}/{id}.prefab");
            if (prefab == null) { Log("sin prefab " + id); continue; }
            var tex = RenderIcon(prefab, 256);
            string png = $"{IconDir}/{id}.png";
            File.WriteAllBytes(png, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(png, ImportAssetOptions.ForceUpdate);
            Log("icono re-renderizado: " + id);
        }
        AssetDatabase.Refresh();
    }

    static Texture2D RenderIcon(GameObject model, int size)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            var inst = Object.Instantiate(model);
            inst.hideFlags = HideFlags.HideAndDontSave;
            foreach (var c in inst.GetComponentsInChildren<MonoBehaviour>()) Object.DestroyImmediate(c);
            pru.AddSingleGO(inst);
            var rends = inst.GetComponentsInChildren<Renderer>();
            Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
            var cam = pru.camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.01f; cam.farClipPlane = 200f; cam.fieldOfView = 25f;
            float dist = b.extents.magnitude / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
            cam.transform.position = b.center + (Quaternion.Euler(52f, -30f, 0f) * Vector3.back) * dist;
            cam.transform.LookAt(b.center);
            pru.lights[0].intensity = 1.1f; pru.lights[0].transform.rotation = Quaternion.Euler(40f, 20f, 0f);
            pru.lights[1].intensity = 0.5f; pru.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
            pru.ambientColor = new Color(0.4f, 0.4f, 0.4f, 1f);

            cam.backgroundColor = Color.black;
            pru.BeginStaticPreview(new Rect(0, 0, size, size)); cam.Render();
            var t1 = pru.EndStaticPreview(); var onBlack = t1.GetPixels(); int w = t1.width, h = t1.height;
            cam.backgroundColor = Color.white;
            pru.BeginStaticPreview(new Rect(0, 0, size, size)); cam.Render();
            var t2 = pru.EndStaticPreview(); var onWhite = t2.GetPixels();

            var result = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var outPx = new Color[onBlack.Length];
            for (int i = 0; i < outPx.Length; i++)
            {
                float a = Mathf.Clamp01(1f - ((onWhite[i].r - onBlack[i].r) + (onWhite[i].g - onBlack[i].g) + (onWhite[i].b - onBlack[i].b)) / 3f);
                outPx[i] = a > 0.003f ? new Color(Mathf.Clamp01(onBlack[i].r / a), Mathf.Clamp01(onBlack[i].g / a), Mathf.Clamp01(onBlack[i].b / a), a) : new Color(0, 0, 0, 0);
            }
            result.SetPixels(outPx); result.Apply();
            Object.DestroyImmediate(inst);
            return result;
        }
        finally { pru.Cleanup(); }
    }

    // ====================================================================== 2) PREFABS DE CRIATURAS
    class CDef
    {
        public string id, display, glb;
        public CreatureAI.Kind kind;
        public CreatureVisual.Mode mode;
        public float yaw, size, thick = 1f; public bool bySpan;
        public float hp, dmg, interval, chase, detect, range = 2f, recovery = 0.5f, knock = 1f, fly = 0f;
        public int copies = 1;                 // hormigas: 3 por enjambre
        public float walkF = 2.5f, runF = 6f, swing = 0.07f, lift = 0.05f, waves = 1.5f, amp = 0.09f, flap = 12f;
        public bool inchworm;
        public (string id, int amount, float chance, string group)[] drops;
        public int count;                      // cuantos se colocan en el salon
    }

    static CDef[] Defs()
    {
        return new[]
        {
            new CDef { id = "Oruga", display = "Oruga verde", glb = "caterpillar", kind = CreatureAI.Kind.Caterpillar, mode = CreatureVisual.Mode.Undulate, yaw = 0, size = 4.2f,
                hp = 120, dmg = 50, interval = 5f, chase = 1.7f, detect = 12f, range = 11f, recovery = 0.6f, walkF = 1.0f, runF = 2.4f, waves = 1.1f, amp = 0.05f, inchworm = true, count = 2,
                drops = new[] { ("Carne", 2, 100f, "") } },
            new CDef { id = "Lombriz", display = "Lombriz", glb = "earthworm", kind = CreatureAI.Kind.Worm, mode = CreatureVisual.Mode.Undulate, yaw = 0, size = 4.6f, thick = 2.3f,
                hp = 30, dmg = 5, interval = 0.5f, chase = 3.6f, detect = 10f, range = 2.1f, recovery = 0.3f, walkF = 1.5f, runF = 3.6f, waves = 2.2f, amp = 0.09f, count = 3,
                drops = new[] { ("Carne", 1, 50f, "") } },
            new CDef { id = "Hormigas", display = "Hormigas", glb = "ant", kind = CreatureAI.Kind.Ants, mode = CreatureVisual.Mode.Skitter, yaw = 90, size = 1.7f, copies = 3,
                hp = 60, dmg = 20, interval = 1f, chase = 5.3f, detect = 11f, range = 2.4f, recovery = 0.35f, walkF = 3f, runF = 9f, swing = 0.1f, lift = 0.07f, count = 3,
                drops = new[] { ("Colmillo", 2, 75f, "") } },
            new CDef { id = "Ciempies", display = "Ciempies", glb = "centipede", kind = CreatureAI.Kind.Centipede, mode = CreatureVisual.Mode.Undulate, yaw = 0, size = 5.6f, thick = 2.6f,
                hp = 100, dmg = 15, interval = 0.8f, chase = 6f, detect = 12f, range = 2.5f, recovery = 0.3f, walkF = 2f, runF = 6.5f, waves = 2.6f, amp = 0.06f, count = 2,
                drops = new[] { ("PataCiempies", 5, 20f, "p"), ("PataCiempies", 4, 25f, "p"), ("PataCiempies", 3, 55f, "p") } },
            new CDef { id = "Escarabajo", display = "Escarabajo rinoceronte", glb = "beetle", kind = CreatureAI.Kind.Beetle, mode = CreatureVisual.Mode.Skitter, yaw = 0, size = 3.4f, thick = 1.4f,
                hp = 80, dmg = 40, interval = 3f, chase = 3f, detect = 13f, range = 2.6f, recovery = 0.7f, walkF = 2f, runF = 6f, swing = 0.06f, lift = 0.05f, count = 2,
                drops = new[] { ("Cuerno", 1, 50f, ""), ("Exoesqueleto", 3, 25f, "e"), ("Exoesqueleto", 2, 50f, "e"), ("Exoesqueleto", 1, 75f, "e") } },
            new CDef { id = "Abeja", display = "Abeja", glb = "bee", kind = CreatureAI.Kind.Bee, mode = CreatureVisual.Mode.Animator, yaw = 0, size = 2.3f, bySpan = true,
                hp = 40, dmg = 30, interval = 1.5f, chase = 6f, detect = 13f, range = 1.7f, recovery = 0.9f, knock = 1.5f, fly = 1.7f, count = 3,
                drops = new[] { ("Aguijon", 1, 90f, "") } },
            new CDef { id = "Cochinilla", display = "Cochinilla", glb = null, kind = CreatureAI.Kind.Woodlouse, mode = CreatureVisual.Mode.Plates, yaw = 0, size = 2.4f,
                hp = 100, dmg = 15, interval = 3f, chase = 2.3f, detect = 12f, range = 2f, recovery = 0.5f, walkF = 2f, runF = 4f, count = 2,
                drops = new[] { ("Exoesqueleto", 2, 15f, "e"), ("Exoesqueleto", 1, 50f, "e") } },
            new CDef { id = "Mariposa", display = "Mariposa", glb = "butterfly2", kind = CreatureAI.Kind.Butterfly, mode = CreatureVisual.Mode.Flutter, yaw = 0, size = 3.0f, bySpan = true,
                hp = 10, dmg = 0, interval = 1f, chase = 2.2f, detect = 9f, range = 1f, recovery = 0.2f, fly = 1.5f, flap = 12f, count = 3,
                drops = new[] { ("Alas", 1, 100f, "") } },
        };
    }

    [MenuItem("Tools/Enemies/2 Prefabs de criaturas")]
    public static void BuildPrefabsMenu() { log = new StringBuilder(); BuildCreaturePrefabs(); Debug.Log("[Enemies] " + log); }

    public static void BuildCreaturePrefabs()
    {
        Directory.CreateDirectory(MeshDir); Directory.CreateDirectory(PrefabDir); Directory.CreateDirectory(MatDir);
        var particles = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RatDeathParticles.prefab");

        foreach (var d in Defs())
        {
            var root = new GameObject("Enemy_" + d.id);
            Bounds vb = default;

            // ---------- visual(es)
            var visuals = new List<CreatureVisual>();
            GameObject firstVis = null;
            for (int c = 0; c < d.copies; c++)
            {
                GameObject vis;
                if (c == 0)
                {
                    vis = new GameObject(d.copies > 1 ? "Visual_0" : "Visual");
                    vis.transform.SetParent(root.transform, false);
                    if (d.kind == CreatureAI.Kind.Woodlouse) vb = BuildWoodlouse(vis);
                    else if (d.kind == CreatureAI.Kind.Bee) vb = BuildBee(vis, d, particles);
                    else vb = BakeMeshes(vis, d);
                    if (d.kind == CreatureAI.Kind.Beetle) AddHorn(vis, vb);
                    var cv = vis.AddComponent<CreatureVisual>();
                    cv.mode = d.mode; cv.walkFreq = d.walkF; cv.runFreq = d.runF; cv.legSwing = d.swing; cv.legLift = d.lift;
                    cv.waves = d.waves; cv.undAmp = d.amp; cv.inchworm = d.inchworm; cv.flapFreq = d.flap;
                    firstVis = vis;
                }
                else
                {
                    vis = Object.Instantiate(firstVis, root.transform);
                    vis.name = "Visual_" + c;
                }
                visuals.Add(vis.GetComponent<CreatureVisual>());
            }

            // ---------- colisionador
            float h = Mathf.Max(vb.size.y, 0.8f);
            var hit = new GameObject("Hitbox"); hit.transform.SetParent(root.transform, false);
            var box = hit.AddComponent<BoxCollider>();
            float w = d.copies > 1 ? 3.2f : Mathf.Max(vb.size.x * 0.9f, 1.2f);
            float l = d.copies > 1 ? 3.2f : Mathf.Max(vb.size.z * 0.9f, 1.2f);
            if (d.kind == CreatureAI.Kind.Worm) { w = Mathf.Max(w, 1.4f); h = 1.0f; }
            if (d.fly > 0f) { box.size = new Vector3(Mathf.Max(w, 1.6f), 1.8f, Mathf.Max(l, 1.6f)); hit.transform.localPosition = Vector3.zero; }   // el propio objeto vuela
            else { box.size = new Vector3(w, h, l); hit.transform.localPosition = new Vector3(0f, h * 0.5f, 0f); }

            // ---------- vida (RatHealth)
            var health = root.AddComponent<RatHealth>();
            var so = new SerializedObject(health);
            so.FindProperty("enemyName").stringValue = d.display;
            so.FindProperty("maxHealth").floatValue = d.hp;
            so.FindProperty("currentHealth").floatValue = d.hp;
            so.ApplyModifiedProperties();

            // ---------- IA
            var ai = root.AddComponent<CreatureAI>();
            ai.kind = d.kind; ai.visuals = visuals.ToArray();
            ai.chaseSpeed = d.chase; ai.returnSpeed = d.chase * 0.8f; ai.detectionDistance = d.detect; ai.loseDistance = d.detect * 1.8f;
            ai.attackRange = d.range; ai.attackDamage = d.dmg; ai.attackInterval = d.interval;
            ai.hitRecoveryTime = d.recovery; ai.knockbackDistance = d.knock; ai.flyHeight = d.fly;
            ai.barHeight = h + 0.9f; ai.deathParticlesPrefab = particles;

            // ---------- drops
            var table = root.AddComponent<DropTable>();
            foreach (var e in d.drops) table.entries.Add(new DropEntry { itemId = e.id, amount = e.amount, chance = e.chance, group = e.group });

            string path = $"{PrefabDir}/Enemy_{d.id}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Log($"prefab {path}: {d.display} hp={d.hp} dano={d.dmg}/{d.interval}s");
        }

        // Rata y Arana (ya existen): vida/dano del documento + tabla de drops
        PatchExistingEnemies();
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }

    // hornea los meshes del modelo en el espacio del Visual: adelante +Z, apoyado en y=0, centrado
    static Bounds BakeMeshes(GameObject vis, CDef d)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{d.glb}.glb");
        var tmp = Object.Instantiate(model);
        tmp.transform.rotation = Quaternion.Euler(0f, d.yaw, 0f);
        var rends = tmp.GetComponentsInChildren<MeshRenderer>();
        Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        float dim = d.bySpan ? b.size.x : b.size.z;
        float s = d.size / dim;
        Vector3 off = new Vector3(-b.center.x, -b.min.y, -b.center.z);
        Bounds outB = new Bounds();
        bool first = true;
        int idx = 0;
        foreach (var r in rends)
        {
            var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            var src = mf.sharedMesh;
            var xf = Matrix4x4.Scale(new Vector3(s * d.thick, s * d.thick, s)) * Matrix4x4.Translate(off) * r.transform.localToWorldMatrix;
            var m = new Mesh { name = $"{d.id}_{idx}" };
            var vs = src.vertices; var ns = src.normals;
            var nv = new Vector3[vs.Length]; var nn = new Vector3[vs.Length];
            for (int i = 0; i < vs.Length; i++)
            {
                nv[i] = xf.MultiplyPoint3x4(vs[i]);
                nn[i] = ns.Length == vs.Length ? xf.inverse.transpose.MultiplyVector(ns[i]).normalized : Vector3.up;
                if (first) { outB = new Bounds(nv[i], Vector3.zero); first = false; } else outB.Encapsulate(nv[i]);
            }
            m.indexFormat = vs.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            m.vertices = nv; m.normals = nn; m.uv = src.uv;
            m.subMeshCount = src.subMeshCount;
            for (int sm = 0; sm < src.subMeshCount; sm++) m.SetTriangles(src.GetTriangles(sm), sm);
            m.RecalculateBounds();
            string mp = $"{MeshDir}/{d.id}_{idx}.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(mp) != null) AssetDatabase.DeleteAsset(mp);
            AssetDatabase.CreateAsset(m, mp);

            var part = new GameObject("Part_" + idx);
            part.transform.SetParent(vis.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = m;
            part.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
            idx++;
        }
        Object.DestroyImmediate(tmp);
        Log($"horneado {d.glb}: {idx} partes, tamano {outB.size}");
        return outB;
    }

    static void AddHorn(GameObject vis, Bounds b)
    {
        // cuerno de rinoceronte sobre la cabeza
        var horn = Bend(Lathe(new[] { 0.16f, 0.12f, 0.06f, 0.0f }, new[] { 0f, 0.35f, 0.75f, 1.1f }, 12), 0.25f);
        float L = b.size.z, H = b.size.y;
        horn = Xform(horn, Matrix4x4.Scale(Vector3.one * (L * 0.30f)));
        string mp = $"{MeshDir}/Escarabajo_horn.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(mp) != null) AssetDatabase.DeleteAsset(mp);
        horn.name = "Escarabajo_horn"; AssetDatabase.CreateAsset(horn, mp);
        var go = new GameObject("Static_Horn");
        go.transform.SetParent(vis.transform, false);
        go.transform.localPosition = new Vector3(0f, H * 0.62f, b.center.z + L * 0.12f);
        go.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);   // apunta adelante y arriba
        go.AddComponent<MeshFilter>().sharedMesh = horn;
        go.AddComponent<MeshRenderer>().sharedMaterial = MakeMat(MatDir, "Cuerno_Escarabajo", new Color(0.18f, 0.1f, 0.06f), 0.75f);
    }

    static Bounds BuildBee(GameObject vis, CDef d, GameObject particles)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{d.glb}.glb");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, vis.transform);
        var tmp = Object.Instantiate(model);
        var rends = tmp.GetComponentsInChildren<Renderer>();
        Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
        Object.DestroyImmediate(tmp);
        float s = d.size / b.size.x;
        inst.transform.localScale = Vector3.one * s;
        inst.transform.localPosition = new Vector3(-b.center.x * s, -b.center.y * s, -b.center.z * s);   // la abeja vuela: el origen es el centro del cuerpo
        var an = inst.GetComponent<Animator>(); if (an == null) an = inst.AddComponent<Animator>();

        // controlador con las animaciones del modelo: Flying (correr/idle), Bite_Front (atacar), Death (morir)
        var clips = AssetDatabase.LoadAllAssetsAtPath($"{Models}/{d.glb}.glb").OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        AnimationClip Copy(string key, bool loop)
        {
            var src = clips.FirstOrDefault(c => c.name.ToLower().Contains(key));
            if (src == null) { Log("Falta animacion " + key); return null; }
            string p = $"{Root}/Bee_{key}.anim";
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(p) != null) AssetDatabase.DeleteAsset(p);
            var cp = Object.Instantiate(src); cp.name = "Bee_" + key; cp.legacy = false;
            var st = AnimationUtility.GetAnimationClipSettings(cp); st.loopTime = loop; AnimationUtility.SetAnimationClipSettings(cp, st);
            AssetDatabase.CreateAsset(cp, p);
            return cp;
        }
        var fly = Copy("flying", true); var bite = Copy("bite", false); var death = Copy("death", false);
        string cpath = $"{Root}/Bee_Controller.controller";
        if (File.Exists(cpath)) AssetDatabase.DeleteAsset(cpath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(cpath);
        var sm = ctrl.layers[0].stateMachine;
        var sIdle = sm.AddState("Idle"); sIdle.motion = fly;
        var sRun = sm.AddState("Run"); sRun.motion = fly; sRun.speed = 1.3f;
        var sAtk = sm.AddState("Attack"); sAtk.motion = bite;
        var sDeath = sm.AddState("Death"); sDeath.motion = death;
        sm.defaultState = sIdle;
        var back = sAtk.AddTransition(sRun); back.hasExitTime = true; back.exitTime = 1f; back.duration = 0.1f;
        an.runtimeAnimatorController = ctrl; an.applyRootMotion = false;
        Log($"abeja: clips {clips.Count} -> flying={(fly != null)} bite={(bite != null)} death={(death != null)}");
        return new Bounds(new Vector3(0, d.fly, 0), new Vector3(d.size, d.size * 0.65f, d.size * 0.6f));
    }

    static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(t);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = pos; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    // cochinilla procedural: 7 placas que se enrollan en bola, patas y antenas
    static Bounds BuildWoodlouse(GameObject vis)
    {
        var plate = MakeMat(MatDir, "Cochinilla_Placa", new Color(0.34f, 0.36f, 0.42f), 0.55f, 0.2f);
        var plate2 = MakeMat(MatDir, "Cochinilla_Placa2", new Color(0.44f, 0.46f, 0.52f), 0.55f, 0.2f);
        var legM = MakeMat(MatDir, "Cochinilla_Pata", new Color(0.55f, 0.55f, 0.58f), 0.3f);
        var eyeM = MakeMat(MatDir, "Cochinilla_Ojo", new Color(0.02f, 0.02f, 0.03f), 0.9f);
        float[] z = { 1.0f, 0.62f, 0.2f, -0.2f, -0.6f, -0.95f, -1.2f };
        float[] w = { 0.75f, 1.2f, 1.4f, 1.5f, 1.4f, 1.2f, 0.85f };
        float[] h = { 0.42f, 0.62f, 0.72f, 0.74f, 0.7f, 0.58f, 0.4f };
        for (int i = 0; i < 7; i++)
            Prim(PrimitiveType.Sphere, "Plate" + i, vis.transform, new Vector3(0f, 0.12f + h[i] * 0.5f, z[i]), new Vector3(w[i], h[i], i == 0 ? 0.62f : 0.6f), i % 2 == 0 ? plate : plate2);
        for (int i = 0; i < 7; i++)
            foreach (float side in new[] { -1f, 1f })
            {
                var leg = Prim(PrimitiveType.Capsule, "Leg_" + (side < 0 ? "L" : "R") + i, vis.transform, new Vector3(side * w[i] * 0.5f, 0.1f, z[i]), new Vector3(0.08f, 0.26f, 0.08f), legM);
                leg.transform.localRotation = Quaternion.Euler(0f, 0f, side * 55f);
            }
        foreach (float side in new[] { -1f, 1f })
        {
            var ant = Prim(PrimitiveType.Capsule, "Ant_" + (side < 0 ? "L" : "R"), vis.transform, new Vector3(side * 0.22f, 0.45f, 1.45f), new Vector3(0.05f, 0.3f, 0.05f), legM);
            ant.transform.localRotation = Quaternion.Euler(70f, side * 18f, 0f);
            Prim(PrimitiveType.Sphere, "Eye_" + (side < 0 ? "L" : "R"), vis.transform, new Vector3(side * 0.25f, 0.5f, 1.22f), Vector3.one * 0.14f, eyeM);
        }
        return new Bounds(new Vector3(0, 0.45f, 0), new Vector3(1.5f, 0.9f, 2.6f));
    }

    static void PatchExistingEnemies()
    {
        // ----- Rata
        foreach (var rat in Object.FindObjectsByType<RatAI>(FindObjectsInactive.Include))
        {
            var hl = rat.GetComponent<RatHealth>();
            var s1 = new SerializedObject(hl); s1.FindProperty("maxHealth").floatValue = 100f; s1.FindProperty("currentHealth").floatValue = 100f; s1.ApplyModifiedProperties();
            var s2 = new SerializedObject(rat); s2.FindProperty("attackDamage").floatValue = 35f; s2.FindProperty("attackInterval").floatValue = 1.3f; s2.ApplyModifiedProperties();
            var t = rat.GetComponent<DropTable>() ?? rat.gameObject.AddComponent<DropTable>();
            t.entries = new List<DropEntry> { new DropEntry { itemId = "Hueso", amount = 1, chance = 100f }, new DropEntry { itemId = "Piel", amount = 1, chance = 100f } };
            EditorUtility.SetDirty(rat.gameObject);
            Log("rata: 100 PV, 35/1.3s, suelta hueso + piel (+carne 3/2/1 al 20/30/50%)");
        }
        // ----- Arana (prefab y instancias)
        var spiderPrefabPath = "Assets/Spider/Spider_Enemy.prefab";
        var pgo = PrefabUtility.LoadPrefabContents(spiderPrefabPath);
        ConfigureSpider(pgo);
        PrefabUtility.SaveAsPrefabAsset(pgo, spiderPrefabPath); PrefabUtility.UnloadPrefabContents(pgo);
        foreach (var sp in Object.FindObjectsByType<SpiderAI>(FindObjectsInactive.Include))
        {
            ConfigureSpider(sp.gameObject);
            EditorUtility.SetDirty(sp.gameObject);
        }
        Log("arana: 80 PV, 30/2s, suelta 2 colmillos (75%) + telarana que ralentiza");
    }

    static void ConfigureSpider(GameObject go)
    {
        var hl = go.GetComponent<RatHealth>();
        var s1 = new SerializedObject(hl); s1.FindProperty("enemyName").stringValue = "Araña negra";
        s1.FindProperty("maxHealth").floatValue = 80f; s1.FindProperty("currentHealth").floatValue = 80f; s1.ApplyModifiedProperties();
        var ai = go.GetComponent<SpiderAI>();
        var s2 = new SerializedObject(ai);
        s2.FindProperty("attackDamage").floatValue = 30f; s2.FindProperty("attackInterval").floatValue = 2f;
        s2.FindProperty("webCooldown").floatValue = 6f; s2.FindProperty("webMinRange").floatValue = 5f; s2.FindProperty("webMaxRange").floatValue = 13f;
        s2.FindProperty("webSlow").floatValue = 0.45f; s2.FindProperty("webSlowSeconds").floatValue = 3f;
        s2.ApplyModifiedProperties();
        var t = go.GetComponent<DropTable>() ?? go.AddComponent<DropTable>();
        t.entries = new List<DropEntry> { new DropEntry { itemId = "Colmillo", amount = 2, chance = 75f } };
    }

    // ====================================================================== 3) COLOCAR EN EL SALON
    [MenuItem("Tools/Enemies/3 Colocar en el salon")]
    public static void PlaceMenu() { log = new StringBuilder(); Place(); Debug.Log("[Enemies] " + log); }

    const float X0 = -60f, X1 = 60f, Z0 = -82f, Z1 = 70f;
    static readonly Vector3 playerStart = new Vector3(-31.5f, 0f, -51.2f);

    static bool FloorAt(float x, float z, out float y)
    {
        y = 0f;
        var hits = Physics.RaycastAll(new Vector3(x, 8f, z), Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits) if (h.normal.y > 0.9f && h.point.y < 0.4f && h.point.y > best) best = h.point.y;
        if (float.IsNegativeInfinity(best)) return false;
        y = best; return true;
    }

    static bool Free(Vector3 p, float r)
    {
        return !Physics.CheckBox(new Vector3(p.x, 1.4f, p.z), new Vector3(r, 0.6f, r), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
    }

    public static void Place()
    {
        var scene = EditorSceneManager.GetActiveScene();
        Physics.SyncTransforms();
        var rng = new System.Random(7771);
        float R() { return (float)rng.NextDouble(); }

        // limpiar una colocacion anterior
        foreach (var old in scene.GetRootGameObjects().Where(g => g.name.StartsWith("Enemy_")).ToList()) Object.DestroyImmediate(old);
        Physics.SyncTransforms();

        // anclas = posiciones de los animales (los existentes cuentan)
        var anchors = new List<Vector3>();
        var existing = new List<GameObject>();
        foreach (var rat in Object.FindObjectsByType<RatAI>(FindObjectsInactive.Include)) { existing.Add(rat.gameObject); anchors.Add(rat.transform.position); }
        foreach (var sp in Object.FindObjectsByType<SpiderAI>(FindObjectsInactive.Include)) { existing.Add(sp.gameObject); anchors.Add(sp.transform.position); }

        var spawnList = new List<(CDef d, int n)>();
        foreach (var d in Defs()) for (int i = 0; i < d.count; i++) spawnList.Add((d, i + 1));
        // una arana mas, para que haya dos
        int placedEnemies = 0;

        // intercalar tipos para que no queden agrupados
        spawnList = spawnList.OrderBy(x => R()).ToList();
        foreach (var (d, n) in spawnList)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/Enemy_{d.id}.prefab");
            if (prefab == null) { Log("sin prefab " + d.id); continue; }
            bool ok = false; Vector3 pos = default; float fy = 0f;
            for (int tries = 0; tries < 3000 && !ok; tries++)
            {
                float x = Mathf.Lerp(X0, X1, R()), z = Mathf.Lerp(Z0, Z1, R());
                if (!FloorAt(x, z, out fy)) continue;
                var p = new Vector3(x, fy, z);
                if ((p - playerStart).magnitude < 24f) continue;
                if (anchors.Any(a => (new Vector3(a.x, 0, a.z) - new Vector3(p.x, 0, p.z)).magnitude < 20f)) continue;
                if (!Free(p, 2.6f)) continue;
                pos = p; ok = true;
            }
            if (!ok) { Log($"sin lugar para {d.id} #{n}"); continue; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.name = $"Enemy_{d.id}_{n}";
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, R() * 360f, 0f);
            anchors.Add(pos); placedEnemies++;
        }
        // arana adicional
        {
            var spPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Spider/Spider_Enemy.prefab");
            for (int tries = 0; tries < 3000; tries++)
            {
                float x = Mathf.Lerp(X0, X1, R()), z = Mathf.Lerp(Z0, Z1, R());
                if (!FloorAt(x, z, out float fy)) continue;
                var p = new Vector3(x, fy, z);
                if ((p - playerStart).magnitude < 30f || anchors.Any(a => (new Vector3(a.x, 0, a.z) - new Vector3(p.x, 0, p.z)).magnitude < 20f) || !Free(p, 2.6f)) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(spPrefab, scene);
                go.name = "Enemy_Arana_2"; go.transform.position = p; go.transform.rotation = Quaternion.Euler(0f, R() * 360f, 0f);
                ConfigureSpider(go);
                anchors.Add(p); placedEnemies++; break;
            }
        }
        Log($"enemigos colocados: {placedEnemies} (+{existing.Count} existentes)");

        // ---------- objetos del suelo alrededor de los animales, siempre visibles
        Physics.SyncTransforms();
        var items = scene.GetRootGameObjects().Where(g => g.GetComponent<WorldItem>() != null && g.GetComponent<DropItem>() == null).OrderBy(x => R()).ToList();
        Log("objetos del suelo a reubicar: " + items.Count);
        var used = new List<Vector3>();
        int moved = 0, failed = 0, ai = 0;
        foreach (var it in items)
        {
            bool ok = false; Vector3 pos = default;
            for (int tries = 0; tries < 400 && !ok; tries++)
            {
                var a = anchors[(ai + tries / 40) % anchors.Count];
                float ang = R() * Mathf.PI * 2f, rad = Mathf.Lerp(3.5f, 8.5f, R());
                float x = a.x + Mathf.Cos(ang) * rad, z = a.z + Mathf.Sin(ang) * rad;
                if (x < -66f || x > 66f || z < -88f || z > 77f) continue;
                if (!FloorAt(x, z, out float fy)) continue;
                var p = new Vector3(x, fy, z);
                if (!Free(p, 1.0f)) continue;
                if (used.Any(u => (u - p).sqrMagnitude < 2.2f * 2.2f)) continue;
                if ((p - playerStart).magnitude < 5f) continue;
                pos = p; ok = true;
            }
            ai++;
            if (!ok) { failed++; continue; }
            // apoyarlo por ENCIMA de los parches de musgo del decorado (que llegan a y ~ 0.15)
            it.transform.rotation = Quaternion.Euler(0f, R() * 360f, 0f) * it.transform.rotation;
            var rs = it.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            it.transform.position += new Vector3(pos.x - b.center.x, pos.y + 0.32f - b.min.y, pos.z - b.center.z);
            if (it.GetComponent<ItemSparkle>() == null) it.AddComponent<ItemSparkle>();
            used.Add(pos); moved++;
            EditorUtility.SetDirty(it);
        }
        Log($"objetos reubicados alrededor de animales: {moved}, sin lugar: {failed}");

        // registrar en el gestor de prompts (lista serializada si existe)
        var mgr = Object.FindAnyObjectByType<FloorItemPromptManager>(FindObjectsInactive.Include);
        if (mgr != null)
        {
            var so = new SerializedObject(mgr);
            var list = so.FindProperty("floorObjects");
            if (list != null)
            {
                var have = new HashSet<Object>();
                for (int i = 0; i < list.arraySize; i++) have.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
                foreach (var it in items)
                    if (!have.Contains(it)) { list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = it; }
                so.ApplyModifiedProperties();
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log("escena guardada");
    }
}
