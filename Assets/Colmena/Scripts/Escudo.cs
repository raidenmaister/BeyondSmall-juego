using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Escudo del jugador (item "Escudo", se crea en CREAR ARMA). Si lo tienes en el inventario va atado al
/// antebrazo izquierdo, asi puedes llevar la antorcha en esa misma mano y pegar con la derecha.
/// Mantener Bloquear (clic derecho) levanta el escudo delante del pecho:
///  - golpes de frente: se reducen un 80% y gastan stamina; sin stamina se rompe la guardia un momento;
///  - bloqueo perfecto (levantarlo justo antes del golpe): no hace daño y empuja al bicho;
///  - golpes de lado o por la espalda entran completos. Mientras bloqueas no golpeas y caminas despacio.
/// </summary>
[DefaultExecutionOrder(-50)]      // su pose de brazo va antes que la de la antorcha (que se coloca al final)
public class Escudo : MonoBehaviour
{
    public const string ItemId = "Escudo";
    public static Escudo Instancia { get; private set; }

    public static bool TieneEscudo => Instancia != null && Instancia.cuantos > 0;
    public static bool Bloqueando => Instancia != null && Instancia.bloqueando;
    public static float PesoBloqueo => Instancia != null ? Instancia.peso : 0f;

    [Header("Bloqueo")]
    [Range(0f, 1f)] public float reduccion = 0.8f;
    public float anguloFrente = 65f;
    public float ventanaPerfecto = 0.25f;
    public float staminaBase = 8f, staminaPorDanio = 0.6f;
    public float guardiaRota = 1.2f;
    [Range(0.1f, 1f)] public float lentitud = 0.5f;
    [Header("Pose (mano respecto al hombro: adelante, arriba, afuera)")]
    public Vector3 pose = new Vector3(0.9f, 0.05f, -0.25f);
    public float diametro = 0.7f;
    /// <summary>Solo para pruebas desde el editor: mantiene la guardia sin pulsar la tecla.</summary>
    [System.NonSerialized] public bool probarGuardia;

    Transform brazo, antebrazo, mano;
    PlayerStamina stamina;
    PlayerSFX sfx;
    AudioSource audioEscudo;
    GameObject modelo;
    int cuantos;
    bool bloqueando;
    float peso, inicioBloqueo, rotaHasta;

    void Awake() { Instancia = this; }
    void OnDestroy() { if (Instancia == this) Instancia = null; RenderPipelineManager.beginCameraRendering -= AntesDeRender; }

    void Start()
    {
        foreach (var t in GetComponentsInChildren<Transform>())
        {
            if (t.name == "mixamorig:LeftArm") brazo = t;
            else if (t.name == "mixamorig:LeftForeArm") antebrazo = t;
            else if (t.name == "mixamorig:LeftHand") mano = t;
        }
        stamina = GetComponent<PlayerStamina>();
        sfx = GetComponent<PlayerSFX>();
        audioEscudo = gameObject.AddComponent<AudioSource>(); audioEscudo.playOnAwake = false; audioEscudo.spatialBlend = 0.3f;
        modelo = CrearModelo(diametro);
        modelo.SetActive(false);
        RenderPipelineManager.beginCameraRendering += AntesDeRender;
    }

    void Update()
    {
        cuantos = ControladorInventario.Instance != null ? ControladorInventario.Instance.GetTotalCount(ItemId) : 0;
        bool quiere = cuantos > 0 && (Controles.Pulsada(Controles.Accion.Bloquear) || probarGuardia) && !GestorPartida.Muerto && Time.time >= rotaHasta;
        if (quiere && !bloqueando) inicioBloqueo = Time.time;
        bloqueando = quiere;
        if (bloqueando) StatusEffects.Slow(lentitud, 0.15f);
        if (modelo != null && modelo.activeSelf != (cuantos > 0)) modelo.SetActive(cuantos > 0);
    }

    void LateUpdate()
    {
        peso = Mathf.MoveTowards(peso, bloqueando ? 1f : 0f, Time.deltaTime * 9f);
        if (peso <= 0.001f || brazo == null || antebrazo == null || mano == null) return;
        // escudo delante del pecho, el codo hacia afuera
        float a = (antebrazo.position - brazo.position).magnitude, b = (mano.position - antebrazo.position).magnitude;
        Vector3 fwd = transform.forward, up = Vector3.up, izq = -transform.right;
        Vector3 objetivo = brazo.position + (fwd * pose.x + up * pose.y + izq * pose.z).normalized * (a + b) * 0.8f;
        Vector3 polo = izq * 0.8f - up * 0.4f;
        Antorcha.IKBrazo(brazo, antebrazo, mano, objetivo, polo, Mathf.SmoothStep(0f, 1f, peso));
    }

