using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Genera HAMSTERLANDIA: el parque de la feria de ciencias del 3°B en la esquina del fondo del salon.
/// Mesa plegable con mantel de papel, tubo de entrada desde el piso (arco y taquilla), la plaza con el
/// cartel de la feria y la caja musical, la Gran Carpa con su escenario y la rueda de Bolita, el carrusel
/// de tapitas, la noria, el Mirador del Jugo, un laberinto de tubos transparentes con conectores en dos
/// niveles (con grietas y un tubo sin salida), la escalera de borradores (solo para el jugador), la caja de
/// pañuelos con la casita de Bolita arriba (su coleccion y la pila verde) y el tobogan en espiral por dentro.
/// Tambien arma a Bolita (por codigo) y el grafo de piso por donde cabe, la musica y los sonidos.
/// Menu: Herramientas > Hamsterlandia > Construir (borra y vuelve a generar todo).
/// </summary>
public static class HamsterBuilder
{
    const string Raiz = "Assets/Hamster/";
    const string Gen = Raiz + "Generado/";
    const string Mat = Raiz + "Materiales/";
    const string Tex = Raiz + "Texturas/";

    // medidas de los tubos de hamster
    const float H = 2.6f;           // del piso del tubo a su eje
    const float RI = 3.6f, RO = 3.9f;
    const float RJ = 5.5f, RJO = 5.8f;   // conectores (esferas)
    const float Mesa = 15f;          // altura de la mesa
    const float Alto2 = 26.3f;       // piso del nivel de arriba del laberinto

    static readonly Bounds Zona = new Bounds(new Vector3(44.25f, 22f, -69f), new Vector3(60.5f, 46f, 56f));   // x 14..74.5, z -97..-41
    static readonly Vector3 CentroGuarida = new Vector3(66f, 25f, -89.6f);
    const float RadioCasa = 4.8f;
    static readonly Vector3 Escenario = new Vector3(36f, Mesa + 0.45f, -71f);

    static int contador;
    static System.Random rnd;
    static float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    // ================================================================== datos del laberinto
    class Conector { public string nombre; public Vector3 piso; public bool apoyado; public List<Vector3> huecos = new List<Vector3>(); public Color color; }
    class Grieta { public float s0, s1, a0, a1; }
    class Tubo
    {
        public string nombre; public Color color; public List<Vector3> control = new List<Vector3>();
        public string ini, fin; public bool tapaFin; public List<Grieta> grietas = new List<Grieta>();
        public List<Vector3> piso = new List<Vector3>();   // muestras del piso cada 0.5
    }
    static Dictionary<string, Conector> conectores;
    static List<Tubo> tubos;

    static Conector J(string n, Vector3 piso, bool apoyado, Color c)
    {
        var j = new Conector { nombre = n, piso = piso, apoyado = apoyado, color = c };
        conectores[n] = j; return j;
    }

    static Vector3 Centro(Conector j) => j.piso + Vector3.up * H;

    /// <summary>Tubo con puntos de piso; si empieza/termina en un conector sale recto en la direccion dada.</summary>
    static Tubo T(string n, Color c, string ini, Vector3 dirIni, string fin, Vector3 dirFin, params Vector3[] puntos)
    {
        var t = new Tubo { nombre = n, color = c, ini = ini, fin = fin };
        if (ini != null) { var j = conectores[ini]; t.control.Add(j.piso); t.control.Add(j.piso + dirIni.normalized * (RJ - 0.6f)); }
        t.control.AddRange(puntos);
        if (fin != null) { var j = conectores[fin]; t.control.Add(j.piso + dirFin.normalized * (RJ - 0.6f)); t.control.Add(j.piso); }
        tubos.Add(t); return t;
    }

    static void DefinirLaberinto()
    {
        conectores = new Dictionary<string, Conector>(); tubos = new List<Tubo>();
        Color rosa = new Color(1f, 0.45f, 0.75f), amarillo = new Color(1f, 0.85f, 0.25f), azul = new Color(0.3f, 0.65f, 1f), verde = new Color(0.35f, 0.95f, 0.5f),
              naranja = new Color(1f, 0.6f, 0.2f), morado = new Color(0.7f, 0.45f, 1f), cian = new Color(0.3f, 0.95f, 0.95f), rojo = new Color(1f, 0.3f, 0.3f);
        J("J1", new Vector3(49f, Mesa, -70.5f), true, azul).huecos.Add(Vector3.left);           // puerta hacia el escenario
        J("J2", new Vector3(50f, Mesa, -86f), true, verde).huecos.Add(Vector3.left);            // puerta hacia la noria
        J("J3", new Vector3(50f, Alto2, -76.5f), false, naranja);
        J("J4", new Vector3(50f, Alto2, -90.5f), false, morado);

        // TR: del piso (junto al arco) sube por el frente de la mesa hasta la plaza
        T("TR_Entrada", rosa, null, Vector3.zero, null, Vector3.zero,
            new Vector3(60f, 0f, -49.5f), new Vector3(60f, 0f, -53f), new Vector3(58f, 0.8f, -57f), new Vector3(51f, 3.5f, -58.5f), new Vector3(41f, 7.5f, -58.5f),
            new Vector3(31f, 11.5f, -58.5f), new Vector3(25f, 14.3f, -60f), new Vector3(22.5f, Mesa, -63.5f), new Vector3(22.5f, Mesa, -68.5f));
        // T2: pasillo entre J1 y J2 sobre la mesa
        T("T2_Pasillo", azul, "J1", Vector3.back, "J2", Vector3.forward);
        // T1: de J1 sale por el frente, cuelga sobre el piso y sube al Mirador del Jugo
        T("T1_Mirador", amarillo, "J1", Vector3.forward, null, Vector3.zero,
            new Vector3(49.5f, Mesa + 0.1f, -63.5f), new Vector3(52f, 16f, -60.3f), new Vector3(56f, 17.8f, -59.3f), new Vector3(60.5f, 20f, -60f),
            new Vector3(63.8f, 21.7f, -62.6f), new Vector3(64f, 22f, -64.6f), new Vector3(64f, 22f, -66f));
        // T5: del Mirador a la terraza de la caja de pañuelos (frente a la puertita)
        T("T5_Terraza", cian, null, Vector3.zero, null, Vector3.zero,
            new Vector3(64.5f, 22f, -72.2f), new Vector3(64.5f, 22.3f, -73.4f), new Vector3(64.5f, 23.6f, -75.6f), new Vector3(64.5f, 25f, -78.2f), new Vector3(64.5f, 25f, -80f));
        // T7: del Mirador sube al conector J3
        T("T7_Subida", verde, "J3", Vector3.forward, null, Vector3.zero,
            new Vector3(50.5f, Alto2 - 0.2f, -69.8f), new Vector3(53f, 24.8f, -68.4f), new Vector3(56f, 22.8f, -69.6f), new Vector3(58f, 22f, -70.6f), new Vector3(59f, 22f, -70.6f));
        // T8: J3 -> J4 (con grieta hacia el oeste: por ahi se puede uno tirar)
        var t8 = T("T8_Grieta", naranja, "J3", Vector3.back, "J4", Vector3.forward);
        t8.grietas.Add(new Grieta { s0 = 0.25f, s1 = 0.75f, a0 = 318f, a1 = 380f });
        // T9: J4 baja a la terraza por el lado oeste
        T("T9_Atajo", morado, "J4", Vector3.right, null, Vector3.zero,
            new Vector3(56.8f, 25.8f, -90.2f), new Vector3(58.2f, 25.4f, -88.9f), new Vector3(58.9f, 25.1f, -87f), new Vector3(59f, 25f, -85.2f), new Vector3(59f, 25f, -84.2f));
        // T10: el tubo sin salida (con grieta al sur)
        var t10 = T("T10_SinSalida", rojo, "J3", Vector3.left, null, Vector3.zero,
            new Vector3(43.5f, Alto2, -77.8f), new Vector3(40.8f, Alto2, -80.2f), new Vector3(39.2f, Alto2, -82.6f));
        t10.tapaFin = true;
        t10.grietas.Add(new Grieta { s0 = 0.55f, s1 = 0.85f, a0 = 160f, a1 = 222f });
    }

