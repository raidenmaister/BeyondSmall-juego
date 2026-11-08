using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// Genera el hormiguero gigante: monticulo con entrada en el suelo de afuera, tuneles laberinticos
/// en dos niveles con camaras (despensas, guarderias con larvas, dormitorios), pasadizos estrechos
/// y, al fondo, la camara de la reina con el hongo blanco gigante.
/// La cueva es un campo de distancia (union suave de capsulas y elipsoides con ruido) convertido en
/// malla con "surface nets", por trozos (culling + colisiones). La luz de los hongos se hornea en el
/// color de vertice. Menu: Herramientas > Hormiguero > Construir (vuelve a generar todo).
/// </summary>
public static class HormigueroBuilder
{
    const string Raiz = "Assets/Hormiguero/";
    const string Gen = Raiz + "Generado/";
    const string Mat = Raiz + "Materiales/";
    const float Celda = 1.5f;
    const int Trozo = 32;
    const float RadioTunel = 6.5f;
    const float RadioPasadizo = 3.4f;
    const float Suavizado = 5f;

    static Vector3 O;                 // entrada (centro del monticulo) en el mundo
    static System.Random rnd;

    // ================================================================== grafo del laberinto
    class Nodo
    {
        public Vector3 p, r;          // centro y radios (mundo)
        public string tipo = "cruce";
        public int nivel;
        public List<int> vec = new List<int>();
        public Vector3 Piso => new Vector3(p.x, p.y - r.y * 0.5f, p.z);
    }

    class Arista { public int a, b; public float r; public bool pasadizo; public List<Vector3> extra = new List<Vector3>(); }

    static List<Nodo> nodos;
    static List<Arista> aristas;
    static int iEntrada, iVestibulo, iReina, iAntesala;

    static Vector3 L(float x, float y, float z) { return O + new Vector3(x, y, z); }

    static float R(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }

    static int NuevoNodo(Vector3 p, Vector3 r, string tipo, int nivel)
    {
        nodos.Add(new Nodo { p = p, r = r, tipo = tipo, nivel = nivel });
        return nodos.Count - 1;
    }

    static Arista Unir(int a, int b, float r = RadioTunel, bool pasadizo = false)
    {
        var e = new Arista { a = a, b = b, r = r, pasadizo = pasadizo };
        aristas.Add(e);
        nodos[a].vec.Add(b); nodos[b].vec.Add(a);
        return e;
    }

    static void ConstruirGrafo()
    {
        nodos = new List<Nodo>(); aristas = new List<Arista>();
        iEntrada = NuevoNodo(L(0f, -1f, 0f), new Vector3(5f, 4f, 5f), "entrada", 0);

        // nivel 1: rejilla 5x5 bajo el monticulo
        var g1 = new int[5, 5];
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
            {
                bool vest = i == 2 && j == 0;
                var p = L(-96f + 48f * i + (vest ? 0f : R(-6f, 6f)), vest ? -34f : -34f + R(-2.5f, 2.5f), 84f + 44f * j + (vest ? 0f : R(-6f, 6f)));
                g1[i, j] = NuevoNodo(p, vest ? new Vector3(13f, 9f, 13f) : new Vector3(R(7.5f, 9.5f), 7f, R(7.5f, 9.5f)), vest ? "vestibulo" : "cruce", 1);
            }
        iVestibulo = g1[2, 0];
        // nivel 2: rejilla 4x5 desplazada media celda
        var g2 = new int[4, 5];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 5; j++)
            {
                var p = L(-72f + 48f * i + R(-6f, 6f), -58f + R(-2.5f, 2.5f), 106f + 44f * j + R(-6f, 6f));
                g2[i, j] = NuevoNodo(p, new Vector3(R(7.5f, 9.5f), 7f, R(7.5f, 9.5f)), "cruce", 2);
            }

        // tunel de entrada: baja en curva desde el crater hasta el vestibulo
        var ent = Unir(iEntrada, iVestibulo);
        ent.extra.AddRange(new[] { L(10f, -7f, 20f), L(-8f, -16f, 40f), L(8f, -25f, 62f) });

        Laberinto(g1, 5, 5);
        Laberinto(g2, 4, 5);

        // rampas entre niveles (bajada suave, unos 20 grados)
        var usadas = new HashSet<int>();
        int rampas = 0;
        for (int intento = 0; intento < 60 && rampas < 3; intento++)
        {
            int i = rnd.Next(0, 4), j = rnd.Next(0, 4);
            if (usadas.Contains(i) && rampas < 2) continue;   // repartidas a lo ancho
            if (nodos[g1[i, j]].vec.Contains(g2[i, j + 1])) continue;
            Unir(g1[i, j], g2[i, j + 1]);
            usadas.Add(i); rampas++;
        }

        // pasadizos estrechos y secretos: atajos en diagonal
        int pas = 0;
        for (int intento = 0; intento < 80 && pas < 5; intento++)
        {
            bool n1 = pas < 3;
            int w = n1 ? 5 : 4;
            int i = rnd.Next(0, w - 1), j = rnd.Next(0, 4);
            int a = n1 ? g1[i, j] : g2[i, j], b = n1 ? g1[i + 1, j + 1] : g2[i + 1, j + 1];
            if (a == iVestibulo || b == iVestibulo || nodos[a].vec.Contains(b)) continue;
            Unir(a, b, RadioPasadizo, true);
            pas++;
        }

        // antesala y camara de la reina, al fondo de todo
        int acceso = g2[2, 4];
        iAntesala = NuevoNodo(L(0f, -72f, 314f), new Vector3(9f, 7f, 9f), "antesala", 3);
        iReina = NuevoNodo(L(0f, -80f, 372f), new Vector3(46f, 24f, 40f), "reina", 3);
        Unir(acceso, iAntesala);
        Unir(iAntesala, iReina, 8f);

