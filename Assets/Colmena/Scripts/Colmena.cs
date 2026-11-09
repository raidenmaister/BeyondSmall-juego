using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gestor del arbol hueco y su colmena (lo arma ArbolBuilder):
///  - dentro del tronco oscurece el ambiente (DayNightCycle.factorInterior) y mueve unas pocas luces
///    reales a los puntos de luz mas cercanos (las paredes ya traen la luz horneada);
///  - dibuja panales, miel, escalones y follaje con GPU instancing;
///  - red de puntos de vuelo para que las abejas no choquen con los pisos al perseguir al jugador;
///  - furia: si matas una larva o a la reina, toda la colmena ataca (y llegan refuerzos tras la reina).
/// </summary>
public class Colmena : MonoBehaviour
{
    public static Colmena Instancia { get; private set; }

    [Header("Zona")]
    public Vector3 centro;            // base del tronco
    public float radioInterior = 29f;
    public float altoInterior = 186f;

    [Header("Luces")]
    public Vector3[] lucesPos;
    public Color[] lucesColor;
    public int lucesReales = 8;
    public float rangoLuz = 22f;
    public float intensidadLuz = 40f;

    [Header("Instancias")]
    public Hormiguero.GrupoInstancias[] grupos;

    [Header("Red de vuelo")]
    public Vector3[] nodos;
    public int[] aristasA, aristasB;

    [Header("Flores cercanas (forrajeras)")]
    public Vector3[] flores;

    [Header("Furia")]
    public AbejaColmena plantillaSoldado;
    public Vector3[] puntosRefuerzo;
    public int maxRefuerzos = 45;
    public float intervaloRefuerzo = 3.5f;

    [Header("Audio")]
    public AudioSource zumbidoInterior, zumbidoExterior, enjambre;

    public bool Furia { get; private set; }
    public bool ReinaMuerta { get; private set; }
    public bool JugadorDentro { get; private set; }
    public readonly List<AbejaColmena> abejas = new List<AbejaColmena>();