    // ================================================================== utilidades de mallas y materiales
    static Mesh Guardar(Mesh m, string nombre)
    {
        Directory.CreateDirectory(Gen);
        string path = Gen + nombre + "_" + (contador++) + ".asset";
        NormalesSeguras.Sanear(m);
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

    static Material Lit(string nombre, Color c, float suave, float metal = 0f, Color emision = default, Texture2D tex = null, Vector2 tiling = default, Texture2D nor = null)
    {
        return GuardarMaterial(nombre, Shader.Find("Universal Render Pipeline/Lit"), m =>
        {
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", suave); m.SetFloat("_Metallic", metal);
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", tiling == default ? Vector2.one : tiling);
            if (nor != null) { m.SetTexture("_BumpMap", nor); m.EnableKeyword("_NORMALMAP"); } else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
            if (emision.maxColorComponent > 0f) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emision); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive; }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            // opaco (por si el material ya existia como transparente)
            m.SetFloat("_Surface", 0f); m.SetFloat("_ZWrite", 1f); m.SetFloat("_SrcBlend", 1f); m.SetFloat("_DstBlend", 0f);
            m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.SetOverrideTag("RenderType", "Opaque"); m.SetShaderPassEnabled("ShadowCaster", true); m.renderQueue = -1;
        });
    }

    static Material Plastico(string nombre, Color c, float alfa)
    {
        var m = Lit(nombre, new Color(c.r, c.g, c.b, alfa), 0.93f, 0f);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 1f);
        m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 10f); m.SetFloat("_SrcBlendAlpha", 1f); m.SetFloat("_DstBlendAlpha", 10f);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetShaderPassEnabled("ShadowCaster", false);
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D GuardarTextura(string nombre, int w, int h, System.Func<float, float, Color> f, bool repetir = true)
    {
        Directory.CreateDirectory(Tex);
        var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
        t.SetPixels(px); t.Apply();
        string path = Tex + nombre + ".png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.wrapMode = repetir ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; ti.sRGBTexture = true; ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static float Ruido(float x, float y) => Mathf.PerlinNoise(x, y);

    static Mesh MallaDe(string nombre, List<Vector3> v, List<int> t, List<Vector2> uv = null, List<Vector3> n = null)
    {
        var m = new Mesh { name = nombre, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.SetVertices(v); m.SetTriangles(t, 0);
        if (uv != null) m.SetUVs(0, uv);
        if (n != null) m.SetNormals(n); else m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // torno: perfil (radio, altura) alrededor de Y
    static Mesh Torno(string nombre, Vector2[] perfil, int lados)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int s = 0; s <= lados; s++)
        {
            float a = s / (float)lados * Mathf.PI * 2f;
            for (int i = 0; i < perfil.Length; i++)
            {
                v.Add(new Vector3(Mathf.Cos(a) * perfil[i].x, perfil[i].y, Mathf.Sin(a) * perfil[i].x));
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
        return MallaDe(nombre, v, t, uv);
    }

    // cilindro cerrado (tapa arriba y abajo) de radio r y alto h, base en y = 0
    static Mesh Cilindro(string nombre, float r, float h, int lados = 24)
    {
        return Torno(nombre, new[] { new Vector2(0f, 0f), new Vector2(r, 0f), new Vector2(r, 0f), new Vector2(r, h), new Vector2(r, h), new Vector2(0f, h) }, lados);
    }

    static Mesh Caja(string nombre, Vector3 tam)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var m = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
        Object.DestroyImmediate(go);
        var v = m.vertices;
        for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], tam);
        m.vertices = v; m.name = nombre;
        // uv en unidades del mundo (para que las texturas no se estiren)
        var uv = m.uv; var nn = m.normals;
        for (int i = 0; i < v.Length; i++)
        {
            Vector3 a = new Vector3(Mathf.Abs(nn[i].x), Mathf.Abs(nn[i].y), Mathf.Abs(nn[i].z));
            uv[i] = a.x > 0.5f ? new Vector2(v[i].z, v[i].y) : a.y > 0.5f ? new Vector2(v[i].x, v[i].z) : new Vector2(v[i].x, v[i].y);
            uv[i] *= 0.25f;
        }
        m.uv = uv;
        m.RecalculateBounds();
        return m;
    }

    static Mesh Bola(string nombre, float radio, Vector3 escala, int lat = 16, int lon = 24)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); var n = new List<Vector3>();
        for (int i = 0; i <= lat; i++)
            for (int j = 0; j <= lon; j++)
            {
                float th = i / (float)lat * Mathf.PI, ph = j / (float)lon * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                v.Add(Vector3.Scale(d * radio, escala)); n.Add(new Vector3(d.x / escala.x, d.y / escala.y, d.z / escala.z).normalized);
                uv.Add(new Vector2(j / (float)lon, 1f - i / (float)lat));
            }
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                int a = i * (lon + 1) + j, b = a + lon + 1;
                t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        return MallaDe(nombre, v, t, uv, n);
    }

    // toro alrededor del eje Z local (anillo en el plano XY)
    static Mesh Toro(string nombre, float R1, float r, int seg = 40, int segT = 10)
    {
        var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            Vector3 c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            for (int j = 0; j <= segT; j++)
            {
                float b = j / (float)segT * Mathf.PI * 2f;
                v.Add(c * (R1 + Mathf.Cos(b) * r) + Vector3.forward * Mathf.Sin(b) * r);
                uv.Add(new Vector2(i / (float)seg, j / (float)segT));
            }
        }
        for (int i = 0; i < seg; i++)
            for (int j = 0; j < segT; j++)
            {
                int a = i * (segT + 1) + j, b = a + segT + 1;
                t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        return MallaDe(nombre, v, t, uv);
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

    static GameObject Pieza(string nombre, Transform padre, Mesh malla, Material mat, Vector3 pos, Quaternion rot, bool colision = true)
    {
        var go = Objeto(nombre, padre, malla, mat, false);
        go.transform.SetPositionAndRotation(pos, rot);
        if (colision) go.AddComponent<MeshCollider>().sharedMesh = malla;
        return go;
    }

    static GameObject Bloque(string nombre, Transform padre, Vector3 min, Vector3 max, Material mat, bool colision = true)
    {
        var go = Objeto(nombre, padre, Guardar(Caja(nombre, max - min), nombre), mat, false);
        go.transform.position = (min + max) * 0.5f;
        if (colision) { var bc = go.AddComponent<BoxCollider>(); bc.size = max - min; }
        return go;
    }

    static Light Luz(Transform padre, string nombre, Vector3 pos, Color c, float rango, float intensidad, LightType tipo = LightType.Point)
    {
        var l = new GameObject(nombre).AddComponent<Light>();
        l.transform.SetParent(padre, false); l.transform.position = pos;
        l.type = tipo; l.color = c; l.range = rango; l.intensity = intensidad; l.shadows = LightShadows.None;
        return l;
    }

    // ================================================================== materiales
    static Material mMesa, mPata, mMantel, mViruta, mCarton, mCartonClaro, mLapiz, mPunta, mGrafito, mGoma, mMetal, mRojo, mBlanco, mDorado, mCD, mRayas, mRayasAzul,
                    mMadera, mEspejo, mPapel, mJugo, mPanuelos, mPanuelo, mBorrador1, mBorrador2, mBorrador3, mPalito, mFocoCalido, mFocoColor, mNegro, mPelaje, mRosa,
                    mOjo, mBrillo, mTapaRoja, mTapaAzul, mCasita, mVerdeOscuro;
    static Dictionary<Color, Material> plasticos, aros;
    static TMP_FontAsset fuente;

    static void CrearMateriales()
    {
        var tMesa = GuardarTextura("Mesa", 256, 256, (u, v) => { float n = Ruido(u * 9f, v * 9f) * 0.08f + Ruido(u * 60f, v * 60f) * 0.04f; float m = Ruido(u * 3f + 5f, v * 3f) > 0.62f ? 0.07f : 0f; return new Color(0.9f - n - m, 0.88f - n - m, 0.84f - n - m * 1.4f); });
        mMesa = Lit("Mesa", Color.white, 0.35f, 0f, default, tMesa, new Vector2(0.15f, 0.15f));
        mPata = Lit("PataMesa", new Color(0.55f, 0.56f, 0.58f), 0.55f, 0.85f);
        var tMantel = GuardarTextura("Mantel", 256, 64, (u, v) =>
        {
            Color[] cs = { new Color(0.95f, 0.35f, 0.5f), new Color(1f, 0.82f, 0.25f), new Color(0.35f, 0.7f, 0.95f), new Color(0.5f, 0.85f, 0.45f) };
            Color c = cs[(int)(u * 8f) % 4];
            float arruga = 0.85f + 0.15f * Mathf.Sin(u * 160f + Ruido(u * 20f, v * 3f) * 4f);
            float desteñido = 0.75f + 0.25f * Ruido(u * 4f, v * 2f);
            return c * arruga * desteñido;
        });
        mMantel = Lit("MantelPapel", Color.white, 0.15f, 0f, default, tMantel, new Vector2(1f, 1f));
        var tViruta = GuardarTextura("Viruta", 256, 256, (u, v) =>
        {
            float n = Ruido(u * 14f, v * 14f), m = Ruido(u * 50f + 3f, v * 50f), r = Mathf.Abs(Mathf.Sin((u + n * 0.3f) * 90f));
            return Color.Lerp(new Color(0.78f, 0.62f, 0.4f), new Color(0.95f, 0.85f, 0.62f), r * 0.6f + m * 0.4f) * (0.85f + 0.15f * n);
        });
        mViruta = Lit("Viruta", Color.white, 0.2f, 0f, default, tViruta, new Vector2(0.35f, 0.35f));
        var tCarton = GuardarTextura("Carton", 256, 256, (u, v) => { float n = Ruido(u * 12f, v * 12f) * 0.1f; float acan = 0.04f * Mathf.Sin(v * 120f); return new Color(0.66f - n + acan, 0.5f - n + acan, 0.32f - n); });
        mCarton = Lit("Carton", Color.white, 0.15f, 0f, default, tCarton, new Vector2(0.3f, 0.3f));
        mCartonClaro = Lit("CartonClaro", new Color(0.95f, 0.92f, 0.85f), 0.15f);
        mLapiz = Lit("LapizAmarillo", new Color(1f, 0.78f, 0.12f), 0.55f);
        mPunta = Lit("LapizMadera", new Color(0.9f, 0.72f, 0.5f), 0.2f);
        mGrafito = Lit("Grafito", new Color(0.18f, 0.18f, 0.2f), 0.6f, 0.4f);
        mGoma = Lit("GomaLapiz", new Color(0.98f, 0.55f, 0.6f), 0.15f);
        mMetal = Lit("MetalHamster", new Color(0.78f, 0.78f, 0.8f), 0.75f, 0.9f);
        mRojo = Lit("RojoFeria", new Color(0.9f, 0.15f, 0.18f), 0.55f);
        mBlanco = Lit("BlancoFeria", new Color(0.97f, 0.95f, 0.9f), 0.45f);
        mNegro = Lit("NegroFeria", new Color(0.07f, 0.06f, 0.08f), 0.55f);
        mDorado = Lit("DoradoFeria", new Color(1f, 0.78f, 0.3f), 0.8f, 1f);
        mCD = Lit("CD", new Color(0.85f, 0.88f, 0.95f), 0.95f, 1f, new Color(0.08f, 0.04f, 0.12f));
        var tRayas = GuardarTextura("RayasCarpa", 128, 16, (u, v) => (Mathf.Repeat(u * 12f, 1f) < 0.5f ? new Color(0.92f, 0.12f, 0.16f) : new Color(0.98f, 0.95f, 0.88f)) * (0.88f + 0.12f * Ruido(u * 30f, v * 4f)));
        mRayas = Lit("RayasCarpa", Color.white, 0.3f, 0f, default, tRayas);
        var tRayasA = GuardarTextura("RayasCarrusel", 128, 16, (u, v) => (Mathf.Repeat(u * 16f, 1f) < 0.5f ? new Color(0.2f, 0.55f, 0.95f) : new Color(1f, 0.85f, 0.3f)) * (0.88f + 0.12f * Ruido(u * 30f, v * 4f)));
        mRayasAzul = Lit("RayasCarrusel", Color.white, 0.3f, 0f, default, tRayasA);
        var tMadera = GuardarTextura("MaderaCaja", 256, 256, (u, v) => { float g = Mathf.Sin((u + Ruido(u * 3f, v * 12f) * 0.25f) * 70f) * 0.5f + 0.5f; return Color.Lerp(new Color(0.42f, 0.22f, 0.12f), new Color(0.6f, 0.34f, 0.18f), g); });
        mMadera = Lit("MaderaCaja", Color.white, 0.55f, 0f, default, tMadera, new Vector2(0.25f, 0.25f));
        mEspejo = Lit("Espejo", new Color(0.75f, 0.8f, 0.85f), 0.98f, 1f);
        mPapel = Lit("PapelCartel", new Color(0.98f, 0.96f, 0.9f), 0.1f);
        var tJugo = GuardarTextura("Jugo", 256, 256, (u, v) =>
        {
            Color c = Color.Lerp(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.82f, 0.3f), v);
            float naranja = Mathf.Clamp01((0.22f - Vector2.Distance(new Vector2(Mathf.Repeat(u * 2f, 1f), v), new Vector2(0.5f, 0.45f))) * 40f);
            c = Color.Lerp(c, new Color(1f, 0.45f, 0.05f), naranja);
            if (v > 0.8f) c = new Color(0.25f, 0.65f, 0.3f);
            return c * (0.9f + 0.1f * Ruido(u * 20f, v * 20f));
        });
        mJugo = Lit("CajitaJugo", Color.white, 0.4f, 0f, default, tJugo, new Vector2(0.1f, 0.14f));
        var tPan = GuardarTextura("Panuelos", 256, 256, (u, v) =>
        {
            Color c = new Color(0.55f, 0.75f, 0.95f);
            float flor = 0f;
            for (int k = 0; k < 6; k++)
            {
                Vector2 p = new Vector2(Mathf.Repeat(u * 2f + k * 0.37f, 1f), Mathf.Repeat(v * 2f + k * 0.61f, 1f));
                float d = Vector2.Distance(p, new Vector2(0.5f, 0.5f)); float a = Mathf.Atan2(p.y - 0.5f, p.x - 0.5f);
                flor = Mathf.Max(flor, Mathf.Clamp01((0.12f + 0.05f * Mathf.Cos(a * 5f) - d) * 60f));
            }
            return Color.Lerp(c, new Color(1f, 0.95f, 0.98f), flor) * (0.92f + 0.08f * Ruido(u * 15f, v * 15f));
        });
        mPanuelos = Lit("CajaPanuelos", Color.white, 0.3f, 0f, default, tPan, new Vector2(0.06f, 0.06f));
        mPanuelo = Lit("Panuelo", new Color(0.98f, 0.98f, 0.97f), 0.1f);
        mBorrador1 = Lit("BorradorRosa", new Color(1f, 0.6f, 0.68f), 0.15f);
        mBorrador2 = Lit("BorradorAzul", new Color(0.4f, 0.65f, 0.95f), 0.15f);
        mBorrador3 = Lit("BorradorBlanco", new Color(0.97f, 0.97f, 0.95f), 0.15f);
        mPalito = Lit("PalitoPaleta", new Color(0.88f, 0.75f, 0.52f), 0.2f);
        mFocoCalido = Lit("FocoCalido", new Color(1f, 0.92f, 0.7f), 0.8f, 0f, new Color(0.05f, 0.04f, 0.02f));
        mFocoColor = Lit("FocoColor", new Color(1f, 1f, 1f), 0.8f, 0f, new Color(0.05f, 0.05f, 0.05f));
        mTapaRoja = Lit("TapaRoja", new Color(0.85f, 0.12f, 0.12f), 0.6f);
        mTapaAzul = Lit("TapaAzul", new Color(0.15f, 0.35f, 0.85f), 0.6f);
        mVerdeOscuro = Lit("VerdeFeria", new Color(0.15f, 0.55f, 0.3f), 0.5f);
        mCasita = Plastico("PlasticoCasita", new Color(0.75f, 0.45f, 1f), 0.4f);
        // bolita: pelaje (degradado naranja -> crema, u = "panza"), piel rosa, ojos
        var tPel = GuardarTextura("PelajeBolita", 256, 64, (u, v) =>
        {
            Color nar = new Color(0.9f, 0.55f, 0.24f), cre = new Color(0.99f, 0.93f, 0.82f);
            float pelo = 0.94f + 0.04f * Mathf.Sin(v * 900f + Ruido(u * 40f, v * 30f) * 9f) + 0.04f * (Ruido(u * 25f, v * 60f) - 0.5f);
            return Color.Lerp(nar, cre, Mathf.SmoothStep(0.35f, 0.65f, u)) * pelo;
        }, false);
        mPelaje = Lit("PelajeBolita", Color.white, 0.25f, 0f, default, tPel);
        mRosa = Lit("PielBolita", new Color(0.98f, 0.62f, 0.66f), 0.4f);
        mOjo = Lit("OjoBolita", new Color(0.03f, 0.02f, 0.03f), 0.97f);
        mBrillo = Lit("BrilloOjo", Color.white, 0.9f, 0f, new Color(0.8f, 0.8f, 0.8f));
        plasticos = new Dictionary<Color, Material>(); aros = new Dictionary<Color, Material>();
    }

    static Material PlasticoDe(Color c)
    {
        if (!plasticos.TryGetValue(c, out var m)) { m = Plastico("Tubo_" + ColorUtility.ToHtmlStringRGB(c), c, 0.26f); plasticos[c] = m; }
        return m;
    }

    static Material AroDe(Color c)
    {
        if (!aros.TryGetValue(c, out var m)) { m = Lit("Aro_" + ColorUtility.ToHtmlStringRGB(c), c * 0.95f, 0.65f); aros[c] = m; }
        return m;
    }

    // ================================================================== construccion principal
    [MenuItem("Herramientas/Hamsterlandia/Construir")]
    public static void Construir()
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var log = new System.Text.StringBuilder();
        rnd = new System.Random(3011);
        contador = 0;
        focoMalla = gomaLapiz = casquillo = hexa = punta = triCache = null;

        // devolver lo que se habia escondido y borrar lo anterior
        var viejo = GameObject.Find("HAMSTERLANDIA");
        if (viejo != null)
        {
            var hv = viejo.GetComponent<Hamsterlandia>();
            if (hv != null && hv.ocultos != null) foreach (var o in hv.ocultos) if (o != null) o.SetActive(true);
            Object.DestroyImmediate(viejo);
        }
        var batVieja = GameObject.Find("BATERIA_Bolita");
        if (batVieja != null) Object.DestroyImmediate(batVieja);
        if (Directory.Exists(Gen)) AssetDatabase.DeleteAsset(Gen.TrimEnd('/'));
        Directory.CreateDirectory(Gen);
        AssetDatabase.Refresh();

        HamsterAudio.Generar();
        CrearMateriales();
        var intro = Object.FindAnyObjectByType<IntroDespertar>(FindObjectsInactive.Include);
        fuente = intro != null ? intro.fuente : null;

        var raiz = new GameObject("HAMSTERLANDIA");
        var gestor = raiz.AddComponent<Hamsterlandia>();
        gestor.ocultos = HacerLugar(log).ToArray();
        DefinirLaberinto();

        var mueble = new GameObject("Mesa").transform; mueble.SetParent(raiz.transform, false);
        CrearMesa(mueble);
        var piso = new GameObject("Entrada").transform; piso.SetParent(raiz.transform, false);
        CrearEntrada(piso);
        var lab = new GameObject("Laberinto").transform; lab.SetParent(raiz.transform, false);
        foreach (var t in tubos) CrearTubo(lab, t);
        foreach (var j in conectores.Values) CrearConector(lab, j);
        var feria = new GameObject("Feria").transform; feria.SetParent(raiz.transform, false);
        var caja = CrearCajaMusical(feria);
        var carpa = CrearCarpa(feria, out var rueda, out var puntoRueda, out var focoEsc, out var palFuncion);
        var carrusel = CrearCarrusel(feria);
        var noria = CrearNoria(feria);
        var mirador = CrearMirador(feria, out var palMirador);
        var focos = CrearFocosGenerales(feria);
        CrearEscalera(feria);
        var castillo = new GameObject("Castillo").transform; castillo.SetParent(raiz.transform, false);
        CrearCajaPanuelos(castillo, gestor);
        CrearCasita(castillo, gestor);
        CrearColeccion(castillo);
        CrearCarteles(raiz.transform);
        Physics.SyncTransforms();
        CrearSoportes(lab, log);
        Physics.SyncTransforms();

        var bat = CrearBateria(new Vector3(67.6f, Mesa + 10f + 1.05f, -92.4f));

        // palancas de las atracciones
        var palCaja = CrearPalanca(feria, "Palanca_CajaMusical", new Vector3(25.8f, Mesa, -79.5f), Quaternion.Euler(0f, -90f, 0f), "la caja musical");
        var palCarrusel = CrearPalanca(feria, "Palanca_Carrusel", new Vector3(31.8f, Mesa, -86.5f), Quaternion.Euler(0f, -90f, 0f), "el carrusel");

        // Bolita y su grafo
        var bolita = CrearBolita(raiz.transform);
        ConstruirGrafo(bolita, log);

        // gestor
        gestor.zona = Zona;
        gestor.centroGuarida = CentroGuarida; gestor.radioGuarida = RadioCasa - 0.4f; gestor.pisoGuarida = CentroGuarida.y;
        gestor.escenario = new GameObject("PuntoEscenario").transform; gestor.escenario.SetParent(raiz.transform, false);
        gestor.escenario.SetPositionAndRotation(Escenario, Quaternion.LookRotation(Vector3.forward));
        gestor.palancas = new[] { palCaja, palCarrusel, palMirador };
        gestor.palancaFuncion = palFuncion;
        gestor.atracciones = new[] { caja, carrusel, mirador };
        gestor.carpa = carpa; gestor.noria = noria; gestor.rueda = rueda; gestor.focos = focos;
        gestor.puntoRueda = puntoRueda; gestor.focoEscenario = focoEsc;
        gestor.bolita = bolita; gestor.bateria = bat;
        AsignarAmagos(gestor);
        CrearAudio(gestor);

        EditorSceneManager.MarkSceneDirty(raiz.scene);
        EditorSceneManager.SaveScene(raiz.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Hamsterlandia] Construida en " + reloj.ElapsedMilliseconds + " ms\n" + log);
    }

    // ================================================================== hacer lugar en la esquina
    static List<GameObject> HacerLugar(System.Text.StringBuilder log)
    {
        var ocultos = new List<GameObject>();
        var huella = new Bounds(); huella.SetMinMax(new Vector3(14f, -1f, -97f), new Vector3(74.5f, 40f, -41f));
        var decay = GameObject.Find("DECAY_ROOT");
        if (decay != null)
            foreach (var cat in new[] { "Planks", "Vegetation", "Trash", "Furniture" })
            {
                var c = decay.transform.Find(cat); if (c == null) continue;
                foreach (Transform t in c)
                {
                    if (!t.gameObject.activeSelf) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>())
                        if (r.bounds.Intersects(huella)) { t.gameObject.SetActive(false); ocultos.Add(t.gameObject); break; }
                }
            }
        // bichos del salon que hayan quedado dentro
        var animales = GameObject.Find("ANIMALES");
        if (animales != null)
            foreach (var rh in animales.GetComponentsInChildren<RatHealth>(true))
            {
                Vector3 p = rh.transform.position;
                if (p.x > 12f && p.x < 76f && p.z < -38f && p.z > -98f) { rh.transform.position = new Vector3(Mathf.Min(p.x, 8f), p.y, Mathf.Max(p.z, -30f)); log.AppendLine("bicho movido: " + rh.name); }
            }
        log.AppendLine("escondidos " + ocultos.Count + " tiliches del salon");
        return ocultos;
    }

    // ================================================================== mesa
    static void CrearMesa(Transform p)
    {
        Bloque("Cubierta", p, new Vector3(18f, Mesa - 1.2f, -95f), new Vector3(72f, Mesa, -63f), mMesa);
        var pata = Guardar(Cilindro("Pata", 0.55f, Mesa - 1.2f, 12), "Pata");
        foreach (var x in new[] { 19.6f, 70.4f })
            foreach (var z in new[] { -93.4f, -64.6f })
                Pieza("Pata", p, pata, mPata, new Vector3(x, 0f, z), Quaternion.identity);
        var barra = Guardar(Caja("Travesano", new Vector3(50.8f, 0.6f, 0.6f)), "Travesano");
        Pieza("Travesano", p, barra, mPata, new Vector3(45f, 3f, -93.4f), Quaternion.identity, false);
        // mantel de papel crepe colgando por el frente y el lado izquierdo (con huecos donde pasan los tubos)
        foreach (var (x0, x1) in new[] { (27.5f, 44.5f), (54f, 72f) })
            Bloque("Mantel", p, new Vector3(x0, Mesa - 6.5f, -63.1f), new Vector3(x1, Mesa - 0.05f, -62.95f), mMantel, false);
        Bloque("MantelIzq", p, new Vector3(17.85f, Mesa - 6.5f, -95f), new Vector3(18f, Mesa - 0.05f, -70f), mMantel, false);
        // barandal de palitos de paleta en el frente derecho (no te puedes bajar por ahi: hay que salir por el laberinto)
        var bar = new GameObject("Barandal").transform; bar.SetParent(p, false);
        var poste = Guardar(Caja("PostePalito", new Vector3(0.35f, 2.8f, 0.6f)), "PostePalito");
        for (float x = 53.2f; x <= 72f; x += 2.2f) Pieza("Palito", bar, poste, mPalito, new Vector3(x, Mesa + 1.4f, -63.6f), Quaternion.identity, false);
        foreach (float y in new[] { 1.2f, 2.5f }) Bloque("Riel", bar, new Vector3(53f, Mesa + y - 0.2f, -63.75f), new Vector3(72f, Mesa + y + 0.2f, -63.45f), mPalito, false);
        var col = bar.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(62.5f, Mesa + 2.5f, -63.6f); col.size = new Vector3(19f, 5f, 0.5f);
    }

    // ================================================================== entrada (en el piso)
    static void CrearEntrada(Transform p)
    {
        // arco: dos lapices de pie y un letrero de carton con focos
        var lapiz = Lapiz(13f);
        foreach (var x in new[] { 54f, 66f }) Pieza("PilarArco", p, lapiz, mLapiz, new Vector3(x, 0f, -44f), Quaternion.identity);
        Bloque("LetreroArco", p, new Vector3(53f, 12.6f, -44.3f), new Vector3(67f, 16.2f, -43.8f), mCarton, false);
        Texto(p, "HAMSTERLANDIA", new Vector3(60f, 14.7f, -43.75f), Vector3.forward, 14f, 2.4f, 13f, new Color(1f, 0.85f, 0.3f));
        Texto(p, "Feria de Ciencias  ·  3°B", new Vector3(60f, 13.1f, -43.75f), Vector3.forward, 12f, 0.8f, 4.5f, Color.white);
        // taquilla (cajita de leche)
        Bloque("Taquilla", p, new Vector3(47.5f, 0f, -48.5f), new Vector3(51.5f, 5f, -45.5f), mCartonClaro);
        Bloque("TechoTaquilla", p, new Vector3(47f, 5f, -49f), new Vector3(52f, 5.4f, -45f), mRojo, false);
        Texto(p, "BOLETOS\n<size=60%>1 semilla</size>", new Vector3(49.5f, 3.4f, -45.45f), Vector3.forward, 3.6f, 2.4f, 5f, new Color(0.85f, 0.15f, 0.2f));
        // banderines del arco a la taquilla y al tubo
        Banderines(p, new Vector3(54f, 12f, -44f), new Vector3(47.5f, 5.3f, -47f), 1.2f);
        Banderines(p, new Vector3(66f, 12f, -44f), new Vector3(72f, 9f, -50f), 1.5f);
    }

    static Mesh Lapiz(float largo)
    {
        // hexagonal amarillo, de pie sobre la goma (abajo) y con la punta hacia arriba
        var cuerpo = Torno("LapizCuerpo", new[] { new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, largo - 1.2f), new Vector2(0.5f, largo - 1.2f), new Vector2(0.12f, largo - 0.15f), new Vector2(0f, largo) }, 6);
        return Guardar(cuerpo, "Lapiz");
    }

    static void PonerLapiz(Transform p, Vector3 pie, float largo)
    {
        if (largo < 1.2f) return;
        var go = new GameObject("Lapiz"); go.transform.SetParent(p, false); go.transform.position = pie;
        float cuerpo = Mathf.Max(0.3f, largo - 2.1f);
        Pieza("Goma", go.transform, gomaLapiz ??= Guardar(Cilindro("GomaLapiz", 0.48f, 0.7f, 12), "GomaLapiz"), mGoma, pie, Quaternion.identity, false);
        Pieza("Casquillo", go.transform, casquillo ??= Guardar(Cilindro("Casquillo", 0.53f, 0.5f, 12), "Casquillo"), mMetal, pie + Vector3.up * 0.7f, Quaternion.identity, false);
        var c = Pieza("Cuerpo", go.transform, hexa ??= Guardar(Cilindro("Hexagono", 0.5f, 1f, 6), "Hexagono"), mLapiz, pie + Vector3.up * 1.2f, Quaternion.identity, false);
        c.transform.localScale = new Vector3(1f, cuerpo, 1f);
        Pieza("Punta", go.transform, punta ??= Guardar(Torno("Punta", new[] { new Vector2(0.5f, 0f), new Vector2(0.15f, 0.75f), new Vector2(0f, 0.9f) }, 6), "Punta"), mPunta, pie + Vector3.up * (1.2f + cuerpo), Quaternion.identity, false);
        var col = go.AddComponent<CapsuleCollider>(); col.center = new Vector3(0f, largo * 0.5f, 0f); col.height = largo; col.radius = 0.5f;
    }
    static Mesh gomaLapiz, casquillo, hexa, punta;

    static void Banderines(Transform p, Vector3 a, Vector3 b, float caida)
    {
        var go = new GameObject("Banderines"); go.transform.SetParent(p, false);
        Color[] cs = { new Color(1f, 0.35f, 0.4f), new Color(1f, 0.85f, 0.25f), new Color(0.35f, 0.75f, 1f), new Color(0.45f, 0.9f, 0.45f), new Color(0.85f, 0.5f, 1f) };
        int n = Mathf.Max(3, Mathf.RoundToInt(Vector3.Distance(a, b) / 1.3f));
        var tri = TrianguloBandera();
        var cuerda = new List<(Mesh, Matrix4x4)>();
        for (int i = 0; i < n; i++)
        {
            float u = (i + 0.5f) / n;
            Vector3 q = Vector3.Lerp(a, b, u) + Vector3.down * caida * 4f * u * (1f - u);
            Vector3 d = (b - a); d.y = 0f;
            var rot = Quaternion.LookRotation(Vector3.Cross(d.normalized, Vector3.up), Vector3.up);
            var mat = Lit("Bandera_" + (i % cs.Length), cs[i % cs.Length], 0.2f);
            var f = Pieza("Bandera", go.transform, tri, mat, q, rot, false);
            f.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        // la cuerda
        var linea = new List<Vector3>();
        for (int i = 0; i <= 16; i++) { float u = i / 16f; linea.Add(Vector3.Lerp(a, b, u) + Vector3.down * caida * 4f * u * (1f - u)); }
        var cm = Cuerda(linea, 0.05f);
        Objeto("Cuerda", go.transform, Guardar(cm, "Cuerda"), mNegro, false, false);
    }

    static Mesh triCache;
    static Mesh TrianguloBandera()
    {
        if (triCache != null) return triCache;
        var v = new List<Vector3> { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0f, -1f, 0f), new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(0f, -1f, 0f) };
        var t = new List<int> { 0, 1, 2, 3, 5, 4 };
        var m = MallaDe("Bandera", v, t);
        triCache = Guardar(m, "Bandera");
        return triCache;
    }

    static Mesh Cuerda(List<Vector3> puntos, float r)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        const int L = 5;
        for (int i = 0; i < puntos.Count; i++)
        {
            Vector3 d = (i < puntos.Count - 1 ? puntos[i + 1] - puntos[i] : puntos[i] - puntos[i - 1]).normalized;
            Vector3 s = Vector3.Cross(Vector3.up, d); if (s.sqrMagnitude < 0.001f) s = Vector3.right; s.Normalize();
            Vector3 n = Vector3.Cross(d, s);
            for (int k = 0; k < L; k++) { float a = k / (float)L * Mathf.PI * 2f; v.Add(puntos[i] + (Mathf.Cos(a) * s + Mathf.Sin(a) * n) * r); }
        }
        for (int i = 0; i < puntos.Count - 1; i++)
            for (int k = 0; k < L; k++)
            {
                int a = i * L + k, b = i * L + (k + 1) % L, c = a + L, e = b + L;
                t.AddRange(new[] { a, c, b, b, c, e, a, b, c, b, e, c });
            }
        return MallaDe("Cuerda", v, t);
    }

    // ================================================================== tubos
    static List<Vector3> CatmullRom(List<Vector3> p, float paso)
    {
        var denso = new List<Vector3>();
        int n = p.Count;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = i > 0 ? p[i - 1] : p[0] - (p[1] - p[0]);
            Vector3 p1 = p[i], p2 = p[i + 1];
            Vector3 p3 = i + 2 < n ? p[i + 2] : p2 + (p2 - p1);
            int k = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / 0.2f));
            for (int j = 0; j < k; j++)
            {
                float t = j / (float)k, t2 = t * t, t3 = t2 * t;
                denso.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
            }
        }
        denso.Add(p[n - 1]);
        // re-muestreo por largo de arco
        var res = new List<Vector3> { denso[0] };
        float acum = 0f;
        for (int i = 1; i < denso.Count; i++)
        {
            float d = Vector3.Distance(denso[i - 1], denso[i]);
            acum += d;
            if (acum >= paso) { res.Add(denso[i]); acum = 0f; }
        }
        if (Vector3.Distance(res[res.Count - 1], denso[denso.Count - 1]) > 0.05f) res.Add(denso[denso.Count - 1]);
        return res;
    }

    static void CrearTubo(Transform padre, Tubo tb)
    {
        tb.piso = CatmullRom(tb.control, 0.5f);
        var eje = tb.piso.Select(q => q + Vector3.up * H).ToList();
        int n = eje.Count;
        float[] s = new float[n];
        for (int i = 1; i < n; i++) s[i] = s[i - 1] + Vector3.Distance(eje[i - 1], eje[i]);
        float largo = s[n - 1];
        // recorte dentro de los conectores
        Vector3? cIni = tb.ini != null ? Centro(conectores[tb.ini]) : (Vector3?)null;
        Vector3? cFin = tb.fin != null ? Centro(conectores[tb.fin]) : (Vector3?)null;
        System.Func<int, float, bool> fuera = (i, rr) => (cIni == null || Vector3.Distance(eje[i], cIni.Value) > rr) && (cFin == null || Vector3.Distance(eje[i], cFin.Value) > rr);
        int i0 = 0; while (i0 < n - 1 && !fuera(i0, 3.7f)) i0++;
        int i1 = n - 1; while (i1 > 0 && !fuera(i1, 3.7f)) i1--;
        if (i1 - i0 < 1) { i0 = Mathf.Max(0, i0 - 1); i1 = Mathf.Min(n - 1, i0 + 1); }
        // huecos en los conectores (direccion del tubo al salir de la esfera)
        if (cIni != null) { int k = 0; while (k < n - 1 && Vector3.Distance(eje[k], cIni.Value) < RJ) k++; conectores[tb.ini].huecos.Add((eje[k] - cIni.Value).normalized); }
        if (cFin != null) { int k = n - 1; while (k > 0 && Vector3.Distance(eje[k], cFin.Value) < RJ) k--; conectores[tb.fin].huecos.Add((eje[k] - cFin.Value).normalized); }

        // marcos
        var lados = new Vector3[n]; var normales = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 tg = (eje[Mathf.Min(n - 1, i + 1)] - eje[Mathf.Max(0, i - 1)]).normalized;
            Vector3 sd = Vector3.Cross(Vector3.up, tg); if (sd.sqrMagnitude < 0.001f) sd = Vector3.right; sd.Normalize();
            lados[i] = sd; normales[i] = Vector3.Cross(tg, sd);
        }

        const int L = 32;
        var v = new List<Vector3>(); var t = new List<int>(); var nn = new List<Vector3>();
        int filas = i1 - i0 + 1;
        // capa exterior (0) e interior (1)
        for (int capa = 0; capa < 2; capa++)
        {
            float r = capa == 0 ? RO : RI;
            for (int i = i0; i <= i1; i++)
                for (int k = 0; k <= L; k++)
                {
                    float a = k / (float)L * Mathf.PI * 2f;
                    Vector3 d = Mathf.Cos(a) * lados[i] + Mathf.Sin(a) * normales[i];
                    v.Add(eje[i] + d * r); nn.Add(capa == 0 ? d : -d);
                }
        }
        int porCapa = filas * (L + 1);
        for (int capa = 0; capa < 2; capa++)
            for (int i = 0; i < filas - 1; i++)
                for (int k = 0; k < L; k++)
                {
                    // grietas: no se ponen esas caras
                    float sm = (s[i0 + i] + s[i0 + i + 1]) * 0.5f / Mathf.Max(0.01f, largo);
                    float am = (k + 0.5f) / L * 360f;
                    bool hueco = false;
                    foreach (var g in tb.grietas)
                        if (sm >= g.s0 && sm <= g.s1 && (Entre(am, g.a0, g.a1) || Entre(am + 360f, g.a0, g.a1))) hueco = true;
                    if (hueco) continue;
                    int a = capa * porCapa + i * (L + 1) + k, b = a + (L + 1);
                    if (capa == 0) t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                    else t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
        // bordes (anillos planos que unen las dos capas en los extremos)
        foreach (int fila in new[] { 0, filas - 1 })
        {
            int baseO = fila * (L + 1), baseI = porCapa + fila * (L + 1);
            bool inicio = fila == 0;
            for (int k = 0; k < L; k++)
            {
                t.AddRange(new[] { baseO + k, baseO + k + 1, baseI + k, baseI + k, baseO + k + 1, baseI + k + 1 });
                t.AddRange(new[] { baseO + k, baseI + k, baseO + k + 1, baseO + k + 1, baseI + k, baseI + k + 1 });
            }
        }
        var malla = Guardar(MallaDe("Tubo_" + tb.nombre, v, t, null, nn), "Tubo_" + tb.nombre);
        var go = Objeto(tb.nombre, padre, malla, PlasticoDe(tb.color), true, false);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

        // piso de viruta (opaco), un poquito arriba para no pelearse con la mesa
        var pv = new List<Vector3>(); var pt = new List<int>(); var puv = new List<Vector2>();
        int j0 = i0, j1 = i1;
        // el piso entra un poco mas al conector (llega hasta su disco)
        while (j0 > 0 && (cIni == null || Vector3.Distance(eje[j0 - 1], cIni.Value) > 4.4f)) j0--;
        while (j1 < n - 1 && (cFin == null || Vector3.Distance(eje[j1 + 1], cFin.Value) > 4.4f)) j1++;
        for (int i = j0; i <= j1; i++)
        {
            Vector3 tg = (eje[Mathf.Min(n - 1, i + 1)] - eje[Mathf.Max(0, i - 1)]).normalized;
            float cos = new Vector2(tg.x, tg.z).magnitude;
            float w = Mathf.Sqrt(Mathf.Max(0.5f, RI * RI - H * H * cos * cos)) - 0.1f;
            Vector3 c = tb.piso[i] + Vector3.up * 0.05f;
            pv.Add(c - lados[i] * w); pv.Add(c + lados[i] * w);
            pv.Add(c - lados[i] * w - Vector3.up * 0.4f); pv.Add(c + lados[i] * w - Vector3.up * 0.4f);
            float uu = s[i] * 0.5f;
            puv.Add(new Vector2(0f, uu)); puv.Add(new Vector2(w, uu)); puv.Add(new Vector2(0f, uu)); puv.Add(new Vector2(w, uu));
        }
        for (int i = 0; i < j1 - j0; i++)
        {
            int a = i * 4, b = a + 4;
            pt.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });         // arriba
            pt.AddRange(new[] { a, a + 2, b, b, a + 2, b + 2 });         // costado izquierdo
            pt.AddRange(new[] { a + 1, b + 1, a + 3, a + 3, b + 1, b + 3 }); // costado derecho
        }
        var mp = Guardar(MallaDe("Piso_" + tb.nombre, pv, pt, puv), "Piso_" + tb.nombre);
        var gp = Objeto(tb.nombre + "_Piso", padre, mp, mViruta, true);
        GameObjectUtility.SetStaticEditorFlags(gp, StaticEditorFlags.BatchingStatic);

        // aros de colores: en las bocas y cada tanto (como los tubos de juguete de verdad)
        var aro = Guardar(Toro("AroTubo", RO + 0.12f, 0.24f, 36, 8), "AroTubo");
        var mAro = AroDe(tb.color);
        for (int i = i0; i <= i1; i++)
        {
            bool boca = (i == i0 && tb.ini == null) || (i == i1 && tb.fin == null && !tb.tapaFin);
            bool cada = (i - i0) % 12 == 6 && i < i1 - 3;
            if (!boca && !cada) continue;
            Vector3 tg = (eje[Mathf.Min(n - 1, i + 1)] - eje[Mathf.Max(0, i - 1)]).normalized;
            var a = Pieza("Aro", go.transform, aro, mAro, eje[i], Quaternion.LookRotation(tg), false);
            if (boca) a.transform.localScale = new Vector3(1.04f, 1.04f, 1.6f);
        }
        if (tb.tapaFin)
        {
            Vector3 tg = (eje[i1] - eje[i1 - 1]).normalized;
            var tapa = Guardar(Cilindro("Tapa", RO + 0.15f, 0.4f, 32), "Tapa");
            Pieza("Tapa", go.transform, tapa, mAro, eje[i1], Quaternion.FromToRotation(Vector3.up, tg));
        }
    }

    static bool Entre(float a, float a0, float a1) => a >= a0 && a <= a1;

    // ================================================================== conectores (esferas con huecos)
    static void CrearConector(Transform padre, Conector j)
    {
        Vector3 c = Centro(j);
        float ang = Mathf.Asin((RO + 0.08f) / RJ) * Mathf.Rad2Deg;
        const int LAT = 28, LON = 56;
        var v = new List<Vector3>(); var nn = new List<Vector3>(); var t = new List<int>();
        for (int capa = 0; capa < 2; capa++)
        {
            float r = capa == 0 ? RJO : RJ;
            int b0 = v.Count;
            for (int i = 0; i <= LAT; i++)
                for (int k = 0; k <= LON; k++)
                {
                    float th = i / (float)LAT * Mathf.PI, ph = k / (float)LON * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(c + d * r); nn.Add(capa == 0 ? d : -d);
                }
            for (int i = 0; i < LAT; i++)
                for (int k = 0; k < LON; k++)
                {
                    int a = b0 + i * (LON + 1) + k, b = a + LON + 1;
                    Vector3 m = ((v[a] + v[b + 1]) * 0.5f - c).normalized;
                    bool hueco = false;
                    foreach (var h in j.huecos) if (Vector3.Angle(m, h) < ang) { hueco = true; break; }
                    if (hueco) continue;
                    if (j.apoyado && m.y * RJ < -H - 0.1f) continue;   // los que estan sobre la mesa no tienen panza
                    if (capa == 0) t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                    else t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
        }
        var malla = Guardar(MallaDe("Conector_" + j.nombre, v, t, null, nn), "Conector_" + j.nombre);
        var go = Objeto("Conector_" + j.nombre, padre, malla, PlasticoDe(j.color), true, false);
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        // disco de piso
        float rd = Mathf.Sqrt(RJ * RJ - H * H) - 0.1f;
        var disco = Guardar(Cilindro("Disco", rd, 0.4f, 40), "Disco");
        Pieza("Piso", go.transform, disco, mViruta, j.piso + Vector3.down * 0.37f, Quaternion.identity);
        // aros en cada hueco
        var aro = Guardar(Toro("AroConector", RO + 0.25f, 0.3f, 36, 8), "AroConector");
        foreach (var h in j.huecos)
            Pieza("Aro", go.transform, aro, AroDe(j.color), c + h * Mathf.Sqrt(RJO * RJO - (RO + 0.2f) * (RO + 0.2f)), Quaternion.LookRotation(h), false);
    }

    // ================================================================== soportes (lapices bajo tubos y conectores)
    static void CrearSoportes(Transform padre, System.Text.StringBuilder log)
    {
        var sop = new GameObject("Soportes").transform; sop.SetParent(padre, false);
        int n = 0;
        foreach (var j in conectores.Values)
        {
            if (j.apoyado) continue;
            foreach (var dx in new[] { -2.6f, 2.6f })
                n += Soporte(sop, Centro(j) + new Vector3(dx, -Mathf.Sqrt(RJO * RJO - dx * dx) + 0.1f, 0f)) ? 1 : 0;
        }
        foreach (var tb in tubos)
        {
            float acum = 4f;
            for (int i = 1; i < tb.piso.Count - 1; i++)
            {
                acum += Vector3.Distance(tb.piso[i - 1], tb.piso[i]);
                if (acum < 9f) continue;
                Vector3 bajo = tb.piso[i] + Vector3.up * (H - RO + 0.1f);
                bool cercaConector = conectores.Values.Any(j => Vector3.Distance(Centro(j), bajo) < RJO + 1.5f);
                if (cercaConector) continue;
                if (Soporte(sop, bajo)) { acum = 0f; n++; }
            }
        }
        log.AppendLine("lapices de soporte: " + n);
    }

    static bool Soporte(Transform p, Vector3 arriba)
    {
        if (!Physics.Raycast(arriba + Vector3.down * 0.2f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) return false;
        float largo = arriba.y - hit.point.y;
        if (largo < 1.5f) return false;
        PonerLapiz(p, hit.point, largo);
        return true;
    }

    // ================================================================== atracciones
    static AtraccionFeria CrearCajaMusical(Transform p)
    {
        var go = new GameObject("CajaMusical"); go.transform.SetParent(p, false);
        Vector3 c = new Vector3(21.5f, Mesa, -78.5f);
        Bloque("Caja", go.transform, c + new Vector3(-3f, 0f, -2.5f), c + new Vector3(3f, 5f, 2.5f), mMadera);
        Bloque("Moldura", go.transform, c + new Vector3(-3.1f, 4.7f, -2.6f), c + new Vector3(3.1f, 5.05f, 2.6f), mDorado, false);
        // tapa abierta con espejo (hacia atras)
        var tapa = new GameObject("Tapa").transform; tapa.SetParent(go.transform, false);
        tapa.position = c + new Vector3(0f, 5f, -2.5f); tapa.rotation = Quaternion.Euler(-100f, 0f, 0f);
        Pieza("TapaMadera", tapa, Guardar(Caja("TapaCaja", new Vector3(6f, 0.4f, 5f)), "TapaCaja"), mMadera, tapa.position + tapa.rotation * new Vector3(0f, 0.2f, 2.5f), tapa.rotation);
        Pieza("Espejo", tapa, Guardar(Caja("EspejoCaja", new Vector3(5f, 0.05f, 4f)), "EspejoCaja"), mEspejo, tapa.position + tapa.rotation * new Vector3(0f, -0.02f, 2.5f), tapa.rotation, false);
        // escenario giratorio con la bailarina (una mini Bolita con tutu)
        var plato = new GameObject("Plato").transform; plato.SetParent(go.transform, false); plato.position = c + Vector3.up * 5.05f;
        Pieza("Base", plato, Guardar(Cilindro("PlatoCaja", 1.5f, 0.3f, 24), "PlatoCaja"), mDorado, plato.position, Quaternion.identity, false);
        var bai = new GameObject("Bailarina").transform; bai.SetParent(plato, false); bai.localPosition = Vector3.up * 0.3f;
        Pieza("Cuerpo", bai, Guardar(Bola("MiniHamster", 0.8f, new Vector3(1f, 1.15f, 0.95f)), "MiniHamster"), mPelaje, bai.position + Vector3.up * 1.3f, Quaternion.identity, false);
        Pieza("Tutu", bai, Guardar(Torno("Tutu", new[] { new Vector2(0.6f, -0.15f), new Vector2(1.5f, -0.05f), new Vector2(1.5f, 0f), new Vector2(0.6f, 0.15f) }, 24), "Tutu"), Lit("Tutu", new Color(1f, 0.6f, 0.8f), 0.3f), bai.position + Vector3.up * 0.9f, Quaternion.identity, false);
        Pieza("OjoI", bai, Guardar(Bola("OjoMini", 0.12f, Vector3.one), "OjoMini"), mOjo, bai.position + new Vector3(-0.3f, 1.75f, 0.68f), Quaternion.identity, false);
        Pieza("OjoD", bai, Guardar(Bola("OjoMini", 0.12f, Vector3.one), "OjoMini"), mOjo, bai.position + new Vector3(0.3f, 1.75f, 0.68f), Quaternion.identity, false);
        // manivela al costado (gira cuando suena)
        var man = new GameObject("Manivela").transform; man.SetParent(go.transform, false); man.position = c + new Vector3(3.2f, 2.6f, 0f);
        Pieza("Eje", man, Guardar(Caja("EjeManivela", new Vector3(0.6f, 0.3f, 0.3f)), "EjeManivela"), mDorado, man.position + Vector3.right * 0.3f, Quaternion.identity, false);
        Pieza("Brazo", man, Guardar(Caja("BrazoManivela", new Vector3(0.25f, 1.8f, 0.3f)), "BrazoManivela"), mDorado, man.position + new Vector3(0.6f, 0.8f, 0f), Quaternion.identity, false);
        Pieza("Mango", man, Guardar(Cilindro("MangoManivela", 0.25f, 0.9f, 10), "MangoManivela"), mRojo, man.position + new Vector3(0.6f, 1.6f, 0f), Quaternion.Euler(0f, 0f, -90f), false);
        var a = go.AddComponent<AtraccionFeria>();
        a.tipo = AtraccionFeria.Tipo.CajaMusical; a.giro = man; a.eje = Vector3.right; a.velocidad = 90f; a.bailarina = bai;
        a.focos = Foquitos(go.transform, c + Vector3.up * 5.2f, 3.2f, 2.7f, 14).ToArray();
        a.coloresFocos = new[] { new Color(1f, 0.85f, 0.5f), new Color(1f, 0.55f, 0.75f) };
        a.luces = new[] { Luz(go.transform, "LuzCaja", c + new Vector3(0f, 8f, 3f), new Color(1f, 0.75f, 0.55f), 12f, 6f) };
        return a;
    }

    // foquitos en un rectangulo/anillo (pequeñas esferas emisivas)
    static Mesh focoMalla;
    static List<Renderer> Foquitos(Transform p, Vector3 c, float rx, float rz, int n)
    {
        focoMalla ??= Guardar(Bola("Foco", 0.22f, Vector3.one, 8, 12), "Foco");
        var l = new List<Renderer>();
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            var f = Pieza("Foco", p, focoMalla, mFocoColor, c + new Vector3(Mathf.Cos(a) * rx, 0f, Mathf.Sin(a) * rz), Quaternion.identity, false);
            f.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            l.Add(f.GetComponent<Renderer>());
        }
        return l;
    }

    static List<Renderer> FoquitosLinea(Transform p, List<Vector3> puntos, float separacion)
    {
        focoMalla ??= Guardar(Bola("Foco", 0.22f, Vector3.one, 8, 12), "Foco");
        var l = new List<Renderer>(); float acum = 0f;
        for (int i = 1; i < puntos.Count; i++)
        {
            acum += Vector3.Distance(puntos[i - 1], puntos[i]);
            if (acum < separacion) continue;
            acum = 0f;
            var f = Pieza("Foco", p, focoMalla, mFocoColor, puntos[i] + Vector3.down * 0.25f, Quaternion.identity, false);
            f.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            l.Add(f.GetComponent<Renderer>());
        }
        Objeto("Cable", p, Guardar(Cuerda(puntos, 0.05f), "CableFocos"), mNegro, false, false);
        return l;
    }

    static List<Vector3> Catenaria(Vector3 a, Vector3 b, float caida, int n = 24)
    {
        var l = new List<Vector3>();
        for (int i = 0; i <= n; i++) { float u = i / (float)n; l.Add(Vector3.Lerp(a, b, u) + Vector3.down * caida * 4f * u * (1f - u)); }
        return l;
    }

    static AtraccionFeria CrearCarpa(Transform p, out AtraccionFeria rueda, out Transform puntoRueda, out Light foco, out PalancaFeria palanca)
    {
        var go = new GameObject("GranCarpa"); go.transform.SetParent(p, false);
        // escenario: tapa de frasco
        var tapa = Guardar(Torno("TapaFrasco", new[] { new Vector2(5.9f, 0f), new Vector2(6f, 0f), new Vector2(6f, 0.4f), new Vector2(5.8f, 0.45f), new Vector2(0f, 0.45f) }, 48), "TapaFrasco");
        Pieza("Escenario", go.transform, tapa, mTapaRoja, new Vector3(Escenario.x, Mesa, Escenario.z), Quaternion.identity);
        Pieza("AroEscenario", go.transform, Guardar(Toro("AroEscenario", 6f, 0.15f, 60, 6), "AroEscenario"), mDorado, new Vector3(Escenario.x, Mesa + 0.45f, Escenario.z), Quaternion.Euler(90f, 0f, 0f), false);
        // carpa: 4 postes de caramelo y un cono a rayas
        Vector3 cc = new Vector3(Escenario.x, Mesa, Escenario.z);
        var poste = Guardar(Cilindro("PosteCarpa", 0.4f, 23.5f - Mesa, 12), "PosteCarpa");
        foreach (float ang in new[] { 45f, 135f, 225f, 315f })
        {
            float an = ang * Mathf.Deg2Rad;
            Pieza("Poste", go.transform, poste, mRayas, cc + new Vector3(Mathf.Cos(an) * 9f, 0f, Mathf.Sin(an) * 9f), Quaternion.identity);
        }
        var cono = Torno("Carpa", new[] { new Vector2(9.6f, 23.3f - Mesa), new Vector2(9.5f, 23.5f - Mesa), new Vector2(0.05f, 28.5f - Mesa) }, 64);
        var conoIn = Torno("CarpaDentro", new[] { new Vector2(0.05f, 28.4f - Mesa), new Vector2(9.5f, 23.4f - Mesa) }, 64);
        Pieza("Carpa", go.transform, Guardar(Combinar("Carpa", (cono, Matrix4x4.identity), (conoIn, Matrix4x4.identity)), "Carpa"), mRayas, cc, Quaternion.identity);
        Pieza("Banderita", go.transform, Guardar(Caja("Banderita", new Vector3(0.1f, 1.2f, 1.8f)), "Banderita"), mRojo, cc + new Vector3(0f, 28.5f - Mesa + 1.6f, 0.9f), Quaternion.identity, false);
        Pieza("Mastil", go.transform, Guardar(Cilindro("Mastil", 0.1f, 2.4f, 6), "Mastil"), mDorado, cc + Vector3.up * (28.4f - Mesa), Quaternion.identity, false);
        var a = go.AddComponent<AtraccionFeria>();
        a.tipo = AtraccionFeria.Tipo.Carpa;
        a.focos = Foquitos(go.transform, cc + Vector3.up * (23.1f - Mesa), 9.6f, 9.6f, 40).ToArray();
        a.coloresFocos = new[] { new Color(1f, 0.85f, 0.45f), new Color(1f, 0.4f, 0.4f), new Color(1f, 0.85f, 0.45f), new Color(0.5f, 0.75f, 1f) };
        foco = Luz(go.transform, "FocoEscenario", cc + Vector3.up * 12.5f, new Color(1f, 0.92f, 0.75f), 22f, 60f, LightType.Spot);
        foco.transform.rotation = Quaternion.Euler(90f, 0f, 0f); foco.spotAngle = 70f;
        a.luces = new[] { Luz(go.transform, "LuzCarpa", cc + Vector3.up * 6f + Vector3.forward * 4f, new Color(1f, 0.6f, 0.45f), 18f, 8f) };
        Banderines(go.transform, cc + Vector3.up * (28.3f - Mesa), cc + new Vector3(9f, 23.4f - Mesa, 0f), 0.4f);
        Banderines(go.transform, cc + Vector3.up * (28.3f - Mesa), cc + new Vector3(-9f, 23.4f - Mesa, 0f), 0.4f);

        // la rueda de Bolita, al fondo del escenario
        var rg = new GameObject("RuedaBolita"); rg.transform.SetParent(go.transform, false);
        Vector3 ejeR = new Vector3(Escenario.x, Mesa + 0.45f + 4.6f, -76.2f);
        float rr = 4.4f, ancho = 5.6f;
        var giro = new GameObject("Giro").transform; giro.SetParent(rg.transform, false); giro.position = ejeR;
        var aroR = Guardar(Toro("AroRueda", rr, 0.18f, 64, 6), "AroRueda");
        foreach (float dz in new[] { -ancho * 0.5f, ancho * 0.5f }) Pieza("Aro", giro, aroR, mMetal, ejeR + Vector3.forward * dz, Quaternion.identity, false);
        var barrote = Guardar(Cilindro("Barrote", 0.07f, ancho, 6), "Barrote");
        for (int i = 0; i < 40; i++)
        {
            float an = i / 40f * Mathf.PI * 2f;
            Vector3 q = ejeR + new Vector3(Mathf.Cos(an) * rr, Mathf.Sin(an) * rr, -ancho * 0.5f);
            Pieza("Barrote", giro, barrote, mMetal, q, Quaternion.Euler(90f, 0f, 0f), false);
        }
        var rayo = Guardar(Caja("RayoRueda", new Vector3(0.2f, rr * 2f, 0.2f)), "RayoRueda");
        for (int i = 0; i < 3; i++) Pieza("Rayo", giro, rayo, mMetal, ejeR - Vector3.forward * ancho * 0.5f, Quaternion.Euler(0f, 0f, i * 60f), false);
        // patas en A
        foreach (float sx in new[] { -1f, 1f })
        {
            Vector3 pie = new Vector3(ejeR.x + sx * 3.4f, Mesa + 0.45f, ejeR.z - ancho * 0.5f - 0.4f);
            Vector3 d = ejeR + Vector3.back * (ancho * 0.5f + 0.4f) - pie;
            Pieza("Pata", rg.transform, Guardar(Caja("PataRueda", new Vector3(0.3f, d.magnitude, 0.3f)), "PataRueda"), mMetal, pie + d * 0.5f, Quaternion.FromToRotation(Vector3.up, d), false);
        }
        var colR = rg.AddComponent<BoxCollider>(); colR.center = ejeR - rg.transform.position; colR.size = new Vector3(rr * 2f + 0.2f, rr * 2f + 0.2f, ancho + 0.2f);
        rueda = rg.AddComponent<AtraccionFeria>();
        rueda.tipo = AtraccionFeria.Tipo.Rueda; rueda.giro = giro; rueda.eje = Vector3.forward; rueda.velocidad = 0f;
        var pr = new GameObject("PuntoRueda").transform; pr.SetParent(rg.transform, false);
        pr.SetPositionAndRotation(new Vector3(ejeR.x, ejeR.y - rr + 0.25f, ejeR.z), Quaternion.LookRotation(Vector3.right));
        puntoRueda = pr;
        // la palanca de la funcion, al frente del escenario
        palanca = CrearPalanca(go.transform, "Palanca_Funcion", new Vector3(41.6f, Mesa + 0.45f, -67.2f), Quaternion.Euler(0f, 180f, 0f), "LA GRAN FUNCION");
        palanca.transform.localScale = Vector3.one * 1.3f;
        return a;
    }

    static AtraccionFeria CrearCarrusel(Transform p)
    {
        var go = new GameObject("Carrusel"); go.transform.SetParent(p, false);
        Vector3 c = new Vector3(25f, Mesa, -89f);
        var giro = new GameObject("Giro").transform; giro.SetParent(go.transform, false); giro.position = c;
        Pieza("CD", go.transform, Guardar(Cilindro("CD", 5f, 0.35f, 64), "CD"), mCD, c, Quaternion.identity);
        Pieza("Centro", giro, Guardar(Cilindro("CentroCD", 0.8f, 0.4f, 24), "CentroCD"), mBlanco, c, Quaternion.identity, false);
        Pieza("Poste", go.transform, Guardar(Cilindro("PosteCarrusel", 0.45f, 9.5f, 6), "PosteCarrusel"), mLapiz, c, Quaternion.identity);
        var techo = Torno("TechoCarrusel", new[] { new Vector2(5.6f, 7f), new Vector2(5.6f, 7.6f), new Vector2(0.1f, 10.2f) }, 48);
        Pieza("Techo", giro, Guardar(techo, "TechoCarrusel"), mRayasAzul, c, Quaternion.identity, false);
        var figuras = new List<Transform>();
        var palillo = Guardar(Cilindro("Palillo", 0.1f, 6.4f, 6), "Palillo");
        var borrador = Guardar(Caja("BorradorCaballo", new Vector3(0.9f, 1f, 2f)), "BorradorCaballo");
        var tapita = Guardar(Cilindro("Tapita", 0.9f, 0.7f, 20), "Tapita");
        Material[] mats = { mBorrador1, mBorrador2, mTapaRoja, mBorrador3, mTapaAzul, mBorrador1 };
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * Mathf.PI * 2f;
            Vector3 q = c + new Vector3(Mathf.Cos(a) * 3.4f, 0.35f, Mathf.Sin(a) * 3.4f);
            Pieza("Palillo", giro, palillo, mDorado, q, Quaternion.identity, false);
            var f = new GameObject("Figura_" + i).transform; f.SetParent(giro, false); f.position = q + Vector3.up * 2.4f;
            f.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
            Pieza("Asiento", f, i % 2 == 0 ? borrador : tapita, mats[i], f.position, f.rotation, false);
            figuras.Add(f);
        }
        var a2 = go.AddComponent<AtraccionFeria>();
        a2.tipo = AtraccionFeria.Tipo.Carrusel; a2.giro = giro; a2.eje = Vector3.up; a2.velocidad = 28f;
        a2.figuras = figuras.ToArray();
        a2.focos = Foquitos(giro, c + Vector3.up * 6.9f, 5.7f, 5.7f, 22).ToArray();
        a2.coloresFocos = new[] { new Color(1f, 0.85f, 0.4f), new Color(0.5f, 0.8f, 1f) };
        a2.luces = new[] { Luz(go.transform, "LuzCarrusel", c + Vector3.up * 5f, new Color(0.6f, 0.75f, 1f), 14f, 7f) };
        return a2;
    }

    static AtraccionFeria CrearNoria(Transform p)
    {
        var go = new GameObject("Noria"); go.transform.SetParent(p, false);
        Vector3 eje = new Vector3(38.5f, Mesa + 10f, -92.5f);
        float r = 7f;
        var giro = new GameObject("Giro").transform; giro.SetParent(go.transform, false); giro.position = eje;
        var aro = Guardar(Toro("AroNoria", r, 0.16f, 64, 6), "AroNoria");
        foreach (float dz in new[] { -0.8f, 0.8f }) Pieza("Aro", giro, aro, mBlanco, eje + Vector3.forward * dz, Quaternion.identity, false);
        var rayo = Guardar(Caja("RayoNoria", new Vector3(0.14f, r * 2f, 0.14f)), "RayoNoria");
        for (int i = 0; i < 4; i++) foreach (float dz in new[] { -0.8f, 0.8f }) Pieza("Rayo", giro, rayo, mBlanco, eje + Vector3.forward * dz, Quaternion.Euler(0f, 0f, i * 45f), false);
        Pieza("Eje", giro, Guardar(Cilindro("EjeNoria", 0.35f, 2.4f, 12), "EjeNoria"), mDorado, eje + Vector3.back * 1.2f, Quaternion.Euler(90f, 0f, 0f), false);
        var cana = Guardar(Cilindro("Canastilla", 0.95f, 0.9f, 16), "Canastilla");
        var asa = Guardar(Caja("AsaCanastilla", new Vector3(0.1f, 1.4f, 0.1f)), "AsaCanastilla");
        var figuras = new List<Transform>();
        Material[] mats = { mTapaRoja, mTapaAzul, mLapiz, mBorrador1, mVerdeOscuro, mTapaRoja, mTapaAzul, mBorrador2 };
        for (int i = 0; i < 8; i++)
        {
            float a = i / 8f * Mathf.PI * 2f;
            var f = new GameObject("Canastilla_" + i).transform; f.SetParent(giro, false);
            f.position = eje + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
            Pieza("Asa", f, asa, mMetal, f.position + Vector3.down * 0.7f, Quaternion.identity, false);
            Pieza("Tapa", f, cana, mats[i], f.position + Vector3.down * 2.1f, Quaternion.identity, false);
            figuras.Add(f);
        }
        // patas en A (las de atras y las de adelante)
        foreach (float dz in new[] { -1.6f, 1.6f })
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 pie = new Vector3(eje.x + sx * 6.5f, Mesa, eje.z + dz);
                Vector3 d = eje + Vector3.forward * dz - pie;
                Pieza("Pata", go.transform, Guardar(Caja("PataNoria", new Vector3(0.35f, d.magnitude, 0.35f)), "PataNoria"), mBlanco, pie + d * 0.5f, Quaternion.FromToRotation(Vector3.up, d));
            }
        var at = go.AddComponent<AtraccionFeria>();
        at.tipo = AtraccionFeria.Tipo.Noria; at.giro = giro; at.eje = Vector3.forward; at.velocidad = 14f;
        at.figuras = figuras.ToArray();
        var fl = new List<Renderer>();
        for (int i = 0; i < 32; i++)
        {
            float a = i / 32f * Mathf.PI * 2f;
            focoMalla ??= Guardar(Bola("Foco", 0.22f, Vector3.one, 8, 12), "Foco");
            var f = Pieza("Foco", giro, focoMalla, mFocoColor, eje + new Vector3(Mathf.Cos(a) * (r + 0.3f), Mathf.Sin(a) * (r + 0.3f), 0.9f), Quaternion.identity, false);
            fl.Add(f.GetComponent<Renderer>());
        }
        at.focos = fl.ToArray();
        at.coloresFocos = new[] { new Color(1f, 0.4f, 0.6f), new Color(1f, 0.9f, 0.4f), new Color(0.4f, 0.9f, 1f) };
        at.luces = new[] { Luz(go.transform, "LuzNoria", eje + Vector3.forward * 4f, new Color(1f, 0.6f, 0.8f), 16f, 6f) };
        return at;
    }

    static AtraccionFeria CrearMirador(Transform p, out PalancaFeria palanca)
    {
        var go = new GameObject("MiradorDelJugo"); go.transform.SetParent(p, false);
        Bloque("CajitaJugo", go.transform, new Vector3(57f, Mesa, -73f), new Vector3(67f, 22f, -65f), mJugo);
        // popote doblado
        Pieza("Popote", go.transform, Guardar(Cilindro("Popote", 0.55f, 7f, 12), "Popote"), Lit("Popote", new Color(1f, 0.95f, 0.95f), 0.6f), new Vector3(57.8f, 22f, -65.6f), Quaternion.identity);
        Pieza("PopoteCodo", go.transform, Guardar(Cilindro("PopoteCodo", 0.55f, 2.6f, 12), "PopoteCodo"), Lit("Popote", new Color(1f, 0.95f, 0.95f), 0.6f), new Vector3(57.8f, 29f, -65.6f), Quaternion.Euler(0f, 0f, -55f), false);
        // reflectores: tres latitas mirando al parque
        var lata = Guardar(Cilindro("Reflector", 0.8f, 1.6f, 16), "Reflector");
        var luces = new List<Light>();
        for (int i = 0; i < 3; i++)
        {
            Vector3 q = new Vector3(60.5f + i * 2.4f, 22.2f, -72.2f);
            Pieza("Reflector", go.transform, lata, mMetal, q, Quaternion.Euler(-60f, -140f + i * 25f, 0f), false);
        }
        var at = go.AddComponent<AtraccionFeria>();
        at.tipo = AtraccionFeria.Tipo.Focos;
        at.focos = FoquitosLinea(go.transform, new List<Vector3> { new Vector3(57.2f, 22.3f, -65.2f), new Vector3(66.8f, 22.3f, -65.2f), new Vector3(66.8f, 22.3f, -72.8f), new Vector3(57.2f, 22.3f, -72.8f), new Vector3(57.2f, 22.3f, -65.2f) }, 1.2f).ToArray();
        at.coloresFocos = new[] { new Color(1f, 0.6f, 0.2f), new Color(1f, 0.9f, 0.4f) };
        at.luces = new[] { Luz(go.transform, "LuzMirador", new Vector3(62f, 27f, -69f), new Color(1f, 0.7f, 0.35f), 14f, 6f) };
        palanca = CrearPalanca(go.transform, "Palanca_Mirador", new Vector3(66.1f, 22f, -68.9f), Quaternion.Euler(0f, -90f, 0f), "las luces del mirador");
        return at;
    }

    static AtraccionFeria CrearFocosGenerales(Transform p)
    {
        var go = new GameObject("FocosGenerales"); go.transform.SetParent(p, false);
        var fl = new List<Renderer>();
        Vector3 punta = new Vector3(Escenario.x, 28.4f, Escenario.z);
        fl.AddRange(FoquitosLinea(go.transform, Catenaria(new Vector3(19f, 33f, -95.5f), new Vector3(55f, 33f, -95.5f), 2.5f), 1.6f));
        fl.AddRange(FoquitosLinea(go.transform, Catenaria(punta, new Vector3(57.8f, 31.5f, -65.6f), 2f), 1.6f));
        fl.AddRange(FoquitosLinea(go.transform, Catenaria(punta, new Vector3(38.5f, 33f, -92.5f), 1.2f), 1.6f));
        fl.AddRange(FoquitosLinea(go.transform, Catenaria(new Vector3(54f, 12.8f, -44f), new Vector3(66f, 12.8f, -44f), 1.2f, 16), 1.2f));
        fl.AddRange(FoquitosLinea(go.transform, Catenaria(new Vector3(66f, 12.8f, -44f), new Vector3(22.5f, 22f, -60f), 3f, 40), 1.8f));
        var at = go.AddComponent<AtraccionFeria>();
        at.tipo = AtraccionFeria.Tipo.Focos;
        at.focos = fl.ToArray();
        at.coloresFocos = new[] { new Color(1f, 0.35f, 0.4f), new Color(1f, 0.85f, 0.3f), new Color(0.4f, 0.8f, 1f), new Color(0.5f, 1f, 0.5f), new Color(0.85f, 0.5f, 1f) };
        at.luces = new[] { Luz(go.transform, "LuzArco", new Vector3(60f, 9f, -41f), new Color(1f, 0.75f, 0.5f), 18f, 5f) };
        return at;
    }

    // escalera de borradores: solo para el jugador (cada escalon pide un salto)
    static void CrearEscalera(Transform p)
    {
        var go = new GameObject("EscaleraBorradores"); go.transform.SetParent(p, false);
        var pasos = new (Vector2 xz, float h)[] { (new Vector2(70.6f, -64.6f), 1.2f), (new Vector2(70.6f, -66.9f), 2.4f), (new Vector2(70.6f, -69.2f), 3.6f), (new Vector2(70.6f, -71.5f), 4.8f), (new Vector2(68.4f, -68.6f), 6.0f) };
        Material[] mats = { mBorrador1, mBorrador2, mBorrador3, mBorrador1, mBorrador2 };
        for (int i = 0; i < pasos.Length; i++)
        {
            var (xz, h) = pasos[i];
            // cada escalon es una pila de borradores
            int capas = Mathf.RoundToInt(h / 1.2f);
            for (int k = 0; k < capas; k++)
            {
                var b = Bloque("Borrador", go.transform, new Vector3(xz.x - 1.1f, Mesa + k * 1.2f, xz.y - 1.1f), new Vector3(xz.x + 1.1f, Mesa + (k + 1) * 1.2f, xz.y + 1.1f), mats[(i + k) % mats.Length], k == capas - 1);
                b.transform.rotation = Quaternion.Euler(0f, (k % 2) * 8f - 4f, 0f);
                if (k < capas - 1) b.AddComponent<BoxCollider>().size = new Vector3(2.2f, 1.2f, 2.2f);
            }
        }
    }

    // ================================================================== castillo: caja de pañuelos + casita
    static void CrearCajaPanuelos(Transform p, Hamsterlandia g)
    {
        var go = new GameObject("CajaPanuelos"); go.transform.SetParent(p, false);
        float x0 = 56f, x1 = 72f, z0 = -95f, z1 = -79f, y0 = Mesa, y1 = 25f, e = 0.5f;
        // paredes (la del frente tiene la salida del tobogan)
        Bloque("Atras", go.transform, new Vector3(x0, y0, z0), new Vector3(x1, y1, z0 + e), mPanuelos);
        Bloque("Izq", go.transform, new Vector3(x0, y0, z0), new Vector3(x0 + e, y1, z1), mPanuelos);
        Bloque("Der", go.transform, new Vector3(x1 - e, y0, z0), new Vector3(x1, y1, z1), mPanuelos);
        float hx0 = 61.8f, hx1 = 64.6f, hy = Mesa + 3.5f;
        Bloque("FrenteA", go.transform, new Vector3(x0, y0, z1 - e), new Vector3(hx0, y1, z1), mPanuelos);
        Bloque("FrenteB", go.transform, new Vector3(hx1, y0, z1 - e), new Vector3(x1, y1, z1), mPanuelos);
        Bloque("FrenteC", go.transform, new Vector3(hx0, hy, z1 - e), new Vector3(hx1, y1, z1), mPanuelos);
        // tapa con el hueco de la trampilla (dentro de la casita)
        float tx0 = 61.6f, tx1 = 64.8f, tz0 = -90.6f, tz1 = -87.4f, ty = y1 - 0.4f;
        Bloque("TapaA", go.transform, new Vector3(x0, ty, z0), new Vector3(x1, y1, tz0), mPanuelos);
        Bloque("TapaB", go.transform, new Vector3(x0, ty, tz1), new Vector3(x1, y1, z1), mPanuelos);
        Bloque("TapaC", go.transform, new Vector3(x0, ty, tz0), new Vector3(tx0, y1, tz1), mPanuelos);
        Bloque("TapaD", go.transform, new Vector3(tx1, ty, tz0), new Vector3(x1, y1, tz1), mPanuelos);
        // el pañuelo que se asoma (bandera blanca)
        var pv = new List<Vector3>(); var pt = new List<int>();
        for (int i = 0; i <= 8; i++) for (int k = 0; k <= 6; k++)
            {
                float u = i / 8f, w = k / 6f;
                pv.Add(new Vector3(u * 4f - 2f, w * 4.2f, Mathf.Sin(u * 5f + w * 2f) * 0.5f + w * w * 0.8f));
            }
        for (int i = 0; i < 8; i++) for (int k = 0; k < 6; k++) { int a = i * 7 + k, b = a + 7; pt.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1, a, a + 1, b, b, a + 1, b + 1 }); }
        Pieza("Panuelo", go.transform, Guardar(MallaDe("Panuelo", pv, pt), "Panuelo"), mPanuelo, new Vector3(58.8f, y1, -93f), Quaternion.Euler(0f, 30f, 0f), false);
        Texto(go.transform, "PAÑUELOS\n<size=55%>suavecitos · 3 capas</size>", new Vector3(68f, 21.5f, z1 + 0.02f), Vector3.forward, 7f, 3.2f, 7f, new Color(0.15f, 0.3f, 0.6f));
        Texto(go.transform, "SALIDA DE EMERGENCIA\n<size=60%>(solo con pila)</size>", new Vector3(63.2f, Mesa + 4.6f, z1 + 0.02f), Vector3.forward, 6f, 1.6f, 3.2f, new Color(0.85f, 0.1f, 0.1f));
        Luz(go.transform, "LuzDentro", new Vector3(65.5f, 20f, -88f), new Color(1f, 0.85f, 0.6f), 10f, 4f);

        // tobogan en espiral por dentro (una vuelta, de la trampilla al piso de la mesa) y salida recta
        Vector3 c = new Vector3(66f, 0f, -89f);
        float r = 2.8f, mitad = 1.3f, ySup = 22.3f, yInf = Mesa + 0.05f;
        var tob = new List<Vector3>();
        var fv = new List<Vector3>(); var ft = new List<int>();
        int pasos = 72;
        for (int i = 0; i <= pasos; i++)
        {
            float u = i / (float)pasos;
            float th = Mathf.PI - u * Mathf.PI * 2f;        // de 180° a -180° (una vuelta)
            Vector3 rad = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
            Vector3 q = c + rad * r + Vector3.up * Mathf.Lerp(ySup, yInf, u);
            tob.Add(q);
            fv.Add(q - rad * mitad); fv.Add(q + rad * mitad);
            fv.Add(q - rad * mitad + Vector3.up * 1.6f); fv.Add(q + rad * mitad + Vector3.up * 1.8f);
        }
        for (int i = 0; i < pasos; i++)
        {
            int a = i * 4, b = a + 4;
            ft.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1, a, a + 1, b, b, a + 1, b + 1 });       // piso (dos caras)
            ft.AddRange(new[] { a, a + 2, b, b, a + 2, b + 2, a, b, a + 2, a + 2, b, b + 2 });       // pared de adentro
            ft.AddRange(new[] { a + 1, b + 1, a + 3, a + 3, b + 1, b + 3, a + 1, a + 3, b + 1, b + 1, a + 3, b + 3 }); // pared de afuera
        }
        var mt = Guardar(MallaDe("Tobogan", fv, ft), "Tobogan");
        var gt = Objeto("Tobogan", go.transform, mt, Lit("Tobogan", new Color(1f, 0.55f, 0.2f), 0.85f), true);
        Pieza("Columna", go.transform, Guardar(Cilindro("ColumnaTobogan", 1.25f, y1 - y0, 20), "ColumnaTobogan"), mCarton, new Vector3(c.x, y0, c.z), Quaternion.identity);
        // canal de salida recto hasta la pared del frente
        Vector3 fin = tob[tob.Count - 1];
        Bloque("CanalIzq", go.transform, new Vector3(fin.x - 1.5f, Mesa, fin.z + 1.2f), new Vector3(fin.x - 1.3f, Mesa + 1.6f, z1 - e), mPanuelos);
        Bloque("CanalDer", go.transform, new Vector3(fin.x + 1.3f, Mesa, fin.z + 1.2f), new Vector3(fin.x + 1.5f, Mesa + 1.6f, z1 - e), mPanuelos);
        tob.Add(new Vector3(fin.x, Mesa, z1 + 0.3f));
        tob.Add(new Vector3(fin.x, Mesa, z1 + 2.5f));
        g.tobogan = tob.ToArray();
        g.anchoTobogan = 1.9f;
    }

    static void CrearCasita(Transform p, Hamsterlandia g)
    {
        var go = new GameObject("CasitaBolita"); go.transform.SetParent(p, false);
        Vector3 c = CentroGuarida;
        Vector3 puerta = new Vector3(c.x, c.y, c.z + RadioCasa);   // al frente (+z)
        float rp = 1.55f;
        // domo (media esfera un poco aplastada) con la puertita redonda al frente
        const int LAT = 16, LON = 56;
        var v = new List<Vector3>(); var nn = new List<Vector3>(); var t = new List<int>();
        for (int capa = 0; capa < 2; capa++)
        {
            float r = capa == 0 ? RadioCasa + 0.25f : RadioCasa;
            int b0 = v.Count;
            for (int i = 0; i <= LAT; i++)
                for (int k = 0; k <= LON; k++)
                {
                    float th = i / (float)LAT * Mathf.PI * 0.5f, ph = k / (float)LON * Mathf.PI * 2f;
                    Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th) * 0.95f, Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(c + d * r); nn.Add(capa == 0 ? d.normalized : -d.normalized);
                }
            for (int i = 0; i < LAT; i++)
                for (int k = 0; k < LON; k++)
                {
                    int a = b0 + i * (LON + 1) + k, b = a + LON + 1;
                    Vector3 m = (v[a] + v[b + 1]) * 0.5f;
                    Vector2 rel = new Vector2(m.x - puerta.x, m.y - (puerta.y + rp));
                    if (m.z > c.z && rel.magnitude < rp) continue;     // la puerta
                    if (capa == 0) t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                    else t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
        }
        var malla = Guardar(MallaDe("Casita", v, t, null, nn), "Casita");
        Objeto("Domo", go.transform, malla, mCasita, true, false);
        Pieza("AroPuerta", go.transform, Guardar(Toro("AroPuerta", rp + 0.1f, 0.25f, 32, 8), "AroPuerta"), AroDe(new Color(1f, 0.85f, 0.25f)), puerta + Vector3.up * rp + Vector3.forward * 0.1f, Quaternion.identity, false);
        // la puertita: una tapita con bisagra a un lado
        var bis = new GameObject("BisagraPuerta").transform; bis.SetParent(go.transform, false); bis.position = puerta + new Vector3(-rp, 0f, 0.35f);
        var tapa = Pieza("Puertita", bis, Guardar(Cilindro("Puertita", rp + 0.1f, 0.25f, 24), "Puertita"), mTapaAzul, puerta + new Vector3(0f, rp, 0.35f), Quaternion.Euler(90f, 0f, 0f), false);
        var col = bis.gameObject.AddComponent<BoxCollider>(); col.center = new Vector3(rp, rp, 0f); col.size = new Vector3(rp * 2.2f, rp * 2.2f, 0.5f);
        Texto(bis, "PRIVADO", puerta + new Vector3(0f, rp + 0.2f, 0.52f), Vector3.forward, 2.6f, 0.9f, 2.2f, Color.white);
        g.bisagraPuerta = bis; g.colPuerta = col;
        var pg = new GameObject("PuertaGuarida").transform; pg.SetParent(go.transform, false);
        pg.SetPositionAndRotation(puerta, Quaternion.LookRotation(Vector3.forward)); g.puertaGuarida = pg;
        // trampilla (tapa del tobogan): se abre al tomar la pila
        var bt = new GameObject("BisagraTrampilla").transform; bt.SetParent(go.transform, false); bt.position = new Vector3(61.6f, 25f, -87.4f);
        Pieza("Trampilla", bt, Guardar(Caja("Trampilla", new Vector3(3.2f, 0.2f, 3.2f)), "Trampilla"), mCarton, new Vector3(63.2f, 25.1f, -89f), Quaternion.identity, false);
        var ct = bt.gameObject.AddComponent<BoxCollider>(); ct.center = new Vector3(1.6f, 0.1f, -1.6f); ct.size = new Vector3(3.2f, 0.25f, 3.2f);
        g.bisagraTrampilla = bt; g.colTrampilla = ct;
        Luz(go.transform, "LuzCasita", c + Vector3.up * 3.5f, new Color(0.85f, 0.6f, 1f), 9f, 4f);
        // cama de viruta
        Pieza("Cama", go.transform, Guardar(Bola("CamaViruta", 1.8f, new Vector3(1f, 0.25f, 0.8f)), "CamaViruta"), mViruta, c + new Vector3(2.2f, 0f, 1.6f), Quaternion.identity, false);
    }

    static void CrearColeccion(Transform p)
    {
        var go = new GameObject("ColeccionBolita"); go.transform.SetParent(p, false);
        Vector3 c = new Vector3(67.6f, 25f, -92.4f);
        // pedestal (tapa de refresco) para la pila
        Pieza("Pedestal", go.transform, Guardar(Cilindro("Pedestal", 1.1f, 1f, 24), "Pedestal"), mTapaRoja, c, Quaternion.identity);
        // monton alrededor: modelos de objetos del salon y cosas hechas por codigo
        string[] modelos =
        {
            "Assets/nuevos items 25-10-26/clip_completo_pintado.fbx", "Assets/nuevos items 25-10-26/chincheta_01_pintado.fbx", "Assets/nuevos items 25-10-26/liga_pintada.fbx",
            "Assets/nuevos items 25-10-26/tornillo_pintado.fbx", "Assets/nuevos items 25-10-26/tuerca_pintado.fbx", "Assets/nuevos items 25-10-26/broche_papel_pintado.fbx",
            "Assets/nuevos items 25-10-26/chincheta_doblada_pintado.fbx", "Assets/nuevos items 25-10-26/rollo_alambre_pintado.fbx", "Assets/simple-eraser/Simple Eraser/Eraser.fbx",
            "Assets/black_generic_hair_tie.glb", "Assets/bmw_m3_competition.glb",
        };
        int n = 0;
        for (int i = 0; i < 18; i++)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelos[i % modelos.Length]);
            if (asset == null) continue;
            var o = (GameObject)PrefabUtility.InstantiatePrefab(asset, go.transform);
            PrefabUtility.UnpackPrefabInstance(o, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var cc in o.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(cc);
            // escala para que mida ~1.6 u
            o.transform.localScale = Vector3.one;
            var rs = o.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { Object.DestroyImmediate(o); continue; }
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float k = (modelos[i % modelos.Length].Contains("bmw") ? 3.2f : R(1.2f, 2f)) / Mathf.Max(0.01f, b.size.magnitude);
            o.transform.localScale = Vector3.one * k;
            float a = R(0f, Mathf.PI * 2f), d = R(1.3f, 2.5f);
            o.transform.position = c + new Vector3(Mathf.Cos(a) * d, R(0.1f, 0.9f), Mathf.Sin(a) * d * 0.6f) + Vector3.forward * 0.6f;
            o.transform.rotation = Quaternion.Euler(R(-40f, 40f), R(0f, 360f), R(-40f, 40f));
            n++;
        }
        // monedas, tapitas y un anillo de "oro"
        var moneda = Guardar(Cilindro("Moneda", 0.55f, 0.12f, 20), "Moneda");
        for (int i = 0; i < 14; i++)
        {
            float a = R(0f, Mathf.PI * 2f), d = R(0.8f, 2.6f);
            Pieza("Moneda", go.transform, moneda, mDorado, c + new Vector3(Mathf.Cos(a) * d, R(0.02f, 0.6f), Mathf.Sin(a) * d * 0.7f), Quaternion.Euler(R(-30f, 30f), R(0f, 360f), R(-30f, 30f)), false);
        }
        var tapita = Guardar(Cilindro("TapitaCol", 0.8f, 0.5f, 18), "TapitaCol");
        Material[] mats = { mTapaRoja, mTapaAzul, mVerdeOscuro, mLapiz };
        for (int i = 0; i < 6; i++)
        {
            float a = R(0f, Mathf.PI * 2f), d = R(1.3f, 2.6f);
            Pieza("Tapita", go.transform, tapita, mats[i % mats.Length], c + new Vector3(Mathf.Cos(a) * d, R(0f, 0.4f), Mathf.Sin(a) * d * 0.7f), Quaternion.Euler(R(-60f, 60f), 0f, R(-60f, 60f)), false);
        }
        Pieza("Anillo", go.transform, Guardar(Toro("Anillo", 0.6f, 0.13f, 24, 8), "Anillo"), mDorado, c + new Vector3(-1.6f, 0.5f, 0.6f), Quaternion.Euler(70f, 20f, 0f), false);
        Texto(go.transform, "Mi pila. MIA.\n<size=70%>-B.</size>", c + new Vector3(-2.6f, 1.4f, 1.5f), Vector3.forward + Vector3.right * 0.4f, 2.4f, 1.2f, 1.6f, new Color(0.2f, 0.15f, 0.15f), true);
    }

    static GameObject CrearBateria(Vector3 pos)
    {
        var go = new GameObject("BATERIA_Bolita");
        go.transform.position = pos;
        var vis = new GameObject("Visual").transform; vis.SetParent(go.transform, false);
        var mc = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Cabina/Generado/BateriaCuerpo.asset");
        var mtap = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Cabina/Generado/BateriaTapas.asset");
        var mf = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Cabina/Generado/BateriaFranjas.asset");
        if (mc != null) Objeto("Cuerpo", vis, mc, AssetDatabase.LoadAssetAtPath<Material>("Assets/Cabina/Materiales/BateriaVerde.mat"), false);
        if (mtap != null) Objeto("Tapas", vis, mtap, AssetDatabase.LoadAssetAtPath<Material>("Assets/Cabina/Materiales/BateriaMetal.mat"), false);
        if (mf != null) Objeto("Franjas", vis, mf, AssetDatabase.LoadAssetAtPath<Material>("Assets/Cabina/Materiales/BateriaBrillo.mat"), false);
        var wi = go.AddComponent<WorldItem>();
        wi.itemId = MaquinaCrecimiento.ItemBateria; wi.displayName = "Bateria verde"; wi.quantity = 1;
        wi.itemIcon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Cabina/Sprites/Bateria.png");
        var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.center = new Vector3(0f, 0.85f, 0f); bc.size = new Vector3(1.2f, 1.9f, 1.2f);
        var sp = go.AddComponent<ItemSparkle>(); sp.color = new Color(0.5f, 1f, 0.6f, 1f);
        var l = new GameObject("Luz").AddComponent<Light>(); l.transform.SetParent(go.transform, false); l.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        l.type = LightType.Point; l.color = new Color(0.35f, 1f, 0.45f); l.range = 12f; l.intensity = 14f; l.shadows = LightShadows.None;
        var bv = go.AddComponent<BateriaVerde>(); bv.visual = vis; bv.luz = l;
        return go;
    }

    // ================================================================== palancas y carteles
    static PalancaFeria CrearPalanca(Transform p, string nombre, Vector3 pos, Quaternion rot, string texto)
    {
        var go = new GameObject(nombre); go.transform.SetParent(p, false); go.transform.SetPositionAndRotation(pos, rot);
        var caja = Pieza("Caja", go.transform, Guardar(Caja("CajaPalanca", new Vector3(1.6f, 1f, 1.2f)), "CajaPalanca"), mRojo, pos + rot * new Vector3(0f, 0.5f, 0f), rot);
        var foco = Pieza("Foco", go.transform, Guardar(Bola("FocoPalanca", 0.25f, Vector3.one), "FocoPalanca"), mFocoColor, pos + rot * new Vector3(0.55f, 1.05f, 0.35f), rot, false);
        var brazo = new GameObject("Brazo").transform; brazo.SetParent(go.transform, false); brazo.localPosition = new Vector3(0f, 1f, 0f);
        Pieza("Palito", brazo, Guardar(Caja("PalitoPalanca", new Vector3(0.3f, 2.2f, 0.18f)), "PalitoPalanca"), mPalito, brazo.position + brazo.rotation * new Vector3(0f, 1.1f, 0f), brazo.rotation, false);
        Pieza("Pomo", brazo, Guardar(Bola("PomoPalanca", 0.42f, Vector3.one), "PomoPalanca"), mRojo, brazo.position + brazo.rotation * new Vector3(0f, 2.3f, 0f), brazo.rotation, false);
        var pl = go.AddComponent<PalancaFeria>();
        pl.nombre = texto; pl.brazo = brazo; pl.foco = foco.GetComponent<Renderer>();
        return pl;
    }

    static void CrearCarteles(Transform p)
    {
        var go = new GameObject("Carteles"); go.transform.SetParent(p, false);
        var t = go.transform;
        Color tinta = new Color(0.2f, 0.12f, 0.1f);
        Cartel(t, "ENTRADA GRATIS\n<size=65%>Salida: ya veremos</size>", new Vector3(69f, 0f, -45.5f), Vector3.forward, 5f, 2.6f, 3.4f, tinta);
        Cartel(t, "Altura minima\npara subir:\n<size=130%>1 hamster</size>", new Vector3(55.5f, 0f, -48f), Vector3.forward, 3.6f, 3f, 2.6f, tinta);
        // el cartel triple de la feria (el proyecto del 3°B)
        Vector3 c = new Vector3(18.8f, Mesa, -73f);
        Bloque("TripticoCentro", t, c + new Vector3(-0.1f, 0f, -2.6f), c + new Vector3(0.1f, 6.2f, 2.6f), mCartonClaro);
        Pieza("TripticoIzq", t, Guardar(Caja("Triptico", new Vector3(0.2f, 6.2f, 2.6f)), "Triptico"), mCartonClaro, c + new Vector3(0.9f, 3.1f, -3.6f), Quaternion.Euler(0f, -35f, 0f));
        Pieza("TripticoDer", t, Guardar(Caja("Triptico", new Vector3(0.2f, 6.2f, 2.6f)), "Triptico"), mCartonClaro, c + new Vector3(0.9f, 3.1f, 3.6f), Quaternion.Euler(0f, 35f, 0f));
        Texto(t, "¿PUEDE UN HAMSTER\nSER FELIZ\nPARA SIEMPRE?", c + new Vector3(0.12f, 4.6f, 0f), Vector3.right, 5f, 2.6f, 4.2f, new Color(0.15f, 0.25f, 0.6f));
        Texto(t, "Hipotesis: SI\n<size=80%>Metodo: tubos, focos\ny mucha musica</size>", c + new Vector3(0.12f, 2.2f, 0f), Vector3.right, 5f, 2f, 2.6f, tinta);
        Texto(t, "<color=#d22>10</color> !\n<size=60%>¡Excelente!\n-la maestra</size>", c + new Vector3(1.0f, 3.3f, 3.52f), new Vector3(0.82f, 0f, -0.57f), 2.2f, 2.2f, 3f, tinta);
        Texto(t, "Integrantes:\n<size=80%>Bolita\n(y otros niños)</size>", c + new Vector3(1.0f, 3.3f, -3.52f), new Vector3(0.82f, 0f, 0.57f), 2.2f, 2.2f, 2.4f, tinta);
        Cartel(t, "Si ves a Bolita:\n<size=120%>NO CORRAS</size>\n<size=65%>(bueno, si corre)</size>", new Vector3(27.2f, Mesa, -70.5f), Vector3.forward, 3.8f, 3.4f, 2.8f, tinta);
        Texto(t, "Dele cuerda\n<size=65%>(no se lo recomendamos)</size>", new Vector3(21.5f, Mesa + 2.6f, -75.97f), Vector3.forward, 5.4f, 2.2f, 3.2f, new Color(1f, 0.85f, 0.45f));
        Cartel(t, "CARRUSEL DE TAPITAS\n<size=65%>Gire bajo su propio riesgo</size>", new Vector3(31.5f, Mesa, -91.5f), Vector3.right, 4.6f, 2.2f, 3f, tinta);
        Cartel(t, "HOY:\n<size=130%>¡EL GRAN BOLITA!</size>\n<size=60%>Funcion unica. Todos los dias.</size>", new Vector3(29.2f, Mesa, -66.6f), new Vector3(0.3f, 0f, 1f), 5.2f, 3.4f, 3.4f, new Color(0.75f, 0.1f, 0.1f));
        Cartel(t, "No alimentar a Bolita\n<size=65%>(ya comio suficiente)</size>", new Vector3(42.6f, Mesa, -76.8f), Vector3.left, 4.2f, 2f, 2.4f, tinta);
        Cartel(t, "Objetos perdidos:\n<size=80%>preguntar a Bolita</size>", new Vector3(43.6f, Mesa, -82.3f), Vector3.left, 4f, 2f, 2.4f, tinta);
        Texto(t, "MIRADOR DEL JUGO\n<size=60%>Interruptor general de luces</size>", new Vector3(56.97f, 18.6f, -69f), Vector3.left, 7.4f, 2.6f, 3.8f, new Color(0.9f, 0.95f, 1f));
        Cartel(t, "ESCALERA DE BORRADORES\n<size=65%>Prohibido para hamsters\n(no caben)</size>", new Vector3(67.6f, Mesa, -64.6f), Vector3.forward, 4.6f, 2.6f, 2.8f, tinta);
        Cartel(t, "COLECCION PRIVADA\nDE BOLITA\n<size=65%>No tocar. No mirar.\nNo respirar cerca.</size>", new Vector3(70.8f, 25f, -79.9f), Vector3.forward, 2.6f, 3.4f, 2.2f, new Color(0.45f, 0.1f, 0.5f));
        // dentro del tubo sin salida, al final
        var t10 = tubos.First(x => x.nombre == "T10_SinSalida");
        Vector3 fin = t10.piso[t10.piso.Count - 1], antes = t10.piso[t10.piso.Count - 6];
        Vector3 dir = (fin - antes); dir.y = 0f; dir.Normalize();
        Texto(t, "¿Que esperabas?\n<size=70%>-Bolita</size>", fin + Vector3.up * 3f - dir * 0.6f, -dir, 4f, 2.4f, 3f, Color.white, true);
    }

    /// <summary>Texto 3D legible desde 'haciaLector' (TextMeshPro mira hacia -Z: lo giramos).</summary>
    static TextMeshPro Texto(Transform p, string texto, Vector3 pos, Vector3 haciaLector, float ancho, float alto, float tam, Color color, bool fondo = false)
    {
        var go = new GameObject("Texto");
        go.transform.SetParent(p, false);
        haciaLector.y = 0f; haciaLector.Normalize();
        go.transform.SetPositionAndRotation(pos + haciaLector * 0.03f, Quaternion.LookRotation(-haciaLector, Vector3.up));
        var tm = go.AddComponent<TextMeshPro>();
        tm.rectTransform.sizeDelta = new Vector2(ancho, alto);
        tm.fontSize = tam; tm.enableAutoSizing = true; tm.fontSizeMin = 0.5f; tm.fontSizeMax = tam;
        tm.alignment = TextAlignmentOptions.Center; tm.fontStyle = FontStyles.Bold;
        tm.text = texto; tm.color = color; tm.textWrappingMode = TextWrappingModes.Normal;
        if (fuente != null) tm.font = fuente;
        if (fondo) Pieza("Fondo", go.transform, Guardar(Caja("FondoTexto", new Vector3(ancho + 0.3f, alto + 0.3f, 0.1f)), "FondoTexto"), mPapel, pos - haciaLector * 0.03f, Quaternion.LookRotation(-haciaLector), false);
        return tm;
    }

    // cartel de carton sobre un palito, clavado en el piso
    static void Cartel(Transform p, string texto, Vector3 pie, Vector3 haciaLector, float ancho, float alto, float tam, Color color)
    {
        haciaLector.y = 0f; haciaLector.Normalize();
        var rot = Quaternion.LookRotation(haciaLector);
        Pieza("Palo", p, Guardar(Caja("PaloCartel", new Vector3(0.3f, 2.2f + alto, 0.25f)), "PaloCartel"), mPalito, pie + Vector3.up * (1.1f + alto * 0.5f) - haciaLector * 0.2f, rot, false);
        var tabla = Pieza("Tabla", p, Guardar(Caja("TablaCartel", new Vector3(ancho, alto, 0.2f)), "TablaCartel"), mCarton, pie + Vector3.up * (2.2f + alto * 0.5f), rot, false);
        Texto(p, texto, pie + Vector3.up * (2.2f + alto * 0.5f) + haciaLector * 0.11f, haciaLector, ancho - 0.3f, alto - 0.2f, tam, color);
    }

    // ================================================================== Bolita
    static Bolita CrearBolita(Transform p)
    {
        var go = new GameObject("Bolita"); go.transform.SetParent(p, false);
        go.transform.position = CentroGuarida;
        var vis = new GameObject("Visual").transform; vis.SetParent(go.transform, false);
        // cuerpo: una bola deformada (cachetes, nalguitas, pancita plana); u de la textura = "que tan panza" es
        var cuerpo = new GameObject("Cuerpo").transform; cuerpo.SetParent(vis, false); cuerpo.localPosition = new Vector3(0f, 2.35f, 0f);
        Objeto("Pelaje", cuerpo, Guardar(CuerpoHamster(), "CuerpoBolita"), mPelaje, false);
        var cabeza = new GameObject("Cabeza").transform; cabeza.SetParent(vis, false); cabeza.localPosition = new Vector3(0f, 3.1f, 2.4f);
        System.Func<string, Transform, Mesh, Material, Vector3, Vector3, Transform> parte = (n, padre, m, mat, pos, esc) =>
        {
            var o = Objeto(n, padre, m, mat, false); o.transform.localPosition = pos; o.transform.localScale = esc; return o.transform;
        };
        var esfera = Guardar(Bola("EsferaBolita", 1f, Vector3.one, 12, 18), "EsferaBolita");
        foreach (float sx in new[] { -1f, 1f })
        {
            parte("Ojo", cabeza, esfera, mOjo, new Vector3(sx * 1.2f, 0.35f, 1.15f), Vector3.one * 0.46f);
            parte("Brillo", cabeza, esfera, mBrillo, new Vector3(sx * 1.12f + 0.1f, 0.55f, 1.55f), Vector3.one * 0.13f);
        }
        var nariz = parte("Nariz", cabeza, esfera, mRosa, new Vector3(0f, -0.25f, 1.95f), new Vector3(0.32f, 0.24f, 0.24f));
        foreach (float sx in new[] { -0.15f, 0.15f }) parte("Diente", cabeza, Guardar(Caja("Diente", new Vector3(0.24f, 0.45f, 0.1f)), "Diente"), mBlanco, new Vector3(sx, -0.85f, 1.8f), Vector3.one);
        var bigote = Guardar(Caja("Bigote", new Vector3(2.4f, 0.04f, 0.04f)), "Bigote");
        for (int i = 0; i < 3; i++)
            foreach (float sx in new[] { -1f, 1f })
            {
                var b = parte("Bigote", cabeza, bigote, mNegro, new Vector3(sx * 1.25f, -0.35f + i * 0.12f, 1.6f), Vector3.one);
                b.localRotation = Quaternion.Euler(0f, -sx * 18f, sx * (i - 1) * 9f);
            }
        Transform orejaI = null, orejaD = null;
        foreach (float sx in new[] { -1f, 1f })
        {
            var piv = new GameObject(sx < 0 ? "OrejaI" : "OrejaD").transform; piv.SetParent(cabeza, false); piv.localPosition = new Vector3(sx * 1.25f, 1.3f, -0.3f);
            parte("Oreja", piv, esfera, mPelaje, new Vector3(0f, 0.55f, 0f), new Vector3(0.75f, 0.75f, 0.28f));
            parte("Dentro", piv, esfera, mRosa, new Vector3(0f, 0.55f, 0.12f), new Vector3(0.52f, 0.55f, 0.18f));
            if (sx < 0) orejaI = piv; else orejaD = piv;
        }
        // sombrero de copa de maestro de ceremonias
        var som = new GameObject("Sombrero").transform; som.SetParent(cabeza, false); som.localPosition = new Vector3(0.2f, 1.25f, 0.1f); som.localRotation = Quaternion.Euler(-8f, 0f, -14f);
        parte("Ala", som, Guardar(Cilindro("AlaSombrero", 1.05f, 0.12f, 24), "AlaSombrero"), mNegro, Vector3.zero, Vector3.one);
        parte("Copa", som, Guardar(Cilindro("CopaSombrero", 0.65f, 1.25f, 24), "CopaSombrero"), mNegro, Vector3.up * 0.1f, Vector3.one);
        parte("Cinta", som, Guardar(Cilindro("CintaSombrero", 0.67f, 0.28f, 24), "CintaSombrero"), mRojo, Vector3.up * 0.2f, Vector3.one);
        // corbatin
        var cor = new GameObject("Corbatin").transform; cor.SetParent(vis, false); cor.localPosition = new Vector3(0f, 1.75f, 3.85f);
        foreach (float sx in new[] { -1f, 1f })
        {
            var o = parte("Lazo", cor, Guardar(Torno("Lazo", new[] { new Vector2(0.05f, 0f), new Vector2(0.38f, 0.55f), new Vector2(0.05f, 0.62f) }, 10), "Lazo"), mRojo, Vector3.zero, Vector3.one);
            o.localRotation = Quaternion.Euler(0f, 0f, sx * 90f);
        }
        parte("Nudo", cor, esfera, mRojo, Vector3.zero, Vector3.one * 0.18f);
        // cachetes (se inflan)
        var mejI = new GameObject("MejillaI").transform; mejI.SetParent(vis, false); mejI.localPosition = new Vector3(-1.75f, 2.2f, 2.6f);
        var mejD = new GameObject("MejillaD").transform; mejD.SetParent(vis, false); mejD.localPosition = new Vector3(1.75f, 2.2f, 2.6f);
        foreach (var m in new[] { mejI, mejD }) parte("Cachete", m, esfera, mPelaje, Vector3.zero, new Vector3(0.95f, 0.85f, 1f));
        // patitas y colita
        var patas = new List<Transform>();
        foreach (var (x, z) in new[] { (-1.25f, 2.7f), (1.25f, 2.7f), (-1.5f, -2.4f), (1.5f, -2.4f) })
            patas.Add(parte("Pata", vis, esfera, mRosa, new Vector3(x, 0.32f, z), new Vector3(0.48f, 0.3f, 0.65f)));
        var cola = parte("Cola", vis, esfera, mRosa, new Vector3(0f, 1.5f, -4.05f), new Vector3(0.28f, 0.22f, 0.4f));

        var b2 = go.AddComponent<Bolita>();
        b2.visual = vis; b2.cuerpo = cuerpo; b2.cabeza = cabeza; b2.orejaI = orejaI; b2.orejaD = orejaD; b2.nariz = nariz;
        b2.mejillaI = mejI; b2.mejillaD = mejD; b2.sombrero = som; b2.cola = cola; b2.patas = patas.ToArray();
        b2.chillidos = new[] { Audio("Chillido_1"), Audio("Chillido_2"), Audio("Chillido_3") };
        b2.chillidoEnojo = Audio("Chillido_Enojo"); b2.paso = Audio("Paso_Bolita"); b2.atorado = Audio("Atorado");
        b2.largoCuerpo = 8.2f; b2.radioAtrapar = 3.3f;
        return b2;
    }

    static Mesh CuerpoHamster()
    {
        const int LAT = 28, LON = 40;
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        for (int i = 0; i <= LAT; i++)
            for (int k = 0; k <= LON; k++)
            {
                float th = i / (float)LAT * Mathf.PI, ph = k / (float)LON * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                float r = 1f;
                // cachetes al frente y a los lados, nalguitas atras, hocico
                r += 0.14f * Mathf.Exp(-((d - new Vector3(0.7f, -0.1f, 0.6f)).sqrMagnitude) / 0.09f);
                r += 0.14f * Mathf.Exp(-((d - new Vector3(-0.7f, -0.1f, 0.6f)).sqrMagnitude) / 0.09f);
                r += 0.08f * Mathf.Exp(-((d - new Vector3(0f, -0.2f, 1f)).sqrMagnitude) / 0.05f);
                r += 0.06f * Mathf.Exp(-((d - new Vector3(0f, 0.1f, -1f)).sqrMagnitude) / 0.2f);
                Vector3 p = new Vector3(d.x * 2.55f, d.y * 2.3f, d.z * 4.1f) * r;
                if (p.y < -1.65f) p.y = -1.65f - (p.y + 1.65f) * 0.25f;    // pancita aplanada
                v.Add(p);
                float panza = Mathf.Clamp01(-d.y * 1.1f + Mathf.Max(0f, d.z) * 0.55f - 0.1f);
                uv.Add(new Vector2(panza, i / (float)LAT));
            }
        for (int i = 0; i < LAT; i++)
            for (int k = 0; k < LON; k++)
            {
                int a = i * (LON + 1) + k, b = a + LON + 1;
                t.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
            }
        return MallaDe("CuerpoBolita", v, t, uv);
    }

    static AudioClip Audio(string n) => AssetDatabase.LoadAssetAtPath<AudioClip>(HamsterAudio.Carpeta + n + ".wav");

    // ================================================================== grafo de Bolita
    const float RadioCuerpo = 1.7f;

    static bool Cabe(Vector3 p)
    {
        return !Physics.CheckCapsule(p + Vector3.up * 2.5f, p + Vector3.up * 3.1f, RadioCuerpo, ~0, QueryTriggerInteraction.Ignore);
    }

    static bool Paso(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a; float l = d.magnitude;
        if (l < 0.01f) return true;
        if (Physics.CapsuleCast(a + Vector3.up * 2.55f, a + Vector3.up * 3.05f, RadioCuerpo - 0.1f, d / l, l, ~0, QueryTriggerInteraction.Ignore)) return false;
        // y de regreso (CapsuleCast no ve lo que ya toca al salir)
        return !Physics.CapsuleCast(b + Vector3.up * 2.55f, b + Vector3.up * 3.05f, RadioCuerpo - 0.1f, -d / l, l, ~0, QueryTriggerInteraction.Ignore);
    }

    static void ConstruirGrafo(Bolita bol, System.Text.StringBuilder log)
    {
        var nodos = new List<Vector3>();
        var aristas = new HashSet<(int, int)>();
        System.Action<int, int> unir = (a, b) => { if (a == b) return; aristas.Add(a < b ? (a, b) : (b, a)); };

        // 1. tubos
        var extremos = new List<(int nodo, string conector, bool boca)>();
        foreach (var tb in tubos)
        {
            var muestras = new List<Vector3>();
            float acum = 0f; muestras.Add(tb.piso[0]);
            for (int i = 1; i < tb.piso.Count; i++)
            {
                acum += Vector3.Distance(tb.piso[i - 1], tb.piso[i]);
                if (acum >= 1.5f || i == tb.piso.Count - 1) { muestras.Add(tb.piso[i]); acum = 0f; }
            }
            int prim = nodos.Count;
            nodos.AddRange(muestras);
            for (int i = prim; i < nodos.Count - 1; i++) unir(i, i + 1);
            extremos.Add((prim, tb.ini, tb.ini == null));
            extremos.Add((nodos.Count - 1, tb.fin, tb.fin == null && !tb.tapaFin));
        }
        // 2. conectores: los extremos de sus tubos se juntan en su centro (ya son el mismo punto de piso)
        var centros = new Dictionary<string, int>();
        foreach (var e in extremos)
        {
            if (e.conector == null) continue;
            if (!centros.TryGetValue(e.conector, out int c)) { c = e.nodo; centros[e.conector] = c; }
            else unir(c, e.nodo);
        }
        // 3. rejillas sobre las plataformas (se valida que Bolita quepa)
        var rejillas = new (Vector2 min, Vector2 max, float y, float paso)[]
        {
            (new Vector2(18.5f, -94.5f), new Vector2(71.5f, -63.5f), Mesa, 2.5f),   // la mesa (y los conectores apoyados)
            (new Vector2(57.5f, -72.5f), new Vector2(66.5f, -65.5f), 22f, 2f),      // mirador
            (new Vector2(56.5f, -94.5f), new Vector2(71.5f, -79.5f), 25f, 2f),      // terraza
        };
        var grid = new List<int>();
        foreach (var (mn, mx, y, paso) in rejillas)
        {
            int antes = nodos.Count;
            int nx = Mathf.FloorToInt((mx.x - mn.x) / paso) + 1, nz = Mathf.FloorToInt((mx.y - mn.y) / paso) + 1;
            var idx = new int[nx, nz];
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    idx[i, k] = -1;
                    Vector3 q = new Vector3(mn.x + i * paso, y + 4.5f, mn.y + k * paso);
                    if (!Physics.Raycast(q, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(hit.point.y - y) > 0.6f || hit.normal.y < 0.8f) continue;
                    Vector3 pt = hit.point;
                    Vector3 dg = pt - CentroGuarida; dg.y = 0f;
                    if (dg.magnitude < RadioCasa + 0.6f && Mathf.Abs(pt.y - CentroGuarida.y) < 2f) continue;
                    if (!Cabe(pt)) continue;
                    idx[i, k] = nodos.Count; nodos.Add(pt); grid.Add(idx[i, k]);
                }
            for (int i = 0; i < nx; i++)
                for (int k = 0; k < nz; k++)
                {
                    int a = idx[i, k]; if (a < 0) continue;
                    foreach (var (di, dk) in new[] { (1, 0), (0, 1), (1, 1), (1, -1) })
                    {
                        int ii = i + di, kk = k + dk;
                        if (ii >= nx || kk < 0 || kk >= nz) continue;
                        int b = idx[ii, kk]; if (b < 0) continue;
                        if (Mathf.Abs(nodos[a].y - nodos[b].y) > 0.7f) continue;
                        if (Paso(nodos[a], nodos[b])) unir(a, b);
                    }
                }
            log.AppendLine($"rejilla y={y}: {nodos.Count - antes} nodos");
        }
        // 4. bocas de los tubos y centros de los conectores apoyados se unen con la rejilla cercana
        var puntos = new List<int>();
        foreach (var e in extremos) if (e.boca) puntos.Add(e.nodo);
        foreach (var kv in centros) if (conectores[kv.Key].apoyado) puntos.Add(kv.Value);
        foreach (int a in puntos)
        {
            int unidos = 0;
            foreach (int g in grid)
            {
                float d = Vector3.Distance(nodos[a], nodos[g]);
                if (d < 3.6f && Mathf.Abs(nodos[a].y - nodos[g].y) < 0.8f && Paso(nodos[a], nodos[g])) { unir(a, g); unidos++; }
            }
            if (unidos == 0) log.AppendLine("  boca SIN unir en " + nodos[a]);
        }
        // 5. salida: del tubo de entrada (boca en el piso) hasta el arco
        var tr = tubos[0];
        int bocaPiso = 0;   // el primer nodo es la boca del piso
        int arco1 = nodos.Count; nodos.Add(new Vector3(60f, 0f, -47.2f));
        int arco2 = nodos.Count; nodos.Add(new Vector3(60f, 0f, -45f));
        unir(bocaPiso, arco1); unir(arco1, arco2);

        // diagnostico: partes sueltas del grafo
        {
            var vv = new List<int>[nodos.Count];
            for (int i = 0; i < nodos.Count; i++) vv[i] = new List<int>();
            foreach (var (a, b) in aristas) { vv[a].Add(b); vv[b].Add(a); }
            var comp = new int[nodos.Count]; for (int i = 0; i < comp.Length; i++) comp[i] = -1;
            int nc = 0;
            for (int i = 0; i < nodos.Count; i++)
            {
                if (comp[i] >= 0) continue;
                var q = new Queue<int>(); q.Enqueue(i); comp[i] = nc; var bb = new Bounds(nodos[i], Vector3.zero); int cnt = 0;
                while (q.Count > 0) { int u = q.Dequeue(); cnt++; bb.Encapsulate(nodos[u]); foreach (int w in vv[u]) if (comp[w] < 0) { comp[w] = nc; q.Enqueue(w); } }
                if (cnt > 2) log.AppendLine($"  parte {nc}: {cnt} nodos, {bb.min:F0}..{bb.max:F0}");
                nc++;
            }
        }
        // 6. solo lo alcanzable desde la puerta de la casita
        int puerta = Cercano(nodos, new Vector3(66f, 25f, -81f), 3f);
        var vecinos = new List<int>[nodos.Count];
        for (int i = 0; i < nodos.Count; i++) vecinos[i] = new List<int>();
        foreach (var (a, b) in aristas) { vecinos[a].Add(b); vecinos[b].Add(a); }
        var visto = new bool[nodos.Count]; var cola = new Queue<int>(); cola.Enqueue(puerta); visto[puerta] = true;
        while (cola.Count > 0) { int u = cola.Dequeue(); foreach (int w in vecinos[u]) if (!visto[w]) { visto[w] = true; cola.Enqueue(w); } }
        var nuevo = new int[nodos.Count]; var finales = new List<Vector3>();
        for (int i = 0; i < nodos.Count; i++) { nuevo[i] = visto[i] ? finales.Count : -1; if (visto[i]) finales.Add(nodos[i]); }
        var ini = new int[finales.Count + 1]; var vec = new List<int>();
        for (int i = 0, j = 0; i < nodos.Count; i++)
        {
            if (!visto[i]) continue;
            ini[j] = vec.Count;
            foreach (int w in vecinos[i]) if (visto[w]) vec.Add(nuevo[w]);
            j++;
        }
        ini[finales.Count] = vec.Count;
        bol.nodos = finales.ToArray(); bol.inicio = ini; bol.vec = vec.ToArray();
        bol.nodoPuerta = nuevo[puerta];
        bol.nodoEscenario = Cercano(finales, Escenario, 1f);
        bol.nodoSalida = nuevo[arco2] >= 0 ? nuevo[arco2] : 0;
        int sueltos = nodos.Count - finales.Count;
        log.AppendLine($"grafo de Bolita: {finales.Count} nodos ({sueltos} sin alcanzar), {vec.Count / 2} aristas; escenario {bol.nodoEscenario} a {Vector3.Distance(finales[bol.nodoEscenario], Escenario):0.0} u; salida {(nuevo[arco2] >= 0 ? "ok" : "NO ALCANZABLE")}");
    }

    static int Cercano(List<Vector3> l, Vector3 p, float pesoY)
    {
        int m = 0; float dm = float.MaxValue;
        for (int i = 0; i < l.Count; i++) { Vector3 d = l[i] - p; d.y *= pesoY; float x = d.sqrMagnitude; if (x < dm) { dm = x; m = i; } }
        return m;
    }

    // ================================================================== audio
    static void CrearAudio(Hamsterlandia g)
    {
        var go = new GameObject("Audio"); go.transform.SetParent(g.transform, false);
        System.Func<string, AudioClip, bool, AudioSource> fuenteDe = (n, clip, loop) =>
        {
            var a = new GameObject(n).AddComponent<AudioSource>(); a.transform.SetParent(go.transform, false);
            a.clip = clip; a.loop = loop; a.playOnAwake = false; a.spatialBlend = 0f; a.volume = 0f;
            return a;
        };
        g.capas = new[] { fuenteDe("Vals_Caja", Audio("Vals_Caja"), true), fuenteDe("Vals_Organillo", Audio("Vals_Organillo"), true), fuenteDe("Vals_Theremin", Audio("Vals_Theremin"), true) };
        g.galope = fuenteDe("Galope", Audio("Galope"), true);
        g.ambiente = fuenteDe("Ambiente", Audio("Ambiente_Feria"), true);
        g.ruedaAudio = fuenteDe("Rueda", Audio("Rueda"), true);
        g.ruedaAudio.transform.position = new Vector3(Escenario.x, Mesa + 5f, -76.2f);
        g.ruedaAudio.spatialBlend = 0.7f; g.ruedaAudio.maxDistance = 80f; g.ruedaAudio.rolloffMode = AudioRolloffMode.Linear;
        g.fx = fuenteDe("Efectos", null, false); g.fx.volume = 1f;
        g.fanfarria = Audio("Fanfarria"); g.escape = Audio("Escape"); g.campana = Audio("Campana"); g.sonidoPalanca = Audio("Palanca");
        g.sonidoTobogan = Audio("Tobogan"); g.plop = Audio("Atorado");
    }

    // ================================================================== utilidades del menu
    [MenuItem("Herramientas/Hamsterlandia/Rehacer grafo de Bolita")]
    public static void RehacerGrafo()
    {
        var g = Object.FindAnyObjectByType<Hamsterlandia>();
        if (g == null || g.bolita == null) { Debug.LogWarning("[Hamsterlandia] primero: Construir"); return; }
        DefinirLaberinto();
        foreach (var t in tubos) t.piso = CatmullRom(t.control, 0.5f);
        var log = new System.Text.StringBuilder();
        Physics.SyncTransforms();
        ConstruirGrafo(g.bolita, log);
        AsignarAmagos(g);
        EditorUtility.SetDirty(g.bolita); EditorUtility.SetDirty(g);
        EditorSceneManager.MarkSceneDirty(g.gameObject.scene);
        Debug.Log("[Hamsterlandia] " + log);
    }

    static void AsignarAmagos(Hamsterlandia g)
    {
        var b = g.bolita;
        g.nodosAmago = new[]
        {
            b.NodoCercano(Centro(conectores["J4"]) - Vector3.up * H), b.NodoCercano(new Vector3(39.5f, Alto2, -82f)),
            b.NodoCercano(Centro(conectores["J2"]) - Vector3.up * H), b.NodoCercano(new Vector3(62f, 22f, -69f)),
        };
    }

    [MenuItem("Herramientas/Hamsterlandia/Llevar al jugador a la entrada")]
    public static void LlevarJugador() { Llevar(new Vector3(60f, 0.1f, -38f), 180f); }

    [MenuItem("Herramientas/Hamsterlandia/Llevar al jugador al escenario")]
    public static void LlevarEscenario() { Llevar(Escenario + new Vector3(0f, 0.1f, 4f), 180f); }

    static void Llevar(Vector3 pos, float yaw)
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc == null) return;
        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pc.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        if (cc != null) cc.enabled = true;
        if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(pc.gameObject.scene);
    }
}
