using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Vegetacion del exterior (pasto, flores, arbustos, abetos, rocas, troncos...).
/// No guarda miles de objetos en la escena: al activarse genera las posiciones de forma
/// determinista (misma semilla = mismo bosque) y las dibuja con GPU instancing.
/// Optimizaciones: celdas con culling por frustum, LOD0/LOD1 por distancia, el pasto se
/// va adelgazando con la distancia, los abetos lejanos pasan a impostor (un quad), y la
/// lista visible solo se recalcula cuando la camara se mueve.
/// Solo las rocas, tocones, troncos y abetos tienen colision (colliders primitivos).
/// </summary>
[ExecuteAlways]
public class VegetacionExterior : MonoBehaviour
{
    public enum Colision { Ninguna, Capsula, Caja }

    [System.Serializable]
    public class Variante
    {
        public Mesh lod0;
        public Mesh lod1;
    }

    [System.Serializable]
    public class Prototipo
    {
        public string nombre;
        public Variante[] variantes;
        public Material[] materiales;
        [Tooltip("Instancias por cada 1000 unidades cuadradas")]
        public float densidad = 1f;
        public Vector2 escala = new Vector2(18f, 24f);
        [Tooltip("0 = repartido por todo el mapa, 1 = solo en manchas")]
        [Range(0f, 1f)] public float agrupamiento;
        public float frecuenciaManchas = 0.012f;
        public float distanciaLod = 60f;
        public float distanciaMax = 200f;
        [Tooltip("Fraccion que se dibuja lejos (entre el LOD y la distancia maxima). Adelgaza el pasto")]
        [Range(0f, 1f)] public float densidadLejos = 1f;
        public bool sombras;
        public bool alinearAlSuelo;
        public bool rotacionLibre;
        [Tooltip("Fraccion de la altura que queda enterrada")]
        [Range(0f, 0.6f)] public float hundir;
        public Colision colision;
        [Tooltip("Las demas plantas no crecen dentro de este radio (fraccion del tamaño)")]
        public float exclusion;
        [Tooltip("Distancia minima al edificio")]
        public float margenEdificio = 4f;
        [Header("Impostor lejano (opcional): se usa entre distanciaMax y distanciaImpostor")]
        public Material impostor;
        public float distanciaImpostor;
        public Vector2 tamImpostor = Vector2.one;   // ancho/alto del impostor relativos a la escala
    }

    public Prototipo[] prototipos;
    public Mesh quadImpostor;
    public int semilla = 1234;
    public float tamCelda = 40f;
    public float margenCerca = 5f;
    [Tooltip("Distancia hasta la que se dibujan las sombras de la vegetacion fuera de camara")]
    public float distanciaSombras = 55f;
    public bool crearColisiones = true;
    [Tooltip("Dibuja las celdas de culling al seleccionar el objeto")]
    public bool debugCeldas;

    // ---------------------------------------------------------------- datos generados
    class Grupo
    {
        public Prototipo proto;
        public Mesh lod0, lod1;
        public Matrix4x4[] instancias;
        public Matrix4x4[] impostores;           // solo si el prototipo tiene impostor
        public Matrix4x4[] buf0, buf1, bufImp;    // visibles de este frame
        public int n0, n1, nImp;
        public RenderParams[] rp;                 // uno por submalla
        public RenderParams rpImp;
    }

    struct Rango { public int grupo, inicio, cuenta; }

    class Celda
    {
        public Bounds bounds;
        public List<Rango> rangos = new List<Rango>();
    }

    struct Solido { public Vector2 pos; public float radio; }

    readonly List<Grupo> grupos = new List<Grupo>();
    readonly List<Celda> celdas = new List<Celda>();
    readonly List<Solido> solidos = new List<Solido>();
    readonly Plane[] planos = new Plane[6];
    Transform colisiones;
    bool generado;

    Camera ultimaCam;
    Vector3 ultimaPos;
    Quaternion ultimaRot;

    /// <summary>Lo activa el hormiguero: bajo tierra no se dibuja la vegetacion de afuera.</summary>
    public static bool ocultoGlobal;

    public int TotalInstancias { get; private set; }
    public int VisiblesUltimoFrame { get; private set; }

    // ---------------------------------------------------------------- ciclo de vida
    void OnEnable()
    {
        Generar();
        RenderPipelineManager.beginCameraRendering += AlRenderizarCamara;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= AlRenderizarCamara;
        Limpiar();
    }