    Transform jugador;
    Light[] pool;
    float factor, timerLuces, timerRefuerzo;
    RenderParams[] rp;
    int[,] siguiente;           // siguiente[a,b] = vecino de a camino a b
    readonly List<int> cercanas = new List<int>();

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
        if (Application.isPlaying) DayNightCycle.factorInterior = 0f;
    }

    void OnValidate() { PrepararDibujo(); }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        pool = new Light[lucesReales];
        for (int i = 0; i < pool.Length; i++)
        {
            var l = new GameObject("LuzColmena_" + i).AddComponent<Light>();
            l.transform.SetParent(transform, false);
            l.type = LightType.Point; l.range = rangoLuz; l.shadows = LightShadows.None; l.enabled = false;
            pool[i] = l;
        }
        CalcularRutas();
    }

    // ------------------------------------------------------------------ dibujo de instancias
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
                receiveShadows = true,
                worldBounds = new Bounds(centro + Vector3.up * 150f, new Vector3(400f, 420f, 400f)),
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

    // ------------------------------------------------------------------ zona, luces y sonido
    public bool DentroDelTronco(Vector3 p)
    {
        Vector3 d = p - centro;
        return d.y > -2f && d.y < altoInterior && new Vector2(d.x, d.z).sqrMagnitude < (radioInterior + 4f) * (radioInterior + 4f);
    }

    void Update()
    {
        if (jugador == null) return;
        Vector3 p = jugador.position;
        JugadorDentro = DentroDelTronco(p);
        float h = p.y - centro.y;
        // dentro del tronco no es tan oscuro como el hormiguero: entra luz por la entrada y los huecos
        float objetivo = JugadorDentro ? Mathf.Lerp(0.35f, 0.8f, Mathf.InverseLerp(4f, 40f, h)) : 0f;
        factor = Mathf.MoveTowards(factor, objetivo, Time.deltaTime * 1.2f);
        DayNightCycle.factorInterior = factor;

        float dist = Vector3.Distance(p, centro + Vector3.up * Mathf.Clamp(h, 0f, altoInterior));
        if (zumbidoInterior != null) zumbidoInterior.volume = (JugadorDentro ? Mathf.InverseLerp(10f, 80f, h) * 0.6f + 0.15f : 0f) * Ajustes.VolAmbiente;
        if (zumbidoExterior != null) zumbidoExterior.volume = (JugadorDentro ? 0f : Mathf.InverseLerp(160f, 40f, dist) * 0.45f) * Ajustes.VolAmbiente;
        if (enjambre != null)
        {
            int hostiles = 0;
            foreach (var a in abejas) if (a != null && a.Hostil && !a.Muerta && (a.transform.position - p).sqrMagnitude < 40f * 40f) hostiles++;
            enjambre.volume = Mathf.MoveTowards(enjambre.volume, Mathf.Clamp01(hostiles / 8f) * 0.7f * Ajustes.VolEfectos, Time.deltaTime * 0.8f);
        }

        timerLuces -= Time.deltaTime;
        if (timerLuces <= 0f) { timerLuces = 0.3f; AsignarLuces(p); }

        if (ReinaMuerta) Refuerzos(p);
    }

    void AsignarLuces(Vector3 p)
    {
        if (pool == null || lucesPos == null) return;
        cercanas.Clear();
        if (JugadorDentro)
            for (int i = 0; i < lucesPos.Length; i++)
            {
                float d = (lucesPos[i] - p).sqrMagnitude;
                if (d > 60f * 60f) continue;
                int k = cercanas.Count; cercanas.Add(i);
                while (k > 0 && (lucesPos[cercanas[k - 1]] - p).sqrMagnitude > d) { cercanas[k] = cercanas[k - 1]; k--; }
                cercanas[k] = i;
                if (cercanas.Count > pool.Length) cercanas.RemoveAt(cercanas.Count - 1);
            }
        for (int i = 0; i < pool.Length; i++)
        {
            var l = pool[i];
            l.enabled = i < cercanas.Count;
            if (!l.enabled) continue;
            l.transform.position = lucesPos[cercanas[i]];
            l.color = lucesColor[cercanas[i]];
            l.intensity = intensidadLuz;
        }
    }

    // ------------------------------------------------------------------ red de vuelo
    void CalcularRutas()
    {
        if (nodos == null || nodos.Length == 0 || aristasA == null) return;
        int n = nodos.Length;
        var vec = new List<int>[n];
        for (int i = 0; i < n; i++) vec[i] = new List<int>();
        for (int i = 0; i < aristasA.Length; i++) { vec[aristasA[i]].Add(aristasB[i]); vec[aristasB[i]].Add(aristasA[i]); }
        siguiente = new int[n, n];
        var cola = new Queue<int>(); var prev = new int[n];
        for (int b = 0; b < n; b++)
        {
            // BFS desde el destino: el "padre" de cada nodo es el siguiente paso hacia b
            for (int i = 0; i < n; i++) prev[i] = -1;
            prev[b] = b; cola.Enqueue(b);
            while (cola.Count > 0)
            {
                int x = cola.Dequeue();
                foreach (int y in vec[x]) if (prev[y] < 0) { prev[y] = x; cola.Enqueue(y); }
            }
            for (int a = 0; a < n; a++) siguiente[a, b] = prev[a];
        }
    }

    public int NodoCercano(Vector3 p)
    {
        int mejor = -1; float md = float.MaxValue;
        if (nodos == null) return -1;
        for (int i = 0; i < nodos.Length; i++)
        {
            float d = (nodos[i] - p).sqrMagnitude;
            if (d < md) { md = d; mejor = i; }
        }
        return mejor;
    }

    /// <summary>Punto intermedio para volar de 'desde' a 'hacia' rodeando pisos y paredes.</summary>
    public Vector3 Paso(Vector3 desde, Vector3 hacia)
    {
        if (siguiente == null) return hacia;
        int a = NodoCercano(desde), b = NodoCercano(hacia);
        if (a < 0 || b < 0 || a == b) return hacia;
        int s = siguiente[a, b];
        if (s < 0) return hacia;
        // si ya estamos encima del nodo cercano, apuntar al siguiente
        return (nodos[a] - desde).sqrMagnitude > 9f ? nodos[a] : nodos[s];
    }

    // ------------------------------------------------------------------ furia
    public void Registrar(AbejaColmena a) { if (!abejas.Contains(a)) abejas.Add(a); }
    public void Quitar(AbejaColmena a) { abejas.Remove(a); }

    /// <summary>Las abejas cercanas se alarman (feromona de alarma al dañar a una).</summary>
    public void Alarma(Vector3 punto, float radio)
    {
        foreach (var a in abejas.ToArray())
            if (a != null && !a.Muerta && (a.transform.position - punto).sqrMagnitude < radio * radio) a.Enfurecer();
    }

    public void DesatarFuria(string mensaje)
    {
        if (!Furia && !string.IsNullOrEmpty(mensaje)) Aviso.Mostrar(mensaje, new Color(1f, 0.75f, 0.15f));
        Furia = true;
        foreach (var a in abejas.ToArray()) if (a != null) a.Enfurecer();
    }

    public void LarvaMuerta()
    {
        DesatarFuria("¡Mataste una larva! La colmena entera va por ti");
    }

    public void ReinaDerrotada()
    {
        ReinaMuerta = true;
        Furia = false;   // vuelve a mostrar el aviso
        DesatarFuria("¡La reina ha caido! Todas las abejas te persiguen");
        timerRefuerzo = 2f;
    }

    /// <summary>Al cargar una partida: furia y reina muerta, sin avisos en pantalla.</summary>
    public void Restaurar(bool furia, bool reinaMuerta)
    {
        if (reinaMuerta) { ReinaMuerta = true; timerRefuerzo = 6f; }
        if (furia && !Furia) { Furia = true; foreach (var a in abejas.ToArray()) if (a != null) a.Enfurecer(); }
    }

    void Refuerzos(Vector3 p)
    {
        if (plantillaSoldado == null || puntosRefuerzo == null || puntosRefuerzo.Length == 0) return;
        if ((p - centro).magnitude > 320f) return;
        timerRefuerzo -= Time.deltaTime;
        if (timerRefuerzo > 0f) return;
        timerRefuerzo = intervaloRefuerzo;
        int vivas = 0;
        foreach (var a in abejas) if (a != null && !a.Muerta) vivas++;
        if (vivas >= maxRefuerzos) return;
        // salen del punto de la colmena mas cercano al jugador
        Vector3 mejor = puntosRefuerzo[0]; float md = float.MaxValue;
        foreach (var s in puntosRefuerzo) { float d = (s - p).sqrMagnitude; if (d < md && d > 15f * 15f) { md = d; mejor = s; } }
        for (int i = 0; i < 3; i++)
        {
            var a = Instantiate(plantillaSoldado, mejor + Random.insideUnitSphere * 2f, Quaternion.identity, transform);
            a.gameObject.SetActive(true);
            a.Enfurecer();
        }
    }
}
