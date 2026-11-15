using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// Gestor de la cloaca (lo arma CloacaBuilder):
///  - detecta si el jugador esta abajo y oscurece el ambiente (DayNightCycle.factorCloaca); oculta el exterior;
///  - mueve unas pocas luces reales a las manchas de moho luminoso mas cercanas (las paredes traen la luz horneada);
///  - los haces de luz de las rejillas siguen al sol;
///  - el agua sucia frena, te arrastra con la corriente y, si te quedas mucho, enferma;
///  - saca ratas forrajeras por el derrumbe (mas de noche) y lleva la cuenta de los robos de comida;
///  - al morir el Rey Rata las ratas entran en panico.
/// </summary>
public class Cloaca : MonoBehaviour
{
    public static Cloaca Instancia { get; private set; }

    [System.Serializable]
    public class Agua
    {
        public Vector3 a, b;          // eje del canal (a la altura del agua); si circulo, a es el centro
        public float medioAncho;
        public bool circulo;
        public float radio;
        public float corriente;       // unidades/s en la direccion a -> b
    }

    [Header("Zona")]
    public Vector3 tapa;              // centro de la rejilla, en la superficie
    public Vector3 derrumbe;          // arriba del derrumbe (entrada caminando)
    public Bounds zona;
    public float profundidadInterior = 4f;

    [Header("Luces")]
    public Vector3[] lucesPos;
    public Color[] lucesColor;
    public int lucesReales = 10;
    public float rangoLuz = 20f;
    public float intensidadLuz = 13f;
    public Light[] focosSol;
    public Renderer[] haces;

    [Header("Instancias estaticas")]
    public Hormiguero.GrupoInstancias[] grupos;

    [Header("Agua")]
    public Agua[] aguas;
    [Range(0.1f, 1f)] public float lentitudAgua = 0.6f;
    public float segundosHastaEnfermar = 7f;
    public AudioClip chapoteo;

    [Header("Ratas")]
    public RataCloaca plantillaForrajera;
    public Vector3 madriguera;
    public Vector3[] puntosMadriguera;
    public float radioJardin = 95f;
    public int forrajerasDia = 2, forrajerasNoche = 6;
    public float intervaloSalida = 16f;
    public ReyRata rey;

    [Header("Audio")]
    public AudioSource ambiente;

    public bool JugadorDentro { get; private set; }
    public bool JugadorEnAgua { get; private set; }
    public bool Panico { get; private set; }
    public readonly List<RataCloaca> ratas = new List<RataCloaca>();

    /// <summary>¿El punto esta en el volumen de la cloaca? (el hormiguero pasa por debajo)</summary>
    public bool Contiene(Vector3 p) { return zona.Contains(p); }

