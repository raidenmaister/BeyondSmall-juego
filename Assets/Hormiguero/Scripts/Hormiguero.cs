using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// Gestor del hormiguero (lo arma HormigueroBuilder):
///  - detecta si el jugador esta bajo tierra y oscurece el ambiente (DayNightCycle.factorSubterraneo);
///  - mueve un pequeño grupo de luces reales a los hongos mas cercanos al jugador (las paredes
///    ya traen la luz de los hongos horneada, estas luces solo iluminan a los personajes);
///  - dibuja hongos, hojas y granos de maiz con GPU instancing;
///  - al morir la reina desata la furia: todas las hormigas atacan y llegan hordas.
/// </summary>
public class Hormiguero : MonoBehaviour
{
    public static Hormiguero Instancia { get; private set; }

    [System.Serializable]
    public class GrupoInstancias
    {
        public string nombre;
        public Mesh malla;
        public Material material;
        public Matrix4x4[] matrices;
        public bool sombras;
    }

    [Header("Zona")]
    public Vector3 entrada;
    public Bounds zonaSubterranea;
    [Tooltip("Profundidad bajo el suelo a partir de la cual se considera que estas dentro")]
    public float profundidadInterior = 5f;

    [Header("Luces de hongos")]
    public Vector3[] lucesPos;
    public Color[] lucesColor;
    public int lucesReales = 10;
    public float rangoLuz = 24f;
    public float intensidadLuz = 45f;

    [Header("Instancias estaticas (hongos, hojas, maiz)")]
    public GrupoInstancias[] grupos;

    [Header("Furia de la colonia")]
    public HormigaNido plantillaHorda;
    public Vector3[] puntosAparicion;
    public int maxHordaViva = 160;
    public float intervaloOleada = 4f;
    public int hormigasPorOleada = 12;
    public float distanciaMaxFuria = 260f;

    [Header("Audio")]
    public AudioSource ambienteCueva;
    public AudioClip alarmaHorda;

    public bool Furia { get; private set; }
    public bool JugadorDentro { get; private set; }
    public readonly List<HormigaNido> hormigas = new List<HormigaNido>();

    Transform jugador;
    Light[] pool;
    Renderer[] exteriores;
    bool exteriorOculto;
    float timerLuces, timerOleada, factor;
    readonly List<int> cercanas = new List<int>();
    RenderParams[] rp;

    void Awake() { Instancia = this; }

    void OnEnable()
    {
        Instancia = this;
        RenderPipelineManager.beginCameraRendering += Dibujar;
        PrepararDibujo();
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= Dibujar;
        if (Application.isPlaying) { DayNightCycle.factorSubterraneo = 0f; if (exteriorOculto) OcultarExterior(false); }
    }

