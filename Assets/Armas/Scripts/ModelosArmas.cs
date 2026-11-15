using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Modelos de las armas y de sus piezas (lapiz, navaja de sacapuntas...) hechos por codigo,
/// como el resto del juego: prismas, conos y tubos con colores planos de material escolar.
/// Convencion: el mango empieza en el origen y el arma crece hacia +Y (la mano agarra en y ~ 0.12).
/// Medidas en unidades del mundo (el jugador mide ~2).
/// </summary>
public static class ModelosArmas
{
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    public static Material Mat(string nombre, Color c, float suave = 0.35f, float metal = 0f, Color? emision = null)
    {
        Material m;
        string clave = nombre + (emision.HasValue ? "_e" : "");
        if (mats.TryGetValue(clave, out m) && m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");
        m = new Material(sh) { name = "Arma_" + nombre };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", suave);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (emision.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emision.Value);
        }
        mats[clave] = m;
        return m;
    }

    static Material Amarillo => Mat("LapizAmarillo", new Color(1f, 0.76f, 0.1f), 0.5f);
    static Material Madera => Mat("Madera", new Color(0.86f, 0.66f, 0.42f), 0.2f);
    static Material Grafito => Mat("Grafito", new Color(0.18f, 0.18f, 0.2f), 0.6f, 0.3f);
    static Material Metal => Mat("Metal", new Color(0.78f, 0.8f, 0.84f), 0.8f, 0.9f);
    static Material MetalOscuro => Mat("MetalOscuro", new Color(0.35f, 0.36f, 0.4f), 0.7f, 0.9f);
    static Material Palo => Mat("Palo", new Color(0.55f, 0.36f, 0.2f), 0.15f);
    static Material Rosa => Mat("Goma", new Color(0.98f, 0.62f, 0.66f), 0.1f);
    static Material Blanco => Mat("Blanco", new Color(0.95f, 0.94f, 0.9f), 0.2f);
    static Material Liga => Mat("Liga", new Color(0.95f, 0.35f, 0.45f), 0.3f);
    static Material Negro => Mat("Broche", new Color(0.08f, 0.08f, 0.09f), 0.6f, 0.5f);
    static Material Hueso => Mat("Cuerno", new Color(0.86f, 0.8f, 0.66f), 0.4f);
    static Material Veneno => Mat("Veneno", new Color(0.35f, 0.9f, 0.25f), 0.7f, 0f, new Color(0.25f, 0.9f, 0.15f) * 1.6f);
    static Material Tela => Mat("Tela", new Color(0.92f, 0.9f, 0.84f), 0.05f);
    static Material ChinchetaRoja => Mat("ChinchetaRoja", new Color(0.9f, 0.15f, 0.15f), 0.6f);

