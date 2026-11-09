using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Genera el arbol hueco con la colmena gigante:
///   - tronco con raices, corteza, ramas y copa; entrada en arco a ras de suelo y huecos por donde salen las abejas;
///   - dentro, una rampa en espiral pegada a la pared (con tablones a modo de escalones y hongos que alumbran)
///     sube hasta la colmena: cuatro pisos de cera alrededor de un pozo central, una rampa de cera en
///     espiral entre pisos, camaras en la pared (guarderias con larvas, almacenes de miel) y pasadizos;
///   - arriba, la camara real con la reina sobre su panal.
/// La geometria es un campo de distancia convertido en malla (surface nets) por trozos; la luz se hornea
/// en el color de vertice. Menu: Herramientas > Colmena > Construir.
/// </summary>
public static class ArbolBuilder
{
    const string Raiz = "Assets/Colmena/";
    const string Gen = Raiz + "Generado/";
    const string Mat = Raiz + "Materiales/";
    const float Celda = 1.25f;
    const int Trozo = 32;

    // medidas (unidades del juego; el jugador mide 2)
    const float ROut = 40f, RIn = 29f, AltoHueco = 186f, AltoTronco = 238f;
    const float RPozo = 9f, RBanda = 14f, RCera = 20f;
    static readonly float[] Pisos = { 64f, 92f, 120f, 148f };
    const float PasoRampaMadera = 64f / 3f;     // 3 vueltas del suelo al primer piso
    const float PasoRampaCera = 28f;            // una vuelta por piso

    static Vector3 T;                // base del tronco (mundo)
    static float AngEntrada, AngRampa;
    static System.Random rnd;

    struct Hueco { public float ang, y, radio; }
    static readonly List<Hueco> huecos = new List<Hueco>();
    struct Camara { public float ang, y, rr, ry, rt; public string tipo; public int piso; }
    static readonly List<Camara> camaras = new List<Camara>();
    struct Rama { public Vector3 a, b; public float ra, rb; }
    static readonly List<Rama> ramas = new List<Rama>();
    static readonly float[] AngRaices = new float[7];

