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
/// Genera la cloaca bajo el jardin: alcantarilla redonda con rejilla (se ve hacia abajo), y junto a ella un derrumbe
/// por donde se baja caminando y por donde salen las ratas. Abajo: tuneles de ladrillo con andenes y un canal de agua
/// sucia que corre, el pozo bajo la rejilla, un cruce, la madriguera con la comida robada y, al fondo, el Gran Colector:
/// la camara redonda del Rey Rata con su estanque, columnas, trono de basura, montones de comida y una rejilla en el techo.
/// La cueva es un campo de distancia (tuneles, salas, pozos y el derrumbe) convertido en malla con "surface nets",
/// por trozos (culling + colisiones); la luz del moho luminoso se hornea en el color de vertice (shader Hormiguero/Cueva).
/// Menu: Herramientas > Cloaca > Construir (borra y vuelve a generar todo, y recorta el suelo exterior).
/// </summary>
public static class CloacaBuilder
{
    const string Raiz = "Assets/Cloaca/";
    const string Gen = Raiz + "Generado/";
    const string Mat = Raiz + "Materiales/";
    const float Celda = 1f;
    const int Trozo = 32;

    // medidas, locales a la tapa (y = 0 es el suelo en la tapa)
    const float Y0 = -28f;                // andenes
    const float YA = -38f;                // piso del Gran Colector
    const float W = 14f, HW = 9f, RA = 12f;
    const float CanalArriba = 6f, CanalAbajo = 2.5f, CanalHondo = 2.8f, AguaBajo = 1f;
    const float XBoca = -30f, XArriba = -100f;
    const float RPozo = 17f, RCruce = 16f, RNido = 20f, RRey = 40f, RPilaRey = 16f;
    static readonly Vector3 LPozo = new Vector3(0f, Y0, 0f), LCruce = new Vector3(90f, Y0, 0f), LNido = new Vector3(90f, Y0, 70f), LRey = new Vector3(216f, YA, 0f);

    static Vector3 O;
    static System.Random rnd;
    static float R(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }
    static Vector3 L(float x, float y, float z) { return O + new Vector3(x, y, z); }
    static Vector3 L(Vector3 l) { return O + l; }
    static float Suelo(float wx, float wz) { return ZonaJuego.Altura(wx, wz); }

    // ================================================================== campo de distancia
    abstract class Prim
    {
        public Bounds bb;
        public int tipo;            // 0 cloaca (ladrillo), 1 derrumbe (tierra), 2 suelo de afuera
        public float k = 1.5f;      // suavizado de la union
        public abstract float D(Vector3 p);
    }