        // camaras: preferentemente en callejones sin salida (premian explorar)
        void Asignar(int nivel, string tipo, int cuantas)
        {
            var cand = Enumerable.Range(0, nodos.Count).Where(k => nodos[k].nivel == nivel && nodos[k].tipo == "cruce" && k != acceso)
                .OrderBy(k => nodos[k].vec.Count + (float)rnd.NextDouble() * 1.5f).ToList();
            for (int k = 0; k < cuantas && k < cand.Count; k++)
            {
                var n = nodos[cand[k]];
                n.tipo = tipo;
                n.r = new Vector3(R(16f, 20f), 10f, R(15f, 19f));
            }
        }
        Asignar(1, "despensa", 2); Asignar(1, "guarderia", 1); Asignar(1, "dormitorio", 1);
        Asignar(2, "despensa", 1); Asignar(2, "guarderia", 2); Asignar(2, "dormitorio", 1);
    }

    // arbol de expansion aleatorio (DFS) + algunos ciclos extra: laberinto con varias rutas
    static void Laberinto(int[,] g, int w, int h)
    {
        var vis = new bool[w, h];
        var pila = new Stack<Vector2Int>();
        pila.Push(new Vector2Int(rnd.Next(w), rnd.Next(h)));
        vis[pila.Peek().x, pila.Peek().y] = true;
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        var usadas = new HashSet<long>();
        while (pila.Count > 0)
        {
            var c = pila.Peek();
            var libres = dirs.Select(d => c + d).Where(n => n.x >= 0 && n.y >= 0 && n.x < w && n.y < h && !vis[n.x, n.y]).ToList();
            if (libres.Count == 0) { pila.Pop(); continue; }
            var sig = libres[rnd.Next(libres.Count)];
            vis[sig.x, sig.y] = true;
            Unir(g[c.x, c.y], g[sig.x, sig.y]);
            usadas.Add(Clave(g[c.x, c.y], g[sig.x, sig.y]));
            pila.Push(sig);
        }
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
                foreach (var d in new[] { Vector2Int.right, Vector2Int.up })
                {
                    int x = i + d.x, y = j + d.y;
                    if (x >= w || y >= h) continue;
                    if (usadas.Contains(Clave(g[i, j], g[x, y]))) continue;
                    if (rnd.NextDouble() < 0.22) Unir(g[i, j], g[x, y]);
                }
    }

    static long Clave(int a, int b) { return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; }

    static Vector3 CentroTunel(Nodo n, float r) { return n.Piso + Vector3.up * 0.6f * r; }

    static List<Vector3> Polilinea(Arista e, bool alReves = false)
    {
        var l = new List<Vector3> { CentroTunel(nodos[e.a], e.r) };
        l.AddRange(e.extra);
        l.Add(CentroTunel(nodos[e.b], e.r));
        if (alReves) l.Reverse();
        return l;
    }

    // ================================================================== campo de distancia
    class Prim { public bool elipse; public Vector3 a, b, r; public float rad; public Bounds bb; }
    static List<Prim> prims;
    static Dictionary<long, List<int>> rejilla;
    const float CeldaRejilla = 24f;

    static long ClaveRejilla(int x, int y, int z) { return ((long)(x + 2048) << 40) | ((long)(y + 2048) << 20) | (long)(z + 2048); }

    static void ConstruirPrimitivas()
    {
        prims = new List<Prim>();
        foreach (var n in nodos)
        {
            var b = new Bounds(n.p, n.r * 2f); b.Expand(Suavizado * 2f + 4f);
            prims.Add(new Prim { elipse = true, a = n.p, r = n.r, bb = b });
        }
        foreach (var e in aristas)
        {
            var pts = Polilinea(e);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var b = new Bounds(pts[i], Vector3.zero); b.Encapsulate(pts[i + 1]); b.Expand(e.r * 2f + Suavizado * 2f + 4f);
                prims.Add(new Prim { elipse = false, a = pts[i], b = pts[i + 1], rad = e.r, bb = b });
            }
        }
        rejilla = new Dictionary<long, List<int>>();
        for (int k = 0; k < prims.Count; k++)
        {
            var b = prims[k].bb;
            Vector3Int mn = Vector3Int.FloorToInt(b.min / CeldaRejilla), mx = Vector3Int.FloorToInt(b.max / CeldaRejilla);
            for (int x = mn.x; x <= mx.x; x++)
                for (int y = mn.y; y <= mx.y; y++)
                    for (int z = mn.z; z <= mx.z; z++)
                    {
                        long c = ClaveRejilla(x, y, z);
                        if (!rejilla.TryGetValue(c, out var l)) rejilla[c] = l = new List<int>();
                        l.Add(k);
                    }
        }
    }

    // altura del monticulo sobre el suelo (r = distancia horizontal a la entrada)
    static float Monticulo(float r)
    {
        float m = r < 9f ? 6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.5f, 9f, r)) : 6f * Mathf.Exp(-(r - 9f) * (r - 9f) / (2f * 12f * 12f));
        m *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 37f, r));
        // en el borde queda un poco por debajo del suelo exterior (que lo tapa sin parpadeo)
        return m - 0.15f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(34f, 38f, r));
    }

    static float AlturaSuelo(float x, float z)
    {
        float r = new Vector2(x - O.x, z - O.z).magnitude;
        return ZonaJuego.Altura(x, z) + Monticulo(r);
    }

    static float SMin(float a, float b, float k)
    {
        float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
        return Mathf.Min(a, b) - h * h * k * 0.25f;
    }

    static float Hash3(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffffff) / 16777215f;
        }
    }

    static float Ruido3(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        float fx = p.x - x, fy = p.y - y, fz = p.z - z;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float a = Mathf.Lerp(Hash3(x, y, z), Hash3(x + 1, y, z), fx), b = Mathf.Lerp(Hash3(x, y + 1, z), Hash3(x + 1, y + 1, z), fx);
        float c = Mathf.Lerp(Hash3(x, y, z + 1), Hash3(x + 1, y, z + 1), fx), d = Mathf.Lerp(Hash3(x, y + 1, z + 1), Hash3(x + 1, y + 1, z + 1), fx);
        return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
    }

    /// <summary>Distancia con signo: negativa en el aire (tuneles, camaras, cielo), positiva en la tierra.</summary>
    static float F(Vector3 p)
    {
        float cielo = AlturaSuelo(p.x, p.z) - p.y;
        long c = ClaveRejilla(Mathf.FloorToInt(p.x / CeldaRejilla), Mathf.FloorToInt(p.y / CeldaRejilla), Mathf.FloorToInt(p.z / CeldaRejilla));
        if (!rejilla.TryGetValue(c, out var lista)) return cielo;
        float d = 1e6f; bool piso = false; float mejor = 1e6f;
        foreach (int k in lista)
        {
            var q = prims[k];
            float dk, pisoK;
            if (q.elipse)
            {
                Vector3 v = p - q.a;
                float k0 = new Vector3(v.x / q.r.x, v.y / q.r.y, v.z / q.r.z).magnitude;
                dk = (k0 - 1f) * Mathf.Min(q.r.x, Mathf.Min(q.r.y, q.r.z));
                pisoK = (q.a.y - q.r.y * 0.5f) - p.y;
            }
            else
            {
                Vector3 ab = q.b - q.a;
                float t = Mathf.Clamp01(Vector3.Dot(p - q.a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                Vector3 pr = q.a + ab * t;
                dk = (p - pr).magnitude - q.rad;
                pisoK = (pr.y - 0.6f * q.rad) - p.y;
            }
            float dd = Mathf.Max(dk, pisoK);
            if (dd < mejor) { mejor = dd; piso = pisoK > dk; }
            d = d > 1e5f ? dd : SMin(d, dd, Suavizado);
        }
        // paredes organicas: ruido (menos en el piso para que se pueda caminar)
        float profundo = Mathf.Clamp01((AlturaSuelo(p.x, p.z) - 3f - p.y) / 6f);
        float n = (Ruido3(p * 0.085f) - 0.5f) * 3.2f + (Ruido3(p * 0.27f + Vector3.one * 17f) - 0.5f) * 1.1f;
        d += n * (piso ? 0.3f : 1f) * profundo;
        return SMin(cielo, d, 3f);
    }

    static Vector3 Gradiente(Vector3 p)
    {
        const float e = 0.6f;
        return new Vector3(F(p + Vector3.right * e) - F(p - Vector3.right * e), F(p + Vector3.up * e) - F(p - Vector3.up * e), F(p + Vector3.forward * e) - F(p - Vector3.forward * e));
    }

    // ================================================================== surface nets por trozos
    class MallaTrozo
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Vector3> n = new List<Vector3>();
        public List<int> cueva = new List<int>(), suelo = new List<int>();
        public Color[] col;
    }

    static readonly int[,] Aristas12 = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };

    static List<MallaTrozo> Poligonizar(Bounds dom)
    {
        var res = new List<MallaTrozo>();
        Vector3Int celdas = Vector3Int.CeilToInt(dom.size / Celda);
        Vector3 mn = dom.min;
        int nS = Trozo + 3;
        var s = new float[nS * nS * nS];
        int Si(int x, int y, int z) => (x * nS + y) * nS + z;
        int nC = Trozo + 1;
        var idx = new int[nC * nC * nC];
        int Ci(int x, int y, int z) => (x * nC + y) * nC + z;
        var cs = new float[8];

        for (int cx = 0; cx < celdas.x; cx += Trozo)
            for (int cy = 0; cy < celdas.y; cy += Trozo)
                for (int cz = 0; cz < celdas.z; cz += Trozo)
                {
                    Vector3 o = mn + new Vector3(cx - 1, cy - 1, cz - 1) * Celda;
                    var bt = new Bounds(o + Vector3.one * (nS - 1) * Celda * 0.5f, Vector3.one * (nS - 1) * Celda);
                    // trozo sin ninguna primitiva cerca ni superficie de la entrada: es tierra maciza
                    bool algo = prims.Any(q => q.bb.Intersects(bt));
                    float rEnt = new Vector2(bt.center.x - O.x, bt.center.z - O.z).magnitude;
                    if (!algo && !(rEnt < 50f + bt.extents.x * 1.5f && bt.max.y > O.y - 12f)) continue;

                    bool hayAire = false, hayTierra = false;
                    for (int x = 0; x < nS; x++)
                        for (int y = 0; y < nS; y++)
                            for (int z = 0; z < nS; z++)
                            {
                                float f = F(o + new Vector3(x, y, z) * Celda);
                                s[Si(x, y, z)] = f;
                                if (f < 0f) hayAire = true; else hayTierra = true;
                            }
                    if (!hayAire || !hayTierra) continue;

                    var m = new MallaTrozo();
                    for (int i = 0; i < idx.Length; i++) idx[i] = -1;
                    // un vertice por celda con cambio de signo: promedio de los cruces en sus aristas
                    for (int x = 0; x < nC; x++)
                        for (int y = 0; y < nC; y++)
                            for (int z = 0; z < nC; z++)
                            {
                                int msk = 0;
                                for (int k = 0; k < 8; k++)
                                {
                                    cs[k] = s[Si(x + (k & 1), y + ((k >> 1) & 1), z + ((k >> 2) & 1))];
                                    if (cs[k] < 0f) msk |= 1 << k;
                                }
                                if (msk == 0 || msk == 255) continue;
                                Vector3 sum = Vector3.zero; int cnt = 0;
                                for (int e = 0; e < 12; e++)
                                {
                                    int a = Aristas12[e, 0], b = Aristas12[e, 1];
                                    if ((cs[a] < 0f) == (cs[b] < 0f)) continue;
                                    float t = cs[a] / (cs[a] - cs[b]);
                                    Vector3 pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1), pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
                                    sum += Vector3.Lerp(pa, pb, t); cnt++;
                                }
                                Vector3 pos = o + (new Vector3(x, y, z) + sum / cnt) * Celda;
                                idx[Ci(x, y, z)] = m.v.Count;
                                m.v.Add(pos);
                                m.n.Add(-Gradiente(pos).normalized);   // apunta hacia el aire
                            }
                    // un quad por arista de la rejilla con cambio de signo (solo las aristas propias del trozo)
                    for (int x = 1; x <= Trozo; x++)
                        for (int y = 1; y <= Trozo; y++)
                            for (int z = 1; z <= Trozo; z++)
                            {
                                if (cx + x - 1 >= celdas.x || cy + y - 1 >= celdas.y || cz + z - 1 >= celdas.z) continue;
                                float f0 = s[Si(x, y, z)];
                                for (int ax = 0; ax < 3; ax++)
                                {
                                    float f1 = ax == 0 ? s[Si(x + 1, y, z)] : ax == 1 ? s[Si(x, y + 1, z)] : s[Si(x, y, z + 1)];
                                    if ((f0 < 0f) == (f1 < 0f)) continue;
                                    int c0, c1, c2, c3;
                                    if (ax == 0) { c0 = idx[Ci(x, y - 1, z - 1)]; c1 = idx[Ci(x, y, z - 1)]; c2 = idx[Ci(x, y, z)]; c3 = idx[Ci(x, y - 1, z)]; }
                                    else if (ax == 1) { c0 = idx[Ci(x - 1, y, z - 1)]; c1 = idx[Ci(x, y, z - 1)]; c2 = idx[Ci(x, y, z)]; c3 = idx[Ci(x - 1, y, z)]; }
                                    else { c0 = idx[Ci(x - 1, y - 1, z)]; c1 = idx[Ci(x, y - 1, z)]; c2 = idx[Ci(x, y, z)]; c3 = idx[Ci(x - 1, y, z)]; }
                                    if (c0 < 0 || c1 < 0 || c2 < 0 || c3 < 0) continue;
                                    Vector3 centro = (m.v[c0] + m.v[c1] + m.v[c2] + m.v[c3]) * 0.25f;
                                    // la superficie del suelo solo se genera alrededor de la entrada
                                    float rr = new Vector2(centro.x - O.x, centro.z - O.z).magnitude;
                                    if (rr > 42f && centro.y > ZonaJuego.Altura(centro.x, centro.z) - 2.5f) continue;
                                    Vector3 esperado = Vector3.zero; esperado[ax] = f0 < 0f ? -1f : 1f;   // hacia el aire
                                    Vector3 nq = Vector3.Cross(m.v[c1] - m.v[c0], m.v[c2] - m.v[c0]);
                                    int[] tri = Vector3.Dot(nq, esperado) >= 0f ? new[] { c0, c1, c2, c0, c2, c3 } : new[] { c0, c2, c1, c0, c3, c2 };
                                    Vector3 nc = (m.n[c0] + m.n[c2]);
                                    bool esSuelo = rr > 26f && centro.y > AlturaSuelo(centro.x, centro.z) - 1.2f && nc.y > 0.6f;
                                    (esSuelo ? m.suelo : m.cueva).AddRange(tri);
                                }
                            }
                    if (m.cueva.Count + m.suelo.Count > 0) res.Add(m);
                }
        return res;
    }

    // ================================================================== mallas procedurales pequeñas
    static Mesh Revolucion(string nombre, int lados, int anillos, System.Func<float, float> radio, System.Func<float, Vector3> centro)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i <= anillos; i++)
        {
            float u = i / (float)anillos;
            Vector3 c = centro(u); float r = radio(u);
            for (int k = 0; k < lados; k++)
            {
                float a = k * Mathf.PI * 2f / lados;
                v.Add(c + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }
        for (int i = 0; i < anillos; i++)
            for (int k = 0; k < lados; k++)
            {
                int a = i * lados + k, b = i * lados + (k + 1) % lados, c = a + lados, d = b + lados;
                t.AddRange(new[] { a, c, b, b, c, d });
            }
        var m = new Mesh { name = nombre }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // a lo largo del eje Y (para hongos)
    static Mesh RevolucionY(string nombre, int lados, int anillos, System.Func<float, float> radio, System.Func<float, float> altura)
    {
        var m = Revolucion(nombre, lados, anillos, radio, u => new Vector3(0f, 0f, altura(u)));
        var v = m.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = new Vector3(v[i].x, v[i].z, v[i].y);
        m.vertices = v;
        var t = m.triangles; for (int i = 0; i < t.Length; i += 3) { int x = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = x; }
        m.triangles = t; m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh Hoja()
    {
        var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
        int L = 8, W = 3;
        for (int i = 0; i <= L; i++)
        {
            float u = i / (float)L;
            float ancho = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(u * Mathf.PI)), 0.75f) * 0.32f * (1f - 0.25f * u);
            for (int k = 0; k <= W * 2; k++)
            {
                float w = (k - W) / (float)W;
                v.Add(new Vector3(w * ancho, Mathf.Sin(u * Mathf.PI) * 0.08f + Mathf.Abs(w) * 0.05f, u - 0.5f));
                uv.Add(new Vector2(k / (float)(W * 2), u));
            }
        }
        int fila = W * 2 + 1;
        for (int i = 0; i < L; i++)
            for (int k = 0; k < W * 2; k++)
            {
                int a = i * fila + k, b = a + 1, c = a + fila, d = c + 1;
                t.AddRange(new[] { a, c, b, b, c, d });
            }
        var m = new Mesh { name = "Hoja" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh Maiz()
    {
        var m = Revolucion("Maiz", 10, 8, u => Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.Lerp(0.15f, 0.97f, u) * Mathf.PI)), 0.6f) * Mathf.Lerp(0.22f, 0.36f, u), u => new Vector3(0f, 0f, u * 0.75f - 0.35f));
        var v = m.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = new Vector3(v[i].x * 1.3f, v[i].y * 0.8f, v[i].z);   // grano aplanado
        m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh Larva()
    {
        return Revolucion("Larva", 12, 22,
            u => 0.42f * Mathf.Pow(Mathf.Sin(Mathf.Lerp(0.04f, 0.96f, u) * Mathf.PI), 0.55f) * (1f + 0.08f * Mathf.Cos(u * Mathf.PI * 2f * 7f)),
            u => new Vector3(0f, 0.42f + Mathf.Sin(u * Mathf.PI) * 0.35f, u * 2.2f - 1.1f));
    }

    static Mesh Huevo()
    {
        return Revolucion("Huevo", 10, 10, u => 0.3f * Mathf.Pow(Mathf.Sin(Mathf.Lerp(0.02f, 0.98f, u) * Mathf.PI), 0.7f), u => new Vector3(0f, 0.3f, u * 0.9f - 0.45f));
    }

    // ================================================================== materiales
    static Material GuardarMaterial(string nombre, Shader shader, System.Action<Material> config)
    {
        Directory.CreateDirectory(Mat);
        string path = Mat + nombre + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
        m.shader = shader;
        config(m);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Lit(string nombre, Color c, float suave, Color emision)
    {
        return GuardarMaterial(nombre, Shader.Find("Universal Render Pipeline/Lit"), m =>
        {
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", suave); m.SetFloat("_Metallic", 0f);
            if (emision.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emision); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            else m.DisableKeyword("_EMISSION");
        });
    }

    static Material MatHormiga(string nombre, Texture tex, Color c, Bounds b, float freq, float amp)
    {
        return GuardarMaterial(nombre, Shader.Find("Hormiguero/Hormiga"), m =>
        {
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", c);
            m.SetFloat("_Frecuencia", freq); m.SetFloat("_Amplitud", amp);
            m.SetFloat("_ZMin", b.min.z); m.SetFloat("_Largo", b.size.z); m.SetFloat("_YMin", b.min.y); m.SetFloat("_Alto", b.size.y);
            m.SetFloat("_MedioAncho", Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x)));
        });
    }

    static Mesh Guardar(Mesh m, string nombre)
    {
        Directory.CreateDirectory(Gen);
        string path = Gen + nombre + ".asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ================================================================== construccion principal
    [MenuItem("Herramientas/Hormiguero/Construir")]
    public static void Construir()
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        rnd = new System.Random(2027);
        var e2 = ZonaJuego.hormigueroEntrada;
        O = new Vector3(e2.x, ZonaJuego.Altura(e2.x, e2.y), e2.y);

        var viejo = GameObject.Find("HORMIGUERO");
        if (viejo != null) Object.DestroyImmediate(viejo);
        if (Directory.Exists(Gen)) { AssetDatabase.DeleteAsset(Gen.TrimEnd('/')); }
        Directory.CreateDirectory(Gen);
        AssetDatabase.Refresh();

        var raiz = new GameObject("HORMIGUERO");
        ConstruirGrafo();
        ConstruirPrimitivas();

        // dominio: todas las primitivas + la superficie alrededor de la entrada
        var dom = new Bounds(O, Vector3.one * 90f);
        foreach (var q in prims) dom.Encapsulate(q.bb);
        dom.Encapsulate(new Bounds(O + Vector3.up * 6f, new Vector3(96f, 24f, 96f)));
        var trozos = Poligonizar(dom);
        Debug.Log("[Hormiguero] Poligonizado: " + trozos.Count + " trozos, " + trozos.Sum(t => t.v.Count) + " vertices, " + reloj.ElapsedMilliseconds + " ms");

        // ---- materiales
        var matCueva = GuardarMaterial("Cueva", Shader.Find("Hormiguero/Cueva"), m =>
        {
            m.SetTexture("_Pared", AssetDatabase.LoadAssetAtPath<Texture2D>(Raiz + "Texturas/excavated_soil_wall_diff.jpg"));
            m.SetTexture("_NormalPared", AssetDatabase.LoadAssetAtPath<Texture2D>(Raiz + "Texturas/excavated_soil_wall_nor.jpg"));
            m.SetTexture("_Piso", AssetDatabase.LoadAssetAtPath<Texture2D>(Raiz + "Texturas/brown_mud_dry_diff.jpg"));
            m.SetTexture("_NormalPiso", AssetDatabase.LoadAssetAtPath<Texture2D>(Raiz + "Texturas/brown_mud_dry_nor.jpg"));
            m.SetFloat("_Tamano", 16f);
        });
        var matSuelo = AssetDatabase.LoadAssetAtPath<Material>("Assets/Exterior/Materiales/Suelo.mat");
        foreach (var p in new[] { "excavated_soil_wall_nor", "brown_mud_dry_nor" })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(Raiz + "Texturas/" + p + ".jpg");
            if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
        }

        // ---- hongos luminosos: racimos sobre paredes y pisos
        var coloresHongo = new[] { new Color(0.25f, 0.95f, 1f), new Color(0.35f, 1f, 0.45f), new Color(0.55f, 0.45f, 1f), new Color(1f, 0.6f, 0.25f) };
        var racimos = ElegirRacimos(trozos, 340);
        var lucesPos = new List<Vector3>(); var lucesCol = new List<Color>(); var lucesInt = new List<float>();
        foreach (var rc in racimos)
        {
            lucesPos.Add(rc.pos + rc.normal * 2.2f);
            lucesCol.Add(coloresHongo[rc.color]);
            lucesInt.Add(rc.tamano);
        }
        // el hongo gigante de la reina tambien ilumina (blanco azulado)
        var reina = nodos[iReina];
        Vector3 posHongo = reina.Piso + new Vector3(0f, 0f, 14f);
        lucesPos.Add(posHongo + Vector3.up * 16f); lucesCol.Add(new Color(0.75f, 0.9f, 1f)); lucesInt.Add(5f);

        HornearLuz(trozos, lucesPos, lucesCol, lucesInt);
        Debug.Log("[Hormiguero] Luz horneada: " + lucesPos.Count + " luces, " + reloj.ElapsedMilliseconds + " ms");

        // ---- mallas de la cueva con colision
        var cueva = new GameObject("Cueva").transform; cueva.SetParent(raiz.transform, false);
        for (int i = 0; i < trozos.Count; i++)
        {
            var t = trozos[i];
            var m = new Mesh { name = "Cueva_" + i, indexFormat = t.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(t.v); m.SetNormals(t.n); m.SetColors(t.col);
            m.subMeshCount = 2;
            m.SetTriangles(t.cueva, 0); m.SetTriangles(t.suelo, 1);
            m.RecalculateBounds();
            m = Guardar(m, "Cueva_" + i);
            var go = new GameObject("Trozo_" + i);
            go.transform.SetParent(cueva, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { matCueva, matSuelo };
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.AddComponent<MeshCollider>().sharedMesh = m;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.NavigationStatic);
        }
        Physics.SyncTransforms();

        // ---- hongo blanco gigante
        var hongo = CrearHongoGigante(raiz.transform, posHongo);

        // ---- NavMesh (para las hordas): cueva + alrededores de la entrada
        Physics.SyncTransforms();
        var surf = raiz.AddComponent<NavMeshSurface>();
        surf.collectObjects = CollectObjects.Volume;
        var vol = new Bounds(dom.center, dom.size); vol.Encapsulate(new Bounds(O, new Vector3(320f, 60f, 320f)));
        surf.center = vol.center - raiz.transform.position; surf.size = vol.size;
        surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surf.layerMask = ~0;
        surf.BuildNavMesh();
        if (surf.navMeshData != null) AssetDatabase.CreateAsset(surf.navMeshData, Gen + "NavMeshHormiguero.asset");
        Debug.Log("[Hormiguero] NavMesh listo, " + reloj.ElapsedMilliseconds + " ms");

        // ---- instancias: hongos, hojas, maiz, huevos
        var grupos = new List<Hormiguero.GrupoInstancias>();
        CrearHongos(grupos, racimos, coloresHongo);
        var mHoja = Guardar(Hoja(), "Hoja"); var mMaiz = Guardar(Maiz(), "Maiz"); var mHuevo = Guardar(Huevo(), "Huevo");
        var matHoja = GuardarMaterial("Hoja", Shader.Find("Exterior/Follaje"), m => { m.SetColor("_BaseColor", new Color(0.32f, 0.62f, 0.2f)); m.SetFloat("_Viento", 0f); m.SetFloat("_AlphaClip", 0f); m.DisableKeyword("_ALPHATEST_ON"); m.SetFloat("_VariacionColor", 0.45f); m.SetFloat("_FadeInicio", 0.5f); m.SetFloat("_FadeFin", 1.5f); });
        var matHojaSeca = GuardarMaterial("HojaSeca", Shader.Find("Exterior/Follaje"), m => { m.SetColor("_BaseColor", new Color(0.62f, 0.55f, 0.2f)); m.SetFloat("_Viento", 0f); m.SetFloat("_AlphaClip", 0f); m.DisableKeyword("_ALPHATEST_ON"); m.SetFloat("_VariacionColor", 0.5f); m.SetFloat("_FadeInicio", 0.5f); m.SetFloat("_FadeFin", 1.5f); });
        var matMaiz = Lit("Maiz", new Color(0.98f, 0.74f, 0.12f), 0.55f, Color.black);
        var matHuevo = Lit("Huevo", new Color(0.96f, 0.95f, 0.88f), 0.75f, new Color(0.05f, 0.05f, 0.045f));
        var hojas = new List<Matrix4x4>(); var hojasSecas = new List<Matrix4x4>(); var maiz = new List<Matrix4x4>(); var huevos = new List<Matrix4x4>();

        // pila de hojas alrededor del hongo de la reina (lo alimentan con ellas)
        for (int i = 0; i < 380; i++)
        {
            float a = R(0f, Mathf.PI * 2f), r = R(13f, 26f);
            Apilar(posHongo + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * 0.85f), R(1.6f, 3.2f), i % 3 == 0 ? hojasSecas : hojas, 2.5f);
        }
        // despensas: monton de maiz y hojas; tuneles: hojas sueltas
        foreach (var n in nodos)
        {
            if (n.tipo == "despensa")
            {
                for (int i = 0; i < 160; i++) { var c = Random2(n, 0.55f); Apilar(c, R(0.9f, 1.5f), maiz, 3.5f * (1f - (c - n.Piso).magnitude / (n.r.x * 0.6f))); }
                for (int i = 0; i < 90; i++) Apilar(Random2(n, 0.7f), R(1.6f, 3f), i % 2 == 0 ? hojas : hojasSecas, 1.5f);
            }
            if (n.tipo == "guarderia") for (int i = 0; i < 40; i++) Apilar(Random2(n, 0.35f), R(0.9f, 1.3f), huevos, 1f);
        }
        foreach (var e in aristas)
        {
            if (e.pasadizo) continue;
            var pts = Polilinea(e);
            for (int k = 0; k < 5; k++)
            {
                int i = rnd.Next(pts.Count - 1);
                Vector3 p = Vector3.Lerp(pts[i], pts[i + 1], R(0f, 1f)) + new Vector3(R(-3f, 3f), 0f, R(-3f, 3f));
                Apilar(p, R(1.4f, 2.6f), rnd.NextDouble() < 0.5 ? hojasSecas : hojas, 0f);
            }
        }
        grupos.Add(new Hormiguero.GrupoInstancias { nombre = "Hojas", malla = mHoja, material = matHoja, matrices = hojas.ToArray() });
        grupos.Add(new Hormiguero.GrupoInstancias { nombre = "HojasSecas", malla = mHoja, material = matHojaSeca, matrices = hojasSecas.ToArray() });
        grupos.Add(new Hormiguero.GrupoInstancias { nombre = "Maiz", malla = mMaiz, material = matMaiz, matrices = maiz.ToArray() });
        grupos.Add(new Hormiguero.GrupoInstancias { nombre = "Huevos", malla = mHuevo, material = matHuevo, matrices = huevos.ToArray() });

        // ---- larvas en las guarderias
        var mLarva = Guardar(Larva(), "Larva");
        var matLarva = Lit("Larva", new Color(0.97f, 0.93f, 0.8f), 0.7f, new Color(0.06f, 0.055f, 0.045f));
        foreach (var n in nodos.Where(x => x.tipo == "guarderia"))
        {
            var g = new GameObject("Larvas").transform; g.SetParent(raiz.transform, false);
            g.gameObject.AddComponent<LarvasAnimadas>();
            for (int i = 0; i < 14; i++)
            {
                Vector3 p = PisoEn(Random2(n, 0.5f));
                var l = new GameObject("Larva_" + i);
                l.transform.SetParent(g, false);
                l.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, R(0f, 360f), 0f));
                l.transform.localScale = Vector3.one * R(1.1f, 1.8f);
                l.AddComponent<MeshFilter>().sharedMesh = mLarva;
                var mr = l.AddComponent<MeshRenderer>(); mr.sharedMaterial = matLarva; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ---- hormigas, reina y gestor
        var gestor = raiz.AddComponent<Hormiguero>();
        CrearHormigas(raiz.transform, gestor, posHongo);

        gestor.entrada = O;
        gestor.intensidadLuz = 45f; gestor.rangoLuz = 24f;
        gestor.maxHordaViva = 160; gestor.intervaloOleada = 4f; gestor.hormigasPorOleada = 12;   // las luces de URP caen con el cuadrado de la distancia
        var zona = new Bounds(dom.center, dom.size); zona.Expand(new Vector3(0f, 0f, 0f));
        gestor.zonaSubterranea = zona;
        gestor.lucesPos = lucesPos.ToArray();
        gestor.lucesColor = lucesCol.ToArray();
        gestor.grupos = grupos.ToArray();
        var pts2 = nodos.Where(n => n.tipo != "reina").Select(n => n.Piso + Vector3.up * 0.5f).ToList();
        for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3f; pts2.Add(new Vector3(O.x + Mathf.Cos(a) * 55f, 0f, O.z + Mathf.Sin(a) * 55f)); }
        for (int i = 0; i < pts2.Count; i++) if (pts2[i].y == 0f) pts2[i] = new Vector3(pts2[i].x, ZonaJuego.Altura(pts2[i].x, pts2[i].z), pts2[i].z);
        gestor.puntosAparicion = pts2.ToArray();

        var amb = raiz.AddComponent<AudioSource>();
        amb.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Cueva_Goteo.mp3");
        amb.loop = true; amb.playOnAwake = true; amb.spatialBlend = 0f; amb.volume = 0f;
        gestor.ambienteCueva = amb;
        gestor.alarmaHorda = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Horda_Alarma.mp3");

        // luz propia de la camara de la reina (el hongo blanco brilla)
        var luzReina = new GameObject("LuzHongoReina").AddComponent<Light>();
        luzReina.transform.SetParent(raiz.transform, false);
        luzReina.transform.position = posHongo + Vector3.up * 20f;
        luzReina.type = LightType.Point; luzReina.range = 80f; luzReina.intensity = 650f; luzReina.color = new Color(0.7f, 0.88f, 1f); luzReina.shadows = LightShadows.None;

        ConfigurarCamara();
        var dn = Object.FindAnyObjectByType<DayNightCycle>();
        if (dn != null) { dn.ambienteCueva = new Color(0.16f, 0.14f, 0.12f); EditorUtility.SetDirty(dn); }

        EditorSceneManager.MarkSceneDirty(raiz.scene);
        EditorSceneManager.SaveScene(raiz.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Hormiguero] Construido en " + reloj.ElapsedMilliseconds + " ms. Nodos: " + nodos.Count + ", tuneles: " + aristas.Count + ", hongos: " + racimos.Count);
    }

    // punto aleatorio en el piso de un nodo
    static Vector3 Random2(Nodo n, float frac)
    {
        var c = new Vector2(R(-1f, 1f), R(-1f, 1f)); if (c.sqrMagnitude > 1f) c.Normalize();
        return n.Piso + new Vector3(c.x * n.r.x * frac, 0f, c.y * n.r.z * frac);
    }

    static Vector3 PisoEn(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var hit, 16f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return p;
    }

    static void Apilar(Vector3 p, float escala, List<Matrix4x4> lista, float altura)
    {
        Vector3 s = PisoEn(p) + Vector3.up * R(0f, Mathf.Max(0f, altura));
        lista.Add(Matrix4x4.TRS(s, Quaternion.Euler(R(-35f, 35f), R(0f, 360f), R(-35f, 35f)), Vector3.one * escala));
    }

    // ================================================================== hongos
    class Racimo { public Vector3 pos, normal; public int color; public float tamano; }

    static List<Racimo> ElegirRacimos(List<MallaTrozo> trozos, int cuantos)
    {
        var cand = new List<(Vector3 p, Vector3 n)>();
        foreach (var t in trozos)
        {
            var enCueva = new HashSet<int>(t.cueva);
            for (int i = 0; i < t.v.Count; i += 3)
            {
                if (!enCueva.Contains(i)) continue;
                var p = t.v[i];
                if (p.y > AlturaSuelo(p.x, p.z) - 7f) continue;   // solo bajo tierra
                if (t.n[i].y < -0.35f || t.n[i].y > 0.72f) continue;   // en paredes y bordes del piso: ni en el techo ni en medio del camino
                cand.Add((p, t.n[i]));
            }
        }
        var res = new List<Racimo>();
        var rej = new Dictionary<long, bool>();
        for (int intento = 0; intento < cuantos * 30 && res.Count < cuantos && cand.Count > 0; intento++)
        {
            var c = cand[rnd.Next(cand.Count)];
            long k = ClaveRejilla(Mathf.FloorToInt(c.p.x / 9f), Mathf.FloorToInt(c.p.y / 9f), Mathf.FloorToInt(c.p.z / 9f));
            if (rej.ContainsKey(k)) continue;
            rej[k] = true;
            // colores por zona: cada region del hormiguero tiene su tono dominante
            int col = Mathf.Abs(Mathf.FloorToInt(c.p.x / 70f) * 3 + Mathf.FloorToInt(c.p.z / 70f)) % 3;
            if (rnd.NextDouble() < 0.25) col = rnd.Next(4);
            res.Add(new Racimo { pos = c.p, normal = c.n.normalized, color = col, tamano = R(0.8f, 1.6f) });
        }
        return res;
    }

    static void CrearHongos(List<Hormiguero.GrupoInstancias> grupos, List<Racimo> racimos, Color[] colores)
    {
        // tres formas: sombrero redondo, plano y conico
        var tallos = new Mesh[3]; var sombreros = new Mesh[3];
        float[] altoTallo = { 1f, 0.55f, 1.35f }, radioTallo = { 0.11f, 0.15f, 0.07f };
        for (int f = 0; f < 3; f++)
        {
            int ff = f;
            tallos[f] = Guardar(RevolucionY("Tallo_" + f, 8, 6, u => radioTallo[ff] * Mathf.Lerp(1.25f, 0.8f, u), u => u * altoTallo[ff]), "Tallo_" + f);
            System.Func<float, float> rad = f == 0 ? (u => 0.46f * Mathf.Sin(Mathf.Lerp(0.5f, 1f, u) * Mathf.PI) + 0.02f)
                                       : f == 1 ? (u => 0.72f * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(u * Mathf.PI * 0.5f)), 0.4f) + 0.01f)
                                                : (u => 0.32f * (1f - u) + 0.01f);
            float h = f == 0 ? 0.36f : f == 1 ? 0.14f : 0.5f;
            sombreros[f] = Guardar(RevolucionY("Sombrero_" + f, 14, 7, rad, u => altoTallo[ff] - 0.05f + u * h), "Sombrero_" + f);
        }
        var matTallo = Lit("HongoTallo", new Color(0.82f, 0.86f, 0.8f), 0.3f, new Color(0.04f, 0.06f, 0.06f));
        var mats = colores.Select((c, i) => Lit("HongoBrillo_" + i, c * 0.4f, 0.6f, c * 2.6f)).ToArray();
        var lTallo = new List<Matrix4x4>[3]; var lSomb = new List<Matrix4x4>[3, 4];
        for (int f = 0; f < 3; f++) { lTallo[f] = new List<Matrix4x4>(); for (int c = 0; c < 4; c++) lSomb[f, c] = new List<Matrix4x4>(); }
        foreach (var rc in racimos)
        {
            int n = rnd.Next(3, 8);
            // los hongos crecen hacia arriba aunque esten en la pared (mezcla entre normal y vertical)
            Vector3 arriba = Vector3.Slerp(rc.normal, Vector3.up, 0.45f).normalized;
            for (int i = 0; i < n; i++)
            {
                int f = rnd.Next(3);
                Vector3 off = Vector3.ProjectOnPlane(new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)), rc.normal) * 2.2f;
                Vector3 p = rc.pos + off - rc.normal * 0.2f;
                float s = R(0.9f, 2.6f) * rc.tamano * (i == 0 ? 1.35f : 1f);
                var q = Quaternion.FromToRotation(Vector3.up, arriba) * Quaternion.Euler(R(-15f, 15f), R(0f, 360f), R(-15f, 15f));
                var m = Matrix4x4.TRS(p, q, Vector3.one * s);
                lTallo[f].Add(m);
                lSomb[f, rc.color].Add(m);
            }
        }
        for (int f = 0; f < 3; f++)
        {
            grupos.Add(new Hormiguero.GrupoInstancias { nombre = "Tallo_" + f, malla = tallos[f], material = matTallo, matrices = lTallo[f].ToArray() });
            for (int c = 0; c < 4; c++)
                if (lSomb[f, c].Count > 0)
                    grupos.Add(new Hormiguero.GrupoInstancias { nombre = "Sombrero_" + f + "_" + c, malla = sombreros[f], material = mats[c], matrices = lSomb[f, c].ToArray() });
        }
    }

    // ================================================================== luz horneada
    static void HornearLuz(List<MallaTrozo> trozos, List<Vector3> pos, List<Color> col, List<float> inten)
    {
        const float alcance = 30f;
        var rejL = new Dictionary<long, List<int>>();
        for (int i = 0; i < pos.Count; i++)
        {
            float alc = i == pos.Count - 1 ? 80f : alcance;   // el hongo de la reina llega mas lejos
            Vector3Int mn = Vector3Int.FloorToInt((pos[i] - Vector3.one * alc) / alcance), mx = Vector3Int.FloorToInt((pos[i] + Vector3.one * alc) / alcance);
            for (int x = mn.x; x <= mx.x; x++) for (int y = mn.y; y <= mx.y; y++) for (int z = mn.z; z <= mx.z; z++)
            {
                long k = ClaveRejilla(x, y, z);
                if (!rejL.TryGetValue(k, out var l)) rejL[k] = l = new List<int>();
                l.Add(i);
            }
        }
        Color ambiente = new Color(0.05f, 0.042f, 0.034f);
        foreach (var t in trozos)
        {
            t.col = new Color[t.v.Count];
            for (int i = 0; i < t.v.Count; i++)
            {
                Vector3 p = t.v[i], n = t.n[i];
                // oclusion ambiental: cuanto aire hay frente a la pared
                float ao = Mathf.Clamp01(-F(p + n * 3f) / 3f);
                ao = Mathf.Lerp(0.35f, 1f, ao);
                Color c = ambiente * ao;
                long k = ClaveRejilla(Mathf.FloorToInt(p.x / alcance), Mathf.FloorToInt(p.y / alcance), Mathf.FloorToInt(p.z / alcance));
                if (rejL.TryGetValue(k, out var l))
                    foreach (int li in l)
                    {
                        float alc = li == pos.Count - 1 ? 80f : alcance;
                        Vector3 d = pos[li] - p; float dist = d.magnitude;
                        if (dist > alc) continue;
                        Vector3 dir = d / Mathf.Max(0.01f, dist);
                        float lambert = 0.3f + 0.7f * Mathf.Max(0f, Vector3.Dot(n, dir));
                        float at = 1f - dist / alc; at *= at;
                        // sombra aproximada: si el camino pasa por tierra, la luz no llega
                        bool tapa = false;
                        for (int s = 1; s <= 3 && !tapa; s++) if (F(p + n * 0.8f + d * (s / 4f)) > 0.9f) tapa = true;
                        if (tapa) continue;
                        c += col[li] * (inten[li] * 1.3f * at * lambert * Mathf.Lerp(0.6f, 1f, ao));
                    }
                // alpha: cuanto da al exterior (cerca de la boca del tunel)
                c.a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-7f, -1f, p.y - AlturaSuelo(p.x, p.z)));
                t.col[i] = c;
            }
        }
    }

    // ================================================================== hongo gigante
    static GameObject CrearHongoGigante(Transform padre, Vector3 basePos)
    {
        // blob esponjoso: elipsoide con ruido en capas, cortado por el piso
        var v = new List<Vector3>(); var t = new List<int>();
        int lat = 28, lon = 48;
        Vector3 r0 = new Vector3(17f, 12f, 15f);
        for (int i = 0; i <= lat; i++)
        {
            float th = Mathf.Lerp(0f, Mathf.PI * 0.62f, i / (float)lat);
            for (int k = 0; k <= lon; k++)
            {
                float ph = k * Mathf.PI * 2f / lon;
                Vector3 dir = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                float n = (Ruido3(dir * 3.1f + Vector3.one * 5f) - 0.5f) * 0.5f + (Ruido3(dir * 9f) - 0.5f) * 0.18f;
                Vector3 p = Vector3.Scale(dir, r0) * (1f + n);
                p.y = Mathf.Max(p.y, -0.5f);
                v.Add(p);
            }
        }
        for (int i = 0; i < lat; i++)
            for (int k = 0; k < lon; k++)
            {
                int a = i * (lon + 1) + k, b = a + 1, c = a + lon + 1, d = c + 1;
                t.AddRange(new[] { a, b, c, b, d, c });
            }
        var m = new Mesh { name = "HongoGigante" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        m = Guardar(m, "HongoGigante");
        var go = new GameObject("HongoGigante");
        go.transform.SetParent(padre, false);
        go.transform.position = PisoEn(basePos) + Vector3.up * 2f;
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Lit("HongoGigante", new Color(0.95f, 0.95f, 0.92f), 0.25f, new Color(0.22f, 0.25f, 0.28f));
        go.AddComponent<MeshCollider>().sharedMesh = m;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
        return go;
    }

    // ================================================================== hormigas
    class Kit { public Mesh malla; public Bounds b; public Material quieta, camina, corre; }

    static GameObject CrearHormiga(string nombre, Transform padre, Kit kit, Vector3 pos, float escala, HormigaNido.Rol rol, float vida, float danio, string nombreVisible, GameObject particulas, Mesh mCarga, Material matCarga)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, R(0f, 360f), 0f));
        var cuerpo = new GameObject("Cuerpo");
        cuerpo.transform.SetParent(go.transform, false);
        cuerpo.transform.localScale = Vector3.one * escala;
        cuerpo.transform.localPosition = new Vector3(0f, -kit.b.min.y * escala, 0f);
        cuerpo.AddComponent<MeshFilter>().sharedMesh = kit.malla;
        var mr = cuerpo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = rol == HormigaNido.Rol.Obrera ? kit.camina : kit.quieta;
        mr.shadowCastingMode = ShadowCastingMode.On;
        mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var hv = go.AddComponent<RatHealth>();
        var so = new SerializedObject(hv);
        so.FindProperty("enemyName").stringValue = nombreVisible;
        so.FindProperty("maxHealth").floatValue = vida;
        so.FindProperty("currentHealth").floatValue = vida;
        so.ApplyModifiedPropertiesWithoutUndo();

        var bc = go.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.center = new Vector3(0f, kit.b.size.y * escala * 0.5f, kit.b.center.z * escala);
        bc.size = kit.b.size * escala;

        var ag = go.AddComponent<NavMeshAgent>();
        ag.radius = 0.55f * Mathf.Max(1f, escala); ag.height = kit.b.size.y * escala; ag.baseOffset = 0f;
        ag.speed = 4.2f; ag.acceleration = 24f; ag.angularSpeed = 600f; ag.stoppingDistance = 1.4f * escala;
        ag.autoBraking = false; ag.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        ag.enabled = false;

        var h = go.AddComponent<HormigaNido>();
        h.rol = rol; h.cuerpo = mr; h.matQuieta = kit.quieta; h.matCamina = kit.camina; h.matCorre = kit.corre;
        h.danio = danio; h.alcance = 2.2f * escala; h.particulasMuerte = particulas; h.hogar = pos;
        h.velocidadAtaque = rol == HormigaNido.Rol.Soldado ? 4.6f : 4.2f;

        var dt = go.AddComponent<DropTable>();
        dt.entries = new List<DropEntry> { new DropEntry { itemId = "Colmillo", amount = 1, chance = rol == HormigaNido.Rol.Soldado ? 60f : 25f } };

        if (mCarga != null)
        {
            var c = new GameObject("Carga");
            c.transform.SetParent(go.transform, false);
            bool esHoja = mCarga.name == "Hoja";
            // la hoja va sujeta con las mandibulas, levantada como una vela; el grano, por delante
            c.transform.localPosition = esHoja ? new Vector3(0f, kit.b.size.y * escala * 1.35f, kit.b.max.z * escala * 0.55f) : new Vector3(0f, kit.b.size.y * escala * 0.55f, kit.b.max.z * escala + 0.15f);
            c.transform.localRotation = esHoja ? Quaternion.Euler(-68f, R(-20f, 20f), R(-15f, 15f)) : Quaternion.Euler(R(0f, 360f), 0f, 90f);
            c.transform.localScale = Vector3.one * (esHoja ? R(2.2f, 3f) : 0.9f);
            c.AddComponent<MeshFilter>().sharedMesh = mCarga;
            var cr = c.AddComponent<MeshRenderer>(); cr.sharedMaterial = matCarga; cr.shadowCastingMode = ShadowCastingMode.On;
            h.carga = c;
        }
        return go;
    }

    static Mesh MallaReina(Mesh baseMesh)
    {
        // la reina: el abdomen (parte trasera) mucho mas grande
        var m = Object.Instantiate(baseMesh); m.name = "Reina";
        var b = baseMesh.bounds;
        var v = m.vertices;
        Vector3 c = new Vector3(0f, b.center.y, b.min.z + b.size.z * 0.18f);
        for (int i = 0; i < v.Length; i++)
        {
            float tt = (v[i].z - b.min.z) / b.size.z;
            float k = Mathf.Lerp(2f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.45f, tt)));
            Vector3 d = v[i] - c;
            v[i] = c + new Vector3(d.x * k, d.y * k, d.z * Mathf.Lerp(k * 1.2f, 1f, Mathf.InverseLerp(0.22f, 0.45f, tt)));
        }
        m.vertices = v; m.RecalculateNormals(); m.RecalculateBounds();
        return Guardar(m, "Reina");
    }

    static List<Vector3> RutaDesde(int inicio, out bool valida)
    {
        // BFS por el grafo (sin pasadizos) hasta la entrada; luego sale y busca comida afuera
        var prev = new Dictionary<int, int> { { inicio, -1 } };
        var cola = new Queue<int>(); cola.Enqueue(inicio);
        while (cola.Count > 0)
        {
            int a = cola.Dequeue();
            if (a == iEntrada) break;
            foreach (var e in aristas)
            {
                if (e.pasadizo) continue;
                int b = e.a == a ? e.b : e.b == a ? e.a : -1;
                if (b < 0 || prev.ContainsKey(b)) continue;
                prev[b] = a; cola.Enqueue(b);
            }
        }
        var ruta = new List<Vector3>();
        valida = prev.ContainsKey(iEntrada);
        if (!valida) return ruta;
        var camino = new List<int>();
        for (int x = iEntrada; x >= 0; x = prev[x]) camino.Add(x);
        camino.Reverse();   // inicio ... entrada
        ruta.Add(nodos[inicio].Piso);
        for (int i = 0; i < camino.Count - 1; i++)
        {
            int a = camino[i], b = camino[i + 1];
            var e = aristas.First(x => !x.pasadizo && ((x.a == a && x.b == b) || (x.a == b && x.b == a)));
            var pts = Polilinea(e, e.a != a);
            for (int k = 0; k < pts.Count; k++) ruta.Add(pts[k] - Vector3.up * 0.6f * e.r);
        }
        // fuera: sube por el crater y se aleja a buscar hojas o granos
        float ang = R(0f, Mathf.PI * 2f), dist = R(48f, 95f);
        Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
        foreach (float r in new[] { 4f, 12f, 22f, 34f, dist * 0.7f, dist })
        {
            Vector3 p = O + dir * r + new Vector3(R(-2f, 2f), 0f, R(-2f, 2f));
            p.y = AlturaSuelo(p.x, p.z);
            ruta.Add(p);
        }
        return ruta;
    }

    static void CrearHormigas(Transform raiz, Hormiguero gestor, Vector3 posHongo)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemies/Prefabs/Enemy_Hormigas.prefab");
        var mf = prefab.GetComponentInChildren<MeshFilter>();
        var tex = prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial.GetTexture("baseColorTexture");
        var part = prefab.GetComponent<CreatureAI>().deathParticlesPrefab;
        var mesh = mf.sharedMesh;
        Kit Hacer(string n, Color c, Mesh m)
        {
            var b = m.bounds;
            return new Kit { malla = m, b = b, quieta = MatHormiga(n + "_Quieta", tex, c, b, 1.2f, 0.12f), camina = MatHormiga(n + "_Camina", tex, c, b, 3.2f, 0.85f), corre = MatHormiga(n + "_Corre", tex, c, b, 7.5f, 1.15f) };
        }
        var obrera = Hacer("Obrera", new Color(0.95f, 0.78f, 0.7f), mesh);
        var soldado = Hacer("Soldado", new Color(0.75f, 0.36f, 0.28f), mesh);
        var kitReina = Hacer("Reina", new Color(0.62f, 0.3f, 0.24f), MallaReina(mesh));

        var mHoja = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "Hoja.asset");
        var mMaiz = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "Maiz.asset");
        var matHoja = AssetDatabase.LoadAssetAtPath<Material>(Mat + "Hoja.mat");
        var matMaiz = AssetDatabase.LoadAssetAtPath<Material>(Mat + "Maiz.mat");
        var sonido = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Animales/Ants_Scuttle.mp3");

        var tObreras = new GameObject("Obreras").transform; tObreras.SetParent(raiz, false);
        var tColonia = new GameObject("Colonia").transform; tColonia.SetParent(raiz, false);

        // obreras que entran y salen cargando hojas o maiz
        var origenes = new List<int> { iReina, iReina, iReina };
        origenes.AddRange(Enumerable.Range(0, nodos.Count).Where(k => nodos[k].tipo == "despensa" || nodos[k].tipo == "guarderia"));
        int nObreras = 46;
        for (int i = 0; i < nObreras; i++)
        {
            int ini = origenes[i % origenes.Count];
            var ruta = RutaDesde(ini, out bool ok);
            if (!ok) continue;
            bool maiz = nodos[ini].tipo == "despensa" && rnd.NextDouble() < 0.5;
            var go = CrearHormiga("Obrera_" + i, tObreras, obrera, ruta[0], R(0.95f, 1.15f), HormigaNido.Rol.Obrera, 30f, 5f, "Hormiga obrera", part, maiz ? mMaiz : mHoja, maiz ? matMaiz : matHoja);
            var h = go.GetComponent<HormigaNido>();
            h.ruta = ruta.ToArray(); h.inicio = (float)rnd.NextDouble(); h.carril = R(-2.2f, 2.2f); h.velocidad = R(3.3f, 4.5f);
            if (i % 6 == 0) Sonido(go, sonido);
        }

        // hormigas en sus camaras
        foreach (var n in nodos)
        {
            int cuantas = n.tipo switch { "dormitorio" => 8, "guarderia" => 4, "despensa" => 3, "vestibulo" => 3, "cruce" => rnd.NextDouble() < 0.3 ? 1 : 0, _ => 0 };
            for (int i = 0; i < cuantas; i++)
            {
                var p = PisoEn(Random2(n, 0.45f));
                var go = CrearHormiga("Hormiga_" + n.tipo + "_" + i, tColonia, obrera, p, R(0.9f, 1.2f), HormigaNido.Rol.Reposo, 30f, 5f, "Hormiga obrera", part, null, null);
                var h = go.GetComponent<HormigaNido>(); h.radioHogar = Mathf.Min(n.r.x, n.r.z) * 0.45f;
                if (i == 0 && n.tipo != "cruce") Sonido(go, sonido);
            }
        }

        // camara de la reina: cuidadoras del hongo, soldados y la reina
        var nr = nodos[iReina];
        for (int i = 0; i < 7; i++)
        {
            float a = R(0f, Mathf.PI * 2f);
            var p = PisoEn(posHongo + new Vector3(Mathf.Cos(a) * 20f, 0f, Mathf.Sin(a) * 17f));
            var go = CrearHormiga("Cuidadora_" + i, tColonia, obrera, p, 1.05f, HormigaNido.Rol.Reposo, 30f, 5f, "Hormiga obrera", part, mHoja, matHoja);
            go.GetComponent<HormigaNido>().radioHogar = 5f;
            go.GetComponent<HormigaNido>().carga.SetActive(true);
        }
        Vector3 hogarReina = PisoEn(nr.Piso + new Vector3(0f, 0f, -10f));
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Lerp(-1.1f, 1.1f, i / 5f) - Mathf.PI * 0.5f;
            var p = PisoEn(hogarReina + new Vector3(Mathf.Cos(a) * 16f, 0f, Mathf.Sin(a) * 12f));
            var go = CrearHormiga("Soldado_" + i, tColonia, soldado, p, 1.6f, HormigaNido.Rol.Soldado, 70f, 11f, "Hormiga soldado", part, null, null);
            go.GetComponent<HormigaNido>().radioHogar = 6f;
            if (i % 2 == 0) Sonido(go, sonido);
        }

        // plantillas (desactivadas): soldado que llama la reina y hormiga de las hordas
        var plantillas = new GameObject("Plantillas").transform; plantillas.SetParent(raiz, false);
        var pSold = CrearHormiga("PlantillaSoldado", plantillas, soldado, hogarReina, 1.5f, HormigaNido.Rol.Soldado, 55f, 10f, "Hormiga soldado", part, null, null);
        pSold.SetActive(false);
        var pHorda = CrearHormiga("PlantillaHorda", plantillas, obrera, O, 1.05f, HormigaNido.Rol.Reposo, 25f, 5f, "Hormiga furiosa", part, null, null);
        pHorda.GetComponent<DropTable>().entries[0].chance = 12f;
        pHorda.SetActive(false);
        gestor.plantillaHorda = pHorda.GetComponent<HormigaNido>();

        // la reina
        var reina = new GameObject("ReinaHormiga");
        reina.transform.SetParent(raiz, false);
        reina.transform.SetPositionAndRotation(hogarReina, Quaternion.LookRotation(Vector3.back));
        var visual = new GameObject("Visual").transform; visual.SetParent(reina.transform, false);
        const float er = 5f;
        var cuerpoR = new GameObject("Cuerpo");
        cuerpoR.transform.SetParent(visual, false);
        cuerpoR.transform.localScale = Vector3.one * er;
        cuerpoR.transform.localPosition = new Vector3(0f, -kitReina.b.min.y * er, 0f);
        cuerpoR.AddComponent<MeshFilter>().sharedMesh = kitReina.malla;
        var mrR = cuerpoR.AddComponent<MeshRenderer>(); mrR.sharedMaterial = kitReina.quieta;
        var hv = reina.AddComponent<RatHealth>();
        var so = new SerializedObject(hv);
        so.FindProperty("enemyName").stringValue = "Reina Hormiga";
        so.FindProperty("maxHealth").floatValue = 1400f;
        so.FindProperty("currentHealth").floatValue = 1400f;
        so.ApplyModifiedPropertiesWithoutUndo();
        var bc = reina.AddComponent<BoxCollider>(); bc.isTrigger = true;
        bc.center = new Vector3(0f, kitReina.b.size.y * er * 0.5f, kitReina.b.center.z * er); bc.size = kitReina.b.size * er;
        var au = reina.AddComponent<AudioSource>(); au.spatialBlend = 1f; au.maxDistance = 140f; au.rolloffMode = AudioRolloffMode.Linear; au.playOnAwake = false;
        var rq = reina.AddComponent<ReinaHormiga>();
        rq.visual = visual; rq.cuerpo = mrR; rq.matQuieta = kitReina.quieta; rq.matCamina = kitReina.camina; rq.matFuriosa = kitReina.corre;
        rq.hogar = hogarReina; rq.particulasMuerte = part;
        rq.plantillaSoldado = pSold.GetComponent<HormigaNido>();
        rq.rugido = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Reina_Rugido.mp3");
        rq.chillido = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Reina_Chillido.mp3");
        rq.grunido = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Reina_Grunido.mp3");
        var dtR = reina.AddComponent<DropTable>();
        dtR.entries = new List<DropEntry>
        {
            new DropEntry { itemId = "Exoesqueleto", amount = 5, chance = 100f },
            new DropEntry { itemId = "Colmillo", amount = 4, chance = 100f },
            new DropEntry { itemId = "Alas", amount = 2, chance = 100f },
        };
        // los soldados que llama salen de la boca del tunel de la camara
        var bocas = new List<Transform>();
        var dirAnte = (nodos[iAntesala].Piso - nr.Piso); dirAnte.y = 0f;
        foreach (float lado in new[] { -14f, 0f, 14f })
        {
            var t = new GameObject("PuntoLlamado").transform; t.SetParent(reina.transform.parent, false);
            t.position = PisoEn(nr.Piso + dirAnte.normalized * (nr.r.z - 8f) + Vector3.Cross(Vector3.up, dirAnte.normalized) * lado);
            t.rotation = Quaternion.LookRotation(-dirAnte);
            bocas.Add(t);
        }
        rq.puntosLlamado = bocas.ToArray();
        // ambiente inquietante en la camara de la reina
        var amb = new GameObject("AmbienteReina").AddComponent<AudioSource>();
        amb.transform.SetParent(raiz, false); amb.transform.position = posHongo;
        amb.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Cueva_Ambiente.mp3");
        amb.loop = true; amb.playOnAwake = true; amb.spatialBlend = 1f; amb.maxDistance = 110f; amb.rolloffMode = AudioRolloffMode.Linear; amb.volume = 0.5f;
    }

    static void Sonido(GameObject go, AudioClip clip)
    {
        if (clip == null) return;
        var s = go.AddComponent<AnimalSound>();
        s.sonidos = new[] { clip }; s.distanciaOir = 14f; s.volumen = 0.55f; s.intervalo = new Vector2(3f, 6f); s.tono = new Vector2(1.1f, 1.35f);
    }

    // ================================================================== camara
    static void ConfigurarCamara()
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc != null && pc.tag != "Player") { pc.tag = "Player"; EditorUtility.SetDirty(pc.gameObject); }
        // en los tuneles la camara no debe atravesar las paredes
        var tipo = System.Type.GetType("Unity.Cinemachine.CinemachineDeoccluder, Unity.Cinemachine");
        if (tipo == null) { Debug.LogWarning("[Hormiguero] No se encontro CinemachineDeoccluder"); return; }
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
        {
            if (mb.GetType().FullName != "Unity.Cinemachine.CinemachineCamera") continue;
            var dz = mb.GetComponent(tipo);
            if (dz == null) dz = mb.gameObject.AddComponent(tipo);
            var so = new SerializedObject(dz);
            void F(string p, float v) { var q = so.FindProperty(p); if (q != null) q.floatValue = v; }
            void B(string p, bool v) { var q = so.FindProperty(p); if (q != null) q.boolValue = v; }
            var cl = so.FindProperty("CollideAgainst"); if (cl != null) cl.intValue = 1 << 0;
            var it = so.FindProperty("IgnoreTag"); if (it != null) it.stringValue = "Player";
            F("MinimumDistanceFromTarget", 0.4f);
            B("AvoidObstacles.Enabled", true);
            F("AvoidObstacles.DistanceLimit", 0f);
            F("AvoidObstacles.CameraRadius", 0.35f);
            F("AvoidObstacles.SmoothingTime", 0.15f);
            F("AvoidObstacles.Damping", 0.3f);
            F("AvoidObstacles.DampingWhenOccluded", 0f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