    void OnValidate() { PrepararDibujo(); }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        pool = new Light[lucesReales];
        for (int i = 0; i < pool.Length; i++)
        {
            var go = new GameObject("LuzHongo_" + i);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = rangoLuz; l.intensity = intensidadLuz; l.shadows = LightShadows.None;
            l.enabled = false;
            pool[i] = l;
        }
    }

    void PrepararDibujo()
    {
        if (grupos == null) { rp = null; return; }
        rp = new RenderParams[grupos.Length];
        for (int i = 0; i < grupos.Length; i++)
        {
            var g = grupos[i];
            if (g == null || g.material == null) continue;
            rp[i] = new RenderParams(g.material)
            {
                shadowCastingMode = g.sombras ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = zonaSubterranea.size.sqrMagnitude > 1f ? new Bounds(zonaSubterranea.center, zonaSubterranea.size + Vector3.one * 60f) : new Bounds(transform.position, Vector3.one * 2000f),
                layer = gameObject.layer,
            };
        }
    }

    void Dibujar(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        if (grupos == null || rp == null || rp.Length != grupos.Length) return;
        for (int i = 0; i < grupos.Length; i++)
        {
            var g = grupos[i];
            if (g == null || g.malla == null || g.material == null || g.matrices == null || g.matrices.Length == 0) continue;
            var p = rp[i]; p.camera = cam;
            for (int k = 0; k < g.matrices.Length; k += 1023)
                Graphics.RenderMeshInstanced(p, g.malla, 0, g.matrices, Mathf.Min(1023, g.matrices.Length - k), k);
        }
    }

    // ------------------------------------------------------------------ zona y luces
    void Update()
    {
        if (jugador == null) return;
        Vector3 p = jugador.position;
        bool dentro = zonaSubterranea.Contains(p) && p.y < ZonaJuego.Altura(p.x, p.z) - profundidadInterior;
        JugadorDentro = dentro;
        // transicion suave al bajar por el tunel de entrada
        float objetivo = dentro ? 1f : 0f;
        if (!dentro && zonaSubterranea.Contains(p))
        {
            float prof = ZonaJuego.Altura(p.x, p.z) - p.y;
            objetivo = Mathf.Clamp01(prof / profundidadInterior);
        }
        factor = Mathf.MoveTowards(factor, objetivo, Time.deltaTime * 1.5f);
        DayNightCycle.factorSubterraneo = factor;
        if (ambienteCueva != null) ambienteCueva.volume = factor * 0.55f * Ajustes.VolAmbiente;

        // ya bajo tierra: no se dibuja nada del exterior (vegetacion, bosque, suelo, cerca, montañas)
        bool ocultar = exteriorOculto ? factor > 0.9f : factor > 0.98f;
        if (ocultar != exteriorOculto) OcultarExterior(ocultar);

        timerLuces -= Time.deltaTime;
        if (timerLuces <= 0f) { timerLuces = 0.3f; AsignarLuces(p); }

        if (Furia) ActualizarFuria(p);
    }

    void AsignarLuces(Vector3 p)
    {
        if (pool == null || lucesPos == null) return;
        bool activas = factor > 0.05f;
        cercanas.Clear();
        if (activas)
        {
            // los N hongos mas cercanos (seleccion parcial, son pocos cientos)
            for (int i = 0; i < lucesPos.Length; i++)
            {
                float d = (lucesPos[i] - p).sqrMagnitude;
                if (d > 70f * 70f) continue;
                int k = cercanas.Count;
                cercanas.Add(i);
                while (k > 0 && (lucesPos[cercanas[k - 1]] - p).sqrMagnitude > d) { cercanas[k] = cercanas[k - 1]; k--; }
                cercanas[k] = i;
                if (cercanas.Count > pool.Length) cercanas.RemoveAt(cercanas.Count - 1);
            }
        }
        for (int i = 0; i < pool.Length; i++)
        {
            var l = pool[i];
            if (i < cercanas.Count)
            {
                l.enabled = true;
                l.transform.position = lucesPos[cercanas[i]];
                l.color = lucesColor[cercanas[i]];
                l.intensity = intensidadLuz * factor;
            }
            else l.enabled = false;
        }
    }

    void OcultarExterior(bool ocultar)
    {
        exteriorOculto = ocultar;
        VegetacionExterior.ocultoGlobal = ocultar;
        BosqueLejano.ocultoGlobal = ocultar;
        if (exteriores == null)
        {
            var ext = GameObject.Find("EXTERIOR");
            exteriores = ext != null ? System.Array.FindAll(ext.GetComponentsInChildren<MeshRenderer>(), r => r.GetComponentInParent<CreatureAI>() == null && r.GetComponentInParent<SpiderAI>() == null) : new Renderer[0];
        }
        foreach (var r in exteriores) if (r != null) r.enabled = !ocultar;
    }

    // ------------------------------------------------------------------ furia
    /// <summary>La reina murio: todas las hormigas atacan y empiezan a llegar hordas.</summary>
    public void DesatarFuria()
    {
        if (Furia) return;
        Furia = true;
        timerOleada = 2f;
        foreach (var h in hormigas.ToArray()) if (h != null) h.Enfurecer();
        if (alarmaHorda != null && jugador != null) AudioSource.PlayClipAtPoint(alarmaHorda, jugador.position + Vector3.up, Ajustes.VolEfectos);
    }

    void ActualizarFuria(Vector3 p)
    {
        if (plantillaHorda == null || puntosAparicion == null || puntosAparicion.Length == 0) return;
        if ((p - entrada).magnitude > distanciaMaxFuria) return;   // muy lejos: dejan de salir
        timerOleada -= Time.deltaTime;
        if (timerOleada > 0f) return;
        timerOleada = intervaloOleada;

        int vivas = 0;
        foreach (var h in hormigas) if (h != null && !h.Muerta) vivas++;
        int cuantas = Mathf.Min(hormigasPorOleada, maxHordaViva - vivas);
        if (cuantas <= 0) return;

        // aparecen en un punto ni muy cerca ni muy lejos del jugador
        Vector3 mejor = puntosAparicion[0]; float mejorScore = float.MaxValue;
        foreach (var s in puntosAparicion)
        {
            float d = Vector3.Distance(s, p);
            float score = Mathf.Abs(d - 45f) + Random.value * 15f;
            if (d > 22f && score < mejorScore) { mejorScore = score; mejor = s; }
        }
        for (int i = 0; i < cuantas; i++)
        {
            Vector3 pos = mejor + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
            if (NavMesh.SamplePosition(pos, out var hit, 8f, NavMesh.AllAreas)) pos = hit.position;
            var h = Instantiate(plantillaHorda, pos, Quaternion.Euler(0f, Random.value * 360f, 0f), transform);
            h.gameObject.SetActive(true);
            h.name = "Horda_" + Time.frameCount + "_" + i;
            h.Enfurecer();
        }
    }

    public void Registrar(HormigaNido h) { if (!hormigas.Contains(h)) hormigas.Add(h); }
    public void Quitar(HormigaNido h) { hormigas.Remove(h); }

    /// <summary>Las hormigas cercanas a un punto acuden (alarma por feromonas).</summary>
    public void Alarma(Vector3 punto, float radio)
    {
        foreach (var h in hormigas)
            if (h != null && !h.Muerta && (h.transform.position - punto).sqrMagnitude < radio * radio) h.Enfurecer();
    }
}