    /// <summary>Crea el modelo de un objeto (arma o pieza). Devuelve null si no hay modelo para ese id.</summary>
    public static GameObject Crear(string id)
    {
        var raiz = new GameObject("Modelo_" + id);
        switch (id)
        {
            case "Lapiz": LapizCorto(raiz.transform, 0f, 0.9f); break;
            case "Navaja": Navaja(raiz.transform, new Vector3(0f, 0.12f, 0f), Quaternion.identity, 1f); break;
            case "Venda":
                Pieza(raiz, Tubo(0.16f, 0.05f, 16, 0.22f), Tela, Vector3.up * 0.11f, Quaternion.identity);
                Pieza(raiz, Cilindro(0.06f, 0.2f, 12), Liga, Vector3.up * 0.11f, Quaternion.Euler(0, 0, 90));
                break;
            case "Lanza":
            case "PicaVenenosa":
                LapizLargo(raiz.transform, 1.45f);
                Navaja(raiz.transform, new Vector3(0f, 1.5f, 0f), Quaternion.Euler(0, 0, 0), 1.1f);
                Pieza(raiz, Tubo(0.075f, 0.02f, 12, 0.08f), Liga, Vector3.up * 1.43f, Quaternion.identity);
                if (id == "PicaVenenosa")
                {
                    Pieza(raiz, Cono(0.05f, 0.32f, 10), Veneno, Vector3.up * 1.85f, Quaternion.identity);
                    Pieza(raiz, Tubo(0.08f, 0.018f, 12, 0.05f), Veneno, Vector3.up * 1.3f, Quaternion.identity);
                }
                break;
            case "Hacha":
                Pieza(raiz, Cilindro(0.055f, 1.1f, 10), Palo, Vector3.zero, Quaternion.identity);
                Navaja(raiz.transform, new Vector3(0.03f, 0.95f, 0f), Quaternion.Euler(0, 0, -90), 1.25f);
                Pieza(raiz, Tubo(0.07f, 0.02f, 12, 0.1f), Liga, Vector3.up * 0.95f, Quaternion.identity);
                break;
            case "Mazo":
                Pieza(raiz, Caja(new Vector3(0.12f, 0.55f, 0.12f)), Rosa, Vector3.zero, Quaternion.identity);       // mango de goma
                Pieza(raiz, Caja(new Vector3(0.3f, 0.42f, 0.26f)), Blanco, Vector3.up * 0.55f, Quaternion.identity);   // cabeza de borrador
                for (int i = 0; i < 10; i++)
                {
                    float a = i * 36f;
                    float y = 0.62f + (i % 2) * 0.2f;
                    var r = Quaternion.Euler(0, a, 0);
                    Vector3 dir = r * Vector3.forward;
                    Vector3 p = Vector3.up * y + new Vector3(dir.x * 0.15f, 0f, dir.z * 0.13f);
                    // la punta hacia fuera: un mazo de pinchos
                    Chincheta(raiz.transform, p, Quaternion.FromToRotation(Vector3.up, dir));
                }
                break;
            case "Latigo":
                {
                    // clip enderezado: alambre fino que ondula, con la chincheta doblada de garfio
                    var pts = new List<Vector3>();
                    for (int i = 0; i <= 24; i++)
                    {
                        float t = i / 24f;
                        pts.Add(new Vector3(Mathf.Sin(t * 9f) * 0.035f * t, t * 1.5f, Mathf.Cos(t * 7f) * 0.02f * t));
                    }
                    Pieza(raiz, Cuerda(pts, 0.022f, 8), Metal, Vector3.zero, Quaternion.identity);
                    // empuñadura: la vuelta del clip
                    Pieza(raiz, Tubo(0.06f, 0.022f, 14, 0.02f), Metal, Vector3.up * 0.1f, Quaternion.Euler(90, 0, 0));
                    var garfio = new List<Vector3>();
                    for (int i = 0; i <= 10; i++)
                    {
                        float a = i / 10f * Mathf.PI * 1.2f;
                        garfio.Add(new Vector3(Mathf.Sin(a) * 0.09f, 1.5f + (1f - Mathf.Cos(a)) * 0.09f + i * 0.004f, 0f));
                    }
                    Pieza(raiz, Cuerda(garfio, 0.02f, 8), MetalOscuro, Vector3.zero, Quaternion.identity);
                    Pieza(raiz, Cono(0.03f, 0.08f, 8), MetalOscuro, garfio[garfio.Count - 1], Quaternion.Euler(0, 0, 160));
                    Pieza(raiz, Cilindro(0.06f, 0.025f, 14), ChinchetaRoja, new Vector3(0f, 1.48f, 0f), Quaternion.identity);
                }
                break;
            case "Martillo":
            case "MartilloCuerno":
                Pieza(raiz, Cilindro(0.055f, 1.0f, 10), Palo, Vector3.zero, Quaternion.identity);
                // la tuerca: prisma hexagonal con agujero (se ve como tuerca)
                Pieza(raiz, TuercaMalla(0.2f, 0.07f, 0.16f), MetalOscuro, Vector3.up * 0.98f, Quaternion.Euler(90, 0, 0));
                if (id == "MartilloCuerno")
                {
                    Pieza(raiz, Cono(0.075f, 0.38f, 12), Hueso, new Vector3(0f, 0.98f, 0.12f), Quaternion.Euler(90, 0, 0));
                    Pieza(raiz, Cono(0.075f, 0.38f, 12), Hueso, new Vector3(0f, 0.98f, -0.12f), Quaternion.Euler(-90, 0, 0));
                }
                break;
            case "Tenazas":
                // las dos patas del broche cerradas abajo (mango) y abiertas arriba (boca), con el tornillo de eje
                Pieza(raiz, Caja(new Vector3(0.055f, 1.0f, 0.1f)), Negro, new Vector3(-0.03f, 0f, 0f), Quaternion.Euler(0, 0, 7));
                Pieza(raiz, Caja(new Vector3(0.055f, 1.0f, 0.1f)), Negro, new Vector3(0.03f, 0f, 0f), Quaternion.Euler(0, 0, -7));
                // dientes de la boca, hacia dentro
                Pieza(raiz, Caja(new Vector3(0.12f, 0.06f, 0.1f)), MetalOscuro, new Vector3(-0.12f, 0.95f, 0f), Quaternion.Euler(0, 0, -20));
                Pieza(raiz, Caja(new Vector3(0.12f, 0.06f, 0.1f)), MetalOscuro, new Vector3(0.12f, 0.95f, 0f), Quaternion.Euler(0, 0, 20));
                // tornillo que atraviesa las dos patas + tuerca
                Pieza(raiz, Cilindro(0.03f, 0.26f, 10), Metal, new Vector3(0.13f, 0.4f, 0f), Quaternion.Euler(0, 0, 90));
                Pieza(raiz, Cilindro(0.055f, 0.03f, 6), Metal, new Vector3(0.16f, 0.4f, 0f), Quaternion.Euler(0, 0, 90));
                Pieza(raiz, TuercaMalla(0.05f, 0.02f, 0.04f), MetalOscuro, new Vector3(-0.15f, 0.4f, 0f), Quaternion.Euler(0, 0, 90));
                // mango forrado con liga
                Pieza(raiz, Tubo(0.06f, 0.02f, 12, 0.12f), Liga, new Vector3(0f, 0.12f, 0f), Quaternion.identity);
                break;
            default:
                Object.Destroy(raiz);
                return null;
        }
        return raiz;
    }