    static float Len(float x, float y) { return Mathf.Sqrt(x * x + y * y); }
    static float Caja2(float qx, float qy) { return Len(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)) + Mathf.Min(Mathf.Max(qx, qy), 0f); }
    static float AnchoCanal(float v) { return CanalAbajo + (v + CanalHondo) / CanalHondo * (CanalArriba - CanalAbajo); }

    class Tunel : Prim
    {
        public Vector3 a, dir, lado; public float largo, ya, yb, extA, extB, cA, cB; public bool canal;
        public Tunel(Vector3 a, Vector3 b, bool canal, float extA, float extB, float cA, float cB)
        {
            this.a = a; Vector3 d = b - a; d.y = 0f; largo = d.magnitude; dir = d / largo; lado = new Vector3(dir.z, 0f, -dir.x);
            ya = a.y; yb = b.y; this.canal = canal; this.extA = extA; this.extB = extB; this.cA = cA; this.cB = cB;
            Vector3 p0 = a - dir * extA, p1 = b + dir * extB;
            bb = new Bounds((p0 + p1) * 0.5f, Vector3.zero);
            foreach (var q in new[] { p0, p1 })
                foreach (float s in new[] { -1f, 1f })
                {
                    bb.Encapsulate(q + lado * s * (W + 2f) + Vector3.down * (CanalHondo + 2f));
                    bb.Encapsulate(q + lado * s * (W + 2f) + Vector3.up * (HW + RA + 2f));
                }
        }
        public float T(Vector3 p) { Vector3 ap = p - a; return ap.x * dir.x + ap.z * dir.z; }
        public override float D(Vector3 p)
        {
            Vector3 ap = p - a;
            float t = ap.x * dir.x + ap.z * dir.z, u = ap.x * lado.x + ap.z * lado.z;
            float yf = Mathf.Lerp(ya, yb, Mathf.Clamp01(t / largo));
            float v = p.y - yf, au = Mathf.Abs(u);
            float caja = Caja2(au - W, Mathf.Abs(v - HW * 0.5f) - HW * 0.5f);
            float ex = u / W, ey = (v - HW) / RA;
            float arco = Mathf.Max((Mathf.Sqrt(ex * ex + ey * ey) - 1f) * Mathf.Min(W, RA), HW - v);
            float cuerpo = Mathf.Max(Mathf.Min(caja, arco), Mathf.Max(-extA - t, t - largo - extB));
            if (!canal) return cuerpo;
            float c = Mathf.Max((au - AnchoCanal(v)) * 0.78f, Mathf.Max(-CanalHondo - v, v - 0.5f));
            c = Mathf.Max(c, Mathf.Max(cA - t, t - cB));
            return Mathf.Min(cuerpo, c);
        }
    }

    class Sala : Prim
    {
        public Vector3 c; public float r, hs, rd, rp; public List<Vector3> pilares = new List<Vector3>();   // pilar: (x, z, radio)
        public Sala(Vector3 c, float r, float hs, float rd, float rp)
        {
            this.c = c; this.r = r; this.hs = hs; this.rd = rd; this.rp = rp;
            bb = new Bounds(c + Vector3.up * (hs + rd) * 0.5f, new Vector3(r * 2f + 4f, hs + rd + CanalHondo + 6f, r * 2f + 4f));
        }
        public override float D(Vector3 p)
        {
            float dx = p.x - c.x, dz = p.z - c.z, rr = Mathf.Sqrt(dx * dx + dz * dz), v = p.y - c.y;
            float cil = Caja2(rr - r, Mathf.Abs(v - hs * 0.5f) - hs * 0.5f);
            float ex = rr / r, ey = (v - hs) / rd;
            float cupula = Mathf.Max((Mathf.Sqrt(ex * ex + ey * ey) - 1f) * Mathf.Min(r, rd), hs - v);
            float d = Mathf.Min(cil, cupula);
            if (rp > 0f)
            {
                float radio = rp - Mathf.Max(0f, -v) / CanalHondo * (CanalArriba - CanalAbajo);
                d = Mathf.Min(d, Mathf.Max((rr - radio) * 0.78f, Mathf.Max(-CanalHondo - v, v - 0.5f)));
            }
            foreach (var q in pilares)
            {
                float px = p.x - q.x, pz = p.z - q.y;
                float rad = q.z * (v < 1.5f ? 1.35f - v / 1.5f * 0.35f : 1f);     // base mas ancha
                d = Mathf.Max(d, rad - Mathf.Sqrt(px * px + pz * pz));
            }
            return d;
        }
    }

    class Pozo : Prim
    {
        public Vector3 c; public float r, y0, y1; public bool cuadrado;
        public Pozo(Vector3 c, float r, float y0, float y1, bool cuadrado)
        {
            this.c = c; this.r = r; this.y0 = y0; this.y1 = y1; this.cuadrado = cuadrado;
            bb = new Bounds(new Vector3(c.x, (y0 + y1) * 0.5f, c.z), new Vector3(r * 2f + 4f, y1 - y0 + 4f, r * 2f + 4f));
        }
        public override float D(Vector3 p)
        {
            float dx = p.x - c.x, dz = p.z - c.z;
            float h = cuadrado ? Caja2(Mathf.Abs(dx) - r, Mathf.Abs(dz) - r) : Mathf.Sqrt(dx * dx + dz * dz) - r;
            float mid = (y0 + y1) * 0.5f, half = (y1 - y0) * 0.5f;
            return Caja2(h, Mathf.Abs(p.y - mid) - half);
        }
    }

    class Tubo : Prim
    {
        public Vector3 a, b; public float r;
        public Tubo(Vector3 a, Vector3 b, float r)
        {
            this.a = a; this.b = b; this.r = r;
            bb = new Bounds((a + b) * 0.5f, Vector3.zero); bb.Encapsulate(a); bb.Encapsulate(b); bb.Expand(r * 2f + 4f);
        }
        public override float D(Vector3 p)
        {
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t) - r;
        }
    }

    class Derrumbe : Prim
    {
        public float yArriba;
        public Derrumbe()
        {
            tipo = 1; k = 2f;
            yArriba = Suelo(O.x + XArriba, O.z) - O.y + 0.8f;
            bb = new Bounds(L((XArriba + XBoca) * 0.5f, (Y0 + 12f) * 0.5f, 0f), new Vector3(XBoca - XArriba + 16f, 12f - Y0 + 8f, 48f));
        }
        public float PisoLocal(float lx) { float s = Mathf.Clamp01((XBoca - lx) / (XBoca - XArriba)); return Mathf.Lerp(Y0, yArriba, s); }
        public override float D(Vector3 p)
        {
            Vector3 l = p - O;
            float yf = PisoLocal(l.x) + (Mathf.PerlinNoise(l.x * 0.21f + 3.7f, l.z * 0.21f + 1.3f) - 0.5f) * 0.9f;
            float hw = 7f + Mathf.Max(0f, l.y - yf) * 0.3f;
            // paredes de tierra irregulares (desprendimientos), distintas a cada lado
            float lado = l.z > 0f ? 17.3f : 0f;
            hw += (Mathf.PerlinNoise(l.x * 0.07f + lado, l.y * 0.09f + 5.1f) - 0.5f) * 4.5f + (Mathf.PerlinNoise(l.x * 0.23f + lado, l.y * 0.27f + 9.4f) - 0.5f) * 1.6f;
            float d = Mathf.Max(yf - l.y, (Mathf.Abs(l.z) - hw) * 0.9f);
            d = Mathf.Max(d, l.x - (XBoca + 1.5f));
            return Mathf.Max(d, (XArriba - 6f) - l.x);
        }
    }

    class SueloAfuera : Prim
    {
        public Rect[] rects;     // en el mundo
        public SueloAfuera()
        {
            tipo = 2; k = 2.5f;
            rects = ZonaJuego.cloacaHuecos.Select(r => new Rect(r.x + ZonaJuego.alcantarilla.x, r.y + ZonaJuego.alcantarilla.y, r.width, r.height)).ToArray();
            bool primero = true;
            foreach (var r in rects)
            {
                float hMin = float.MaxValue, hMax = float.MinValue;
                for (float x = r.xMin - 8f; x <= r.xMax + 8f; x += 2f)
                    for (float z = r.yMin - 8f; z <= r.yMax + 8f; z += 2f) { float h = Suelo(x, z); hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h); }
                var b = new Bounds(new Vector3(r.center.x, (hMin + hMax) * 0.5f, r.center.y), new Vector3(r.width + 16f, hMax - hMin + 10f, r.height + 16f));
                if (primero) { bb = b; primero = false; } else bb.Encapsulate(b);
            }
        }
        public float DistRect(float x, float z)
        {
            float best = float.MaxValue;
            foreach (var r in rects) best = Mathf.Min(best, Caja2(Mathf.Abs(x - r.center.x) - r.width * 0.5f, Mathf.Abs(z - r.center.y) - r.height * 0.5f));
            return best;
        }
        public override float D(Vector3 p) { return Mathf.Max((Suelo(p.x, p.z) - p.y) * 0.95f, DistRect(p.x, p.z) - 6f); }
    }

    static List<Prim> prims;
    static Derrumbe derrumbe;
    static SueloAfuera sueloAfuera;
    static Tunel t1, t2, t3, t4;
    static Sala sPozo, sCruce, sNido, sRey;

    static float SMin(float a, float b, float k)
    {
        float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
        return Mathf.Min(a, b) - h * h * k * 0.25f;
    }

    static float F(Vector3 p, List<Prim> lista)
    {
        float d = 1e9f;
        foreach (var q in lista) d = SMin(d, q.D(p), q.k);
        return d;
    }

    static Vector3 Gradiente(Vector3 p, List<Prim> lista)
    {
        const float e = 0.35f;
        return new Vector3(F(p + Vector3.right * e, lista) - F(p - Vector3.right * e, lista),
                           F(p + Vector3.up * e, lista) - F(p - Vector3.up * e, lista),
                           F(p + Vector3.forward * e, lista) - F(p - Vector3.forward * e, lista));
    }

    static void ConstruirPrimitivas()
    {
        prims = new List<Prim>();
        // tuneles (andenes a Y0; el T4 baja hacia el Gran Colector)
        t1 = new Tunel(L(XBoca, Y0, 0f), L(LPozo), true, 0f, 0f, 6f, 1e4f);
        t2 = new Tunel(L(LPozo), L(LCruce), true, 0f, 0f, -1e4f, 1e4f);
        t3 = new Tunel(L(LNido), L(LCruce), true, 0f, 0f, RNido - 2f, 1e4f);
        t4 = new Tunel(L(LCruce.x + RCruce, Y0, 0f), L(LRey.x - RRey, YA, 0f), true, RCruce, RRey, -1e4f, 1e4f);
        prims.AddRange(new Prim[] { t1, t2, t3, t4 });
        // salas
        sPozo = new Sala(L(LPozo), RPozo, 10f, 9f, 7f);
        sCruce = new Sala(L(LCruce), RCruce, 10f, 8f, 7f);
        sNido = new Sala(L(LNido), RNido, 8f, 9f, 0f);
        sRey = new Sala(L(LRey), RRey, 12f, 16f, RPilaRey);
        for (int i = 0; i < 6; i++)
        {
            float a = (30f + i * 60f) * Mathf.Deg2Rad;
            sRey.pilares.Add(new Vector3(O.x + LRey.x + Mathf.Cos(a) * 28f, O.z + LRey.z + Mathf.Sin(a) * 28f, 2.4f));
        }
        prims.AddRange(new Prim[] { sPozo, sCruce, sNido, sRey });
        // pozo de la alcantarilla y rejilla cuadrada sobre el Gran Colector (hasta arriba del suelo)
        prims.Add(new Pozo(L(0f, Y0 + 8f, 0f), 5.5f, O.y + Y0 + 8f, O.y + 6f, false) { });
        var cRey = L(LRey); float sueloRey = Suelo(cRey.x, cRey.z);
        prims.Add(new Pozo(new Vector3(cRey.x, 0f, cRey.z), 3.8f, O.y + YA + 20f, sueloRey + 6f, true));
        // tuberias por donde salen las crias del rey
        foreach (float ang in new[] { 90f, 150f, 210f, 270f })
        {
            float a = ang * Mathf.Deg2Rad; Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            prims.Add(new Tubo(L(LRey) + d * (RRey - 3f) + Vector3.up * 2.6f, L(LRey) + d * (RRey + 10f) + Vector3.up * 2.6f, 2.6f));
        }
        derrumbe = new Derrumbe();
        sueloAfuera = new SueloAfuera();
        prims.Add(derrumbe);
        prims.Add(sueloAfuera);
        foreach (var q in prims) q.bb.Expand(2f);
    }

    // ================================================================== surface nets
    class MallaTrozo
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Vector3> n = new List<Vector3>();
        public List<Color> col = new List<Color>();
        public List<int>[] sub = { new List<int>(), new List<int>(), new List<int>() };
    }

    static readonly int[,] Aristas12 = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };

    static List<MallaTrozo> Poligonizar(Bounds dom)
    {
        var res = new List<MallaTrozo>();
        Vector3 dmin = new Vector3(Mathf.Floor(dom.min.x), Mathf.Floor(dom.min.y), Mathf.Floor(dom.min.z));
        int nx = Mathf.CeilToInt(dom.size.x / Celda / Trozo) + 1, ny = Mathf.CeilToInt(dom.size.y / Celda / Trozo) + 1, nz = Mathf.CeilToInt(dom.size.z / Celda / Trozo) + 1;
        const int S = Trozo + 3, C = S - 1;
        var f = new float[S * S * S];
        var vid = new int[C * C * C];
        var esq = new float[8];
        for (int cx = 0; cx < nx; cx++)
            for (int cy = 0; cy < ny; cy++)
                for (int cz = 0; cz < nz; cz++)
                {
                    Vector3 c0 = dmin + new Vector3(cx, cy, cz) * Trozo * Celda;
                    var cb = new Bounds(c0 + Vector3.one * Trozo * Celda * 0.5f, Vector3.one * (Trozo + 4) * Celda);
                    var act = prims.Where(q => q.bb.Intersects(cb)).ToList();
                    if (act.Count == 0) continue;
                    bool aire = false, solido = false;
                    for (int i = 0; i < S; i++)
                        for (int j = 0; j < S; j++)
                            for (int k = 0; k < S; k++)
                            {
                                float val = F(c0 + new Vector3(i - 1, j - 1, k - 1) * Celda, act);
                                f[(i * S + j) * S + k] = val;
                                if (val < 0f) aire = true; else solido = true;
                            }
                    if (!aire || !solido) continue;
                    var m = new MallaTrozo();
                    for (int q = 0; q < vid.Length; q++) vid[q] = -1;
                    // un vertice por celda que corta la superficie
                    for (int i = 0; i < C; i++)
                        for (int j = 0; j < C; j++)
                            for (int k = 0; k < C; k++)
                            {
                                int signos = 0;
                                for (int e = 0; e < 8; e++)
                                {
                                    esq[e] = f[((i + (e & 1)) * S + (j + ((e >> 1) & 1))) * S + (k + ((e >> 2) & 1))];
                                    if (esq[e] < 0f) signos |= 1 << e;
                                }
                                if (signos == 0 || signos == 255) continue;
                                Vector3 acc = Vector3.zero; int cuenta = 0;
                                for (int e = 0; e < 12; e++)
                                {
                                    int a = Aristas12[e, 0], b = Aristas12[e, 1];
                                    if ((esq[a] < 0f) == (esq[b] < 0f)) continue;
                                    float t = esq[a] / (esq[a] - esq[b]);
                                    Vector3 pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1), pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
                                    acc += Vector3.Lerp(pa, pb, t); cuenta++;
                                }
                                Vector3 pos = c0 + (new Vector3(i - 1, j - 1, k - 1) + acc / cuenta) * Celda;
                                vid[(i * C + j) * C + k] = m.v.Count;
                                m.v.Add(pos);
                                Vector3 g = Gradiente(pos, act);
                                m.n.Add(g.sqrMagnitude > 1e-8f ? -g.normalized : Vector3.up);
                            }
                    // caras: cada arista de la rejilla que cruza la superficie (las que empiezan dentro del trozo)
                    for (int i = 1; i <= Trozo; i++)
                        for (int j = 1; j <= Trozo; j++)
                            for (int k = 1; k <= Trozo; k++)
                            {
                                float f0 = f[(i * S + j) * S + k];
                                for (int eje = 0; eje < 3; eje++)
                                {
                                    int i2 = i + (eje == 0 ? 1 : 0), j2 = j + (eje == 1 ? 1 : 0), k2 = k + (eje == 2 ? 1 : 0);
                                    float f1 = f[(i2 * S + j2) * S + k2];
                                    if ((f0 < 0f) == (f1 < 0f)) continue;
                                    int a, b, c, d;
                                    if (eje == 0) { a = V(vid, i, j - 1, k - 1); b = V(vid, i, j, k - 1); c = V(vid, i, j, k); d = V(vid, i, j - 1, k); }
                                    else if (eje == 1) { a = V(vid, i - 1, j, k - 1); b = V(vid, i, j, k - 1); c = V(vid, i, j, k); d = V(vid, i - 1, j, k); }
                                    else { a = V(vid, i - 1, j - 1, k); b = V(vid, i, j - 1, k); c = V(vid, i, j, k); d = V(vid, i - 1, j, k); }
                                    if (a < 0 || b < 0 || c < 0 || d < 0) continue;
                                    Vector3 haciaAire = Vector3.zero; haciaAire[eje] = f0 < 0f ? -1f : 1f;
                                    Tri(m, a, b, c, haciaAire); Tri(m, a, c, d, haciaAire);
                                }
                            }
                    if (m.v.Count > 0) res.Add(m);
                }
        return res;
    }

    static int V(int[] vid, int i, int j, int k) { const int C = Trozo + 2; return vid[(i * C + j) * C + k]; }

    // guarda el triangulo en una lista temporal (sub[0]); se clasifica despues. Cara frontal (Cross(b-a, c-a)) hacia el aire
    static void Tri(MallaTrozo m, int a, int b, int c, Vector3 haciaAire)
    {
        Vector3 n = Vector3.Cross(m.v[b] - m.v[a], m.v[c] - m.v[a]);
        if (n.sqrMagnitude < 1e-10f) return;
        if (Vector3.Dot(n, haciaAire) > 0f) m.sub[0].AddRange(new[] { a, b, c });
        else m.sub[0].AddRange(new[] { a, c, b });
    }

    // ================================================================== clasificacion, luz y exposicion
    struct Luz { public Vector3 p; public Color c; public float i, r; }
    static List<Luz> luces;

    static int Duenio(Vector3 p, out float dSuelo)
    {
        float dS = 1e9f, dT = 1e9f, dG = 1e9f;
        foreach (var q in prims)
        {
            if (!q.bb.Contains(p)) continue;
            float d = Mathf.Abs(q.D(p));
            if (q.tipo == 0) dS = Mathf.Min(dS, d); else if (q.tipo == 1) dT = Mathf.Min(dT, d); else dG = Mathf.Min(dG, d);
        }
        dSuelo = dG;
        if (dG <= dS && dG <= dT) return 2;
        if (dT <= dS) return 1;
        return 0;
    }

    static void Clasificar(List<MallaTrozo> trozos)
    {
        foreach (var m in trozos)
        {
            var todos = m.sub[0];
            var nuevos = new[] { new List<int>(), new List<int>(), new List<int>() };
            for (int t = 0; t < todos.Count; t += 3)
            {
                Vector3 c = (m.v[todos[t]] + m.v[todos[t + 1]] + m.v[todos[t + 2]]) / 3f;
                float suelo = Suelo(c.x, c.z);
                float dr = sueloAfuera.DistRect(c.x, c.z);
                // por encima del suelo y fuera de los huecos: lo cubre el suelo exterior
                if (dr > 2.5f && c.y > suelo - 1.2f) continue;
                int d = Duenio(c, out _);
                if (d == 2 && dr > 2.5f) continue;
                if (d == 2)
                {
                    Vector3 n = Vector3.Cross(m.v[todos[t + 1]] - m.v[todos[t]], m.v[todos[t + 2]] - m.v[todos[t]]).normalized;
                    if (n.y < 0.35f) d = 1;      // taludes empinados: tierra
                }
                nuevos[d].AddRange(new[] { todos[t], todos[t + 1], todos[t + 2] });
            }
            m.sub = nuevos;
            // los bordes del suelo que quedan bajo el suelo exterior se hunden un poco (sin parpadeo)
            for (int i = 0; i < m.v.Count; i++)
            {
                var p = m.v[i];
                if (sueloAfuera.DistRect(p.x, p.z) > 0f && Mathf.Abs(p.y - Suelo(p.x, p.z)) < 1.5f) m.v[i] = p + Vector3.down * 0.14f;
            }
        }
    }

    static float Exposicion(Vector3 p)
    {
        Vector3 l = p - O;
        float suelo = Suelo(p.x, p.z) - O.y;
        // el derrumbe y la superficie dan al cielo
        if (l.x < XBoca - 0.5f && l.y > derrumbe.PisoLocal(l.x) - 3f) return 1f;
        if (l.y > suelo - 2f) return 1f;
        float e = 0f;
        // boca del derrumbe: la luz entra unos metros en el tunel
        if (l.x < XBoca + 16f && Mathf.Abs(l.z) < W + 2f) e = Mathf.Max(e, Mathf.Clamp01((XBoca + 16f - l.x) / 16f) * 0.8f);
        // bajo la alcantarilla
        float dp = new Vector2(l.x, l.z).magnitude;
        if (dp < 15f) e = Mathf.Max(e, 1f - dp / 15f);
        // bajo la rejilla del Gran Colector
        float dr = new Vector2(l.x - LRey.x, l.z - LRey.z).magnitude;
        if (dr < 20f) e = Mathf.Max(e, (1f - dr / 20f) * 0.9f);
        return e;
    }

    static Color Hornear(Vector3 p, Vector3 n)
    {
        Color acc = Color.black;
        foreach (var lz in luces)
        {
            Vector3 d = lz.p - p;
            float dist = d.magnitude;
            if (dist >= lz.r) continue;
            float at = 1f - dist / lz.r; at *= at;
            float lam = 0.3f + 0.7f * Mathf.Max(0f, Vector3.Dot(n, d / Mathf.Max(dist, 0.001f)));
            acc += lz.c * (lz.i * at * lam);
        }
        return acc;
    }

    static void Iluminar(List<MallaTrozo> trozos)
    {
        foreach (var m in trozos)
        {
            m.col = new List<Color>(m.v.Count);
            for (int i = 0; i < m.v.Count; i++)
            {
                Color c = Hornear(m.v[i], m.n[i]);
                c.a = Exposicion(m.v[i]);
                m.col.Add(c);
            }
        }
    }

    // ================================================================== utilidades de mallas
    static int contador;

    static Mesh Guardar(Mesh m, string nombre)
    {
        Directory.CreateDirectory(Gen);
        string path = Gen + nombre + ".asset";
        NormalesSeguras.Sanear(m);
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

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

    static Texture2D Tex(string nombre, bool normal = false)
    {
        string path = Raiz + "Texturas/" + nombre + ".jpg";
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti != null && normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Lit(string nombre, Color c, float suave, float metal = 0f, Color emision = default, Texture2D tex = null, Texture2D nor = null, Vector2 tiling = default)
    {
        return GuardarMaterial(nombre, Shader.Find("Universal Render Pipeline/Lit"), m =>
        {
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", suave); m.SetFloat("_Metallic", metal);
            m.SetTexture("_BaseMap", tex);
            if (tiling != default) m.SetTextureScale("_BaseMap", tiling);
            if (nor != null) { m.SetTexture("_BumpMap", nor); m.EnableKeyword("_NORMALMAP"); } else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
            if (emision.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emision); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }   // con None, URP le quita _EMISSION al validar
            else m.DisableKeyword("_EMISSION");
        });
    }

    // torno: perfil (radio, altura) alrededor de Y
    static Mesh Torno(string nombre, Vector2[] perfil, int lados, System.Func<float, float> radioAng = null)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int s = 0; s <= lados; s++)
        {
            float a = s / (float)lados * Mathf.PI * 2f;
            float k = radioAng != null ? radioAng(a) : 1f;
            for (int i = 0; i < perfil.Length; i++)
            {
                v.Add(new Vector3(Mathf.Cos(a) * perfil[i].x * k, perfil[i].y, Mathf.Sin(a) * perfil[i].x * k));
                uv.Add(new Vector2(s / (float)lados, i / (float)(perfil.Length - 1)));
            }
        }
        int n = perfil.Length;
        for (int s = 0; s < lados; s++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = s * n + i, b = (s + 1) * n + i;
                t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        var m = new Mesh { name = nombre };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh Caja(string nombre, Vector3 tam, float ruido = 0f, int seed = 0)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var m = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(go);
        var v = m.vertices;
        var r2 = new System.Random(seed);
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 p = Vector3.Scale(v[i], tam);
            if (ruido > 0f)
            {
                // misma posicion = mismo desplazamiento (las esquinas siguen unidas)
                int h = (Mathf.RoundToInt(v[i].x * 2f) * 73856093) ^ (Mathf.RoundToInt(v[i].y * 2f) * 19349663) ^ (Mathf.RoundToInt(v[i].z * 2f) * 83492791) ^ seed;
                var rr = new System.Random(h);
                p += new Vector3((float)rr.NextDouble() - 0.5f, (float)rr.NextDouble() - 0.5f, (float)rr.NextDouble() - 0.5f) * ruido;
            }
            v[i] = p;
        }
        m.vertices = v; m.name = nombre; m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh Bola(string nombre, float radio, Vector3 escala, float ruido, int seed)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var m = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(go);
        var v = m.vertices;
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 d = v[i].normalized;
            float n = Mathf.PerlinNoise(d.x * 2.3f + seed * 3.1f + d.z, d.y * 2.3f + seed * 1.7f) - 0.5f;
            v[i] = Vector3.Scale(d * radio * (1f + n * ruido), escala);
        }
        m.vertices = v; m.name = nombre; m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh Combinar(string nombre, params (Mesh m, Matrix4x4 t)[] partes)
    {
        var ci = partes.Select(p => new CombineInstance { mesh = p.m, transform = p.t }).ToArray();
        var m = new Mesh { name = nombre, indexFormat = IndexFormat.UInt32 };
        m.CombineMeshes(ci, true, true);
        m.RecalculateBounds();
        return m;
    }

    static GameObject Objeto(string nombre, Transform padre, Mesh malla, Material mat, bool colision, bool sombras = true)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = sombras ? ShadowCastingMode.On : ShadowCastingMode.Off;
        if (colision) go.AddComponent<MeshCollider>().sharedMesh = malla;
        return go;
    }

    static Vector3 PisoEn(Vector3 p, float arriba = 4f, float abajo = 14f)
    {
        if (Physics.Raycast(p + Vector3.up * arriba, Vector3.down, out var hit, arriba + abajo, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return p;
    }

    // ================================================================== construccion principal
    static Material mHierro, mLadrillo, mTierra, mSueloAfuera, mAgua, mHaz, mOxido, mMoho, mHongo, mTallo, mLadrilloSuelto, mConcreto, mPapel, mPeriodico, mPaja,
                    mQueso, mPan, mMiga, mTapaRoja, mTapaAzul, mBotella, mLata, mPlato, mOro, mRubi, mHoja, mHojaSeca, mRemolino, mComida;

    [MenuItem("Herramientas/Cloaca/Construir")]
    public static void Construir()
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var log = new System.Text.StringBuilder();
        rnd = new System.Random(4411);
        contador = 0;
        var a2 = ZonaJuego.alcantarilla;
        O = new Vector3(a2.x, Suelo(a2.x, a2.y), a2.y);

        var viejo = GameObject.Find("CLOACA");
        if (viejo != null) Object.DestroyImmediate(viejo);
        foreach (var wi in Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include))
            if (wi != null && wi.name.StartsWith("Cloaca_")) Object.DestroyImmediate(wi.gameObject);
        if (Directory.Exists(Gen)) AssetDatabase.DeleteAsset(Gen.TrimEnd('/'));
        Directory.CreateDirectory(Gen);
        AssetDatabase.Refresh();

        RecortarSueloExterior(log);
        DespejarHuecos(log);
        CrearMateriales();

        var raiz = new GameObject("CLOACA");
        ConstruirPrimitivas();

        // dominio
        var dom = new Bounds(prims[0].bb.center, prims[0].bb.size);
        foreach (var q in prims) dom.Encapsulate(q.bb);
        var trozos = Poligonizar(dom);
        Clasificar(trozos);
        Debug.Log("[Cloaca] Poligonizado: " + trozos.Count + " trozos, " + trozos.Sum(t => t.v.Count) + " vertices, " + reloj.ElapsedMilliseconds + " ms");

        // ---- mallas de la cueva con colision (la luz se hornea despues, cuando ya se puede apuntar a las paredes)
        var cueva = new GameObject("Cueva").transform; cueva.SetParent(raiz.transform, false);
        var mallas = new List<(Mesh m, MallaTrozo t)>();
        for (int i = 0; i < trozos.Count; i++)
        {
            var t = trozos[i];
            if (t.sub.All(s => s.Count == 0)) continue;
            var m = new Mesh { name = "Cloaca_" + i, indexFormat = t.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(t.v); m.SetNormals(t.n);
            m.subMeshCount = 3;
            for (int s = 0; s < 3; s++) m.SetTriangles(t.sub[s], s);
            m.RecalculateBounds();
            m = Guardar(m, "Cloaca_" + i);
            mallas.Add((m, t));
            var go = new GameObject("Trozo_" + i);
            go.transform.SetParent(cueva, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { mLadrillo, mTierra, mSueloAfuera };
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.AddComponent<MeshCollider>().sharedMesh = m;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
        Physics.SyncTransforms();

        // ---- luces (moho luminoso en las paredes, hongos del nido, brillo del trono)
        luces = new List<Luz>();
        var manchasMoho = ColocarMoho();
        var gestorLucesPos = new List<Vector3>(); var gestorLucesCol = new List<Color>();
        foreach (var mm in manchasMoho) { luces.Add(new Luz { p = mm.pos + mm.normal * 2f, c = mm.color, i = 1.15f, r = 22f }); gestorLucesPos.Add(mm.pos + mm.normal * 2.2f); gestorLucesCol.Add(mm.color); }
        var hongosNido = new List<(Vector3 pos, Vector3 normal)>();
        for (int i = 0; i < 9; i++)
        {
            float a = R(0f, Mathf.PI * 2f);
            Vector3 p = L(LNido) + new Vector3(Mathf.Cos(a) * (RNido - 1.5f), R(0.5f, 3f), Mathf.Sin(a) * (RNido - 1.5f));
            Vector3 nrm = -new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            hongosNido.Add((p, nrm));
            var col = new Color(1f, 0.6f, 0.25f);
            luces.Add(new Luz { p = p + nrm * 2f, c = col, i = 1.1f, r = 20f });
            if (i % 2 == 0) { gestorLucesPos.Add(p + nrm * 2f); gestorLucesCol.Add(col); }
        }
        // luz tenue de relleno a lo largo de los tuneles (que nada quede negro del todo)
        foreach (var t in new[] { t1, t2, t3, t4 })
            for (float s = 0f; s <= t.largo; s += 22f)
            {
                Vector3 p = t.a + t.dir * s; p.y = Mathf.Lerp(t.ya, t.yb, s / t.largo) + 8f;
                luces.Add(new Luz { p = p, c = new Color(0.55f, 0.65f, 0.5f), i = 0.18f, r = 26f });
            }
        // bajo las rejillas: luz del cielo
        luces.Add(new Luz { p = L(0f, Y0 + 6f, 0f), c = new Color(0.9f, 0.88f, 0.8f), i = 0.7f, r = 26f });
        luces.Add(new Luz { p = L(LRey.x, YA + 8f, 0f), c = new Color(0.9f, 0.88f, 0.8f), i = 0.6f, r = 30f });
        // el Gran Colector: moho en lo alto de la pared, para que la pelea se vea bien
        for (int i = 0; i < 8; i++)
        {
            float a = (i + 0.5f) / 8f * Mathf.PI * 2f;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 desde = L(LRey) + Vector3.up * 9f;
            if (Physics.Raycast(desde, d, out var hitW, RRey + 6f, ~0, QueryTriggerInteraction.Ignore) && hitW.distance > RRey - 6f)
            {
                var col = new Color(0.55f, 1f, 0.45f);
                manchasMoho.Add(new Mancha { pos = hitW.point, normal = hitW.normal, color = col });
                luces.Add(new Luz { p = hitW.point + hitW.normal * 3f, c = col, i = 1.1f, r = 30f });
                gestorLucesPos.Add(hitW.point + hitW.normal * 3f); gestorLucesCol.Add(col);
            }
        }
        luces.Add(new Luz { p = L(LRey) + Vector3.up * 14f, c = new Color(0.6f, 0.7f, 0.55f), i = 0.45f, r = 46f });
        // el trono del rey brilla con un rojo inquietante (cristales rotos)
        Vector3 posTrono = L(LRey.x + 33f, YA, 0f);
        luces.Add(new Luz { p = posTrono + Vector3.up * 6f, c = new Color(1f, 0.35f, 0.25f), i = 1.2f, r = 26f });
        gestorLucesPos.Add(posTrono + Vector3.up * 6f); gestorLucesCol.Add(new Color(1f, 0.4f, 0.3f));

        Iluminar(trozos);
        foreach (var (m, t) in mallas) { m.SetColors(t.col); EditorUtility.SetDirty(m); }
        Debug.Log("[Cloaca] Luz horneada: " + luces.Count + " luces, " + reloj.ElapsedMilliseconds + " ms");

        // ---- rejillas, collar de concreto, tuberias y agua
        var estructura = new GameObject("Estructura").transform; estructura.SetParent(raiz.transform, false);
        var haces = new List<Renderer>(); var focos = new List<Light>();
        CrearAlcantarilla(estructura, haces, focos);
        CrearRejillaRey(estructura, haces, focos);
        CrearTuberias(estructura);
        var aguas = new List<Cloaca.Agua>();
        CrearAgua(estructura, aguas);
        Physics.SyncTransforms();

        // ---- NavMesh (ratas): cloaca + jardin alrededor del derrumbe
        var surf = raiz.AddComponent<NavMeshSurface>();
        surf.collectObjects = CollectObjects.Volume;
        var vol = new Bounds(dom.center, dom.size);
        vol.Encapsulate(new Bounds(L(-60f, 0f, 0f), new Vector3(330f, 70f, 300f)));
        surf.center = vol.center - raiz.transform.position; surf.size = vol.size;
        surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surf.layerMask = ~0;
        surf.BuildNavMesh();
        if (surf.navMeshData != null) AssetDatabase.CreateAsset(surf.navMeshData, Gen + "NavMeshCloaca.asset");
        Debug.Log("[Cloaca] NavMesh listo, " + reloj.ElapsedMilliseconds + " ms");

        // ---- instancias: moho, escombros, basura, nido, hojas
        var grupos = new List<Hormiguero.GrupoInstancias>();
        CrearInstancias(grupos, manchasMoho, hongosNido, posTrono);

        // ---- trono, montones de comida, carne en el nido
        var objetos = new GameObject("Objetos").transform; objetos.SetParent(raiz.transform, false);
        CrearTrono(objetos, posTrono);
        var montones = CrearMontones(objetos);
        CrearRecogibles(log);

        // ---- gestor, ratas y rey
        var gestor = raiz.AddComponent<Cloaca>();
        gestor.tapa = O;
        gestor.derrumbe = L(XArriba + 4f, Suelo(O.x + XArriba + 4f, O.z) - O.y, 0f);
        gestor.zona = new Bounds(dom.center, dom.size + new Vector3(0f, 0f, 0f));
        gestor.lucesPos = gestorLucesPos.ToArray();
        gestor.lucesColor = gestorLucesCol.ToArray();
        gestor.intensidadLuz = 13f;
        gestor.lucesReales = 12;
        gestor.grupos = grupos.ToArray();
        gestor.aguas = aguas.ToArray();
        gestor.haces = haces.ToArray();
        gestor.focosSol = focos.ToArray();
        gestor.chapoteo = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Agua_Chapoteo.mp3");
        gestor.madriguera = L(LNido);
        var pm = new List<Vector3>();
        for (int i = 0; i < 6; i++) { float a = i * Mathf.PI / 3f + 0.3f; pm.Add(PisoEn(L(LNido) + new Vector3(Mathf.Cos(a) * 10f, 2f, Mathf.Sin(a) * 10f))); }
        gestor.puntosMadriguera = pm.ToArray();
        CrearRatas(raiz.transform, gestor);

        var amb = raiz.AddComponent<AudioSource>();
        amb.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Cloaca_Ambiente.mp3");
        amb.loop = true; amb.playOnAwake = true; amb.spatialBlend = 0f; amb.volume = 0f;
        gestor.ambiente = amb;
        Sonido3D(raiz.transform, "Goteo_Pozo", L(LPozo) + Vector3.up * 4f, "Cloaca_Goteo", 0.55f, 60f);
        Sonido3D(raiz.transform, "Goteo_Nido", L(LNido) + Vector3.up * 4f, "Cloaca_Goteo", 0.45f, 50f);
        Sonido3D(raiz.transform, "Cascada_Rey", L(LRey.x - RRey + 6f, YA + 2f, 0f), "Cascada", 0.6f, 90f);
        Sonido3D(raiz.transform, "Agua_Cruce", L(LCruce) + Vector3.up * 2f, "Cascada", 0.3f, 45f);

        ConfigurarCamara();
        var dn = Object.FindAnyObjectByType<DayNightCycle>();
        if (dn != null) EditorUtility.SetDirty(dn);

        EditorSceneManager.MarkSceneDirty(raiz.scene);
        EditorSceneManager.SaveScene(raiz.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Cloaca] Construida en " + reloj.ElapsedMilliseconds + " ms. Montones: " + montones + "\n" + log);
    }

    // ================================================================== suelo exterior
    static void RecortarSueloExterior(System.Text.StringBuilder log)
    {
        var suelo = GameObject.Find("EXTERIOR/Suelo");
        var mf = suelo != null ? suelo.GetComponent<MeshFilter>() : null;
        if (mf == null || mf.sharedMesh == null) { log.AppendLine("sin EXTERIOR/Suelo"); return; }
        var m = mf.sharedMesh;
        var v = m.vertices; var t = m.triangles;
        var nt = new List<int>(t.Length);
        int quitados = 0;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 c = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
            if (ZonaJuego.EnHuecoCloaca(c.x, c.z)) { quitados++; continue; }
            nt.Add(t[i]); nt.Add(t[i + 1]); nt.Add(t[i + 2]);
        }
        if (quitados > 0)
        {
            m.SetTriangles(nt, 0);
            m.RecalculateBounds();
            EditorUtility.SetDirty(m);
            var mc = suelo.GetComponent<MeshCollider>();
            if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = m; }
        }
        log.AppendLine("suelo exterior: " + quitados + " triangulos quitados");
    }

    // los bichos y objetos que quedaron sobre los huecos se mueven a un lado
    static void DespejarHuecos(System.Text.StringBuilder log)
    {
        var mover = new List<Transform>();
        var bichos = GameObject.Find("EXTERIOR/Bichos");
        if (bichos != null) foreach (Transform b in bichos.transform) mover.Add(b);
        foreach (var go in EditorSceneManager.GetActiveScene().GetRootGameObjects()) if (go.GetComponent<WorldItem>() != null) mover.Add(go.transform);
        foreach (var t in mover)
        {
            Vector3 p = t.position;
            if (ZonaJuego.DistanciaCloaca(p.x, p.z) > 6f) continue;
            Vector3 q = ZonaJuego.Limitar(p, false, 8f);
            q.y = Suelo(q.x, q.z) + (p.y - Suelo(p.x, p.z));
            Undo.RecordObject(t, "Despejar");
            t.position = q;
            log.AppendLine("movido " + t.name + " de " + p + " a " + q);
        }
    }

    // ================================================================== materiales
    static void CrearMateriales()
    {
        var cueva = Shader.Find("Hormiguero/Cueva");
        mLadrillo = GuardarMaterial("Ladrillo", cueva, m =>
        {
            m.SetTexture("_Pared", Tex("castle_brick_02_red_diff")); m.SetTexture("_NormalPared", Tex("castle_brick_02_red_nor_gl", true));
            m.SetTexture("_Piso", Tex("dirty_concrete_diff")); m.SetTexture("_NormalPiso", Tex("dirty_concrete_nor_gl", true));
            m.SetFloat("_Tamano", 24f);
            m.SetColor("_ColorPared", new Color(0.78f, 0.74f, 0.66f)); m.SetColor("_ColorPiso", new Color(0.72f, 0.74f, 0.62f));
            m.SetColor("_LuzMinima", new Color(0.03f, 0.033f, 0.028f)); m.SetFloat("_FuerzaHorneada", 1f);
        });
        mTierra = GuardarMaterial("Tierra", cueva, m =>
        {
            m.SetTexture("_Pared", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hormiguero/Texturas/excavated_soil_wall_diff.jpg"));
            m.SetTexture("_NormalPared", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hormiguero/Texturas/excavated_soil_wall_nor.jpg"));
            m.SetTexture("_Piso", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hormiguero/Texturas/brown_mud_dry_diff.jpg"));
            m.SetTexture("_NormalPiso", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Hormiguero/Texturas/brown_mud_dry_nor.jpg"));
            m.SetFloat("_Tamano", 16f);
            m.SetColor("_ColorPared", Color.white); m.SetColor("_ColorPiso", new Color(0.9f, 0.85f, 0.78f));
            m.SetColor("_LuzMinima", new Color(0.03f, 0.028f, 0.025f)); m.SetFloat("_FuerzaHorneada", 1f);
        });
        mSueloAfuera = AssetDatabase.LoadAssetAtPath<Material>("Assets/Exterior/Materiales/Suelo.mat");
        mAgua = GuardarMaterial("Agua", Shader.Find("Cloaca/Agua"), m =>
        {
            m.SetColor("_ColorHondo", new Color(0.06f, 0.08f, 0.045f)); m.SetColor("_ColorSucio", new Color(0.3f, 0.32f, 0.15f));
            m.SetColor("_ColorNata", new Color(0.5f, 0.56f, 0.32f)); m.SetFloat("_Escala", 6f); m.SetFloat("_Velocidad", 2.2f); m.SetFloat("_Brillo", 2.5f);
        });
        mHaz = GuardarMaterial("Haz", Shader.Find("Cloaca/Haz"), m => { m.SetColor("_Color", new Color(1f, 0.93f, 0.78f)); m.SetFloat("_Intensidad", 0.32f); m.SetFloat("_Polvo", 0.7f); });
        mOxido = Lit("MetalOxidado", new Color(0.55f, 0.5f, 0.46f), 0.3f, 0.5f, default, Tex("rusty_metal_02_diff"), Tex("rusty_metal_02_nor_gl", true), new Vector2(1f, 10f));
        mHierro = Lit("Hierro", new Color(0.19f, 0.15f, 0.12f), 0.38f, 0.65f);
        mMoho = Lit("MohoLuminoso", new Color(0.3f, 0.7f, 0.25f), 0.8f, 0f, new Color(0.45f, 1.6f, 0.35f));
        mHongo = Lit("HongoNido", new Color(1f, 0.6f, 0.3f), 0.5f, 0f, new Color(1.6f, 0.7f, 0.2f));
        mTallo = Lit("TalloHongo", new Color(0.85f, 0.8f, 0.7f), 0.3f);
        mLadrilloSuelto = Lit("LadrilloSuelto", new Color(0.8f, 0.72f, 0.66f), 0.15f, 0f, default, Tex("castle_brick_02_red_diff"), null, new Vector2(0.18f, 0.1f));
        mConcreto = Lit("Concreto", new Color(0.62f, 0.62f, 0.58f), 0.15f, 0f, default, Tex("dirty_concrete_diff"), Tex("dirty_concrete_nor_gl", true), new Vector2(0.25f, 0.25f));
        mPapel = Lit("Papel", new Color(0.86f, 0.84f, 0.78f), 0.2f);
        mPeriodico = Lit("Periodico", new Color(0.62f, 0.62f, 0.58f), 0.15f);
        mPaja = Lit("Paja", new Color(0.78f, 0.66f, 0.32f), 0.25f);
        mQueso = Lit("Queso", new Color(1f, 0.78f, 0.22f), 0.45f, 0f, new Color(0.06f, 0.045f, 0.01f));
        mPan = Lit("Pan", new Color(0.72f, 0.45f, 0.2f), 0.25f);
        mMiga = Lit("Miga", new Color(0.92f, 0.82f, 0.6f), 0.2f);
        mTapaRoja = Lit("TapaRoja", new Color(0.75f, 0.1f, 0.08f), 0.7f, 0.7f);
        mTapaAzul = Lit("TapaAzul", new Color(0.12f, 0.25f, 0.7f), 0.7f, 0.7f);
        mBotella = Lit("Botella", new Color(0.25f, 0.55f, 0.35f), 0.92f, 0f, new Color(0.01f, 0.03f, 0.015f));
        mLata = Lit("Lata", new Color(0.85f, 0.83f, 0.8f), 0.45f, 0.3f, default, Tex("rusty_metal_02_diff"), null, new Vector2(2f, 1f));
        mPlato = Lit("Plato", new Color(0.82f, 0.8f, 0.74f), 0.55f);
        mOro = Lit("Oro", new Color(1f, 0.76f, 0.25f), 0.6f, 0.45f, new Color(0.45f, 0.3f, 0.05f));
        mRubi = Lit("Rubi", new Color(0.9f, 0.1f, 0.12f), 0.9f, 0f, new Color(1.2f, 0.08f, 0.08f));
        mComida = Lit("Comida", new Color(0.95f, 0.72f, 0.35f), 0.3f);
        mRemolino = Lit("Remolino", new Color(0.2f, 0.28f, 0.12f), 0.95f, 0f, new Color(0.08f, 0.12f, 0.04f));
        var follaje = Shader.Find("Exterior/Follaje");
        mHoja = GuardarMaterial("Hoja", follaje, m => { m.SetColor("_BaseColor", new Color(0.32f, 0.52f, 0.2f)); m.SetFloat("_Viento", 0f); m.SetFloat("_AlphaClip", 0f); m.DisableKeyword("_ALPHATEST_ON"); m.SetFloat("_VariacionColor", 0.45f); m.SetFloat("_FadeInicio", 0.5f); m.SetFloat("_FadeFin", 1.5f); });
        mHojaSeca = GuardarMaterial("HojaSeca", follaje, m => { m.SetColor("_BaseColor", new Color(0.58f, 0.45f, 0.2f)); m.SetFloat("_Viento", 0f); m.SetFloat("_AlphaClip", 0f); m.DisableKeyword("_ALPHATEST_ON"); m.SetFloat("_VariacionColor", 0.5f); m.SetFloat("_FadeInicio", 0.5f); m.SetFloat("_FadeFin", 1.5f); });
    }

    // ================================================================== rejillas
    static void CrearAlcantarilla(Transform padre, List<Renderer> haces, List<Light> focos)
    {
        var t = new GameObject("Alcantarilla").transform; t.SetParent(padre, false);
        t.position = O;
        float y = 0.22f;
        // aro de hierro, barras y collar de concreto roto
        var aro = Torno("AroRejilla", new[] { new Vector2(6.75f, y - 0.6f), new Vector2(6.75f, y - 0.05f), new Vector2(6.6f, y + 0.08f), new Vector2(5.4f, y + 0.08f), new Vector2(5.3f, y), new Vector2(5.3f, y - 0.5f) }, 48);
        Objeto("Aro", t, Guardar(aro, "AroRejilla"), mHierro, false);
        var partes = new List<(Mesh, Matrix4x4)>();
        var barra = Caja("Barra", new Vector3(0.38f, 0.42f, 1f));
        for (float x = -4.8f; x <= 4.81f; x += 1.2f)
        {
            float largo = 2f * Mathf.Sqrt(Mathf.Max(0.1f, 5.45f * 5.45f - x * x));
            partes.Add((barra, Matrix4x4.TRS(new Vector3(x, y - 0.18f, 0f), Quaternion.identity, new Vector3(1f, 1f, largo))));
        }
        foreach (float z in new[] { -2.6f, 0f, 2.6f })
            partes.Add((barra, Matrix4x4.TRS(new Vector3(0f, y - 0.32f, z), Quaternion.identity, new Vector3(27f, 0.6f, 0.6f))));
        Objeto("Rejilla", t, Guardar(Combinar("Rejilla", partes.ToArray()), "Rejilla"), mHierro, false);
        // collar de concreto agrietado (le falta un pedazo)
        var collar = Torno("Collar", new[] { new Vector2(9.8f, -0.6f), new Vector2(9.2f, y + 0.05f), new Vector2(7.2f, y + 0.2f), new Vector2(6.7f, y + 0.05f) }, 40, a => 1f + Mathf.PerlinNoise(a * 2f, 3.3f) * 0.08f);
        Objeto("Collar", t, Guardar(collar, "Collar"), mConcreto, false);
        // se camina encima (un solo colisionador plano)
        var bc = t.gameObject.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, y - 0.2f, 0f); bc.size = new Vector3(13.4f, 0.4f, 13.4f);
        // haz de luz hacia el pozo y foco que sigue al sol
        HazYFoco(t, O + Vector3.up * y, 5.3f, (Y0 - 0.9f) - y, haces, focos, 650f);
    }

    static void CrearRejillaRey(Transform padre, List<Renderer> haces, List<Light> focos)
    {
        var c = L(LRey); float s = Suelo(c.x, c.z);
        var t = new GameObject("RejillaRey").transform; t.SetParent(padre, false);
        t.position = new Vector3(c.x, s, c.z);
        float y = 0.18f;
        var partes = new List<(Mesh, Matrix4x4)>();
        var barra = Caja("BarraR", new Vector3(1f, 1f, 1f));
        // marco cuadrado
        foreach (var (pos, esc) in new[] { (new Vector3(0f, y - 0.25f, 4.3f), new Vector3(9.4f, 0.5f, 0.8f)), (new Vector3(0f, y - 0.25f, -4.3f), new Vector3(9.4f, 0.5f, 0.8f)),
                                           (new Vector3(4.3f, y - 0.25f, 0f), new Vector3(0.8f, 0.5f, 9.4f)), (new Vector3(-4.3f, y - 0.25f, 0f), new Vector3(0.8f, 0.5f, 9.4f)) })
            partes.Add((barra, Matrix4x4.TRS(pos, Quaternion.identity, esc)));
        for (float x = -3.2f; x <= 3.21f; x += 1.07f) partes.Add((barra, Matrix4x4.TRS(new Vector3(x, y - 0.25f, 0f), Quaternion.identity, new Vector3(0.36f, 0.45f, 8f))));
        Objeto("Rejilla", t, Guardar(Combinar("RejillaRey", partes.ToArray()), "RejillaRey"), mHierro, false);
        var bc = t.gameObject.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, y - 0.25f, 0f); bc.size = new Vector3(9.4f, 0.5f, 9.4f);
        HazYFoco(t, t.position + Vector3.up * y, 3.8f, (O.y + YA - 0.9f) - (s + y), haces, focos, 900f, true);
    }

    static void HazYFoco(Transform padre, Vector3 arriba, float radio, float alto, List<Renderer> haces, List<Light> focos, float intensidad, bool cuadrado = false)
    {
        // cono abierto: arriba del radio de la rejilla, abajo un poco mas ancho
        int lados = 24;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        for (int s = 0; s <= lados; s++)
        {
            float a = s / (float)lados * Mathf.PI * 2f;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            if (cuadrado) d /= Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z));
            v.Add(d * radio * 0.92f); uv.Add(new Vector2(s / (float)lados, 0f));
            v.Add(d * radio * 1.25f + Vector3.up * alto); uv.Add(new Vector2(s / (float)lados, 1f));
        }
        for (int s = 0; s < lados; s++) { int a = s * 2; tri.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 }); }
        var m = new Mesh { name = "Haz" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
        var haz = Objeto("Haz", padre, Guardar(m, "Haz_" + (contador++)), mHaz, false, false);
        haz.transform.position = arriba;
        haces.Add(haz.GetComponent<Renderer>());
        var l = new GameObject("FocoSol").AddComponent<Light>();
        l.transform.SetParent(padre, false);
        l.transform.SetPositionAndRotation(arriba + Vector3.down * 1.6f, Quaternion.LookRotation(Vector3.down));   // bajo las barras: no las quema
        l.type = LightType.Spot; l.spotAngle = 58f; l.innerSpotAngle = 30f; l.range = Mathf.Abs(alto) + 14f; l.intensity = intensidad;
        l.color = new Color(1f, 0.93f, 0.78f); l.shadows = LightShadows.Soft;
        focos.Add(l);
    }

    // ================================================================== tuberias en las paredes
    static void CrearTuberias(Transform padre)
    {
        var t = new GameObject("Tuberias").transform; t.SetParent(padre, false);
        var tubo = Torno("Tubo", new[] { new Vector2(0.85f, 0f), new Vector2(0.85f, 1f) }, 16);
        var brida = Torno("Brida", new[] { new Vector2(0.85f, -0.15f), new Vector2(1.15f, -0.15f), new Vector2(1.15f, 0.15f), new Vector2(0.85f, 0.15f) }, 16);
        tubo = Guardar(tubo, "Tubo"); brida = Guardar(brida, "Brida");
        int n = 0;
        foreach (var (tn, lado) in new[] { (t1, 1f), (t2, -1f), (t2, 1f), (t3, 1f), (t4, -1f) })
        {
            float ini = tn == t1 ? 8f : 4f, fin = tn.largo - 4f;
            if (tn == t3) { ini = RNido + 2f; fin = tn.largo - RCruce - 2f; }
            if (tn == t2) { ini = RPozo + 1f; fin = tn.largo - RCruce - 1f; }
            if (fin - ini < 6f) continue;
            float alto = lado > 0 ? 6.5f : 5f;
            Vector3 p0 = tn.a + tn.dir * ini + tn.lado * lado * (W - 1.3f), p1 = tn.a + tn.dir * fin + tn.lado * lado * (W - 1.3f);
            p0.y = Mathf.Lerp(tn.ya, tn.yb, Mathf.Clamp01(ini / tn.largo)) + alto; p1.y = Mathf.Lerp(tn.ya, tn.yb, Mathf.Clamp01(fin / tn.largo)) + alto;
            var go = Objeto("Tuberia_" + (n++), t, tubo, mOxido, false);
            go.transform.position = p0;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, p1 - p0);
            go.transform.localScale = new Vector3(1f, (p1 - p0).magnitude, 1f);
            for (float s = 0f; s < (p1 - p0).magnitude; s += 12f)
            {
                var b = Objeto("Brida", go.transform.parent, brida, mOxido, false);
                b.transform.position = Vector3.Lerp(p0, p1, s / (p1 - p0).magnitude);
                b.transform.rotation = go.transform.rotation;
            }
        }
        // tuberias de las crias en el Gran Colector: un aro oxidado en cada boca
        var aro = Guardar(Torno("AroTubo", new[] { new Vector2(2.4f, -0.4f), new Vector2(3.2f, -0.4f), new Vector2(3.2f, 0.4f), new Vector2(2.4f, 0.4f), new Vector2(2.4f, -0.4f) }, 24), "AroTubo");
        foreach (float ang in new[] { 90f, 150f, 210f, 270f })
        {
            float a = ang * Mathf.Deg2Rad; Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var go = Objeto("BocaTubo", t, aro, mHierro, false);
            go.transform.position = L(LRey) + d * (RRey - 2.2f) + Vector3.up * 2.6f;
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, d);
        }
    }

    // ================================================================== agua
    static void CrearAgua(Transform padre, List<Cloaca.Agua> aguas)
    {
        var t = new GameObject("Agua").transform; t.SetParent(padre, false);
        float hw = AnchoCanal(-AguaBajo) + 0.25f;
        // canales: de donde nace el agua hacia donde corre
        void Canal(string nombre, Tunel tn, float s0, float s1, bool alReves, float corriente)
        {
            Vector3 a = tn.a + tn.dir * s0, b = tn.a + tn.dir * s1;
            a.y = Mathf.Lerp(tn.ya, tn.yb, Mathf.Clamp01(s0 / tn.largo)) - AguaBajo;
            b.y = Mathf.Lerp(tn.ya, tn.yb, Mathf.Clamp01(s1 / tn.largo)) - AguaBajo;
            if (alReves) { var x = a; a = b; b = x; }
            var go = Objeto(nombre, t, Guardar(Tira(nombre, a, b, hw, corriente, tn), nombre), mAgua, false, false);
            go.GetComponent<MeshRenderer>().receiveShadows = true;
            aguas.Add(new Cloaca.Agua { a = a, b = b, medioAncho = hw - 0.3f, corriente = corriente });
        }
        Canal("Canal_Entrada", t1, 6f, t1.largo, false, 1.6f);
        Canal("Canal_Principal", t2, 0f, t2.largo, false, 2f);
        Canal("Canal_Nido", t3, RNido - 2f, t3.largo, false, 1.6f);
        // el del Gran Colector baja: corre mas
        Canal("Canal_Bajada", t4, -RCruce, 0f, false, 2f);
        Canal("Canal_Rampa", t4, 0f, t4.largo, false, 3.2f);
        Canal("Canal_Colector", t4, t4.largo, t4.largo + RRey - RPilaRey + 1.5f, false, 2.6f);
        // estanques
        void Estanque(string nombre, Vector3 c, float rp)
        {
            float r = rp - AguaBajo / CanalHondo * (CanalArriba - CanalAbajo) + 0.25f;
            Vector3 cen = c + Vector3.down * (AguaBajo - 0.02f);
            var go = Objeto(nombre, t, Guardar(Disco(nombre, cen, r), nombre), mAgua, false, false);
            go.GetComponent<MeshRenderer>().receiveShadows = true;
            aguas.Add(new Cloaca.Agua { a = cen, circulo = true, radio = r - 0.3f });
        }
        Estanque("Estanque_Pozo", L(LPozo), 7f);
        Estanque("Estanque_Cruce", L(LCruce), 7f);
        Estanque("Estanque_Rey", L(LRey), RPilaRey);
    }

    static Color LuzAgua(Vector3 p, float corriente)
    {
        Color c = Hornear(p, Vector3.up);
        c.a = Mathf.Clamp01(corriente / 2.2f);
        return c;
    }

    // tira de agua a lo largo de un canal; UV en unidades del mundo con V a favor de la corriente
    static Mesh Tira(string nombre, Vector3 a, Vector3 b, float hw, float corriente, Tunel tn)
    {
        Vector3 d = b - a; float largo = new Vector2(d.x, d.z).magnitude;
        Vector3 dir = new Vector3(d.x, 0f, d.z) / largo, lado = new Vector3(dir.z, 0f, -dir.x);
        int pasos = Mathf.Max(2, Mathf.CeilToInt(largo / 3f));
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var col = new List<Color>(); var tri = new List<int>();
        for (int i = 0; i <= pasos; i++)
        {
            float s = i / (float)pasos;
            Vector3 c = Vector3.Lerp(a, b, s);
            foreach (float k in new[] { -1f, 0f, 1f })
            {
                Vector3 p = c + lado * hw * k;
                v.Add(p);
                uv.Add(new Vector2(k * hw, s * largo + Vector3.Dot(a, dir)));
                uv2.Add(new Vector2(Exposicion(p), 0f));
                col.Add(LuzAgua(p, corriente));
            }
        }
        for (int i = 0; i < pasos; i++)
            for (int k = 0; k < 2; k++)
            {
                int q = i * 3 + k;
                tri.AddRange(new[] { q, q + 3, q + 1, q + 1, q + 3, q + 4 });
            }
        var m = new Mesh { name = nombre };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetColors(col); m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        // que miren hacia arriba
        var n = m.normals; bool abajo = n.Length > 0 && n[0].y < 0f;
        if (abajo) { for (int i = 0; i < tri.Count; i += 3) { int x = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = x; } m.SetTriangles(tri, 0); m.RecalculateNormals(); }
        m.RecalculateBounds();
        return m;
    }

    static Mesh Disco(string nombre, Vector3 c, float r)
    {
        int lados = 48, anillos = 6;
        var v = new List<Vector3> { c }; var uv = new List<Vector2> { new Vector2(c.x, c.z) }; var uv2 = new List<Vector2> { new Vector2(Exposicion(c), 0f) };
        var col = new List<Color> { LuzAgua(c, 0f) }; var tri = new List<int>();
        for (int k = 1; k <= anillos; k++)
            for (int s = 0; s < lados; s++)
            {
                float a = s / (float)lados * Mathf.PI * 2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r * k / anillos;
                v.Add(p); uv.Add(new Vector2(p.x, p.z)); uv2.Add(new Vector2(Exposicion(p), 0f)); col.Add(LuzAgua(p, 0f));
            }
        for (int s = 0; s < lados; s++) tri.AddRange(new[] { 0, 1 + (s + 1) % lados, 1 + s });
        for (int k = 1; k < anillos; k++)
            for (int s = 0; s < lados; s++)
            {
                int a0 = 1 + (k - 1) * lados + s, a1 = 1 + (k - 1) * lados + (s + 1) % lados, b0 = a0 + lados, b1 = a1 + lados;
                tri.AddRange(new[] { a0, a1, b0, b0, a1, b1 });
            }
        var m = new Mesh { name = nombre };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(1, uv2); m.SetColors(col); m.SetTriangles(tri, 0);
        m.RecalculateNormals();
        if (m.normals[0].y < 0f) { for (int i = 0; i < tri.Count; i += 3) { int x = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = x; } m.SetTriangles(tri, 0); m.RecalculateNormals(); }
        m.RecalculateBounds();
        return m;
    }

    // ================================================================== moho luminoso
    struct Mancha { public Vector3 pos, normal; public Color color; }

    static List<Mancha> ColocarMoho()
    {
        var res = new List<Mancha>();
        var colores = new[] { new Color(0.45f, 1f, 0.35f), new Color(0.3f, 0.95f, 0.75f), new Color(0.65f, 1f, 0.3f) };
        // a lo largo de los tuneles, alternando paredes, cerca del anden
        foreach (var tn in new[] { t1, t2, t3, t4 })
        {
            float lado = 1f;
            for (float s = 6f; s < tn.largo - 4f; s += R(12f, 18f))
            {
                lado = -lado;
                Vector3 p = tn.a + tn.dir * s + tn.lado * lado * 30f;
                p.y = Mathf.Lerp(tn.ya, tn.yb, s / tn.largo) + R(1.2f, 4f);
                Vector3 desde = p - tn.lado * lado * 28f;
                if (Physics.Raycast(desde, (p - desde).normalized, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore))
                    res.Add(new Mancha { pos = hit.point, normal = hit.normal, color = colores[rnd.Next(colores.Length)] });
            }
        }
        // paredes de las salas
        foreach (var (s, cuantas) in new[] { (sPozo, 5), (sCruce, 5), (sRey, 12) })
            for (int i = 0; i < cuantas; i++)
            {
                float a = (i + R(0.2f, 0.8f)) / cuantas * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 desde = s.c + Vector3.up * R(1.5f, 5f);
                if (Physics.Raycast(desde, d, out var hit, s.r + 6f, ~0, QueryTriggerInteraction.Ignore))
                    res.Add(new Mancha { pos = hit.point, normal = hit.normal, color = colores[rnd.Next(colores.Length)] });
            }
        return res;
    }

    // ================================================================== instancias
    static void Apilar(List<Matrix4x4> lista, Vector3 p, float escala, bool tumbar = true, float hundir = 0f)
    {
        var q = Quaternion.Euler(tumbar ? R(-25f, 25f) : 0f, R(0f, 360f), tumbar ? R(-25f, 25f) : 0f);
        lista.Add(Matrix4x4.TRS(p + Vector3.down * hundir, q, Vector3.one * escala));
    }

    static Vector3 PuntoEnSala(Sala s, float frac)
    {
        var c = new Vector2(R(-1f, 1f), R(-1f, 1f)); if (c.sqrMagnitude > 1f) c.Normalize();
        return s.c + new Vector3(c.x * s.r * frac, 0f, c.y * s.r * frac);
    }

    static Vector3 PuntoEnAnden(Tunel tn)
    {
        float s = R(2f, tn.largo - 2f), u = R(CanalArriba + 1f, W - 1f) * (rnd.NextDouble() < 0.5 ? -1f : 1f);
        Vector3 p = tn.a + tn.dir * s + tn.lado * u;
        p.y = Mathf.Lerp(tn.ya, tn.yb, s / tn.largo);
        return p;
    }

    static void CrearInstancias(List<Hormiguero.GrupoInstancias> grupos, List<Mancha> moho, List<(Vector3 pos, Vector3 normal)> hongos, Vector3 posTrono)
    {
        void Grupo(string nombre, Mesh malla, Material mat, List<Matrix4x4> m, bool sombras = false)
        {
            if (m.Count == 0) return;
            grupos.Add(new Hormiguero.GrupoInstancias { nombre = nombre, malla = malla, material = mat, matrices = m.ToArray(), sombras = sombras });
        }
        // moho luminoso: manchas de gotas aplastadas sobre la pared
        var mGota = Guardar(Bola("GotaMoho", 1f, new Vector3(1f, 0.32f, 1f), 0.6f, 3), "GotaMoho");
        var lMoho = new List<Matrix4x4>();
        foreach (var mm in moho)
        {
            Quaternion q0 = Quaternion.FromToRotation(Vector3.up, mm.normal);
            Vector3 t1v = Vector3.Cross(mm.normal, Vector3.up); if (t1v.sqrMagnitude < 0.01f) t1v = Vector3.right; t1v.Normalize();
            Vector3 t2v = Vector3.Cross(mm.normal, t1v);
            int n = rnd.Next(7, 13);
            for (int i = 0; i < n; i++)
            {
                Vector2 o = Random2() * 2.6f;
                Vector3 p = mm.pos + t1v * o.x + t2v * o.y * 1.4f + Vector3.down * Mathf.Abs(o.y) * 0.3f;
                lMoho.Add(Matrix4x4.TRS(p - mm.normal * 0.1f, q0 * Quaternion.Euler(0f, R(0f, 360f), 0f), new Vector3(R(0.35f, 1f), R(0.5f, 1f), R(0.35f, 1f))));
            }
        }
        Grupo("Moho", mGota, mMoho, lMoho);

        // hongos naranjas del nido
        var mSombrero = Guardar(Torno("SombreroNido", new[] { new Vector2(0.1f, 0.45f), new Vector2(0.85f, 0.38f), new Vector2(1f, 0.45f), new Vector2(0.7f, 0.75f), new Vector2(0f, 0.9f) }, 14), "SombreroNido");
        var mTallo2 = Guardar(Torno("TalloNido", new[] { new Vector2(0.16f, 0f), new Vector2(0.13f, 0.5f), new Vector2(0.1f, 0.5f) }, 8), "TalloNido");
        var lSomb = new List<Matrix4x4>(); var lTallo = new List<Matrix4x4>();
        foreach (var (pos, nrm) in hongos)
        {
            for (int i = 0; i < 7; i++)
            {
                Vector3 p = pos + new Vector3(R(-1.6f, 1.6f), R(-1f, 1f), R(-1.6f, 1.6f));
                if (Physics.Raycast(p + nrm * 3f, -nrm, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
                var q = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(nrm, Vector3.up, 0.5f));
                float e = R(0.8f, 2f);
                lSomb.Add(Matrix4x4.TRS(p, q, Vector3.one * e)); lTallo.Add(Matrix4x4.TRS(p, q, Vector3.one * e));
            }
        }
        Grupo("HongoNido", mSombrero, mHongo, lSomb);
        Grupo("TalloNido", mTallo2, mTallo, lTallo);

        // escombros: ladrillos y trozos de concreto en el derrumbe y en la boca
        var mLadr = Guardar(Caja("LadrilloSuelto", new Vector3(1.3f, 0.55f, 0.65f), 0.12f, 7), "LadrilloSuelto");
        var mTrozo = Guardar(Caja("TrozoConcreto", new Vector3(1.8f, 0.9f, 1.4f), 0.5f, 11), "TrozoConcreto");
        var lLadr = new List<Matrix4x4>(); var lTrozo = new List<Matrix4x4>();
        for (int i = 0; i < 260; i++)
        {
            float lx = Mathf.Lerp(XBoca + 8f, XArriba + 8f, Mathf.Pow((float)rnd.NextDouble(), 1.6f));
            Vector3 p = PisoEn(L(lx, derrumbe.PisoLocal(lx) + 2f, R(-7f, 7f)), 6f, 20f);
            if (i % 4 == 0) Apilar(lTrozo, p, R(0.6f, 1.6f), true, 0.2f); else Apilar(lLadr, p, R(0.8f, 1.3f), true, 0.1f);
        }
        // y desperdigados por los andenes
        foreach (var tn in new[] { t1, t2, t3, t4 })
            for (int i = 0; i < (int)(tn.largo / 6f); i++) Apilar(lLadr, PisoEn(PuntoEnAnden(tn) + Vector3.up * 2f), R(0.8f, 1.2f), true, 0.1f);
        Grupo("Ladrillos", mLadr, mLadrilloSuelto, lLadr, true);
        Grupo("Trozos", mTrozo, mConcreto, lTrozo, true);

        // basura: tapas de botella, papeles arrugados, latas y botellas
        var mTapa = Guardar(Torno("Tapa", new[] { new Vector2(0f, 0.24f), new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0f), new Vector2(0.55f, 0f), new Vector2(0.55f, 0.28f), new Vector2(0.48f, 0.32f), new Vector2(0f, 0.32f) }, 21, a => 1f + 0.06f * Mathf.Sign(Mathf.Sin(a * 21f))), "Tapa");
        var mBola = Guardar(Bola("PapelArrugado", 0.8f, Vector3.one, 0.9f, 5), "PapelArrugado");
        var mLata = Guardar(Torno("Lata", new[] { new Vector2(0f, 0f), new Vector2(0.92f, 0f), new Vector2(0.95f, 0.1f), new Vector2(0.95f, 2.1f), new Vector2(0.9f, 2.2f), new Vector2(0.75f, 2.2f), new Vector2(0.72f, 2.12f), new Vector2(0f, 2.12f) }, 20), "Lata");
        var mBot = Guardar(Torno("Botella", new[] { new Vector2(0f, 0f), new Vector2(1.15f, 0.05f), new Vector2(1.2f, 0.3f), new Vector2(1.2f, 2.4f), new Vector2(0.95f, 2.9f), new Vector2(0.45f, 3.3f), new Vector2(0.42f, 3.7f), new Vector2(0f, 3.7f) }, 18), "Botella");
        var lTapR = new List<Matrix4x4>(); var lTapA = new List<Matrix4x4>(); var lBola = new List<Matrix4x4>(); var lLata = new List<Matrix4x4>(); var lBot = new List<Matrix4x4>();
        foreach (var tn in new[] { t1, t2, t3, t4 })
            for (int i = 0; i < (int)(tn.largo / 4f); i++)
            {
                var p = PisoEn(PuntoEnAnden(tn) + Vector3.up * 2f);
                switch (rnd.Next(6))
                {
                    case 0: Apilar(lTapR, p, R(0.9f, 1.1f), true); break;
                    case 1: Apilar(lTapA, p, R(0.9f, 1.1f), true); break;
                    case 2: case 3: Apilar(lBola, p, R(0.6f, 1.1f), true, 0.2f); break;
                    case 4: lLata.Add(Matrix4x4.TRS(p + Vector3.up * 0.95f, Quaternion.Euler(R(-5f, 5f), R(0f, 360f), 90f), Vector3.one)); break;
                    default: lBot.Add(Matrix4x4.TRS(p + Vector3.up * 1.15f, Quaternion.Euler(0f, R(0f, 360f), 90f), Vector3.one)); break;
                }
            }
        // el gran colector esta lleno de basura (la junta el rey)
        for (int i = 0; i < 120; i++)
        {
            float a = R(0f, Mathf.PI * 2f), r = R(RPilaRey + 1.5f, RRey - 2f);
            var p = PisoEn(L(LRey) + new Vector3(Mathf.Cos(a) * r, 3f, Mathf.Sin(a) * r), 4f, 10f);
            int k = rnd.Next(5);
            if (k == 0) Apilar(lTapR, p, 1f); else if (k == 1) Apilar(lTapA, p, 1f); else if (k == 2) Apilar(lBola, p, R(0.7f, 1.2f), true, 0.2f);
            else lLata.Add(Matrix4x4.TRS(p + Vector3.up * 0.95f, Quaternion.Euler(R(-5f, 5f), R(0f, 360f), 90f), Vector3.one));
        }
        // la pila del trono
        for (int i = 0; i < 160; i++)
        {
            float a = R(-1.4f, 1.4f), r = R(0f, 8f);
            Vector3 p = posTrono + new Vector3(Mathf.Cos(a) * r + 2f, 0f, Mathf.Sin(a) * r * 1.4f);
            float alto = Mathf.Max(0f, 4.5f - r * 0.55f) * R(0.4f, 1f);
            p = PisoEn(p + Vector3.up * 3f, 4f, 10f) + Vector3.up * alto;
            int k = rnd.Next(6);
            if (k == 0) Apilar(lTapR, p, 1.1f); else if (k == 1) Apilar(lTapA, p, 1.1f); else if (k <= 3) Apilar(lBola, p, R(0.9f, 1.4f));
            else if (k == 4) lLata.Add(Matrix4x4.TRS(p, Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f)), Vector3.one));
            else lBot.Add(Matrix4x4.TRS(p, Quaternion.Euler(R(0f, 360f), R(0f, 360f), 90f), Vector3.one));
        }
        Grupo("TapasRojas", mTapa, mTapaRoja, lTapR); Grupo("TapasAzules", mTapa, mTapaAzul, lTapA);
        Grupo("Papeles", mBola, mPapel, lBola, true); Grupo("Latas", mLata, mLata_(), lLata, true); Grupo("Botellas", mBot, mBotella, lBot, true);

        // nido: cama de papel triturado y paja, migas y comida robada
        var mTira = Guardar(Caja("TiraPapel", new Vector3(0.3f, 0.04f, 2.2f)), "TiraPapel");
        var mPajita = Guardar(Caja("Pajita", new Vector3(0.12f, 0.08f, 2.6f)), "Pajita");
        var lTiraB = new List<Matrix4x4>(); var lTiraG = new List<Matrix4x4>(); var lPaja = new List<Matrix4x4>();
        // camas: montoncitos de papel triturado y paja junto a las paredes (donde duermen)
        for (int c = 0; c < 4; c++)
        {
            float a = c * Mathf.PI * 0.5f + 0.2f;
            Vector3 cama = sNido.c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (sNido.r - 7f);
            for (int i = 0; i < 110; i++)
            {
                Vector2 o = Random2() * 4.5f;
                Vector3 p = cama + new Vector3(o.x, 0f, o.y);
                float alto = Mathf.Max(0f, 0.7f - o.magnitude / 4.5f * 0.7f) * R(0.3f, 1f);
                p = PisoEn(p + Vector3.up * 3f) + Vector3.up * alto;
                var m = Matrix4x4.TRS(p, Quaternion.Euler(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), new Vector3(R(0.6f, 1f), 1f, R(0.4f, 0.7f)));
                int k = rnd.Next(3); if (k == 0) lTiraB.Add(m); else if (k == 1) lTiraG.Add(m); else lPaja.Add(m);
            }
        }
        // sueltas por el piso
        for (int i = 0; i < 90; i++)
        {
            var p = PisoEn(PuntoEnSala(sNido, 0.85f) + Vector3.up * 3f) + Vector3.up * 0.03f;
            var m = Matrix4x4.TRS(p, Quaternion.Euler(R(-4f, 4f), R(0f, 360f), R(-4f, 4f)), new Vector3(R(0.6f, 1f), 1f, R(0.4f, 0.7f)));
            int k = rnd.Next(3); if (k == 0) lTiraB.Add(m); else if (k == 1) lTiraG.Add(m); else lPaja.Add(m);
        }
        Grupo("TirasPapel", mTira, mPapel, lTiraB); Grupo("TirasPeriodico", mTira, mPeriodico, lTiraG); Grupo("Paja", mPajita, mPaja, lPaja);
        var mQueso = Guardar(Queso(), "Queso");
        var mPan = Guardar(Bola("Pan", 1f, new Vector3(1.4f, 0.55f, 0.9f), 0.35f, 9), "Pan");
        var mMiga = Guardar(Bola("Miga", 0.3f, Vector3.one, 0.8f, 13), "Miga");
        var lQueso = new List<Matrix4x4>(); var lPan = new List<Matrix4x4>(); var lMiga = new List<Matrix4x4>();
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + 0.8f;
            Vector3 c = PisoEn(sNido.c + new Vector3(Mathf.Cos(a) * 12f, 3f, Mathf.Sin(a) * 12f));
            for (int j = 0; j < 4; j++) Apilar(lQueso, PisoEn(c + new Vector3(R(-2f, 2f), 3f, R(-2f, 2f))) + Vector3.up * R(0f, 0.6f), R(0.9f, 1.4f));
            for (int j = 0; j < 3; j++) Apilar(lPan, PisoEn(c + new Vector3(R(-2.5f, 2.5f), 3f, R(-2.5f, 2.5f))) + Vector3.up * 0.3f, R(0.9f, 1.5f));
            for (int j = 0; j < 40; j++) Apilar(lMiga, PisoEn(c + new Vector3(R(-4f, 4f), 3f, R(-4f, 4f))), R(0.6f, 1.4f));
        }
        // migas por el camino de las forrajeras (rastro hasta el derrumbe)
        foreach (var tn in new[] { t3, t2, t1 })
            for (int i = 0; i < (int)(tn.largo / 1.5f); i++) Apilar(lMiga, PisoEn(PuntoEnAnden(tn) + Vector3.up * 2f), R(0.5f, 1f));
        Grupo("Queso", mQueso, mQueso_(), lQueso, true); Grupo("Pan", mPan, mPan_(), lPan, true); Grupo("Migas", mMiga, mMiga_(), lMiga);

        // hojas caidas por la rejilla y en el derrumbe
        var mHojaM = Guardar(Hoja(), "Hoja");
        var lHoja = new List<Matrix4x4>(); var lHojaSeca = new List<Matrix4x4>();
        for (int i = 0; i < 120; i++)
        {
            Vector2 c = Random2() * 9f;
            var p = PisoEn(L(c.x, Y0 + 3f, c.y), 4f, 10f);
            Apilar(rnd.NextDouble() < 0.5 ? lHoja : lHojaSeca, p + Vector3.up * 0.05f, R(1.2f, 2f), true);
        }
        for (int i = 0; i < 200; i++)
        {
            float lx = R(XArriba, XBoca + 4f);
            var p = PisoEn(L(lx, derrumbe.PisoLocal(lx) + 3f, R(-7f, 7f)), 6f, 20f);
            Apilar(rnd.NextDouble() < 0.6 ? lHojaSeca : lHoja, p + Vector3.up * 0.05f, R(1.2f, 2.2f), true);
        }
        Grupo("Hojas", mHojaM, mHoja, lHoja); Grupo("HojasSecas", mHojaM, mHojaSeca, lHojaSeca);
    }

    static Material mLata_() { return mLata; }
    static Material mQueso_() { return mQueso; }
    static Material mPan_() { return mPan; }
    static Material mMiga_() { return mMiga; }

    static Vector2 Random2() { var c = new Vector2(R(-1f, 1f), R(-1f, 1f)); if (c.sqrMagnitude > 1f) c.Normalize(); return c; }

    static Mesh Queso()
    {
        // cuña de queso: prisma triangular
        var v = new List<Vector3>(); var t = new List<int>();
        Vector3[] baseT = { new Vector3(0f, 0f, 0f), new Vector3(1.4f, 0f, -0.6f), new Vector3(1.4f, 0f, 0.6f) };
        void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int i = v.Count; v.AddRange(new[] { a, b, c, d }); t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
        void T3(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.AddRange(new[] { a, b, c }); t.AddRange(new[] { i, i + 1, i + 2 }); }
        Vector3 up = Vector3.up * 0.9f;
        T3(baseT[0], baseT[2], baseT[1]);
        T3(baseT[0] + up, baseT[1] + up, baseT[2] + up);
        Q(baseT[0], baseT[1], baseT[1] + up, baseT[0] + up);
        Q(baseT[1], baseT[2], baseT[2] + up, baseT[1] + up);
        Q(baseT[2], baseT[0], baseT[0] + up, baseT[2] + up);
        for (int i = 0; i < t.Count; i += 3) { int x = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = x; }   // caras hacia fuera
        var m = new Mesh { name = "Queso" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Mesh Hoja()
    {
        var v = new List<Vector3>(); var t = new List<int>();
        int n = 6;
        for (int i = 0; i <= n; i++)
        {
            float s = i / (float)n, w = Mathf.Sin(s * Mathf.PI) * 0.45f, z = s * 1.6f - 0.8f, curva = Mathf.Sin(s * Mathf.PI) * 0.08f;
            v.Add(new Vector3(-w, curva + w * 0.25f, z)); v.Add(new Vector3(0f, curva, z)); v.Add(new Vector3(w, curva + w * 0.25f, z));
        }
        for (int i = 0; i < n; i++) for (int k = 0; k < 2; k++) { int q = i * 3 + k; t.AddRange(new[] { q, q + 3, q + 1, q + 1, q + 3, q + 4 }); t.AddRange(new[] { q, q + 1, q + 3, q + 1, q + 4, q + 3 }); }
        var m = new Mesh { name = "Hoja" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // ================================================================== trono y comida del rey
    static void CrearTrono(Transform padre, Vector3 posTrono)
    {
        var t = new GameObject("Trono").transform; t.SetParent(padre, false);
        // un plato roto de respaldo y una tapa de frasco gigante como asiento
        var plato = Guardar(Torno("Plato", new[] { new Vector2(0f, 0f), new Vector2(3.2f, 0f), new Vector2(4.4f, 0.55f), new Vector2(4.6f, 0.65f), new Vector2(4.5f, 0.75f), new Vector2(3.1f, 0.2f), new Vector2(0f, 0.2f) }, 40,
            a => a > 5.2f ? 0.82f + 0.18f * Mathf.Abs(Mathf.Sin(a * 9f)) : 1f), "Plato");
        var go = Objeto("Respaldo", t, plato, mPlato, true);
        Vector3 haciaCentro = (L(LRey) - posTrono); haciaCentro.y = 0f; haciaCentro.Normalize();
        go.transform.position = PisoEn(posTrono + Vector3.up * 3f) - haciaCentro * 3.2f + Vector3.up * 4.2f;
        go.transform.rotation = Quaternion.LookRotation(Vector3.up, -haciaCentro) * Quaternion.Euler(-8f, 0f, 0f);
        go.transform.rotation = Quaternion.FromToRotation(Vector3.up, haciaCentro + Vector3.up * 0.25f);
        var tapa = Guardar(Torno("TapaFrasco", new[] { new Vector2(2.6f, 0f), new Vector2(2.8f, 0f), new Vector2(2.8f, 1f), new Vector2(2.6f, 1.1f), new Vector2(0f, 1.1f) }, 40, a => 1f + 0.015f * Mathf.Sign(Mathf.Sin(a * 60f))), "TapaFrasco");
        var asiento = Objeto("Asiento", t, tapa, mTapaRoja, true);
        asiento.transform.position = PisoEn(posTrono + Vector3.up * 3f) + haciaCentro * 0.5f;
        // cristales rojos (vidrio roto) que dan el brillo del trono
        var cristal = Guardar(Torno("Cristal", new[] { new Vector2(0f, 0f), new Vector2(0.35f, 0.1f), new Vector2(0.12f, 1.4f), new Vector2(0f, 1.5f) }, 5), "Cristal");
        for (int i = 0; i < 9; i++)
        {
            var c = Objeto("Cristal_" + i, t, cristal, mRubi, false);
            Vector3 p = PisoEn(posTrono + new Vector3(R(-4f, 4f), 3f, R(-6f, 6f)) - haciaCentro * 2f);
            c.transform.SetPositionAndRotation(p, Quaternion.Euler(R(-25f, 25f), R(0f, 360f), R(-25f, 25f)));
            c.transform.localScale = Vector3.one * R(0.8f, 1.8f);
        }
        var luz = new GameObject("LuzTrono").AddComponent<Light>();
        luz.transform.SetParent(t, false); luz.transform.position = posTrono + Vector3.up * 5f - haciaCentro * 2f;
        luz.type = LightType.Point; luz.color = new Color(1f, 0.35f, 0.25f); luz.range = 22f; luz.intensity = 25f; luz.shadows = LightShadows.None;
    }

    static int CrearMontones(Transform padre)
    {
        var t = new GameObject("MontonesComida").transform; t.SetParent(padre, false);
        var mQueso = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "Queso.asset");
        var mPan = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "Pan.asset");
        var mMiga = AssetDatabase.LoadAssetAtPath<Mesh>(Gen + "Miga.asset");
        var part = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RatDeathParticles.prefab");
        int n = 0;
        foreach (float ang in new[] { 60f, 120f, 240f, 300f })
        {
            float a = ang * Mathf.Deg2Rad;
            Vector3 p = PisoEn(L(LRey) + new Vector3(Mathf.Cos(a) * 32f, 4f, Mathf.Sin(a) * 32f), 4f, 12f);
            var go = new GameObject("MontonComida_" + n); go.transform.SetParent(t, false); go.transform.position = p;
            var vis = new GameObject("Visual").transform; vis.SetParent(go.transform, false);
            var partes = new List<(Mesh, Matrix4x4)>();
            for (int i = 0; i < 5; i++) partes.Add((mQueso, Matrix4x4.TRS(new Vector3(R(-1.5f, 1.5f), R(0f, 1.2f), R(-1.5f, 1.5f)), Quaternion.Euler(R(-20f, 20f), R(0f, 360f), R(-20f, 20f)), Vector3.one * R(1f, 1.5f))));
            var queso = Combinar("MontonQueso_" + n, partes.ToArray());
            partes.Clear();
            for (int i = 0; i < 4; i++) partes.Add((mPan, Matrix4x4.TRS(new Vector3(R(-2f, 2f), R(0.3f, 1.5f), R(-2f, 2f)), Quaternion.Euler(R(-20f, 20f), R(0f, 360f), R(-20f, 20f)), Vector3.one * R(1.1f, 1.6f))));
            for (int i = 0; i < 30; i++) partes.Add((mMiga, Matrix4x4.TRS(new Vector3(R(-3f, 3f), R(0f, 0.6f), R(-3f, 3f)), Quaternion.identity, Vector3.one * R(0.8f, 1.6f))));
            var pan = Combinar("MontonPan_" + n, partes.ToArray());
            Objeto("Queso", vis, Guardar(queso, "MontonQueso_" + n), mQueso_(), false);
            Objeto("Pan", vis, Guardar(pan, "MontonPan_" + n), mPan_(), false);
            var hv = go.AddComponent<RatHealth>();
            var so = new SerializedObject(hv);
            so.FindProperty("enemyName").stringValue = "Monton de comida";
            so.FindProperty("maxHealth").floatValue = 80f; so.FindProperty("currentHealth").floatValue = 80f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.center = new Vector3(0f, 1.2f, 0f); bc.size = new Vector3(6f, 2.6f, 6f);
            var mc = go.AddComponent<MontonComida>(); mc.visual = vis; mc.porciones = 2; mc.particulas = part;
            var dt = go.AddComponent<DropTable>();
            dt.entries = new List<DropEntry> { new DropEntry { itemId = "Carne", amount = 1, chance = 100f }, new DropEntry { itemId = "Carne", amount = 1, chance = 40f } };
            n++;
        }
        return n;
    }

    // carne y huesos para recoger en el nido (objetos de la escena, se guardan como los demas)
    static void CrearRecogibles(System.Text.StringBuilder log)
    {
        void Poner(string id, Vector3 p, int i)
        {
            var pref = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Drops/" + id + ".prefab");
            if (pref == null) { log.AppendLine("sin prefab " + id); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(pref);
            go.name = "Cloaca_" + id + "_" + i;
            go.transform.SetPositionAndRotation(p + Vector3.up * 0.6f, Quaternion.Euler(0f, R(0f, 360f), 0f));
        }
        for (int i = 0; i < 4; i++) { float a = i * Mathf.PI * 0.5f + 0.8f; Poner("Carne", PisoEn(sNido.c + new Vector3(Mathf.Cos(a) * 12f + R(-1f, 1f), 3f, Mathf.Sin(a) * 12f + R(-1f, 1f))), i); }
        for (int i = 0; i < 3; i++) Poner("Hueso", PisoEn(PuntoEnSala(sNido, 0.7f) + Vector3.up * 3f), i);
        Poner("Piel", PisoEn(PuntoEnSala(sNido, 0.6f) + Vector3.up * 3f), 0);
        Poner("Colmillo", PisoEn(L(LRey) + new Vector3(-30f, 4f, 10f), 4f, 12f), 0);
    }

    // ================================================================== ratas
    class KitRata { public GameObject modelo; public RuntimeAnimatorController ctrl; public Material comun, mojada, rey; public GameObject part; public AudioClip[] chillidos; public Mesh carne; public Material matCarne; }

    static KitRata kit;

    static GameObject CrearRata(string nombre, Transform padre, Vector3 pos, float escala, RataCloaca.Rol rol, float vida, float danio, string nombreVisible, Material mat, bool conCarga)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.identity);
        var vis = new GameObject("Visual").transform; vis.SetParent(go.transform, false);
        var modelo = (GameObject)PrefabUtility.InstantiatePrefab(kit.modelo, vis);
        modelo.transform.localPosition = Vector3.zero; modelo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        modelo.transform.localScale = Vector3.one * escala;
        var an = modelo.GetComponent<Animator>(); if (an == null) an = modelo.AddComponent<Animator>();
        an.runtimeAnimatorController = kit.ctrl; an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        foreach (var r in modelo.GetComponentsInChildren<SkinnedMeshRenderer>()) { r.sharedMaterial = mat; r.updateWhenOffscreen = false; }
        Physics.SyncTransforms();
        // medidas con la rata mirando a +Z (antes de girarla)
        var b = new Bounds(go.transform.position, Vector3.zero); bool hay = false;
        foreach (var r in modelo.GetComponentsInChildren<Renderer>()) { if (!hay) { b = r.bounds; hay = true; } else b.Encapsulate(r.bounds); }

        var hv = go.AddComponent<RatHealth>();
        var so = new SerializedObject(hv);
        so.FindProperty("enemyName").stringValue = nombreVisible;
        so.FindProperty("maxHealth").floatValue = vida; so.FindProperty("currentHealth").floatValue = vida;
        so.ApplyModifiedPropertiesWithoutUndo();

        var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true;
        Vector3 cl = b.center - go.transform.position;
        // la cola es larga: la caja cubre cabeza y cuerpo (la mitad delantera)
        bc.center = new Vector3(cl.x, b.size.y * 0.5f, cl.z + b.size.z * 0.18f);
        bc.size = new Vector3(b.size.x, b.size.y, b.size.z * 0.62f);

        var ag = go.AddComponent<NavMeshAgent>();
        ag.radius = 1f * escala / 105f; ag.height = b.size.y; ag.baseOffset = 0f;
        ag.speed = 3.2f; ag.acceleration = 30f; ag.angularSpeed = 540f; ag.stoppingDistance = 0.4f;
        ag.autoBraking = true; ag.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
        ag.avoidancePriority = 50 + rnd.Next(30);

        var au = go.AddComponent<AudioSource>(); au.spatialBlend = 1f; au.maxDistance = 40f; au.rolloffMode = AudioRolloffMode.Linear; au.playOnAwake = false;

        var rc = go.AddComponent<RataCloaca>();
        rc.rol = rol; rc.animator = an; rc.visual = vis; rc.hogar = pos; rc.danio = danio;
        rc.particulasMuerte = kit.part; rc.chillidos = kit.chillidos;
        rc.alcance = 3.4f * escala / 105f;
        var dt = go.AddComponent<DropTable>();
        dt.entries = new List<DropEntry> { new DropEntry { itemId = "Hueso", amount = 1, chance = 45f }, new DropEntry { itemId = "Piel", amount = 1, chance = 35f } };

        if (conCarga)
        {
            // trozo de carne en el hocico (sigue a la cabeza)
            var cabeza = Buscar(modelo.transform, "ratHead_end") ?? Buscar(modelo.transform, "ratHead");
            var c = new GameObject("Carga");
            c.transform.SetParent(cabeza != null ? cabeza : go.transform, false);
            c.transform.position = (cabeza != null ? cabeza.position : go.transform.position + go.transform.forward * 3f) + go.transform.forward * 0.4f + Vector3.down * 0.25f;
            c.transform.rotation = Quaternion.Euler(R(0f, 360f), R(0f, 360f), 0f);
            c.transform.localScale = Vector3.one * 0.9f / Mathf.Max(0.0001f, c.transform.parent.lossyScale.x);
            c.AddComponent<MeshFilter>().sharedMesh = kit.carne;
            var cr = c.AddComponent<MeshRenderer>(); cr.sharedMaterial = kit.matCarne; cr.shadowCastingMode = ShadowCastingMode.On;
            rc.carga = c;
        }
        go.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
        return go;
    }

    static Transform Buscar(Transform t, string nombre)
    {
        if (t.name == nombre) return t;
        foreach (Transform h in t) { var r = Buscar(h, nombre); if (r != null) return r; }
        return null;
    }

    static Vector3 EnAnden(Tunel tn, float s, float lado)
    {
        Vector3 p = tn.a + tn.dir * s + tn.lado * lado * 10f;
        p.y = Mathf.Lerp(tn.ya, tn.yb, Mathf.Clamp01(s / tn.largo));
        return PisoEn(p + Vector3.up * 2f);
    }

    static Vector3 EnCanal(Tunel tn, float s)
    {
        Vector3 p = tn.a + tn.dir * s;
        p.y = Mathf.Lerp(tn.ya, tn.yb, Mathf.Clamp01(s / tn.largo)) - 1f;
        return PisoEn(p, 2f, 6f);
    }

    static void CrearRatas(Transform raiz, Cloaca gestor)
    {
        kit = new KitRata
        {
            modelo = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rata_muerte.fbx"),
            ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Rat_AnimatorController.controller"),
            part = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RatDeathParticles.prefab"),
            chillidos = new[] { AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Animales/Rat_Squeak1.mp3"), AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Animales/Rat_Squeak2.mp3"),
                                AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Rata_Chillido.mp3"), AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Rata_Chillido2.mp3") }.Where(c => c != null).ToArray(),
        };
        var carnePref = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Drops/Carne.prefab");
        if (carnePref != null) { kit.carne = carnePref.GetComponent<MeshFilter>().sharedMesh; kit.matCarne = carnePref.GetComponent<MeshRenderer>().sharedMaterial; }
        // material: el de la rata del salon
        Material baseMat = null;
        var ratSalon = Object.FindAnyObjectByType<RatAI>(FindObjectsInactive.Include);
        if (ratSalon != null) { var smr = ratSalon.GetComponentInChildren<SkinnedMeshRenderer>(); if (smr != null) baseMat = smr.sharedMaterial; }
        if (baseMat == null) { var g = AssetDatabase.FindAssets("rat_brown t:Material"); if (g.Length > 0) baseMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g[0])); }
        kit.comun = baseMat;
        kit.mojada = GuardarMaterial("RataMojada", baseMat.shader, m => { m.CopyPropertiesFromMaterial(baseMat); m.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.58f)); m.SetFloat("_Smoothness", 0.72f); });
        kit.rey = GuardarMaterial("RataRey", baseMat.shader, m => { m.CopyPropertiesFromMaterial(baseMat); m.SetColor("_BaseColor", new Color(0.34f, 0.3f, 0.29f)); m.SetFloat("_Smoothness", 0.35f); });

        var tRatas = new GameObject("Ratas").transform; tRatas.SetParent(raiz, false);
        var tPlant = new GameObject("Plantillas").transform; tPlant.SetParent(raiz, false);
        int n = 0;
        GameObject Rata(RataCloaca.Rol rol, Vector3 p, Material m, float vida, float danio, string nombre, bool carga = false, float escala = 105f)
        {
            var go = CrearRata("Rata_" + rol + "_" + (n++), tRatas, p, escala * R(0.92f, 1.08f), rol, vida, danio, nombre, m, carga);
            return go;
        }

        // entrada: ladronas que acechan a quien baja con carne, y una nadadora
        var l1 = Rata(RataCloaca.Rol.Ladrona, EnAnden(t1, 12f, 1f), kit.comun, 45f, 6f, "Rata ladrona").GetComponent<RataCloaca>(); l1.radioHogar = 9f; l1.velocidadHuida = 5.6f;
        var l2 = Rata(RataCloaca.Rol.Ladrona, PisoEn(L(LPozo) + new Vector3(10f, 2f, -10f)), kit.comun, 45f, 6f, "Rata ladrona").GetComponent<RataCloaca>(); l2.radioHogar = 8f;
        Nadadora(Rata(RataCloaca.Rol.Nadadora, EnCanal(t1, 14f), kit.mojada, 55f, 9f, "Rata nadadora"), t1, 8f, t1.largo - 2f);

        // tunel principal: guardias en los andenes y nadadoras en el canal
        Guardia(Rata(RataCloaca.Rol.Guardia, EnAnden(t2, 30f, 1f), kit.comun, 60f, 8f, "Rata guardiana"), t2, 22f, 45f, 1f);
        Guardia(Rata(RataCloaca.Rol.Guardia, EnAnden(t2, 60f, -1f), kit.comun, 60f, 8f, "Rata guardiana"), t2, 50f, 72f, -1f);
        Nadadora(Rata(RataCloaca.Rol.Nadadora, EnCanal(t2, 25f), kit.mojada, 55f, 9f, "Rata nadadora"), t2, 12f, 50f);
        Nadadora(Rata(RataCloaca.Rol.Nadadora, EnCanal(t2, 65f), kit.mojada, 55f, 9f, "Rata nadadora"), t2, 50f, 82f);
        Rata(RataCloaca.Rol.Guardia, PisoEn(L(LCruce) + new Vector3(-8f, 2f, 9f)), kit.comun, 60f, 8f, "Rata guardiana").GetComponent<RataCloaca>().radioHogar = 6f;

        // hacia la madriguera
        Nadadora(Rata(RataCloaca.Rol.Nadadora, EnCanal(t3, 40f), kit.mojada, 55f, 9f, "Rata nadadora"), t3, RNido + 2f, t3.largo - RCruce);
        Guardia(Rata(RataCloaca.Rol.Guardia, EnAnden(t3, RNido + 8f, -1f), kit.comun, 70f, 9f, "Rata guardiana"), t3, RNido + 4f, RNido + 20f, -1f);
        // la madriguera: ratas durmiendo y forrajeras que van y vienen
        for (int i = 0; i < 5; i++)
        {
            var r = Rata(RataCloaca.Rol.Reposo, PisoEn(PuntoEnSala(sNido, 0.6f) + Vector3.up * 3f), kit.comun, 50f, 7f, "Rata").GetComponent<RataCloaca>();
            r.radioHogar = 7f; r.radioVista = 11f;
        }
        for (int i = 0; i < 3; i++)
        {
            var r = Rata(RataCloaca.Rol.Forrajera, gestor.puntosMadriguera[i], kit.comun, 40f, 6f, "Rata forrajera", true).GetComponent<RataCloaca>();
            r.velocidadCamina = 3.6f;
        }

        // bajada al Gran Colector
        Guardia(Rata(RataCloaca.Rol.Guardia, EnAnden(t4, t4.largo - 12f, 1f), kit.comun, 70f, 9f, "Rata guardiana"), t4, t4.largo - 30f, t4.largo - 4f, 1f);
        Guardia(Rata(RataCloaca.Rol.Guardia, EnAnden(t4, t4.largo - 20f, -1f), kit.comun, 70f, 9f, "Rata guardiana"), t4, t4.largo - 36f, t4.largo - 8f, -1f);
        Nadadora(Rata(RataCloaca.Rol.Nadadora, EnCanal(t4, 30f), kit.mojada, 55f, 9f, "Rata nadadora"), t4, 6f, t4.largo - 6f);

        // plantillas (desactivadas): forrajera que saca el gestor y cria que llama el rey
        var pf = CrearRata("PlantillaForrajera", tPlant, gestor.puntosMadriguera[0], 100f, RataCloaca.Rol.Forrajera, 40f, 6f, "Rata forrajera", kit.comun, true);
        pf.GetComponent<RataCloaca>().velocidadCamina = 3.6f;
        pf.SetActive(false);
        gestor.plantillaForrajera = pf.GetComponent<RataCloaca>();
        var pc = CrearRata("PlantillaCria", tPlant, L(LRey) + Vector3.left * 30f, 90f, RataCloaca.Rol.Guardia, 35f, 7f, "Cria del rey", kit.mojada, false);
        var crc = pc.GetComponent<RataCloaca>(); crc.radioVista = 60f; crc.radioHogar = 20f; crc.velocidadCorre = 7.4f;
        pc.GetComponent<DropTable>().entries[0].chance = 20f;
        pc.SetActive(false);

        CrearRey(tRatas, gestor, crc);
    }

    static void Guardia(GameObject go, Tunel tn, float s0, float s1, float lado)
    {
        var r = go.GetComponent<RataCloaca>();
        r.patrulla = new[] { EnAnden(tn, s0, lado), EnAnden(tn, s1, lado) };
        r.radioVista = 18f;
    }

    static void Nadadora(GameObject go, Tunel tn, float s0, float s1)
    {
        var r = go.GetComponent<RataCloaca>();
        r.patrulla = new[] { EnCanal(tn, s0), EnCanal(tn, s1) };
        r.radioVista = 22f; r.velocidadCamina = 2.8f; r.velocidadCorre = 6.2f;
    }

    static void CrearRey(Transform padre, Cloaca gestor, RataCloaca cria)
    {
        Vector3 hogar = PisoEn(L(LRey.x + 26f, YA + 3f, 0f));
        var go = new GameObject("ReyRata");
        go.transform.SetParent(padre, false);
        go.transform.SetPositionAndRotation(hogar, Quaternion.identity);
        var vis = new GameObject("Visual").transform; vis.SetParent(go.transform, false);
        var modelo = (GameObject)PrefabUtility.InstantiatePrefab(kit.modelo, vis);
        modelo.transform.localPosition = Vector3.zero; modelo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        modelo.transform.localScale = Vector3.one * 330f;
        var an = modelo.GetComponent<Animator>(); if (an == null) an = modelo.AddComponent<Animator>();
        an.runtimeAnimatorController = kit.ctrl; an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var r in modelo.GetComponentsInChildren<SkinnedMeshRenderer>()) { r.sharedMaterial = kit.rey; r.updateWhenOffscreen = true; }
        Physics.SyncTransforms();
        var b = new Bounds(go.transform.position, Vector3.zero); bool hay = false;
        foreach (var r in modelo.GetComponentsInChildren<Renderer>()) { if (!hay) { b = r.bounds; hay = true; } else b.Encapsulate(r.bounds); }
        Vector3 centroLocal = b.center - go.transform.position;
        go.transform.rotation = Quaternion.LookRotation(Vector3.left);     // mira hacia la entrada del colector
        Physics.SyncTransforms();

        // corona de tapas y alambre dorado sobre la cabeza, y un brillo rojo en los ojos
        var cabeza = Buscar(modelo.transform, "ratHead");
        var corona = new GameObject("Corona");
        Vector3 posCorona = cabeza != null ? cabeza.position + Vector3.up * b.size.y * 0.2f : hogar + Vector3.up * b.size.y + go.transform.forward * b.size.z * 0.35f;
        corona.transform.SetPositionAndRotation(posCorona, go.transform.rotation);
        var aro = Torno("Corona", new[] { new Vector2(0.95f, 0f), new Vector2(1f, 0.05f), new Vector2(1f, 0.55f), new Vector2(0.92f, 0.6f), new Vector2(0.9f, 0.05f) }, 28);
        var punta = Torno("Punta", new[] { new Vector2(0.22f, 0f), new Vector2(0.05f, 0.75f), new Vector2(0f, 0.8f) }, 8);
        var gema = Bola("Gema", 0.17f, Vector3.one, 0.2f, 2);
        var partes = new List<(Mesh, Matrix4x4)> { (aro, Matrix4x4.identity) };
        var gemas = new List<(Mesh, Matrix4x4)>();
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            Vector3 p = new Vector3(Mathf.Cos(a) * 0.96f, 0.5f, Mathf.Sin(a) * 0.96f);
            partes.Add((punta, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one)));
            gemas.Add((gema, Matrix4x4.TRS(new Vector3(Mathf.Cos(a + 0.52f) * 1.02f, 0.28f, Mathf.Sin(a + 0.52f) * 1.02f), Quaternion.identity, Vector3.one)));
        }
        Objeto("Oro", corona.transform, Guardar(Combinar("CoronaOro", partes.ToArray()), "CoronaOro"), mOro, false);
        Objeto("Gemas", corona.transform, Guardar(Combinar("CoronaGemas", gemas.ToArray()), "CoronaGemas"), mRubi, false);
        corona.transform.localScale = Vector3.one * 1.25f;
        corona.transform.rotation = go.transform.rotation * Quaternion.Euler(-12f, 0f, 8f);
        if (cabeza != null) corona.transform.SetParent(cabeza, true); else corona.transform.SetParent(vis, true);
        var ojo = new GameObject("BrilloOjos").AddComponent<Light>();
        ojo.transform.SetParent(cabeza != null ? cabeza : vis, false);
        ojo.transform.position = (cabeza != null ? cabeza.position : posCorona) + go.transform.forward * 1.5f;
        ojo.type = LightType.Point; ojo.color = new Color(1f, 0.15f, 0.1f); ojo.range = 7f; ojo.intensity = 12f; ojo.shadows = LightShadows.None;

        var hv = go.AddComponent<RatHealth>();
        var so = new SerializedObject(hv);
        so.FindProperty("enemyName").stringValue = "Rey Rata";
        so.FindProperty("maxHealth").floatValue = 1600f; so.FindProperty("currentHealth").floatValue = 1600f;
        so.ApplyModifiedPropertiesWithoutUndo();
        var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true;
        bc.center = new Vector3(centroLocal.x, b.size.y * 0.5f, centroLocal.z + b.size.z * 0.18f);
        bc.size = new Vector3(b.size.x, b.size.y, b.size.z * 0.62f);
        var ag = go.AddComponent<NavMeshAgent>();
        ag.radius = 3f; ag.height = b.size.y; ag.baseOffset = 0f; ag.speed = 5.5f; ag.acceleration = 40f; ag.angularSpeed = 0f;
        ag.stoppingDistance = 6f; ag.autoBraking = true; ag.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance; ag.avoidancePriority = 10;
        var au = go.AddComponent<AudioSource>(); au.spatialBlend = 1f; au.maxDistance = 160f; au.rolloffMode = AudioRolloffMode.Linear; au.playOnAwake = false;

        var rey = go.AddComponent<ReyRata>();
        rey.visual = vis; rey.animator = an; rey.hogar = hogar;
        rey.centroEstanque = L(LRey); rey.radioEstanque = RPilaRey - 2f; rey.nivelAgua = O.y + YA - AguaBajo;
        rey.radioCorrea = RRey - 4f; rey.radioDespertar = 34f;
        rey.plantillaCria = cria;
        rey.rugido = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Rey_Rugido.mp3");
        rey.chillido = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Rey_Chillido.mp3");
        rey.siseo = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Rey_Siseo.mp3");
        rey.chapoteo = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Agua_Chapoteo.mp3");
        rey.particulasMuerte = kit.part;
        rey.matRemolino = mRemolino;
        var bocas = new List<Transform>();
        foreach (float ang in new[] { 90f, 150f, 210f, 270f })
        {
            float a = ang * Mathf.Deg2Rad; Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var t = new GameObject("PuntoLlamado").transform; t.SetParent(padre, false);
            t.position = PisoEn(L(LRey) + d * (RRey - 5f) + Vector3.up * 3f);
            t.rotation = Quaternion.LookRotation(-d);
            bocas.Add(t);
        }
        rey.puntosLlamado = bocas.ToArray();
        var dt = go.AddComponent<DropTable>();
        dt.entries = new List<DropEntry>
        {
            new DropEntry { itemId = MaquinaCrecimiento.ItemBateria, amount = 1, chance = 100f },
            new DropEntry { itemId = "Piel", amount = 4, chance = 100f },
            new DropEntry { itemId = "Hueso", amount = 4, chance = 100f },
            new DropEntry { itemId = "Colmillo", amount = 3, chance = 100f },
        };
        gestor.rey = rey;
        CrearPrefabBateria();
    }

    // la bateria como objeto que se suelta (Resources/Drops/Bateria): asi el guardado la restaura como los demas drops
    static void CrearPrefabBateria()
    {
        var mCuerpo = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Cabina/Generado/BateriaCuerpo.asset");
        var mTapas = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Cabina/Generado/BateriaTapas.asset");
        var mFranjas = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Cabina/Generado/BateriaFranjas.asset");
        if (mCuerpo == null || mTapas == null || mFranjas == null) { Debug.LogWarning("[Cloaca] No estan las mallas de la bateria (Herramientas > Cabina > Construir)"); return; }
        // copias propias (la cabina regenera las suyas)
        mCuerpo = Guardar(Object.Instantiate(mCuerpo), "BateriaCuerpo"); mTapas = Guardar(Object.Instantiate(mTapas), "BateriaTapas"); mFranjas = Guardar(Object.Instantiate(mFranjas), "BateriaFranjas");
        var go = new GameObject("Bateria");
        var vis = new GameObject("Visual").transform; vis.SetParent(go.transform, false);
        Objeto("Cuerpo", vis, mCuerpo, AssetDatabase.LoadAssetAtPath<Material>("Assets/Cabina/Materiales/BateriaVerde.mat"), false);
        Objeto("Tapas", vis, mTapas, AssetDatabase.LoadAssetAtPath<Material>("Assets/Cabina/Materiales/BateriaMetal.mat"), false);
        Objeto("Franjas", vis, mFranjas, AssetDatabase.LoadAssetAtPath<Material>("Assets/Cabina/Materiales/BateriaBrillo.mat"), false);
        var wi = go.AddComponent<WorldItem>();
        wi.itemId = MaquinaCrecimiento.ItemBateria; wi.displayName = "Bateria verde"; wi.quantity = 1;
        wi.itemIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Cabina/Sprites/Bateria.png");
        var bc = go.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.82f, 0f); bc.size = new Vector3(0.9f, 1.65f, 0.9f);
        var rb = go.AddComponent<Rigidbody>(); rb.mass = 1f;
        go.AddComponent<DropItem>();
        var sp = go.AddComponent<ItemSparkle>(); sp.color = new Color(0.5f, 1f, 0.6f, 1f);
        var l = new GameObject("Luz").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        l.type = LightType.Point; l.color = new Color(0.35f, 1f, 0.45f); l.range = 12f; l.intensity = 14f; l.shadows = LightShadows.None;
        var bv = go.AddComponent<BateriaVerde>(); bv.visual = vis; bv.luz = l;
        PrefabUtility.SaveAsPrefabAsset(go, "Assets/Resources/Drops/Bateria.prefab");
        Object.DestroyImmediate(go);
    }

    // ================================================================== sonido y camara
    static void Sonido3D(Transform padre, string nombre, Vector3 pos, string clip, float vol, float dist)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false); go.transform.position = pos;
        var a = go.AddComponent<AudioSource>();
        a.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/" + clip + ".mp3");
        a.loop = true; a.playOnAwake = true; a.spatialBlend = 1f; a.maxDistance = dist; a.rolloffMode = AudioRolloffMode.Linear; a.volume = vol;
    }

    static void ConfigurarCamara()
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc != null && pc.tag != "Player") { pc.tag = "Player"; EditorUtility.SetDirty(pc.gameObject); }
    }

    // ================================================================== ver la cloaca
    [MenuItem("Herramientas/Cloaca/Llevar al jugador al derrumbe")]
    public static void LlevarJugador()
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc == null) return;
        var a2 = ZonaJuego.alcantarilla;
        Vector3 p = new Vector3(a2.x + XArriba - 12f, 0f, a2.y);
        p.y = Suelo(p.x, p.z) + 1.2f;
        Undo.RecordObject(pc.transform, "Mover jugador");
        pc.transform.SetPositionAndRotation(p, Quaternion.LookRotation(Vector3.right));
    }
}