    // el escudo se coloca con la pose final del brazo (despues de la antorcha y de la animacion)
    void AntesDeRender(ScriptableRenderContext ctx, Camera cam)
    {
        if (modelo == null || !modelo.activeSelf || antebrazo == null || mano == null) return;
        Vector3 E = antebrazo.position, H = mano.position;
        Vector3 dir = (H - E).normalized;
        if (dir.sqrMagnitude < 0.5f) return;
        // hacia afuera del antebrazo; al bloquear mira de frente
        Vector3 afuera = Vector3.Lerp(transform.forward * 0.15f - transform.right, transform.forward, Mathf.SmoothStep(0f, 1f, peso));
        Vector3 n = Vector3.ProjectOnPlane(afuera, dir).normalized;
        if (n.sqrMagnitude < 0.5f) n = -transform.right;
        modelo.transform.SetPositionAndRotation(Vector3.Lerp(E, H, 0.45f) + n * 0.07f, Quaternion.LookRotation(n, dir));
    }

    /// <summary>Lo llama PlayerHealth con cada golpe de un animal. Devuelve el daño que entra.</summary>
    public float Filtrar(float danio)
    {
        if (!bloqueando || peso < 0.5f) return danio;
        Vector3 origen; Transform atacante;
        if (!Atacante(out origen, out atacante)) return danio * (1f - reduccion);     // sin saber de donde: se bloquea
        Vector3 hacia = origen - transform.position; hacia.y = 0f;
        if (hacia.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, hacia) > anguloFrente) return danio;   // de lado o por detras