    // ------------------------------------------------------------------ piezas compuestas
    static void LapizLargo(Transform padre, float largo)
    {
        Pieza(padre.gameObject, Prisma(6, 0.06f, largo), Amarillo, Vector3.zero, Quaternion.identity);
        Pieza(padre.gameObject, Cilindro(0.062f, 0.08f, 12), Metal, Vector3.zero, Quaternion.identity);               // casquillo
        Pieza(padre.gameObject, Cilindro(0.058f, 0.1f, 12), Rosa, Vector3.down * 0.1f, Quaternion.identity);           // goma
    }

    static void LapizCorto(Transform padre, float y0, float largo)
    {
        Pieza(padre.gameObject, Prisma(6, 0.06f, largo), Amarillo, Vector3.up * y0, Quaternion.identity);
        Pieza(padre.gameObject, Cono(0.06f, 0.18f, 12), Madera, Vector3.up * (y0 + largo), Quaternion.identity);
        Pieza(padre.gameObject, Cono(0.022f, 0.065f, 8), Grafito, Vector3.up * (y0 + largo + 0.115f), Quaternion.identity);
    }

    static void Navaja(Transform padre, Vector3 pos, Quaternion rot, float k)
    {
        // hoja trapezoidal con filo biselado y el agujero del tornillo
        var go = Pieza(padre.gameObject, Hoja(0.16f * k, 0.36f * k, 0.018f * k), Metal, pos, rot);
        Pieza(go, Cilindro(0.022f * k, 0.022f * k, 10), MetalOscuro, new Vector3(0f, 0.08f * k, 0f), Quaternion.Euler(90, 0, 0));
    }

    static void Chincheta(Transform padre, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject("Chincheta");
        go.transform.SetParent(padre, false);
        go.transform.localPosition = pos; go.transform.localRotation = rot;
        Pieza(go, Cilindro(0.045f, 0.02f, 12), ChinchetaRoja, Vector3.zero, Quaternion.identity);
        Pieza(go, Cono(0.01f, 0.09f, 6), Metal, Vector3.up * 0.02f, Quaternion.identity);
    }

    static GameObject Pieza(GameObject padre, Mesh malla, Material mat, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(malla.name);
        go.transform.SetParent(padre.transform, false);
        go.transform.localPosition = pos; go.transform.localRotation = rot;
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go;
    }

    // ------------------------------------------------------------------ mallas
    static readonly Dictionary<string, Mesh> cacheMallas = new Dictionary<string, Mesh>();
    static Mesh Cache(string clave, System.Func<Mesh> f)
    {
        Mesh m;
        if (cacheMallas.TryGetValue(clave, out m) && m != null) return m;
        m = f(); m.name = clave; m.RecalculateBounds();
        cacheMallas[clave] = m;
        return m;
    }