    static float R(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }
    static Vector3 Dir(float ang) { return new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)); }
    static Vector3 Local(float ang, float r, float y) { return T + Dir(ang) * r + Vector3.up * y; }
    static float Envolver(float a) { a %= Mathf.PI * 2f; return a < 0f ? a + Mathf.PI * 2f : a; }

    // ================================================================== forma
    static void Planear()
    {
        huecos.Clear(); camaras.Clear(); ramas.Clear();
        // la entrada mira hacia el salon
        Vector3 haciaSalon = new Vector3(-T.x, 0f, -6f - T.z);
        AngEntrada = Mathf.Atan2(haciaSalon.z, haciaSalon.x);
        AngRampa = AngEntrada + 0.55f;       // la rampa empieza a la derecha de la entrada
        for (int i = 0; i < AngRaices.Length; i++) AngRaices[i] = AngEntrada + Mathf.PI / AngRaices.Length + i * Mathf.PI * 2f / AngRaices.Length + R(-0.15f, 0.15f);
        // huecos en el tronco (salida de las abejas y entrada de luz)
        huecos.Add(new Hueco { ang = AngEntrada + 2.4f, y = 40f, radio = 3.2f });
        huecos.Add(new Hueco { ang = AngEntrada + 3.6f, y = 104f, radio = 4.4f });
        huecos.Add(new Hueco { ang = AngEntrada + 1.1f, y = 134f, radio = 4.4f });
        huecos.Add(new Hueco { ang = AngEntrada - 1.6f, y = 76f, radio = 4.0f });
        huecos.Add(new Hueco { ang = AngEntrada + 0.4f, y = 166f, radio = 4.6f });
        // camaras en la pared de cera de los tres primeros pisos
        string[][] tipos = { new[] { "cria", "miel", "polen", "miel" }, new[] { "miel", "cria", "miel", "polen" }, new[] { "polen", "miel", "cria", "miel" } };
        for (int p = 0; p < 3; p++)
            for (int i = 0; i < 4; i++)
                camaras.Add(new Camara { piso = p, ang = AngEntrada + 1.1f + p * 0.8f + i * Mathf.PI * 0.5f, y = Pisos[p], rr = 8.5f, ry = 6.8f, rt = 10f, tipo = tipos[p][i] });
        // ramas que salen de la copa
        for (int i = 0; i < 6; i++)
        {
            float a = AngEntrada + 0.5f + i * Mathf.PI * 2f / 6f + R(-0.2f, 0.2f);
            Vector3 b0 = T + Vector3.up * R(205f, 228f) + Dir(a) * 14f;
            Vector3 b1 = T + Vector3.up * R(262f, 292f) + Dir(a) * R(88f, 118f);
            ramas.Add(new Rama { a = b0, b = b1, ra = 13f, rb = 4.5f });
            // ramita secundaria
            Vector3 m = Vector3.Lerp(b0, b1, 0.55f);
            float a2 = a + R(-0.8f, 0.8f);
            ramas.Add(new Rama { a = m, b = m + Dir(a2) * R(35f, 55f) + Vector3.up * R(18f, 40f), ra = 6f, rb = 2.4f });
        }
        ramas.Add(new Rama { a = T + Vector3.up * 220f, b = T + Vector3.up * 300f + Dir(AngEntrada) * 10f, ra = 14f, rb = 5f });
    }

    static float Ruido(float x, float y) { return Mathf.PerlinNoise(x, y); }

    static float RadioExterior(float y, float ang)
    {
        float r = ROut - 0.028f * Mathf.Max(0f, y) + 16f * Mathf.Exp(-Mathf.Max(0f, y) / 13f);
        // raices
        foreach (float ar in AngRaices)
        {
            float c = Mathf.Cos(ang - ar);
            if (c <= 0f) continue;
            r += 26f * Mathf.Pow(c, 40f) * Mathf.Exp(-Mathf.Max(0f, y) / 7f);
        }
        // corteza: surcos verticales
        r += (Ruido(ang * 9f, y * 0.035f) - 0.5f) * 3.2f + (Ruido(ang * 31f + 4f, y * 0.12f) - 0.5f) * 1.1f;
        return r;
    }

    static float DistRama(Vector3 p, Rama q)
    {
        Vector3 ab = q.b - q.a;
        float t = Mathf.Clamp01(Vector3.Dot(p - q.a, ab) / ab.sqrMagnitude);
        return Mathf.Lerp(q.ra, q.rb, t) - (p - (q.a + ab * t)).magnitude;
    }

    // altura de la helice (rampa) mas cercana a y en esta posicion angular; -1 si no hay
    static float Helice(float ang, float y, float y0, float paso, int vueltas, float angInicio)
    {
        float a = Envolver(ang - angInicio) / (Mathf.PI * 2f);
        float mejor = -1e6f;
        for (int k = 0; k < vueltas; k++)
        {
            float h = y0 + (a + k) * paso;
            if (Mathf.Abs(h - y) < Mathf.Abs(mejor - y)) mejor = h;
        }
        return mejor;
    }

    // ultimo tramo de una helice que llega a un piso (para abrir hueco en el piso)
    static bool TramoFinal(float ang, float y0, float paso, int vueltas, float angInicio, float desdeAltura)
    {
        float a = Envolver(ang - angInicio) / (Mathf.PI * 2f);
        float h = y0 + (a + vueltas - 1) * paso;
        return h > desdeAltura;
    }

    struct Partes { public float madera, cera; }

    /// <summary>Campo de distancia: positivo dentro de lo solido, negativo en el aire.</summary>
    static float F(Vector3 p) { var q = Evaluar(p); return Mathf.Max(q.madera, q.cera); }

    static Partes Evaluar(Vector3 p)
    {
        Vector3 q = p - T;
        float y = q.y, r = new Vector2(q.x, q.z).magnitude, ang = Mathf.Atan2(q.z, q.x);

        // suelo (plano junto al arbol)
        float suelo = (r < 60f ? -0.05f : ZonaJuego.Altura(p.x, p.z) - T.y) - y;

        // ---------------- madera: tronco hueco, tapa, ramas, rampa
        float madera = -1e5f;
        if (y < AltoTronco + 20f && r < 140f)
        {
            float rout = RadioExterior(y, ang);
            float dOut = rout - r;
            float tope = AltoTronco - y + 25f * Mathf.Clamp01(1f - r / 30f);   // copa redondeada
            float riN = RIn + (Ruido(ang * 5f, y * 0.06f) - 0.5f) * 2.4f;
            float hueco = Mathf.Min(riN - r, AltoHueco - y);                  // >0 dentro del hueco
            madera = Mathf.Min(Mathf.Min(dOut, tope), -hueco);
            madera = Mathf.Min(madera, y + 8f);
            foreach (var rm in ramas) madera = Mathf.Max(madera, DistRama(p, rm));
            // rampa de madera en espiral pegada a la pared, del suelo al primer piso
            if (r > 19f && r < RIn + 3f && y > -3f && y < Pisos[0] + 1f)
            {
                float h = Helice(ang, y, 0f, PasoRampaMadera, 3, AngRampa);
                float d = Mathf.Min(Mathf.Min(h - y, y - (h - 2.6f)), Mathf.Min(r - 21f, RIn + 2f - r));
                madera = Mathf.Max(madera, d);
            }
        }
        madera = Mathf.Max(madera, suelo);

        // ---------------- cera: pared de panal, pisos, rampa central, trono
        float cera = -1e5f;
        if (y > Pisos[0] - 4f && y < AltoHueco + 2f && r < RIn + 2f)
        {
            float bulto = (Ruido(ang * 4f + 2f, y * 0.08f) - 0.5f) * 4f;
            cera = Mathf.Min(Mathf.Min(r - (RCera - bulto), RIn + 1.5f - r), Mathf.Min(y - (Pisos[0] + 2f), AltoHueco + 0.5f - y));
            for (int i = 0; i < Pisos.Length; i++)
            {
                float Y = Pisos[i];
                bool ultimo = i == Pisos.Length - 1;
                float dp = Mathf.Min(Mathf.Min(y - (Y - 2.5f), Y - y), Mathf.Min(r - (ultimo ? -1f : RBanda), RIn + 1.5f - r));
                // huecos en el piso donde la rampa de abajo lo atraviesa
                if (i == 0 && r > 20.5f && TramoFinal(ang, 0f, PasoRampaMadera, 3, AngRampa, Y - 11f)) dp = -1f;
                if (ultimo && r > RPozo - 0.5f && r < RBanda + 0.5f && TramoFinal(ang, Pisos[0], PasoRampaCera, 3, AngRampa, Y - 10f)) dp = -1f;
                cera = Mathf.Max(cera, dp);
            }
            // rampa de cera en espiral (banda interior) del primer piso al ultimo
            if (r > RPozo - 1f && r < RBanda + 1f && y > Pisos[0] - 4f && y < Pisos[3] + 1f)
            {
                float h = Helice(ang, y, Pisos[0], PasoRampaCera, 3, AngRampa);
                cera = Mathf.Max(cera, Mathf.Min(Mathf.Min(h - y, y - (h - 2.2f)), Mathf.Min(r - RPozo, RBanda + 0.5f - r)));
            }
        }

        // ---------------- huecos excavados (aire)
        float aire = -1e5f;
        // entrada en arco
        {
            Vector3 d = Dir(AngEntrada);
            float u = q.x * d.x + q.z * d.z, v = -q.x * d.z + q.z * d.x;
            float arco = y > 7f ? 6.8f - new Vector2(v, y - 7f).magnitude : 6.8f - Mathf.Abs(v);
            aire = Mathf.Max(aire, Mathf.Min(Mathf.Min(arco, y + 0.04f), Mathf.Min(u - (RIn - 4f), ROut + 30f - u)));
        }
        // huecos del tronco
        foreach (var h in huecos)
        {
            Vector3 d = Dir(h.ang);
            float u = q.x * d.x + q.z * d.z, v = -q.x * d.z + q.z * d.x;
            float c = h.radio - new Vector2(v, y - h.y).magnitude;
            aire = Mathf.Max(aire, Mathf.Min(c, Mathf.Min(u - (RIn - 12f), ROut + 12f - u)));
        }
        // camaras y pasadizos de la colmena
        foreach (var c in camaras)
        {
            Vector3 cc = Dir(c.ang) * 26.5f; cc.y = c.y + c.ry * 0.75f;
            Vector3 rel = q - cc;
            Vector3 rad = Dir(c.ang), tan = new Vector3(-rad.z, 0f, rad.x);
            float a = Vector3.Dot(rel, rad) / c.rr, b = rel.y / c.ry, t = Vector3.Dot(rel, tan) / c.rt;
            float e = (1f - Mathf.Sqrt(a * a + b * b + t * t)) * Mathf.Min(c.rr, Mathf.Min(c.ry, c.rt));
            aire = Mathf.Max(aire, Mathf.Min(e, y - c.y));
        }
        for (int piso = 0; piso < 3; piso++)
            for (int i = 0; i < 4; i += 2)
            {
                // pasadizo curvo dentro de la pared, entre dos camaras vecinas
                var c0 = camaras[piso * 4 + i]; var c1 = camaras[piso * 4 + (i + 1) % 4];
                float da = Envolver(ang - c0.ang), dtot = Envolver(c1.ang - c0.ang);
                if (da < dtot)
                {
                    float dd = 3.1f - new Vector2(r - 26.5f, y - (c0.y + 3.1f)).magnitude;
                    aire = Mathf.Max(aire, Mathf.Min(dd, y - c0.y));
                }
            }
        // camara real: cupula sobre el ultimo piso
        {
            float techo = AltoHueco - 1f - (r / RCera) * (r / RCera) * 10f;
            aire = Mathf.Max(aire, Mathf.Min(Mathf.Min(RCera - 1f - r, techo - y), y - Pisos[3]));
        }

        madera = Mathf.Min(madera, -aire);
        cera = Mathf.Min(cera, -aire);
        // trono de cera de la reina (despues de excavar)
        if (r < 8f && y > Pisos[3] - 1f && y < Pisos[3] + 6f)
            cera = Mathf.Max(cera, Mathf.Min(Mathf.Min(6f - r, Pisos[3] + 3.5f - y), y - (Pisos[3] - 1f)));
        return new Partes { madera = madera, cera = cera };
    }

    static Vector3 Gradiente(Vector3 p)
    {
        const float e = 0.5f;
        return new Vector3(F(p + Vector3.right * e) - F(p - Vector3.right * e), F(p + Vector3.up * e) - F(p - Vector3.up * e), F(p + Vector3.forward * e) - F(p - Vector3.forward * e));
    }

    // ================================================================== surface nets
    class MallaTrozo { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<int> madera = new List<int>(), cera = new List<int>(), suelo = new List<int>(); public Color[] col; }
    static readonly int[,] Aristas12 = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };

    static bool TrozoUtil(Bounds b)
    {
        Vector3 c = b.center - T;
        float rMin = Mathf.Max(0f, new Vector2(c.x, c.z).magnitude - b.extents.x * 1.42f);
        if (b.max.y < T.y - 10f) return false;
        if (rMin < 75f && b.min.y < T.y + AltoTronco + 30f) return true;
        foreach (var rm in ramas)
        {
            var bb = new Bounds(rm.a, Vector3.zero); bb.Encapsulate(rm.b); bb.Expand(rm.ra * 2f + 4f);
            if (bb.Intersects(b)) return true;
        }
        return false;
    }

    static List<MallaTrozo> Poligonizar(Bounds dom)
    {
        var res = new List<MallaTrozo>();
        Vector3Int celdas = Vector3Int.CeilToInt(dom.size / Celda);
        Vector3 mn = dom.min;
        int nS = Trozo + 3, nC = Trozo + 1;
        var s = new float[nS * nS * nS];
        var idx = new int[nC * nC * nC];
        var cs = new float[8];
        int Si(int x, int y, int z) => (x * nS + y) * nS + z;
        int Ci(int x, int y, int z) => (x * nC + y) * nC + z;
        for (int cx = 0; cx < celdas.x; cx += Trozo)
            for (int cy = 0; cy < celdas.y; cy += Trozo)
                for (int cz = 0; cz < celdas.z; cz += Trozo)
                {
                    Vector3 o = mn + new Vector3(cx - 1, cy - 1, cz - 1) * Celda;
                    var bt = new Bounds(o + Vector3.one * (nS - 1) * Celda * 0.5f, Vector3.one * (nS - 1) * Celda);
                    if (!TrozoUtil(bt)) continue;
                    bool aire = false, solido = false;
                    for (int x = 0; x < nS; x++) for (int y = 0; y < nS; y++) for (int z = 0; z < nS; z++)
                    {
                        float f = F(o + new Vector3(x, y, z) * Celda);
                        s[Si(x, y, z)] = f;
                        if (f < 0f) aire = true; else solido = true;
                    }
                    if (!aire || !solido) continue;
                    var m = new MallaTrozo();
                    for (int i = 0; i < idx.Length; i++) idx[i] = -1;
                    for (int x = 0; x < nC; x++) for (int y = 0; y < nC; y++) for (int z = 0; z < nC; z++)
                    {
                        int msk = 0;
                        for (int k = 0; k < 8; k++) { cs[k] = s[Si(x + (k & 1), y + ((k >> 1) & 1), z + ((k >> 2) & 1))]; if (cs[k] < 0f) msk |= 1 << k; }
                        if (msk == 0 || msk == 255) continue;
                        Vector3 sum = Vector3.zero; int cnt = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int a = Aristas12[e, 0], b = Aristas12[e, 1];
                            if ((cs[a] < 0f) == (cs[b] < 0f)) continue;
                            float t = cs[a] / (cs[a] - cs[b]);
                            sum += Vector3.Lerp(new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1), new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1), t); cnt++;
                        }
                        Vector3 pos = o + (new Vector3(x, y, z) + sum / cnt) * Celda;
                        idx[Ci(x, y, z)] = m.v.Count;
                        m.v.Add(pos); m.n.Add(-Gradiente(pos).normalized);
                    }
                    for (int x = 1; x <= Trozo; x++) for (int y = 1; y <= Trozo; y++) for (int z = 1; z <= Trozo; z++)
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
                            Vector3 cen = (m.v[c0] + m.v[c1] + m.v[c2] + m.v[c3]) * 0.25f;
                            Vector3 ql = cen - T;
                            float rr = new Vector2(ql.x, ql.z).magnitude;
                            // el suelo solo alrededor del tronco (el resto lo pone el suelo exterior)
                            bool esSuelo = ql.y < 1.5f && ql.y > -1.5f && Vector3.Dot(m.n[c0] + m.n[c2], Vector3.up) > 1.2f && rr > RadioExterior(0f, Mathf.Atan2(ql.z, ql.x)) - 1f;
                            if (esSuelo && rr > ZonaJuego.arbolRadio + 3f) continue;
                            if (ql.y < -1.5f && rr > ZonaJuego.arbolRadio) continue;
                            Vector3 esperado = Vector3.zero; esperado[ax] = f0 < 0f ? -1f : 1f;
                            Vector3 nq = Vector3.Cross(m.v[c1] - m.v[c0], m.v[c2] - m.v[c0]);
                            int[] tri = Vector3.Dot(nq, esperado) >= 0f ? new[] { c0, c1, c2, c0, c2, c3 } : new[] { c0, c2, c1, c0, c3, c2 };
                            var pt = Evaluar(cen - (m.n[c0] + m.n[c2]).normalized * 0.5f);
                            bool esCera = pt.cera >= pt.madera && ql.y > Pisos[0] - 5f;
                            (esSuelo ? m.suelo : esCera ? m.cera : m.madera).AddRange(tri);
                        }
                    }
                    if (m.madera.Count + m.cera.Count + m.suelo.Count > 0) res.Add(m);
                }
        return res;
    }

    // ================================================================== luz horneada
    static void HornearLuz(List<MallaTrozo> trozos, List<Vector3> pos, List<Color> col, List<float> alc)
    {
        foreach (var t in trozos)
        {
            t.col = new Color[t.v.Count];
            for (int i = 0; i < t.v.Count; i++)
            {
                Vector3 p = t.v[i], n = t.n[i];
                Vector3 ql = p - T;
                float rr = new Vector2(ql.x, ql.z).magnitude;
                float ao = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(-F(p + n * 3f) / 3f));
                Color c = new Color(0.07f, 0.055f, 0.04f) * ao;
                for (int k = 0; k < pos.Count; k++)
                {
                    Vector3 d = pos[k] - p; float dist = d.magnitude;
                    if (dist > alc[k]) continue;
                    Vector3 dir = d / Mathf.Max(0.01f, dist);
                    float lam = 0.3f + 0.7f * Mathf.Max(0f, Vector3.Dot(n, dir));
                    float at = 1f - dist / alc[k]; at *= at;
                    bool tapa = false;
                    for (int s = 1; s <= 3 && !tapa; s++) if (F(p + n * 0.7f + d * (s / 4f)) > 0.8f) tapa = true;
                    if (tapa) continue;
                    c += col[k] * at * lam * Mathf.Lerp(0.6f, 1f, ao);
                }
                // alpha: cuanto sol recibe (afuera del tronco, junto a la entrada y a los huecos)
                float afuera = rr > RadioExterior(ql.y, Mathf.Atan2(ql.z, ql.x)) - 3f || ql.y > AltoHueco + 2f ? 1f : 0f;
                Vector3 ent = Local(AngEntrada, RIn, 4f) - T;
                afuera = Mathf.Max(afuera, Mathf.Clamp01(1f - (ql - ent).magnitude / 22f) * 0.8f);
                foreach (var h in huecos)
                {
                    Vector3 hp = Dir(h.ang) * RIn; hp.y = h.y;
                    afuera = Mathf.Max(afuera, Mathf.Clamp01(1f - (ql - hp).magnitude / 16f) * 0.7f);
                }
                c.a = afuera;
                t.col[i] = c;
            }
        }
    }

    // ================================================================== mallas pequeñas
    static Mesh Guardar(Mesh m, string nombre)
    {
        Directory.CreateDirectory(Gen);
        string path = Gen + nombre + ".asset";
        NormalesSeguras.Sanear(m);   // normales nulas = NaN = destellos del Bloom
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Material GuardarMaterial(string nombre, Shader sh, System.Action<Material> cfg)
    {
        Directory.CreateDirectory(Mat);
        string path = Mat + nombre + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        m.shader = sh; cfg(m); m.enableInstancing = true; EditorUtility.SetDirty(m);
        return m;
    }

    static Material Lit(string n, Color c, float suave, Color emision)
    {
        return GuardarMaterial(n, Shader.Find("Universal Render Pipeline/Lit"), m =>
        {
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", suave); m.SetFloat("_Metallic", 0f);
            if (emision.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emision); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            else m.DisableKeyword("_EMISSION");
        });
    }

    // panal 3D: placa con celdas hexagonales de verdad (paredes con profundidad); la miel va en otra malla
    static void MallaPanal(int columnas, int filas, float radio, float hondo, int semilla, out Mesh cera, out Mesh miel)
    {
        var rr = new System.Random(semilla);
        var v = new List<Vector3>(); var t = new List<int>(); var vm = new List<Vector3>(); var tm = new List<int>();
        float w = radio * Mathf.Sqrt(3f);
        float ancho = columnas * w, alto = filas * radio * 1.5f;
        // fondo de la placa
        int b0 = v.Count;
        v.AddRange(new[] { new Vector3(-ancho * 0.5f - w, -alto * 0.5f - radio, 0f), new Vector3(ancho * 0.5f + w, -alto * 0.5f - radio, 0f), new Vector3(ancho * 0.5f + w, alto * 0.5f + radio, 0f), new Vector3(-ancho * 0.5f - w, alto * 0.5f + radio, 0f) });
        t.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
        for (int f = 0; f < filas; f++)
            for (int c = 0; c < columnas; c++)
            {
                float cx = -ancho * 0.5f + (c + (f % 2 == 1 ? 0.5f : 0f)) * w + w * 0.5f, cy = -alto * 0.5f + f * radio * 1.5f + radio * 0.5f;
                // forma irregular de panal: se descartan celdas en los bordes
                float bx = Mathf.Abs(cx) / (ancho * 0.5f), by = Mathf.Abs(cy) / (alto * 0.5f);
                if (bx * bx + by * by > 1.05f + (float)rr.NextDouble() * 0.15f) continue;
                float h = hondo * (0.85f + (float)rr.NextDouble() * 0.3f);
                var exterior = new Vector3[6]; var interior = new Vector3[6];
                for (int k = 0; k < 6; k++)
                {
                    float a = Mathf.PI / 6f + k * Mathf.PI / 3f;
                    exterior[k] = new Vector3(cx + Mathf.Cos(a) * radio, cy + Mathf.Sin(a) * radio, -h);
                    interior[k] = new Vector3(cx + Mathf.Cos(a) * radio * 0.84f, cy + Mathf.Sin(a) * radio * 0.84f, -h);
                }
                for (int k = 0; k < 6; k++)
                {
                    int j = (k + 1) % 6;
                    // borde superior de la pared
                    int s = v.Count;
                    v.AddRange(new[] { exterior[k], exterior[j], interior[j], interior[k] });
                    t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                    // pared interior de la celda hacia el fondo
                    s = v.Count;
                    var fk = new Vector3(interior[k].x, interior[k].y, 0f); var fj = new Vector3(interior[j].x, interior[j].y, 0f);
                    v.AddRange(new[] { interior[k], interior[j], fj, fk });
                    t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                }
                double tipo = rr.NextDouble();
                if (tipo < 0.55)
                {   // celda llena de miel (o tapada): superficie casi al borde
                    float z = -h * (tipo < 0.4 ? 0.82f : 0.98f);
                    int s = vm.Count;
                    vm.Add(new Vector3(cx, cy, z - (tipo < 0.4 ? 0f : 0.08f)));
                    for (int k = 0; k < 6; k++) vm.Add(new Vector3(interior[k].x, interior[k].y, z));
                    for (int k = 0; k < 6; k++) tm.AddRange(new[] { s, s + 1 + (k + 1) % 6, s + 1 + k });
                }
            }
        cera = new Mesh { name = "PanalCera_" + semilla, indexFormat = IndexFormat.UInt32 }; cera.SetVertices(v); cera.SetTriangles(t, 0); cera.RecalculateNormals(); cera.RecalculateBounds();
        miel = new Mesh { name = "PanalMiel_" + semilla, indexFormat = IndexFormat.UInt32 }; miel.SetVertices(vm); miel.SetTriangles(tm, 0); miel.RecalculateNormals(); miel.RecalculateBounds();
    }

    static Mesh Revolucion(string nombre, int lados, int anillos, System.Func<float, float> radio, System.Func<float, Vector3> centro)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i <= anillos; i++)
        {
            float u = i / (float)anillos; Vector3 c = centro(u); float r = radio(u);
            for (int k = 0; k < lados; k++) { float a = k * Mathf.PI * 2f / lados; v.Add(c + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f)); }
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

    static Mesh Caja(string nombre, Vector3 tam)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var m = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(go);
        var v = m.vertices; for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], tam); m.vertices = v;
        m.name = nombre; m.RecalculateBounds();
        return m;
    }

    // hongo de repisa (crece de lado en la pared)
    // OJO: la cara de arriba y la de abajo llevan vertices PROPIOS. Si comparten vertices, RecalculateNormals
    // suma dos normales opuestas -> (0,0,0) -> NaN en el shader -> destellos blancos enormes por el Bloom.
    static Mesh HongoRepisa()
    {
        var v = new List<Vector3>(); var t = new List<int>();
        int n = 14;
        for (int lado = 0; lado < 2; lado++)
        {
            int c = v.Count;
            v.Add(new Vector3(0f, 0.25f, 0f));
            for (int i = 0; i <= n; i++) { float a = Mathf.PI * i / n; v.Add(new Vector3(Mathf.Cos(a) * 1.4f, 0.05f + Mathf.Sin(i * 1.7f) * 0.03f, Mathf.Sin(a) * 1.0f)); }
            for (int i = 0; i < n; i++)
                if (lado == 0) t.AddRange(new[] { c, c + i + 2, c + i + 1 });
                else t.AddRange(new[] { c, c + i + 1, c + i + 2 });
        }
        var m = new Mesh { name = "HongoRepisa" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // ================================================================== construccion principal
    [MenuItem("Herramientas/Colmena/Construir")]
    public static void Construir()
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        rnd = new System.Random(4242);
        T = new Vector3(ZonaJuego.arbolCentro.x, ZonaJuego.Altura(ZonaJuego.arbolCentro.x, ZonaJuego.arbolCentro.y), ZonaJuego.arbolCentro.y);
        var viejo = GameObject.Find("ARBOL_COLMENA"); if (viejo != null) Object.DestroyImmediate(viejo);
        if (Directory.Exists(Gen)) AssetDatabase.DeleteAsset(Gen.TrimEnd('/'));
        Directory.CreateDirectory(Gen); AssetDatabase.Refresh();
        ConfigurarTexturas();
        Planear();
        var raiz = new GameObject("ARBOL_COLMENA");

        var dom = new Bounds(T + Vector3.up * 140f, new Vector3(270f, 330f, 270f));
        var trozos = Poligonizar(dom);
        Debug.Log("[Colmena] Poligonizado: " + trozos.Count + " trozos, " + trozos.Sum(t => t.v.Count) + " vertices, " + reloj.ElapsedMilliseconds + " ms");

        // ---- luces horneadas: miel en las camaras, hongos en la rampa, cupula real, bocas
        var lp = new List<Vector3>(); var lc = new List<Color>(); var la = new List<float>();
        Color ambar = new Color(1f, 0.62f, 0.18f), hongo = new Color(0.3f, 0.95f, 0.75f);
        foreach (var c in camaras) { lp.Add(Local(c.ang, 27f, c.y + 3.5f)); lc.Add(ambar * (c.tipo == "cria" ? 1.1f : 1.4f)); la.Add(22f); }
        for (int i = 0; i < 3; i++) for (int k = 0; k < 5; k++) { lp.Add(Local(AngRampa + k * 1.26f + i * 0.6f, 18f, Pisos[i] + 6f)); lc.Add(ambar * 0.9f); la.Add(20f); }
        for (int k = 0; k < 4; k++) { lp.Add(Local(k * Mathf.PI * 0.5f + 0.3f, 12f, Pisos[3] + 10f)); lc.Add(ambar * 1.3f); la.Add(28f); }
        lp.Add(T + Vector3.up * (Pisos[3] + 6f)); lc.Add(new Color(1f, 0.85f, 0.45f) * 1.6f); la.Add(26f);
        var hongos = new List<Matrix4x4>();
        for (float y = 3f; y < Pisos[0] - 4f; y += 7f)
        {
            float ang = AngRampa + y / PasoRampaMadera * Mathf.PI * 2f + 0.9f;
            Vector3 p = Local(ang, RIn - 0.6f, y + 3.2f);
            lp.Add(p + (T + Vector3.up * p.y - p).normalized * 2f); lc.Add(hongo); la.Add(18f);
            for (int j = 0; j < 4; j++)
            {
                Vector3 adentro = (T + Vector3.up * p.y - p).normalized;
                var q = Quaternion.LookRotation(Vector3.Cross(Vector3.up, adentro), Vector3.up) * Quaternion.Euler(R(-8f, 8f), 0f, R(-10f, 10f));
                hongos.Add(Matrix4x4.TRS(p + Vector3.up * (j * 0.9f) + Vector3.Cross(Vector3.up, adentro) * R(-1.5f, 1.5f), q, Vector3.one * R(0.7f, 1.5f)));
            }
        }
        // luz que entra por la entrada
        lp.Add(Local(AngEntrada, RIn - 6f, 5f)); lc.Add(new Color(0.8f, 0.85f, 0.75f)); la.Add(26f);
        HornearLuz(trozos, lp, lc, la);
        Debug.Log("[Colmena] Luz horneada, " + reloj.ElapsedMilliseconds + " ms");

        // ---- materiales y mallas del arbol
        var tex = new System.Func<string, Texture2D>(n => AssetDatabase.LoadAssetAtPath<Texture2D>(Raiz + "Texturas/" + n + ".jpg"));
        var matMadera = GuardarMaterial("Madera", Shader.Find("Hormiguero/Cueva"), m =>
        {
            m.SetTexture("_Pared", tex("bark_brown_02_diff")); m.SetTexture("_NormalPared", tex("bark_brown_02_nor"));
            m.SetTexture("_Piso", tex("rough_wood_diff")); m.SetTexture("_NormalPiso", tex("rough_wood_nor"));
            m.SetFloat("_Tamano", 18f); m.SetColor("_LuzMinima", new Color(0.035f, 0.03f, 0.025f));
        });
        var matCera = GuardarMaterial("Cera", Shader.Find("Colmena/Panal"), m => { });
        var matSuelo = AssetDatabase.LoadAssetAtPath<Material>("Assets/Exterior/Materiales/Suelo.mat");
        var arbol = new GameObject("Arbol").transform; arbol.SetParent(raiz.transform, false);
        for (int i = 0; i < trozos.Count; i++)
        {
            var t = trozos[i];
            var m = new Mesh { name = "Arbol_" + i, indexFormat = t.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(t.v); m.SetNormals(t.n); m.SetColors(t.col);
            m.subMeshCount = 3; m.SetTriangles(t.madera, 0); m.SetTriangles(t.cera, 1); m.SetTriangles(t.suelo, 2);
            m.RecalculateBounds();
            m = Guardar(m, "Arbol_" + i);
            var go = new GameObject("Trozo_" + i); go.transform.SetParent(arbol, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { matMadera, matCera, matSuelo };
            mr.shadowCastingMode = ShadowCastingMode.On; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.AddComponent<MeshCollider>().sharedMesh = m;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
        Physics.SyncTransforms();

        // ---- instancias: panales 3D, gotas de miel, escalones, hongos, copa
        var gr = new List<Hormiguero.GrupoInstancias>();
        var matCera3D = Lit("Cera3D", new Color(0.95f, 0.74f, 0.33f), 0.45f, new Color(0.08f, 0.05f, 0.01f));
        var matMiel = Lit("Miel", new Color(1f, 0.56f, 0.06f), 0.92f, new Color(0.55f, 0.24f, 0.02f));
        var panales = new List<(Mesh c, Mesh m)>();
        for (int i = 0; i < 3; i++) { MallaPanal(9 + i * 2, 7 + i, 0.75f, 0.9f, 10 + i, out var mc, out var mm); panales.Add((Guardar(mc, mc.name), Guardar(mm, mm.name))); }
        var listasPanal = new List<Matrix4x4>[3]; for (int i = 0; i < 3; i++) listasPanal[i] = new List<Matrix4x4>();
        void Panal(Vector3 p, Vector3 normal, float escala)
        {
            int k = rnd.Next(3);
            listasPanal[k].Add(Matrix4x4.TRS(p, Quaternion.LookRotation(-normal, Vector3.up) * Quaternion.Euler(0f, 0f, R(-12f, 12f)), Vector3.one * escala));
        }
        // en el fondo de cada camara y en la pared del pozo
        foreach (var c in camaras)
        {
            for (int j = -1; j <= 1; j++)
            {
                float a = c.ang + j * 0.22f;
                Vector3 desde = Local(a, 16f, c.y + R(2.5f, 5.5f));
                if (Physics.Raycast(desde, Dir(a), out var hit, 24f, ~0, QueryTriggerInteraction.Ignore)) Panal(hit.point + hit.normal * 0.05f, hit.normal, R(1.1f, 1.6f));
            }
        }
        for (int i = 0; i < 3; i++)
            for (int k = 0; k < 7; k++)
            {
                float a = k * Mathf.PI * 2f / 7f + i * 0.4f;
                Vector3 desde = Local(a, RBanda + 1f, Pisos[i] + R(10f, 20f));
                if (Physics.Raycast(desde, Dir(a), out var hit, 14f, ~0, QueryTriggerInteraction.Ignore)) Panal(hit.point + hit.normal * 0.05f, hit.normal, R(1.3f, 2.1f));
            }
        // en la cupula real, colgando alrededor del trono
        for (int k = 0; k < 10; k++)
        {
            float a = k * Mathf.PI * 2f / 10f;
            Vector3 desde = Local(a, 8f, Pisos[3] + R(8f, 20f));
            if (Physics.Raycast(desde, Dir(a), out var hit, 16f, ~0, QueryTriggerInteraction.Ignore)) Panal(hit.point + hit.normal * 0.05f, hit.normal, R(1.6f, 2.4f));
        }
        for (int i = 0; i < 3; i++)
        {
            gr.Add(new Hormiguero.GrupoInstancias { nombre = "PanalCera" + i, malla = panales[i].c, material = matCera3D, matrices = listasPanal[i].ToArray() });
            gr.Add(new Hormiguero.GrupoInstancias { nombre = "PanalMiel" + i, malla = panales[i].m, material = matMiel, matrices = listasPanal[i].ToArray() });
        }
        // gotas de miel colgando del borde de los pisos
        var gota = Guardar(Revolucion("GotaMiel", 8, 8, u => 0.45f * Mathf.Sin(Mathf.Lerp(0.05f, 1f, u) * Mathf.PI) * Mathf.Lerp(0.5f, 1f, u), u => new Vector3(0f, 0f, -u * 1.6f)), "GotaMiel");
        var gotas = new List<Matrix4x4>();
        for (int i = 0; i < 4; i++)
            for (int k = 0; k < 40; k++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector3 p = Local(a, i == 3 ? RCera - 2f : RBanda + 0.3f, Pisos[i] - 2.4f);
                gotas.Add(Matrix4x4.TRS(p, Quaternion.Euler(-90f, 0f, 0f), new Vector3(1f, 1f, R(0.6f, 2.2f))));
            }
        gr.Add(new Hormiguero.GrupoInstancias { nombre = "Gotas", malla = gota, material = matMiel, matrices = gotas.ToArray() });
        // tablones como escalones sobre la rampa de madera
        var tablon = Guardar(Caja("Tablon", new Vector3(6.5f, 0.35f, 1.4f)), "Tablon");
        var matTablon = GuardarMaterial("Tablon", Shader.Find("Universal Render Pipeline/Lit"), m => { m.SetTexture("_BaseMap", tex("rough_wood_diff")); m.SetColor("_BaseColor", new Color(0.75f, 0.65f, 0.55f)); m.SetFloat("_Smoothness", 0.1f); });
        var tablones = new List<Matrix4x4>();
        for (float s = 0.5f; s < 3f * Mathf.PI * 2f * 25f; s += 2.1f)
        {
            float ang = AngRampa + s / 25f;
            float y = (ang - AngRampa) / (Mathf.PI * 2f) * PasoRampaMadera;
            if (y > Pisos[0] - 0.5f) break;
            Vector3 p = Local(ang, 25f, y + 0.12f);
            Vector3 tan = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            var q = Quaternion.LookRotation(tan, Vector3.up) * Quaternion.Euler(-Mathf.Atan2(PasoRampaMadera, Mathf.PI * 2f * 25f) * Mathf.Rad2Deg, 90f + R(-4f, 4f), 0f);
            tablones.Add(Matrix4x4.TRS(p, q, Vector3.one));
        }
        gr.Add(new Hormiguero.GrupoInstancias { nombre = "Tablones", malla = tablon, material = matTablon, matrices = tablones.ToArray(), sombras = true });
        var mHongo = Guardar(HongoRepisa(), "HongoRepisa");
        var matHongo = Lit("HongoRepisa", new Color(0.25f, 0.6f, 0.5f), 0.5f, hongo * 1.6f);
        gr.Add(new Hormiguero.GrupoInstancias { nombre = "Hongos", malla = mHongo, material = matHongo, matrices = hongos.ToArray() });
        // copa: matas de hojas grandes alrededor de las ramas
        var hojas = AssetDatabase.LoadAllAssetsAtPath("Assets/Exterior/Modelos/shrub_02.fbx").OfType<Mesh>().Where(x => x.name.EndsWith("_LOD1")).ToArray();
        var matHojas = AssetDatabase.LoadAssetAtPath<Material>("Assets/Exterior/Materiales/Follaje_shrub_02.mat");
        if (hojas.Length > 0 && matHojas != null)
        {
            var porVar = new List<Matrix4x4>[hojas.Length]; for (int i = 0; i < hojas.Length; i++) porVar[i] = new List<Matrix4x4>();
            void Mata(Vector3 p, float esc) { porVar[rnd.Next(hojas.Length)].Add(Matrix4x4.TRS(p, Quaternion.Euler(R(-25f, 25f), R(0f, 360f), R(-25f, 25f)), Vector3.one * esc)); }
            foreach (var rm in ramas)
            {
                // a lo largo de la rama y una bola de hojas que tapa la punta
                for (int k = 0; k < 14; k++)
                {
                    float u = Mathf.Lerp(0.3f, 1.0f, k / 13f);
                    Mata(Vector3.Lerp(rm.a, rm.b, u) + Random.insideUnitSphere * 14f + Vector3.up * 5f, R(40f, 62f));
                }
                for (int k = 0; k < 10; k++) Mata(rm.b + Random.insideUnitSphere * (rm.ra > 8f ? 26f : 16f) + Vector3.up * 6f, R(45f, 70f));
            }
            // copa: cupula de hojas sobre el tronco
            for (int k = 0; k < 60; k++)
            {
                float a = R(0f, Mathf.PI * 2f), rr = 90f * Mathf.Sqrt(R(0f, 1f));
                Mata(T + Dir(a) * rr + Vector3.up * (AltoTronco + 40f + 40f * (1f - rr / 90f) + R(-12f, 12f)), R(55f, 80f));
            }
            for (int i = 0; i < hojas.Length; i++) gr.Add(new Hormiguero.GrupoInstancias { nombre = "Copa" + i, malla = hojas[i], material = matHojas, matrices = porVar[i].ToArray(), sombras = true });
        }

        // ---- gestor
        var col = raiz.AddComponent<Colmena>();
        col.centro = T; col.radioInterior = RIn; col.altoInterior = AltoHueco;
        col.lucesPos = lp.ToArray(); col.lucesColor = lc.ToArray();
        col.grupos = gr.ToArray();
        RedDeVuelo(col);
        var veg = Object.FindAnyObjectByType<VegetacionExterior>();
        if (veg != null) col.flores = veg.CimasDe(new Vector2(T.x, T.z), 170f, "Gazania", "Ursinia", "Empodium", "DienteDeLeon").Where(f => ZonaJuego.DistanciaArbol(f.x, f.z) > 55f).Take(150).ToArray();
        Debug.Log("[Colmena] Flores para polinizar: " + (col.flores != null ? col.flores.Length : 0));
        AudioClip A(string n) { return AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/" + n + ".mp3"); }
        col.zumbidoInterior = Fuente(raiz.transform, "ZumbidoInterior", A("Colmena_Interior"), 0f, T + Vector3.up * 100f, 0f);
        col.zumbidoExterior = Fuente(raiz.transform, "ZumbidoExterior", A("Colmena_Exterior"), 1f, T + Vector3.up * 110f, 200f);
        col.enjambre = Fuente(raiz.transform, "Enjambre", A("Enjambre_Furioso"), 0f, T, 0f);

        CrearAbejas(raiz.transform, col);
        CrearAntorchas(raiz.transform);

        EditorSceneManager.MarkSceneDirty(raiz.scene);
        EditorSceneManager.SaveScene(raiz.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Colmena] Construido en " + reloj.ElapsedMilliseconds + " ms. Abejas: " + col.GetComponentsInChildren<AbejaColmena>(true).Length);
    }

    static AudioSource Fuente(Transform padre, string n, AudioClip c, float espacial, Vector3 pos, float max)
    {
        var go = new GameObject(n); go.transform.SetParent(padre, false); go.transform.position = pos;
        var s = go.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.playOnAwake = true; s.volume = 0f; s.spatialBlend = espacial;
        if (max > 0f) { s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 20f; s.maxDistance = max; }
        return s;
    }

    static void ConfigurarTexturas()
    {
        foreach (var n in new[] { "bark_brown_02_nor", "rough_wood_nor", "moss_wood_nor" })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(Raiz + "Texturas/" + n + ".jpg");
            if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
        }
    }

    // ================================================================== red de vuelo de las abejas
    static void RedDeVuelo(Colmena col)
    {
        var n = new List<Vector3>(); var a = new List<int>(); var b = new List<int>();
        int Nodo(Vector3 p) { n.Add(p); return n.Count - 1; }
        void Unir(int x, int y) { a.Add(x); b.Add(y); }
        // columna del pozo central
        var pozo = new List<int>();
        for (float y = 6f; y <= Pisos[3] - 5f; y += 10f) pozo.Add(Nodo(T + Vector3.up * y));
        for (int i = 1; i < pozo.Count; i++) Unir(pozo[i - 1], pozo[i]);
        int PozoEn(float y) { int m = 0; float md = 1e9f; for (int i = 0; i < pozo.Count; i++) { float d = Mathf.Abs(n[pozo[i]].y - T.y - y); if (d < md) { md = d; m = pozo[i]; } } return m; }
        // anillos de cada piso y camaras
        for (int i = 0; i < 3; i++)
        {
            var anillo = new List<int>();
            for (int k = 0; k < 8; k++) anillo.Add(Nodo(Local(k * Mathf.PI / 4f, 17f, Pisos[i] + 5f)));
            for (int k = 0; k < 8; k++) { Unir(anillo[k], anillo[(k + 1) % 8]); if (k % 2 == 0) Unir(anillo[k], PozoEn(Pisos[i] + 5f)); }
            foreach (var c in camaras.Where(c => c.piso == i))
            {
                int ch = Nodo(Local(c.ang, 26f, c.y + 4f));
                int mejor = anillo.OrderBy(x => (n[x] - n[ch]).sqrMagnitude).First();
                Unir(ch, mejor);
            }
        }
        // cupula real
        int cupula = Nodo(T + Vector3.up * (Pisos[3] + 12f));
        for (int k = 0; k < 6; k++) { int c = Nodo(Local(k * Mathf.PI / 3f, 12f, Pisos[3] + 9f)); Unir(c, cupula); }
        // el hueco del ultimo piso conecta la cupula con el pozo
        float angH = AngRampa - 0.6f;
        int hueco = Nodo(Local(angH, 11.5f, Pisos[3] - 1f));
        Unir(hueco, cupula); Unir(hueco, PozoEn(Pisos[3] - 8f));
        // huecos del tronco hacia afuera y la entrada
        foreach (var h in huecos)
        {
            int dentro = Nodo(Local(h.ang, RIn - 8f, h.y));
            int fuera = Nodo(Local(h.ang, ROut + 14f, h.y));
            Unir(dentro, PozoEn(h.y)); Unir(dentro, fuera);
            if (h.y > Pisos[3]) Unir(dentro, cupula);
        }
        int entDentro = Nodo(Local(AngEntrada, RIn - 8f, 5f)), entFuera = Nodo(Local(AngEntrada, ROut + 14f, 5f));
        Unir(entDentro, entFuera); Unir(entDentro, pozo[0]);
        // anillo exterior alrededor del arbol (para rodearlo)
        var ext = new List<int>();
        for (int k = 0; k < 10; k++) ext.Add(Nodo(Local(AngEntrada + k * Mathf.PI / 5f, ROut + 26f, 8f)));
        for (int k = 0; k < 10; k++) Unir(ext[k], ext[(k + 1) % 10]);
        Unir(entFuera, ext[0]);
        for (int i = 0; i < n.Count; i++)
            if ((n[i] - T).y > 2f && new Vector2(n[i].x - T.x, n[i].z - T.z).magnitude > ROut + 10f && !ext.Contains(i))
                Unir(i, ext.OrderBy(x => (n[x] - n[i]).sqrMagnitude).First());
        col.nodos = n.ToArray(); col.aristasA = a.ToArray(); col.aristasB = b.ToArray();
    }

    // ================================================================== abejas, larvas y reina
    static Vector3 PuntoEnPared(float ang, float r0, float y, float maxDist, out Vector3 normal)
    {
        Vector3 desde = Local(ang, r0, y);
        if (Physics.Raycast(desde, Dir(ang), out var hit, maxDist, ~0, QueryTriggerInteraction.Ignore))
        { normal = hit.normal; return hit.point + hit.normal * 1.0f; }
        normal = -Dir(ang); return Local(ang, r0 + maxDist * 0.5f, y);
    }

    static GameObject CrearAbeja(string nombre, Transform padre, Vector3 pos, float escala, AbejaColmena.Rol rol, float vida, string nombreVisible, GameObject visualBase, GameObject part)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, R(0f, 360f), 0f));
        var vis = Object.Instantiate(visualBase, go.transform);
        vis.name = "Visual"; vis.transform.localPosition = Vector3.zero; vis.transform.localScale = Vector3.one * escala;
        var cv = vis.GetComponent<CreatureVisual>(); if (cv != null) Object.DestroyImmediate(cv);
        var hv = go.AddComponent<RatHealth>();
        var so = new SerializedObject(hv);
        so.FindProperty("enemyName").stringValue = nombreVisible;
        so.FindProperty("maxHealth").floatValue = vida; so.FindProperty("currentHealth").floatValue = vida;
        so.ApplyModifiedPropertiesWithoutUndo();
        var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(2f, 1.6f, 2.4f) * escala; bc.center = Vector3.zero;
        var ab = go.AddComponent<AbejaColmena>();
        ab.rol = rol; ab.animator = vis.GetComponentInChildren<Animator>(); ab.visual = vis.transform; ab.particulasMuerte = part;
        var dt = go.AddComponent<DropTable>();
        dt.entries = new List<DropEntry> { new DropEntry { itemId = "Aguijon", amount = 1, chance = 35f } };
        return go;
    }

    static void CrearAbejas(Transform raiz, Colmena col)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Enemies/Prefabs/Enemy_Abeja.prefab");
        var visualBase = prefab.transform.Find("Visual").gameObject;
        var part = prefab.GetComponent<CreatureAI>().deathParticlesPrefab;
        var buzz = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/Animales/Bee_Buzz.mp3");
        var tAbejas = new GameObject("Abejas").transform; tAbejas.SetParent(raiz, false);
        int nAbeja = 0;
        void Zumbido(GameObject go)
        {
            if (buzz == null || nAbeja++ % 4 != 0) return;
            var s = go.AddComponent<AnimalSound>(); s.sonidos = new[] { buzz }; s.bucle = true; s.distanciaOir = 12f; s.volumen = 0.45f; s.tono = new Vector2(1.05f, 1.3f);
        }

        // obreras: lugares de trabajo en las paredes de cera de su piso y en las camaras de miel/polen
        for (int i = 0; i < 30; i++)
        {
            int piso = i % 4;
            var pts = new List<Vector3>(); var nor = new List<Vector3>();
            for (int k = 0; k < 3; k++)
            {
                float y = piso < 3 ? Pisos[piso] + R(3f, 14f) : Pisos[3] + R(4f, 16f);
                float ang = R(0f, Mathf.PI * 2f);
                var cam = camaras.Where(c => c.piso == piso && c.tipo != "cria").ToList();
                if (piso < 3 && k == 0 && cam.Count > 0) { var c = cam[rnd.Next(cam.Count)]; ang = c.ang + R(-0.2f, 0.2f); y = c.y + R(2f, 6f); }
                pts.Add(PuntoEnPared(ang, piso < 3 ? RBanda : 6f, y, 22f, out var nn)); nor.Add(nn);
            }
            var go = CrearAbeja("Obrera_" + i, tAbejas, pts[0], 0.8f, AbejaColmena.Rol.Obrera, 22f, "Abeja obrera", visualBase, part);
            var ab = go.GetComponent<AbejaColmena>(); ab.puntos = pts.ToArray(); ab.normales = nor.ToArray(); ab.velocidad = R(4.5f, 6.5f);
            Zumbido(go);
        }
        // guarderias: larvas en sus celdas y nodrizas que las cuidan
        var mLarva = Guardar(Revolucion("LarvaAbeja", 12, 18, u => 0.5f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.Lerp(0.05f, 0.95f, u) * Mathf.PI)), 0.55f) * (1f + 0.1f * Mathf.Cos(u * Mathf.PI * 14f)),
            u => new Vector3(0f, 0.5f + Mathf.Sin(u * Mathf.PI) * 0.6f, u * 2.4f - 1.2f)), "LarvaAbeja");
        var matLarva = Lit("LarvaAbeja", new Color(0.98f, 0.95f, 0.84f), 0.75f, new Color(0.08f, 0.07f, 0.05f));
        var aplastar = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Larva_Aplastar.mp3");
        var tLarvas = new GameObject("Larvas").transform; tLarvas.SetParent(raiz, false);
        int nl = 0;
        foreach (var c in camaras.Where(c => c.tipo == "cria"))
        {
            var cerca = new List<Vector3>();
            for (int i = 0; i < 9; i++)
            {
                // en el piso de la camara, mas hacia el fondo
                Vector3 desde = Local(c.ang + R(-0.25f, 0.25f), R(23f, 30f), c.y + 6f);
                if (!Physics.Raycast(desde, Vector3.down, out var hit, 12f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var go = new GameObject("Larva_" + nl++);
                go.transform.SetParent(tLarvas, false);
                go.transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, R(0f, 360f), 0f));
                go.transform.localScale = Vector3.one * R(1.1f, 1.5f);
                go.AddComponent<MeshFilter>().sharedMesh = mLarva;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = matLarva; mr.shadowCastingMode = ShadowCastingMode.Off;
                var hv = go.AddComponent<RatHealth>();
                var so = new SerializedObject(hv); so.FindProperty("enemyName").stringValue = "Larva de abeja"; so.FindProperty("maxHealth").floatValue = 10f; so.FindProperty("currentHealth").floatValue = 10f; so.ApplyModifiedPropertiesWithoutUndo();
                var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.center = new Vector3(0f, 0.6f, 0f); bc.size = new Vector3(1.2f, 1.2f, 2.6f);
                go.AddComponent<LarvaAbeja>().sonidoAplastar = aplastar;
                cerca.Add(hit.point + Vector3.up * 1.6f);
            }
            for (int i = 0; i < 4 && cerca.Count > 0; i++)
            {
                var pts = Enumerable.Range(0, 3).Select(_ => cerca[rnd.Next(cerca.Count)] + new Vector3(R(-0.6f, 0.6f), R(0f, 0.6f), R(-0.6f, 0.6f))).ToArray();
                var go = CrearAbeja("Nodriza_" + c.piso + "_" + i, tAbejas, pts[0], 0.75f, AbejaColmena.Rol.Nodriza, 20f, "Abeja nodriza", visualBase, part);
                var ab = go.GetComponent<AbejaColmena>(); ab.puntos = pts; ab.normales = pts.Select(_ => Vector3.up).ToArray(); ab.velocidad = 3.5f;
                Zumbido(go);
            }
        }
        // guardias: frente a la entrada, en el pozo y en la cupula real
        void Guardia(string n, Vector3 centro, float radio)
        {
            var go = CrearAbeja(n, tAbejas, centro + Random.insideUnitSphere * radio * 0.5f, 0.95f, AbejaColmena.Rol.Guardia, 30f, "Abeja guardia", visualBase, part);
            var ab = go.GetComponent<AbejaColmena>(); ab.puntos = new[] { centro }; ab.radioPatrulla = radio; ab.velocidad = 5f; ab.danio = 9f;
            Zumbido(go);
        }
        for (int i = 0; i < 4; i++) Guardia("GuardiaEntrada_" + i, Local(AngEntrada, ROut + 10f, 6f + i * 2f), 6f);
        for (int i = 0; i < 4; i++) Guardia("GuardiaPozo_" + i, T + Vector3.up * (Pisos[0] + 10f + i * 18f), 5f);
        for (int i = 0; i < 6; i++) Guardia("GuardiaReal_" + i, T + Vector3.up * (Pisos[3] + 10f), 11f);

        // forrajeras: salen por los huecos del tronco hacia las flores
        var hivHuecos = huecos.Where(h => h.y > Pisos[0]).ToList();
        for (int i = 0; i < 16; i++)
        {
            var h = hivHuecos[i % hivHuecos.Count];
            var ruta = new[] { T + Vector3.up * h.y, Local(h.ang, RIn - 9f, h.y), Local(h.ang, ROut + 8f, h.y), Local(h.ang, ROut + 30f, Mathf.Max(10f, h.y - 30f)) };
            var pts = new[] { PuntoEnPared(h.ang + R(-0.5f, 0.5f), RBanda, h.y + R(-4f, 4f), 22f, out var n0) };
            Vector3 inicio = Vector3.Lerp(ruta[0], ruta[ruta.Length - 1], R(0f, 1f));
            var go = CrearAbeja("Forrajera_" + i, tAbejas, inicio, 0.8f, AbejaColmena.Rol.Forrajera, 22f, "Abeja forrajera", visualBase, part);
            var ab = go.GetComponent<AbejaColmena>(); ab.ruta = ruta; ab.puntos = pts; ab.normales = new[] { n0 }; ab.velocidad = R(7f, 9f);
            Zumbido(go);
        }

        // plantilla de soldado (refuerzos y llamado de la reina)
        var plant = CrearAbeja("PlantillaSoldado", raiz, T + Vector3.up * 100f, 0.95f, AbejaColmena.Rol.Guardia, 28f, "Abeja soldado", visualBase, part);
        plant.GetComponent<AbejaColmena>().danio = 9f;
        plant.SetActive(false);
        col.plantillaSoldado = plant.GetComponent<AbejaColmena>();
        var refuerzo = new List<Vector3>();
        foreach (var h in huecos) refuerzo.Add(Local(h.ang, ROut + 6f, h.y));
        for (int i = 0; i < 3; i++) refuerzo.Add(T + Vector3.up * (Pisos[i] + 8f));
        refuerzo.Add(Local(AngEntrada, ROut + 8f, 6f));
        col.puntosRefuerzo = refuerzo.ToArray();

        // la reina, sobre el trono de la cupula
        Vector3 trono = T + Vector3.up * (Pisos[3] + 4.2f);
        var reina = CrearAbeja("ReinaAbeja", raiz, trono, 3.2f, AbejaColmena.Rol.Guardia, 1600f, "Reina Abeja", visualBase, part);
        Object.DestroyImmediate(reina.GetComponent<AbejaColmena>());
        var mr2 = reina.GetComponentInChildren<SkinnedMeshRenderer>();
        if (mr2 != null)
        {
            var mq = new Material(mr2.sharedMaterial) { name = "ReinaAbeja" };
            if (mq.HasProperty("baseColorFactor")) mq.SetColor("baseColorFactor", new Color(1f, 0.78f, 0.45f));
            AssetDatabase.CreateAsset(mq, Gen + "ReinaAbeja.mat");
            mr2.sharedMaterial = mq;
        }
        var au = reina.AddComponent<AudioSource>(); au.spatialBlend = 1f; au.maxDistance = 120f; au.rolloffMode = AudioRolloffMode.Linear; au.playOnAwake = false;
        var rq = reina.AddComponent<ReinaAbeja>();
        rq.animator = reina.GetComponentInChildren<Animator>(); rq.visual = reina.transform.Find("Visual");
        rq.centroCamara = T + Vector3.up * Pisos[3]; rq.radioCamara = RCera - 3f; rq.alturaVuelo = 11f;
        rq.plantillaSoldado = col.plantillaSoldado; rq.particulasMuerte = part;
        rq.chillido = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Reina_Chillido.mp3");
        rq.zumbido = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Enjambre_Furioso.mp3");
        reina.GetComponent<DropTable>().entries = new List<DropEntry>
        {
            new DropEntry { itemId = "Aguijon", amount = 5, chance = 100f }, new DropEntry { itemId = "Alas", amount = 3, chance = 100f },
            new DropEntry { itemId = "Exoesqueleto", amount = 2, chance = 100f },
        };
    }

    // ================================================================== antorchas
    static void CrearAntorchas(Transform raiz)
    {
        var fog = GameObject.Find("Fogata");
        var llamas = fog != null ? fog.transform.Find("Llamas") : null;
        var humo = fog != null ? fog.transform.Find("Humo") : null;
        var palo = Guardar(Revolucion("PaloAntorcha", 8, 4, u => Mathf.Lerp(0.07f, 0.055f, u), u => new Vector3(0f, 0f, u * 1.1f)), "PaloAntorcha");
        var cabeza = Guardar(Revolucion("CabezaAntorcha", 10, 5, u => 0.1f + 0.035f * Mathf.Sin(u * Mathf.PI), u => new Vector3(0f, 0f, 1.0f + u * 0.32f)), "CabezaAntorcha");
        var matPalo = Lit("PaloAntorcha", new Color(0.42f, 0.28f, 0.16f), 0.15f, Color.black);
        var matTrapo = Lit("TrapoAntorcha", new Color(0.18f, 0.13f, 0.09f), 0.1f, new Color(0.35f, 0.1f, 0.02f));
        var icono = IconoAntorcha();

        GameObject Modelo(string n, bool encendida, float esc)
        {
            var go = new GameObject(n);
            var p = new GameObject("Palo"); p.transform.SetParent(go.transform, false); p.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            p.AddComponent<MeshFilter>().sharedMesh = palo; p.AddComponent<MeshRenderer>().sharedMaterial = matPalo;
            var c = new GameObject("Cabeza"); c.transform.SetParent(go.transform, false); c.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            c.AddComponent<MeshFilter>().sharedMesh = cabeza; c.AddComponent<MeshRenderer>().sharedMaterial = matTrapo;
            if (encendida)
            {
                var fuego = new GameObject("Fuego").transform; fuego.SetParent(go.transform, false); fuego.localPosition = new Vector3(0f, 1.32f, 0f);
                foreach (var ps in new[] { llamas, humo })
                {
                    if (ps == null) continue;
                    var cp = Object.Instantiate(ps.gameObject, fuego);
                    cp.transform.localPosition = Vector3.zero; cp.transform.localRotation = ps.localRotation;
                    float k = ps == humo ? 0.22f : 0.12f;
                    cp.transform.localScale = Vector3.one * k;
                    var sys = cp.GetComponent<ParticleSystem>(); var main = sys.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.simulationSpace = ParticleSystemSimulationSpace.World;
                }
                var luz = new GameObject("Luz").AddComponent<Light>(); luz.transform.SetParent(fuego, false); luz.transform.localPosition = Vector3.up * 0.3f;
                luz.type = LightType.Point; luz.color = new Color(1f, 0.62f, 0.3f); luz.range = 14f; luz.intensity = 30f; luz.shadows = LightShadows.None;
                luz.gameObject.AddComponent<ParpadeoLuz>();
            }
            go.transform.localScale = Vector3.one * esc;
            return go;
        }

        // la que lleva el jugador
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc != null)
        {
            var an = pc.GetComponent<Antorcha>(); if (an == null) an = pc.gameObject.AddComponent<Antorcha>();
            var vieja = pc.transform.Find("AntorchaMano"); if (vieja != null) Object.DestroyImmediate(vieja.gameObject);
            foreach (var t in pc.GetComponentsInChildren<Transform>(true)) if (t.name == "AntorchaMano") Object.DestroyImmediate(t.gameObject);
            var m = Modelo("AntorchaMano", true, 1.3f);
            m.transform.SetParent(pc.transform, false);
            m.SetActive(false);
            an.modeloMano = m;
            an.sonidoEncender = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Antorcha_Encender.mp3");
            an.sonidoFuego = AssetDatabase.LoadAssetAtPath<AudioClip>(Raiz + "Audio/Antorcha_Fuego.mp3");
            EditorUtility.SetDirty(an);
        }
        // antorchas para recoger: junto a la entrada del arbol y junto a la fogata del salon
        foreach (var g in Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include)) if (g.itemId == Antorcha.ItemId) Object.DestroyImmediate(g.gameObject);
        var lugares = new List<(Vector3 p, float ang)>
        {
            (Local(AngEntrada + 0.35f, ROut + 9f, 0f), 0f), (Local(AngEntrada - 0.4f, ROut + 11f, 0f), 30f),
        };
        if (fog != null) lugares.Add((fog.transform.position + new Vector3(4.5f, 0f, 3.5f), 70f));
        int i = 0;
        foreach (var l in lugares)
        {
            Vector3 p = l.p;
            if (Physics.Raycast(p + Vector3.up * 10f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
            var go = Modelo("Antorcha_" + i++, false, 1.3f);
            go.transform.SetPositionAndRotation(p + Vector3.up * 0.12f, Quaternion.Euler(0f, l.ang, 88f));
            var wi = go.AddComponent<WorldItem>();
            wi.itemId = Antorcha.ItemId; wi.displayName = "Antorcha"; wi.itemIcon = icono; wi.quantity = 1;
            var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.center = new Vector3(0f, 0.65f, 0f); bc.size = new Vector3(0.6f, 1.5f, 0.6f);
            go.AddComponent<ItemSparkle>();
        }
    }

    static Sprite IconoAntorcha()
    {
        string path = Raiz + "Sprites/Antorcha.png";
        if (!File.Exists(path))
        {
            int s = 128; var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var px = new Color[s * s];
            for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
            {
                float u = x / (float)s, v = y / (float)s; Color c = Color.clear;
                // palo inclinado
                float d = Mathf.Abs((u - 0.5f) - (v - 0.45f) * 0.45f);
                if (v < 0.7f && v > 0.08f && d < 0.045f) c = new Color(0.5f, 0.32f, 0.17f);
                if (v > 0.6f && v < 0.74f && d < 0.075f) c = new Color(0.2f, 0.14f, 0.1f);
                // llama
                float fx = (u - 0.565f) / 0.13f, fy = (v - 0.83f) / 0.16f;
                float f = 1f - (fx * fx + fy * fy * (fy > 0 ? 1.8f : 0.8f));
                if (f > 0f) c = Color.Lerp(new Color(1f, 0.35f, 0.05f), new Color(1f, 0.92f, 0.45f), f * 1.5f);
                px[y * s + x] = c;
            }
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.Sprite; ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