    void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        generado = false;   // se regenera en el siguiente Update (no se puede destruir objetos aqui)
    }

    void Update()
    {
        if (!generado) Generar();
    }

    void Limpiar()
    {
        grupos.Clear(); celdas.Clear(); solidos.Clear(); rejillaSolidos.Clear();
        if (colisiones != null)
        {
            if (Application.isPlaying) Destroy(colisiones.gameObject); else DestroyImmediate(colisiones.gameObject);
        }
        colisiones = null;
        generado = false;
        ultimaCam = null;
    }

    // ---------------------------------------------------------------- generacion
    /// <summary>Posiciones de los objetos solidos (rocas, troncos...) para que el generador de bichos no los ponga encima.</summary>
    /// <summary>Punto mas alto de cada planta de esos prototipos dentro de un radio (para que las abejas se posen en las flores).</summary>
    public List<Vector3> CimasDe(Vector2 centro, float radio, params string[] nombres)
    {
        if (!generado) Generar();
        var l = new List<Vector3>();
        foreach (var g in grupos)
        {
            if (System.Array.IndexOf(nombres, g.proto.nombre) < 0) continue;
            float alto = g.lod0.bounds.max.y;
            foreach (var m in g.instancias)
            {
                Vector3 p = m.GetColumn(3);
                if ((new Vector2(p.x, p.z) - centro).sqrMagnitude > radio * radio) continue;
                l.Add(p + (Vector3)m.GetColumn(1) * alto * 0.92f);
            }
        }
        return l;
    }

    public List<Vector3> PosicionesSolidas()
    {
        if (!generado) Generar();
        var l = new List<Vector3>();
        foreach (var s in solidos) l.Add(new Vector3(s.pos.x, s.radio, s.pos.y));
        return l;
    }

    public void Generar()
    {
        Limpiar();
        if (prototipos == null) return;
        var rnd = new System.Random(semilla);
        Vector3 c = ZonaJuego.centro;
        float R = ZonaJuego.radioCerca - margenCerca;
        float area = Mathf.PI * R * R - (ZonaJuego.edificioMax.x - ZonaJuego.edificioMin.x) * (ZonaJuego.edificioMax.y - ZonaJuego.edificioMin.y);

        // celdas en una rejilla que cubre el circulo
        int n = Mathf.CeilToInt(2f * R / tamCelda);
        var celdaDe = new Dictionary<int, Celda>();
        var porGrupoCelda = new Dictionary<long, List<Matrix4x4>>();
        var porGrupoCeldaImp = new Dictionary<long, List<Matrix4x4>>();

        if (crearColisiones)
        {
            var go = new GameObject("Colisiones vegetacion");
            go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;
            go.transform.SetParent(transform, false);
            go.isStatic = true;
            colisiones = go.transform;
        }

        // primero los solidos, luego el resto (que evita crecer dentro de ellos)
        var orden = new List<Prototipo>(prototipos);
        orden.Sort((a, b) => (b.exclusion > 0f).CompareTo(a.exclusion > 0f));

        foreach (var p in orden)
        {
            if (p == null || p.variantes == null || p.variantes.Length == 0 || p.materiales == null || p.materiales.Length == 0) continue;
            int primerGrupo = grupos.Count;
            foreach (var v in p.variantes)
            {
                if (v == null || v.lod0 == null) continue;
                var g = new Grupo { proto = p, lod0 = v.lod0, lod1 = v.lod1 != null ? v.lod1 : v.lod0 };
                grupos.Add(g);
            }
            int nGrupos = grupos.Count - primerGrupo;
            if (nGrupos == 0) continue;

            int objetivo = Mathf.RoundToInt(p.densidad * area / 1000f);
            int intentos = objetivo * 6, hechos = 0;
            float semMancha = (float)rnd.NextDouble() * 500f;
            for (int i = 0; i < intentos && hechos < objetivo; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = R * Mathf.Sqrt((float)rnd.NextDouble());
                float x = c.x + Mathf.Cos(a) * r, z = c.z + Mathf.Sin(a) * r;
                double prob = rnd.NextDouble(), rv = rnd.NextDouble(), rs = rnd.NextDouble(), ry = rnd.NextDouble(), rx = rnd.NextDouble(), rz = rnd.NextDouble();
                int vi = rnd.Next(nGrupos);

                if (ZonaJuego.DistanciaEdificio(x, z) < p.margenEdificio) continue;
                if (ZonaJuego.DistanciaHormiguero(x, z) < ZonaJuego.hormigueroRadio + 2f) continue;   // monticulo del hormiguero
                if (ZonaJuego.DistanciaArbol(x, z) < ZonaJuego.arbolRadio + (p.exclusion > 0f ? 14f : 2f)) continue;   // tronco del arbol
                if (p.agrupamiento > 0f)
                {
                    float m = Mathf.PerlinNoise(x * p.frecuenciaManchas + semMancha, z * p.frecuenciaManchas + semMancha * 0.7f);
                    m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.42f, 0.68f, m));
                    if (prob > Mathf.Lerp(1f, m, p.agrupamiento)) continue;
                }
                var g = grupos[primerGrupo + vi];
                float s = Mathf.Lerp(p.escala.x, p.escala.y, (float)rs);
                Bounds mb = g.lod0.bounds;
                float radio = Mathf.Max(mb.extents.x, mb.extents.z) * s;
                if (ChocaSolido(x, z, radio * 0.6f)) continue;

                float y = ZonaJuego.Altura(x, z) - mb.size.y * s * p.hundir;
                Quaternion rot = Quaternion.Euler(0f, (float)ry * 360f, 0f);
                if (p.rotacionLibre) rot = Quaternion.Euler(((float)rx - 0.5f) * 50f, (float)ry * 360f, ((float)rz - 0.5f) * 50f);
                if (p.alinearAlSuelo) rot = Quaternion.FromToRotation(Vector3.up, NormalSuelo(x, z)) * rot;
                var pos = new Vector3(x, y, z);
                var m4 = Matrix4x4.TRS(pos, rot, Vector3.one * s);

                if (p.exclusion > 0f) AgregarSolido(x, z, radio * p.exclusion);
                if (p.colision != Colision.Ninguna && colisiones != null) CrearColision(p, g.lod0, pos, rot, s);

                int cx = Mathf.Clamp(Mathf.FloorToInt((x - (c.x - R)) / tamCelda), 0, n - 1);
                int cz = Mathf.Clamp(Mathf.FloorToInt((z - (c.z - R)) / tamCelda), 0, n - 1);
                int ci = cz * n + cx;
                if (!celdaDe.TryGetValue(ci, out var celda))
                {
                    celda = new Celda();
                    celda.bounds = new Bounds(new Vector3(c.x - R + (cx + 0.5f) * tamCelda, 0f, c.z - R + (cz + 0.5f) * tamCelda), new Vector3(tamCelda, 1f, tamCelda));
                    celdaDe[ci] = celda;
                }
                // la celda crece para contener la planta completa (para el culling)
                var wb = TransformarBounds(mb, m4);
                celda.bounds.Encapsulate(wb);

                long clave = ((long)(primerGrupo + vi) << 32) | (uint)ci;
                if (!porGrupoCelda.TryGetValue(clave, out var lista)) porGrupoCelda[clave] = lista = new List<Matrix4x4>();
                lista.Add(m4);
                if (p.impostor != null && quadImpostor != null)
                {
                    if (!porGrupoCeldaImp.TryGetValue(clave, out var li)) porGrupoCeldaImp[clave] = li = new List<Matrix4x4>();
                    li.Add(Matrix4x4.TRS(new Vector3(x, ZonaJuego.Altura(x, z), z), Quaternion.identity,
                        new Vector3(p.tamImpostor.x * s, p.tamImpostor.y * s, 1f)));
                }
                hechos++;
            }
        }

        // pasar las listas a arreglos contiguos por grupo, ordenados por celda
        var porGrupo = new List<KeyValuePair<long, List<Matrix4x4>>>(porGrupoCelda);
        porGrupo.Sort((a, b) => a.Key.CompareTo(b.Key));
        var acumulado = new List<Matrix4x4>[grupos.Count];
        var acumuladoImp = new List<Matrix4x4>[grupos.Count];
        for (int i = 0; i < grupos.Count; i++) { acumulado[i] = new List<Matrix4x4>(); acumuladoImp[i] = new List<Matrix4x4>(); }
        foreach (var kv in porGrupo)
        {
            int gi = (int)(kv.Key >> 32), ci = (int)(kv.Key & 0xffffffff);
            var celda = celdaDe[ci];
            // orden aleatorio dentro de la celda: dibujar los primeros N adelgaza de forma uniforme
            var l = kv.Value;
            for (int i = l.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var t = l[i]; l[i] = l[j]; l[j] = t; }
            celda.rangos.Add(new Rango { grupo = gi, inicio = acumulado[gi].Count, cuenta = l.Count });
            acumulado[gi].AddRange(l);
            if (porGrupoCeldaImp.TryGetValue(kv.Key, out var li)) acumuladoImp[gi].AddRange(li);
        }
        celdas.AddRange(celdaDe.Values);

        TotalInstancias = 0;
        for (int i = 0; i < grupos.Count; i++)
        {
            var g = grupos[i];
            g.instancias = acumulado[i].ToArray();
            g.impostores = acumuladoImp[i].Count == g.instancias.Length && g.instancias.Length > 0 ? acumuladoImp[i].ToArray() : null;
            g.buf0 = new Matrix4x4[g.instancias.Length];
            g.buf1 = new Matrix4x4[g.instancias.Length];
            g.bufImp = g.impostores != null ? new Matrix4x4[g.impostores.Length] : null;
            int subs = Mathf.Min(g.lod0.subMeshCount, g.proto.materiales.Length);
            g.rp = new RenderParams[subs];
            for (int s = 0; s < subs; s++)
            {
                g.rp[s] = new RenderParams(g.proto.materiales[s])
                {
                    shadowCastingMode = g.proto.sombras ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                    worldBounds = new Bounds(ZonaJuego.centro, Vector3.one * ZonaJuego.radioCerca * 2.2f),
                    layer = gameObject.layer,
                };
            }
            if (g.impostores != null)
                g.rpImp = new RenderParams(g.proto.impostor) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, worldBounds = g.rp[0].worldBounds, layer = gameObject.layer };
            TotalInstancias += g.instancias.Length;
        }
        generado = true;
    }

    // rejilla espacial de solidos para que la comprobacion de choque no recorra todos
    readonly Dictionary<long, List<int>> rejillaSolidos = new Dictionary<long, List<int>>();
    const float celdaSolidos = 50f;

    static long ClaveSolido(int x, int z) { return ((long)x << 32) ^ (uint)z; }

    void AgregarSolido(float x, float z, float radio)
    {
        solidos.Add(new Solido { pos = new Vector2(x, z), radio = radio });
        long k = ClaveSolido(Mathf.FloorToInt(x / celdaSolidos), Mathf.FloorToInt(z / celdaSolidos));
        if (!rejillaSolidos.TryGetValue(k, out var l)) rejillaSolidos[k] = l = new List<int>();
        l.Add(solidos.Count - 1);
    }

    bool ChocaSolido(float x, float z, float r)
    {
        int cx = Mathf.FloorToInt(x / celdaSolidos), cz = Mathf.FloorToInt(z / celdaSolidos);
        for (int ix = cx - 1; ix <= cx + 1; ix++)
            for (int iz = cz - 1; iz <= cz + 1; iz++)
            {
                if (!rejillaSolidos.TryGetValue(ClaveSolido(ix, iz), out var l)) continue;
                foreach (int i in l)
                {
                    var s = solidos[i];
                    float dx = s.pos.x - x, dz = s.pos.y - z, rr = s.radio + r;
                    if (dx * dx + dz * dz < rr * rr) return true;
                }
            }
        return false;
    }

    static Vector3 NormalSuelo(float x, float z)
    {
        const float e = 1.5f;
        float hx = ZonaJuego.Altura(x + e, z) - ZonaJuego.Altura(x - e, z);
        float hz = ZonaJuego.Altura(x, z + e) - ZonaJuego.Altura(x, z - e);
        return new Vector3(-hx, 2f * e, -hz).normalized;
    }

    static Bounds TransformarBounds(Bounds b, Matrix4x4 m)
    {
        var c = m.MultiplyPoint3x4(b.center);
        var ex = b.extents;
        var ax = m.MultiplyVector(new Vector3(ex.x, 0, 0)); var ay = m.MultiplyVector(new Vector3(0, ex.y, 0)); var az = m.MultiplyVector(new Vector3(0, 0, ex.z));
        var e = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y), Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
        return new Bounds(c, e * 2f);
    }

    void CrearColision(Prototipo p, Mesh m, Vector3 pos, Quaternion rot, float s)
    {
        var go = new GameObject(p.nombre);
        go.hideFlags = HideFlags.DontSave | HideFlags.NotEditable | HideFlags.HideInHierarchy;
        go.isStatic = true;
        go.layer = gameObject.layer;
        go.transform.SetParent(colisiones, false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = Vector3.one * s;
        Bounds b = m.bounds;
        if (p.colision == Colision.Caja)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = b.center; bc.size = b.size * 0.9f;
        }
        else
        {
            // abetos: solo el tronco; tocones: casi todo el ancho
            var cc = go.AddComponent<CapsuleCollider>();
            float r = Mathf.Min(b.extents.x, b.extents.z);
            cc.radius = p.sombras && b.size.y > b.size.x * 1.2f ? r * 0.12f : r * 0.8f;
            cc.height = b.size.y;
            cc.center = new Vector3(b.center.x, b.size.y * 0.5f, b.center.z);
        }
    }

    // ---------------------------------------------------------------- dibujo
    void AlRenderizarCamara(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        if (!generado || grupos.Count == 0) return;
        if (ocultoGlobal && cam.cameraType == CameraType.Game) return;

        // la lista visible solo se recalcula si la camara se movio o giro
        if (cam != ultimaCam || (cam.transform.position - ultimaPos).sqrMagnitude > 1f
            || Quaternion.Angle(cam.transform.rotation, ultimaRot) > 1.5f)
        {
            CalcularVisibles(cam);
            ultimaCam = cam; ultimaPos = cam.transform.position; ultimaRot = cam.transform.rotation;
        }

        for (int i = 0; i < grupos.Count; i++)
        {
            var g = grupos[i];
            for (int s = 0; s < g.rp.Length; s++)
            {
                var rp = g.rp[s]; rp.camera = cam;
                if (g.n0 > 0) Dibujar(rp, g.lod0, s, g.buf0, g.n0);
                if (g.n1 > 0 && s < g.lod1.subMeshCount) Dibujar(rp, g.lod1, s, g.buf1, g.n1);
            }
            if (g.nImp > 0) { var ri = g.rpImp; ri.camera = cam; Dibujar(ri, quadImpostor, 0, g.bufImp, g.nImp); }
        }
    }

    static void Dibujar(RenderParams rp, Mesh m, int sub, Matrix4x4[] buf, int n)
    {
        const int lote = 1023;
        for (int i = 0; i < n; i += lote)
            Graphics.RenderMeshInstanced(rp, m, sub, buf, Mathf.Min(lote, n - i), i);
    }

    void CalcularVisibles(Camera cam)
    {
        GeometryUtility.CalculateFrustumPlanes(cam, planos);
        Vector3 cp = cam.transform.position;
        foreach (var g in grupos) { g.n0 = 0; g.n1 = 0; g.nImp = 0; }
        int total = 0;

        for (int ci = 0; ci < celdas.Count; ci++)
        {
            var celda = celdas[ci];
            float dist = Mathf.Sqrt(celda.bounds.SqrDistance(cp));
            bool enVista = GeometryUtility.TestPlanesAABB(planos, celda.bounds);
            if (!enVista && dist > distanciaSombras) continue;

            foreach (var r in celda.rangos)
            {
                var g = grupos[r.grupo];
                var p = g.proto;
                float max = g.impostores != null ? Mathf.Max(p.distanciaMax, p.distanciaImpostor) : p.distanciaMax;
                if (dist > max) continue;
                if (!enVista && !p.sombras) continue;

                if (g.impostores != null && dist > p.distanciaMax)
                {   // lejos: un quad por arbol
                    if (!enVista) continue;
                    System.Array.Copy(g.impostores, r.inicio, g.bufImp, g.nImp, r.cuenta);
                    g.nImp += r.cuenta; total += r.cuenta;
                }
                else if (dist <= p.distanciaLod)
                {
                    System.Array.Copy(g.instancias, r.inicio, g.buf0, g.n0, r.cuenta);
                    g.n0 += r.cuenta; total += r.cuenta;
                }
                else
                {
                    // entre el LOD y la distancia maxima: se adelgaza y desaparece suavemente
                    float t = Mathf.InverseLerp(p.distanciaLod, p.distanciaMax, dist);
                    float frac = Mathf.Lerp(1f, p.densidadLejos, Mathf.Clamp01(t * 3f));
                    if (g.impostores == null) frac *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, t));
                    int k = Mathf.CeilToInt(r.cuenta * frac);
                    if (k <= 0) continue;
                    System.Array.Copy(g.instancias, r.inicio, g.buf1, g.n1, k);
                    g.n1 += k; total += k;
                }
            }
        }
        VisiblesUltimoFrame = total;
    }

    void OnDrawGizmosSelected()
    {
        if (!debugCeldas) return;
        Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.3f);
        foreach (var c in celdas) Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
    }
}