    /// <summary>Prisma de n lados de y=0 a y=alto (lapiz hexagonal; muchos lados = cilindro).</summary>
    public static Mesh Prisma(int lados, float radio, float alto) => Cache($"prisma{lados}_{radio}_{alto}", () =>
    {
        var v = new List<Vector3>(); var t = new List<int>(); var n = new List<Vector3>();
        for (int i = 0; i < lados; i++)
        {
            float a0 = i * Mathf.PI * 2f / lados, a1 = (i + 1) * Mathf.PI * 2f / lados;
            Vector3 p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radio, p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radio;
            Vector3 nn = lados > 8 ? Vector3.zero : new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0, Mathf.Sin((a0 + a1) * 0.5f));
            int b = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p1 + Vector3.up * alto); v.Add(p0 + Vector3.up * alto);
            if (lados > 8) { n.Add(p0.normalized); n.Add(p1.normalized); n.Add(p1.normalized); n.Add(p0.normalized); }
            else { n.Add(nn); n.Add(nn); n.Add(nn); n.Add(nn); }
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        // tapas
        for (int cara = 0; cara < 2; cara++)
        {
            float y = cara == 0 ? 0f : alto;
            Vector3 nn = cara == 0 ? Vector3.down : Vector3.up;
            int c = v.Count; v.Add(Vector3.up * y); n.Add(nn);
            for (int i = 0; i <= lados; i++) { float a = i * Mathf.PI * 2f / lados; v.Add(new Vector3(Mathf.Cos(a) * radio, y, Mathf.Sin(a) * radio)); n.Add(nn); }
            for (int i = 0; i < lados; i++)
                if (cara == 1) t.AddRange(new[] { c, c + i + 2, c + i + 1 }); else t.AddRange(new[] { c, c + i + 1, c + i + 2 });
        }
        var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); return m;
    });

    public static Mesh Cilindro(float radio, float alto, int lados) => Prisma(Mathf.Max(lados, 9), radio, alto);

    public static Mesh Cono(float radio, float alto, int lados) => Cache($"cono{lados}_{radio}_{alto}", () =>
    {
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < lados; i++)
        {
            float a0 = i * Mathf.PI * 2f / lados, a1 = (i + 1) * Mathf.PI * 2f / lados;
            int b = v.Count;
            v.Add(new Vector3(Mathf.Cos(a0) * radio, 0, Mathf.Sin(a0) * radio));
            v.Add(new Vector3(Mathf.Cos(a1) * radio, 0, Mathf.Sin(a1) * radio));
            v.Add(Vector3.up * alto);
            t.AddRange(new[] { b, b + 2, b + 1 });
            int c = v.Count;
            v.Add(Vector3.zero); v.Add(new Vector3(Mathf.Cos(a0) * radio, 0, Mathf.Sin(a0) * radio)); v.Add(new Vector3(Mathf.Cos(a1) * radio, 0, Mathf.Sin(a1) * radio));
            t.AddRange(new[] { c, c + 1, c + 2 });
        }
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
    });

    public static Mesh Caja(Vector3 tam) => Cache($"caja{tam.x}_{tam.y}_{tam.z}", () =>
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var src = g.GetComponent<MeshFilter>().sharedMesh;
        var v = src.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = new Vector3(v[i].x * tam.x, (v[i].y + 0.5f) * tam.y, v[i].z * tam.z);
        var m = new Mesh(); m.vertices = v; m.normals = src.normals; m.uv = src.uv; m.triangles = src.triangles;
        if (Application.isPlaying) Object.Destroy(g); else Object.DestroyImmediate(g);
        return m;
    });

    /// <summary>Anillo (toro) acostado en el plano XZ, centrado en el origen y con algo de alto.</summary>
    public static Mesh Tubo(float radio, float grosor, int lados, float alto) => Cache($"tubo{radio}_{grosor}_{lados}_{alto}", () =>
    {
        var pts = new List<Vector3>();
        for (int i = 0; i <= lados; i++) { float a = i * Mathf.PI * 2f / lados; pts.Add(new Vector3(Mathf.Cos(a) * radio, 0f, Mathf.Sin(a) * radio)); }
        var m = CuerdaMalla(pts, grosor + alto * 0.25f, 8);
        return m;
    });

    /// <summary>Tubo que sigue una linea de puntos (alambre, cuerda).</summary>
    public static Mesh Cuerda(List<Vector3> pts, float grosor, int lados)
    {
        var m = CuerdaMalla(pts, grosor, lados);
        m.name = "cuerda";
        return m;
    }

    static Mesh CuerdaMalla(List<Vector3> pts, float grosor, int lados)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        Vector3 refUp = Vector3.forward;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 d = (i < pts.Count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
            if (Mathf.Abs(Vector3.Dot(d, refUp)) > 0.9f) refUp = Vector3.right;
            Vector3 a = Vector3.Cross(d, refUp).normalized, b = Vector3.Cross(d, a);
            for (int k = 0; k < lados; k++)
            {
                float ang = k * Mathf.PI * 2f / lados;
                v.Add(pts[i] + (a * Mathf.Cos(ang) + b * Mathf.Sin(ang)) * grosor);
            }
        }
        for (int i = 0; i < pts.Count - 1; i++)
            for (int k = 0; k < lados; k++)
            {
                int k1 = (k + 1) % lados;
                int a0 = i * lados + k, a1 = i * lados + k1, b0 = (i + 1) * lados + k, b1 = (i + 1) * lados + k1;
                t.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
            }
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    /// <summary>Tuerca: prisma hexagonal con agujero redondo (eje Y), centrada en el origen.</summary>
    public static Mesh TuercaMalla(float radio, float agujero, float alto) => Cache($"tuerca{radio}_{agujero}_{alto}", () =>
    {
        var v = new List<Vector3>(); var t = new List<int>();
        int n = 6, sub = 4; int pasos = n * sub;
        float h = alto * 0.5f;
        System.Func<int, Vector3> fuera = i =>
        {
            float lado = (float)i / sub; int l0 = Mathf.FloorToInt(lado) % n; float f = lado - Mathf.Floor(lado);
            float a0 = l0 * Mathf.PI * 2f / n, a1 = (l0 + 1) * Mathf.PI * 2f / n;
            return Vector3.Lerp(new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)), f) * radio;
        };
        System.Func<int, Vector3> dentro = i => { float a = i * Mathf.PI * 2f / pasos; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * agujero; };
        for (int i = 0; i < pasos; i++)
        {
            int j = i + 1;
            Vector3 o0 = fuera(i), o1 = fuera(j), i0 = dentro(i), i1 = dentro(j);
            int b = v.Count;
            // caras de arriba y abajo
            v.AddRange(new[] { o0 + Vector3.up * h, o1 + Vector3.up * h, i1 + Vector3.up * h, i0 + Vector3.up * h });
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            b = v.Count;
            v.AddRange(new[] { o0 - Vector3.up * h, o1 - Vector3.up * h, i1 - Vector3.up * h, i0 - Vector3.up * h });
            t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
            // lado de fuera y agujero
            b = v.Count;
            v.AddRange(new[] { o0 - Vector3.up * h, o1 - Vector3.up * h, o1 + Vector3.up * h, o0 + Vector3.up * h });
            t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            b = v.Count;
            v.AddRange(new[] { i0 - Vector3.up * h, i1 - Vector3.up * h, i1 + Vector3.up * h, i0 + Vector3.up * h });
            t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
    });

    /// <summary>Hoja de navaja: trapecio con un lado afilado (cuña), centrada en X, de y=0 a y=largo.</summary>
    public static Mesh Hoja(float ancho, float largo, float grosor) => Cache($"hoja{ancho}_{largo}_{grosor}", () =>
    {
        float w = ancho * 0.5f, g = grosor * 0.5f;
        // lomo (x = -w) grueso; filo (x = +w) sin grosor; punta inclinada arriba
        Vector3[] p =
        {
            new Vector3(-w, 0, -g), new Vector3(-w, 0, g), new Vector3(w, 0, 0),
            new Vector3(-w, largo * 0.82f, -g), new Vector3(-w, largo * 0.82f, g), new Vector3(w, largo, 0),
        };
        var v = new List<Vector3>(); var t = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.AddRange(new[] { i, i + 1, i + 2 }); }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }
        Quad(p[0], p[3], p[5], p[2]);   // cara -z
        Quad(p[1], p[2], p[5], p[4]);   // cara +z
        Quad(p[0], p[1], p[4], p[3]);   // lomo
        Tri(p[0], p[2], p[1]);          // base
        Tri(p[3], p[4], p[5]);          // punta
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
    });
}