    Transform jugador;
    CharacterController cc;
    Light[] pool;
    float[] focosI0;
    Renderer[] exteriores;
    bool exteriorOculto, avisoAgua, avisoEntrada;
    float timerLuces, timerSalida = 8f, factor, tAgua, proximoRobo;
    readonly List<int> cercanas = new List<int>();
    readonly List<RataCloaca> sacadas = new List<RataCloaca>();
    RenderParams[] rp;
    MaterialPropertyBlock mpb;
    static readonly int IdIntensidad = Shader.PropertyToID("_Intensidad");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { Instancia = null; DayNightCycle.factorCloaca = 0f; }

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
        if (Application.isPlaying) { DayNightCycle.factorCloaca = 0f; if (exteriorOculto) OcultarExterior(false); }
    }

    void OnValidate() { PrepararDibujo(); }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) { jugador = pc.transform; cc = pc.GetComponent<CharacterController>(); }
        pool = new Light[lucesReales];
        for (int i = 0; i < pool.Length; i++)
        {
            var go = new GameObject("LuzMoho_" + i);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = rangoLuz; l.intensity = intensidadLuz; l.shadows = LightShadows.None;
            l.enabled = false;
            pool[i] = l;
        }
        if (focosSol != null)
        {
            focosI0 = new float[focosSol.Length];
            for (int i = 0; i < focosSol.Length; i++) if (focosSol[i] != null) focosI0[i] = focosSol[i].intensity;
        }
        mpb = new MaterialPropertyBlock();
        // si al cargar la partida el rey ya estaba muerto, las ratas siguen asustadas (sin aviso)
        Invoke(nameof(RevisarRey), 1f);
    }

    void RevisarRey()
    {
        if (rey == null || !rey.gameObject.activeInHierarchy || rey.Muerto) DesatarPanico(false);
    }

    // ------------------------------------------------------------------ dibujo instanciado
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
                worldBounds = zona.size.sqrMagnitude > 1f ? new Bounds(zona.center, zona.size + Vector3.one * 60f) : new Bounds(transform.position, Vector3.one * 2000f),
                layer = gameObject.layer,
            };
        }
    }

    void Dibujar(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        if (grupos == null || rp == null || rp.Length != grupos.Length) return;
        // desde muy lejos y en la superficie no hace falta dibujar la basura de abajo
        if (cam.cameraType == CameraType.Game && (cam.transform.position - zona.ClosestPoint(cam.transform.position)).sqrMagnitude > 150f * 150f) return;
        for (int i = 0; i < grupos.Length; i++)
        {
            var g = grupos[i];
            if (g == null || g.malla == null || g.material == null || g.matrices == null || g.matrices.Length == 0) continue;
            var p = rp[i]; p.camera = cam;
            for (int k = 0; k < g.matrices.Length; k += 1023)
                Graphics.RenderMeshInstanced(p, g.malla, 0, g.matrices, Mathf.Min(1023, g.matrices.Length - k), k);
        }
    }

    // ------------------------------------------------------------------ bucle
    void Update()
    {
        if (jugador == null) return;
        float dt = Time.deltaTime;
        Vector3 p = jugador.position;
        float suelo = ZonaJuego.Altura(p.x, p.z);
        bool enZona = zona.Contains(p);
        bool dentro = enZona && p.y < suelo - profundidadInterior;
        JugadorDentro = dentro;
        float objetivo = dentro ? 1f : 0f;
        if (!dentro && enZona) objetivo = Mathf.Clamp01((suelo - p.y) / profundidadInterior);
        factor = Mathf.MoveTowards(factor, objetivo, dt * 1.5f);
        DayNightCycle.factorCloaca = factor;
        if (ambiente != null) ambiente.volume = factor * 0.6f * Ajustes.VolAmbiente;

        bool ocultar = exteriorOculto ? factor > 0.9f : factor > 0.98f;
        if (ocultar != exteriorOculto) OcultarExterior(ocultar);

        if (dentro && !avisoEntrada)
        {
            avisoEntrada = true;
            Aviso.Mostrar(Panico ? "La cloaca: las ratas ya no son tan valientes" : "La cloaca de las ratas", new Color(0.65f, 0.85f, 0.45f), 3f);
        }

        timerLuces -= dt;
        if (timerLuces <= 0f) { timerLuces = 0.3f; AsignarLuces(p); ActualizarHaces(); }

        Agua_(p, dt);
        Salidas(p, dt);
    }

    void AsignarLuces(Vector3 p)
    {
        if (pool == null || lucesPos == null) return;
        bool activas = factor > 0.05f || zona.Contains(p);
        cercanas.Clear();
        if (activas)
        {
            for (int i = 0; i < lucesPos.Length; i++)
            {
                float d = (lucesPos[i] - p).sqrMagnitude;
                if (d > 60f * 60f) continue;
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
                l.color = Color.Lerp(lucesColor[cercanas[i]], Color.white, 0.45f);   // a los personajes no los tiñe tanto
                l.intensity = intensidadLuz * Mathf.Max(0.35f, factor);
            }
            else l.enabled = false;
        }
    }

    void ActualizarHaces()
    {
        var dn = DayNightCycle.Instance;
        float sol = dn != null ? Mathf.Lerp(1f, 0.12f, dn.Noche) : 1f;
        if (focosSol != null && focosI0 != null)
            for (int i = 0; i < focosSol.Length; i++) if (focosSol[i] != null) { focosSol[i].intensity = focosI0[i] * sol; focosSol[i].color = dn != null && dn.Noche > 0.5f ? new Color(0.6f, 0.7f, 1f) : new Color(1f, 0.93f, 0.78f); }
        if (haces != null && mpb != null)
            foreach (var r in haces)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetFloat(IdIntensidad, 0.32f * sol);
                r.SetPropertyBlock(mpb);
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

    // ------------------------------------------------------------------ agua
    /// <summary>¿El punto esta sobre agua? Devuelve el nivel y la corriente.</summary>
    public bool SobreAgua(Vector3 p, out float nivel, out Vector3 flujo)
    {
        nivel = 0f; flujo = Vector3.zero;
        if (aguas == null) return false;
        bool hay = false;
        foreach (var w in aguas)
        {
            if (w.circulo)
            {
                float dx = p.x - w.a.x, dz = p.z - w.a.z;
                if (dx * dx + dz * dz > w.radio * w.radio) continue;
                if (hay && w.a.y < nivel) continue;
                nivel = w.a.y; flujo = Vector3.zero; hay = true;
            }
            else
            {
                Vector3 ab = w.b - w.a; ab.y = 0f;
                float largo = ab.magnitude; if (largo < 0.01f) continue;
                Vector3 dir = ab / largo;
                Vector3 ap = p - w.a; ap.y = 0f;
                float t = Vector3.Dot(ap, dir);
                if (t < 0f || t > largo) continue;
                float lado = Vector3.Cross(dir, ap).y;
                if (Mathf.Abs(lado) > w.medioAncho) continue;
                float y = Mathf.Lerp(w.a.y, w.b.y, t / largo);
                if (hay && y < nivel) continue;
                nivel = y; flujo = dir * w.corriente; hay = true;
            }
        }
        return hay;
    }

    void Agua_(Vector3 p, float dt)
    {
        bool antes = JugadorEnAgua;
        float pies = p.y - (cc != null ? cc.height * 0.5f - cc.center.y : 1f);
        JugadorEnAgua = SobreAgua(p, out float nivel, out Vector3 flujo) && pies < nivel - 0.15f;
        if (!JugadorEnAgua) { tAgua = Mathf.Max(0f, tAgua - dt * 2f); return; }
        if (!antes && chapoteo != null) AudioSource.PlayClipAtPoint(chapoteo, p, 0.8f * Ajustes.VolEfectos);
        StatusEffects.Slow(lentitudAgua, 0.3f, "~En el agua sucia");
        if (cc != null && cc.enabled && flujo.sqrMagnitude > 0.001f) cc.Move(flujo * dt);
        tAgua += dt;
        if (tAgua > segundosHastaEnfermar)
        {
            StatusEffects.Poison(1.5f, 1.5f);
            if (!avisoAgua) { avisoAgua = true; Aviso.Mostrar("El agua sucia te esta enfermando: ¡sal de ahi!", new Color(0.7f, 0.9f, 0.3f), 3f); }
        }
    }

    // ------------------------------------------------------------------ ratas
    public void Registrar(RataCloaca r) { if (r != null && !ratas.Contains(r)) ratas.Add(r); }
    public void Quitar(RataCloaca r) { ratas.Remove(r); sacadas.Remove(r); }

    /// <summary>Las ratas cercanas a un punto acuden.</summary>
    public void Alarma(Vector3 punto, float radio)
    {
        if (Panico) return;
        foreach (var r in ratas.ToArray())
            if (r != null && !r.Muerta && (r.transform.position - punto).sqrMagnitude < radio * radio) r.Enfurecer();
    }

    public float InicioPanico { get; private set; }

    public void DesatarPanico(bool aviso)
    {
        if (Panico) return;
        InicioPanico = Time.time;
        Panico = true;      // las hostiles se calman solas (RataCloaca) y ya no salen tantas al jardin
    }

    public Vector3 PuntoMadriguera()
    {
        if (puntosMadriguera != null && puntosMadriguera.Length > 0) return puntosMadriguera[Random.Range(0, puntosMadriguera.Length)];
        return madriguera;
    }

    /// <summary>Un punto del jardin cerca del derrumbe, sobre el NavMesh.</summary>
    public Vector3 PuntoJardin()
    {
        for (int i = 0; i < 10; i++)
        {
            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(30f, radioJardin);
            Vector3 q = derrumbe + new Vector3(c.x, 0f, c.y);
            if (ZonaJuego.EnEdificio(q, 6f) || ZonaJuego.DistanciaCloaca(q.x, q.z) < 4f) continue;
            q.y = ZonaJuego.Altura(q.x, q.z);
            if (NavMesh.SamplePosition(q, out var hit, 6f, NavMesh.AllAreas)) return hit.position;
        }
        return derrumbe;
    }

    /// <summary>¿Puede una ladrona robarle comida al jugador ahora? Devuelve que comida.</summary>
    public bool PuedeRobar(out string comida)
    {
        comida = null;
        if (Panico || !Partida.Agresivos || Time.time < proximoRobo) return false;
        var inv = ControladorInventario.Instance;
        if (inv == null) return false;
        if (inv.GetTotalCount("Carne_Cocinada") > 0) comida = "Carne_Cocinada";
        else if (inv.GetTotalCount("Carne") > 0) comida = "Carne";
        return comida != null;
    }

    public void RegistrarRobo() { proximoRobo = Time.time + 40f; }

    void Salidas(Vector3 p, float dt)
    {
        if (plantillaForrajera == null || puntosMadriguera == null || puntosMadriguera.Length == 0) return;
        timerSalida -= dt;
        if (timerSalida > 0f) return;
        timerSalida = intervaloSalida * Random.Range(0.7f, 1.3f);
        if ((p - tapa).sqrMagnitude > 280f * 280f) return;      // lejos: no hace falta
        sacadas.RemoveAll(r => r == null || r.Muerta);
        bool noche = DayNightCycle.Instance != null && DayNightCycle.Instance.Noche > 0.5f;
        int objetivo = Panico ? 1 : (noche ? forrajerasNoche : forrajerasDia);
        if (sacadas.Count >= objetivo) return;
        // salen de un rincon de la madriguera que no estes viendo de cerca
        Vector3 donde = PuntoMadriguera();
        if ((donde - p).sqrMagnitude < 30f * 30f) return;
        if (NavMesh.SamplePosition(donde, out var hit, 6f, NavMesh.AllAreas)) donde = hit.position;
        var r2 = Instantiate(plantillaForrajera, donde, Quaternion.Euler(0f, Random.value * 360f, 0f), plantillaForrajera.transform.parent.parent);
        r2.name = "Forrajera_" + Time.frameCount;
        r2.hogar = donde;
        r2.temporal = true;
        r2.gameObject.SetActive(true);
        sacadas.Add(r2);
    }
}