        // bloqueo perfecto
        if (Time.time - inicioBloqueo <= ventanaPerfecto)
        {
            Sonar(1.35f, 1f);
            Empujar(atacante);
            SurvivalUI.Aviso("¡Bloqueo perfecto!", new Color(0.6f, 0.95f, 1f));
            Estadisticas.Actual.bloqueosPerfectos++;
            return 0f;
        }
        float costo = staminaBase + danio * staminaPorDanio;
        if (stamina == null || stamina.TryConsumeStamina(costo))
        {
            Sonar(0.7f, 0.8f);
            return danio * (1f - reduccion);
        }
        // sin stamina: la guardia se rompe
        rotaHasta = Time.time + guardiaRota;
        bloqueando = false;
        Sonar(0.5f, 1f);
        SurvivalUI.Aviso("¡Te rompieron la guardia!", new Color(1f, 0.55f, 0.4f));
        return danio * 0.6f;
    }

    bool Atacante(out Vector3 origen, out Transform quien)
    {
        origen = Vector3.zero; quien = null;
        float mejor = float.MaxValue;
        Vector3 p = transform.position;
        foreach (var pr in FindObjectsByType<EnemyProjectile>(FindObjectsInactive.Exclude))
        {
            float d = (pr.transform.position - p).sqrMagnitude;
            if (d < 16f && d < mejor) { mejor = d; origen = pr.transform.position - pr.velocity.normalized * 2f; quien = null; }
        }
        if (quien == null && mejor < float.MaxValue) return true;
        foreach (var rh in FindObjectsByType<RatHealth>(FindObjectsInactive.Exclude))
        {
            if (rh.IsDead) continue;
            float d = (rh.transform.position - p).sqrMagnitude;
            if (d < 12f * 12f && d < mejor) { mejor = d; origen = rh.transform.position; quien = rh.transform; }
        }
        return mejor < float.MaxValue;
    }

    void Empujar(Transform t)
    {
        if (t == null) return;
        Vector3 dir = t.position - transform.position; dir.y = 0f; dir.Normalize();
        var c = t.GetComponent<CreatureAI>(); if (c != null) { c.TakeHit(dir); return; }
        var s = t.GetComponent<SpiderAI>(); if (s != null) { s.TakeHit(dir); return; }
        var r = t.GetComponent<RatAI>(); if (r != null) r.TakeHit(dir);
    }

    void Sonar(float tono, float vol)
    {
        if (sfx == null || sfx.golpeImpacto == null || sfx.golpeImpacto.Length == 0) return;
        audioEscudo.pitch = tono;
        audioEscudo.PlayOneShot(sfx.golpeImpacto[Random.Range(0, sfx.golpeImpacto.Length)], vol * Ajustes.VolEfectos);
    }

    // ------------------------------------------------------------------ modelo: caparazon atado con ligas
    static GameObject CrearModelo(float diametro)
    {
        var raiz = new GameObject("EscudoAntebrazo");
        float r = diametro * 0.5f;
        var caparazon = Mat(new Color(0.3f, 0.21f, 0.12f), 0.8f);
        var borde = Mat(new Color(0.55f, 0.4f, 0.22f), 0.5f);
        var liga = Mat(new Color(0.05f, 0.05f, 0.05f), 0.4f);
        // cupula: perfil (radio, altura) girado alrededor de Z (la normal del escudo es +Z)
        var perfil = new List<Vector2>();
        for (int i = 0; i <= 10; i++) { float u = i / 10f; perfil.Add(new Vector2(r * (1f - u), Mathf.Sin(u * Mathf.PI * 0.5f) * r * 0.42f)); }
        Pieza(raiz.transform, Torno(perfil, 28), caparazon);
        // borde y surco central (como los elitros de un escarabajo)
        var aro = new List<Vector2>();
        for (int i = 0; i <= 8; i++) { float a = i * Mathf.PI * 2f / 8f; aro.Add(new Vector2(r + Mathf.Cos(a) * 0.035f, Mathf.Sin(a) * 0.035f)); }
        Pieza(raiz.transform, Torno(aro, 28), borde);
        var surco = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(surco.GetComponent<Collider>());
        surco.transform.SetParent(raiz.transform, false); surco.transform.localScale = new Vector3(0.025f, diametro * 0.92f, 0.03f); surco.transform.localPosition = new Vector3(0f, 0f, r * 0.38f);
        surco.GetComponent<Renderer>().sharedMaterial = borde;
        // dos ligas que lo atan al antebrazo (por detras)
        foreach (float y in new[] { -0.12f, 0.12f })
        {
            var l = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(l.GetComponent<Collider>());
            l.transform.SetParent(raiz.transform, false); l.transform.localScale = new Vector3(diametro * 0.55f, 0.05f, 0.05f); l.transform.localPosition = new Vector3(0f, y * diametro, -0.02f);
            l.GetComponent<Renderer>().sharedMaterial = liga;
        }
        foreach (var rr in raiz.GetComponentsInChildren<Renderer>()) rr.shadowCastingMode = ShadowCastingMode.On;
        return raiz;
    }

    static Material Mat(Color c, float suave)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", suave); m.SetFloat("_Metallic", 0.1f);
        return m;
    }

    static void Pieza(Transform padre, Mesh malla, Material m)
    {
        var g = new GameObject("Pieza"); g.transform.SetParent(padre, false);
        g.AddComponent<MeshFilter>().sharedMesh = malla; g.AddComponent<MeshRenderer>().sharedMaterial = m;
    }

    /// <summary>Gira un perfil (radio, z) alrededor del eje Z; la normal sale a la derecha del recorrido.</summary>
    static Mesh Torno(List<Vector2> perfil, int lados)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < perfil.Count; i++)
            for (int k = 0; k <= lados; k++)
            {
                float a = k * Mathf.PI * 2f / lados;
                v.Add(new Vector3(Mathf.Cos(a) * perfil[i].x, Mathf.Sin(a) * perfil[i].x, perfil[i].y));
            }
        int c = lados + 1;
        for (int i = 0; i < perfil.Count - 1; i++)
            for (int k = 0; k < lados; k++)
            {
                int p0 = i * c + k, p1 = p0 + 1, p2 = p0 + c, p3 = p2 + 1;
                t.AddRange(new[] { p0, p2, p1, p1, p2, p3 });
            }
        var m = new Mesh(); m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
        // si las normales quedaron hacia adentro, voltear
        var nn = m.normals;
        if (nn.Length > c && Vector3.Dot(nn[c], v[c]) < 0f) { for (int i = 0; i < t.Count; i += 3) { int x = t[i]; t[i] = t[i + 1]; t[i + 1] = x; } m.SetTriangles(t, 0); m.RecalculateNormals(); }
        return m;
    }
}
