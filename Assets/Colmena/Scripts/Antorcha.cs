using UnityEngine;

/// <summary>
/// Antorcha del jugador. Si tienes una en el inventario, se enciende y se apaga con su tecla (T por defecto)
/// o eligiendola en la barra rapida. Encendida, el humo ahuyenta a las abejas: no se acercan a menos
/// de Radio, ni siquiera furiosas. Va en la mano izquierda: al encenderla el personaje levanta
/// el brazo (IK de dos huesos encima de la animacion) para alumbrar el camino.
/// </summary>
public class Antorcha : MonoBehaviour
{
    public static bool Encendida { get; private set; }
    static Antorcha instancia;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar()
    {
        Encendida = false; instancia = null; Posicion = Vector3.zero;
        // tambien estado de otras clases que el hormiguero o la colmena dejan puesto
        DayNightCycle.factorSubterraneo = 0f; DayNightCycle.factorInterior = 0f;
        VegetacionExterior.ocultoGlobal = false; BosqueLejano.ocultoGlobal = false;
    }
    /// <summary>Apaga la antorcha del jugador (cinematicas).</summary>
    public static void Apagar() { if (instancia != null && Encendida) instancia.Poner(false); }
    public static Vector3 Posicion { get; private set; }
    public const float Radio = 12f;
    public const string ItemId = "Antorcha";

    public GameObject modeloMano;     // instancia de la antorcha encendida (se pega a la mano)
    public string huesoMano = "mixamorig:LeftHand";
    public string huesoBrazo = "mixamorig:LeftArm", huesoAntebrazo = "mixamorig:LeftForeArm";
    public float escala = 0.8f;
    public float inclinacion = 12f;
    [Tooltip("Hacia donde levanta la mano (adelante, arriba, afuera), relativo al personaje")]
    public Vector3 alzar = new Vector3(0.5f, 0.75f, 0.32f);
    [Range(0.5f, 1f)] public float extension = 0.88f;
    public float rangoLuz = 22f, intensidadLuz = 38f;
    public AudioClip sonidoEncender, sonidoFuego;

    Transform mano, brazo, antebrazo;
    float peso;          // 0 = brazo con la animacion normal, 1 = levantado
    AudioSource fuego, fx;
    int teniaAntes;
    bool avisado;

    void Start()
    {
        instancia = this;
        foreach (var t in GetComponentsInChildren<Transform>())
        {
            if (t.name == huesoMano) mano = t;
            else if (t.name == huesoBrazo) brazo = t;
            else if (t.name == huesoAntebrazo) antebrazo = t;
        }
        if (modeloMano != null)
        {
            // va suelta (no hija del hueso): en LateUpdate se coloca en la mano, siempre derecha
            modeloMano.transform.SetParent(transform, true);
            // luz mas amplia para alumbrar el camino (antes de que ParpadeoLuz tome su valor base)
            foreach (var l in modeloMano.GetComponentsInChildren<Light>(true)) { l.range = rangoLuz; l.intensity = intensidadLuz; }
            modeloMano.SetActive(false);
        }
        fuego = gameObject.AddComponent<AudioSource>();
        fuego.clip = sonidoFuego; fuego.loop = true; fuego.spatialBlend = 0.6f; fuego.playOnAwake = false;
        fx = gameObject.AddComponent<AudioSource>(); fx.playOnAwake = false;
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed += AlUsarRapido;
    }

    void OnDestroy()
    {
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed -= AlUsarRapido;
        Encendida = false;
    }

    void AlUsarRapido(int slot, string itemId) { if (itemId == ItemId) Alternar(); }

    int Cuantas => ControladorInventario.Instance != null ? ControladorInventario.Instance.GetTotalCount(ItemId) : 0;

    void Update()
    {
        int n = Cuantas;
        if (n > teniaAntes && !avisado)
        {
            avisado = true;
            Aviso.Mostrar("Tienes una antorcha: pulsa [" + Controles.NombreTecla(Controles.Accion.Antorcha) + "] para encenderla", new Color(1f, 0.7f, 0.3f), 4.5f);
        }
        teniaAntes = n;
        if (Encendida && n <= 0) Poner(false);
        if (Controles.Bajada(Controles.Accion.Antorcha)) Alternar();
        if (Encendida && modeloMano != null) Posicion = modeloMano.transform.position;
        if (fuego != null) fuego.volume = Encendida ? 0.45f * Ajustes.VolEfectos : 0f;
    }

    void LateUpdate()
    {
        // el brazo sube y baja suave (se aplica despues de la animacion)
        peso = Mathf.MoveTowards(peso, Encendida && !GestorPartida.Muerto ? 1f : 0f, Time.deltaTime * 2.8f);
        // si se esta bloqueando con el escudo, el brazo queda en la guardia (Escudo ya lo coloco)
        float w = Mathf.SmoothStep(0f, 1f, peso) * (1f - Escudo.PesoBloqueo);
        if (w > 0.001f && brazo != null && antebrazo != null && mano != null) LevantarBrazo(w);
        if (!Encendida || modeloMano == null) return;
        // la llama siempre hacia arriba, un poco inclinada hacia delante; se sujeta por el tercio inferior
        var rot = Quaternion.LookRotation(transform.forward, Vector3.up) * Quaternion.Euler(inclinacion, 0f, 0f);
        Vector3 agarre = mano != null ? mano.position : transform.position + transform.right * 0.45f + Vector3.up * 1.1f;
        modeloMano.transform.SetPositionAndRotation(agarre - rot * Vector3.up * (0.33f * escala), rot);
        modeloMano.transform.localScale = Vector3.one * escala / Mathf.Max(0.001f, transform.lossyScale.x);
    }

    /// <summary>Pose de antorcha: la mano va delante, a la altura de la cabeza y un poco afuera.</summary>
    void LevantarBrazo(float w)
    {
        Vector3 S = brazo.position, E = antebrazo.position, H = mano.position;
        float a = (E - S).magnitude, b = (H - E).magnitude;
        if (a < 1e-4f || b < 1e-4f) return;
        Vector3 fwd = transform.forward, up = Vector3.up, izq = -transform.right;
        Vector3 objetivo = S + (fwd * alzar.x + up * alzar.y + izq * alzar.z).normalized * (a + b) * extension;
        // el codo apunta hacia afuera y un poco hacia abajo
        IKBrazo(brazo, antebrazo, mano, objetivo, -up * 0.35f + izq * 0.9f - fwd * 0.1f, w);
    }

    /// <summary>IK analitica de dos huesos (hombro-codo-mano) hacia un objetivo, mezclada con la animacion por w.</summary>
    public static void IKBrazo(Transform brazo, Transform antebrazo, Transform mano, Vector3 objetivo, Vector3 polo, float w)
    {
        Vector3 S = brazo.position, E = antebrazo.position, H = mano.position;
        float a = (E - S).magnitude, b = (H - E).magnitude;
        if (a < 1e-4f || b < 1e-4f) return;
        Vector3 izq = (S - H).sqrMagnitude > 0f ? Vector3.Cross(Vector3.up, objetivo - S).normalized : Vector3.left;

        Vector3 haciaT = objetivo - S;
        float d = Mathf.Clamp(haciaT.magnitude, Mathf.Abs(a - b) + 1e-3f, (a + b) * 0.999f);
        Vector3 dir = haciaT.normalized;
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
        float angA = Mathf.Acos(cosA);
        Vector3 lado = Vector3.ProjectOnPlane(polo, dir).normalized;
        if (lado.sqrMagnitude < 1e-4f) lado = izq;
        Vector3 codo = S + (dir * Mathf.Cos(angA) + lado * Mathf.Sin(angA)) * a;

        Quaternion rBrazo = Quaternion.FromToRotation(E - S, codo - S) * brazo.rotation;
        brazo.rotation = Quaternion.Slerp(brazo.rotation, rBrazo, w);
        E = antebrazo.position; H = mano.position;
        Quaternion rAnte = Quaternion.FromToRotation(H - E, objetivo - E) * antebrazo.rotation;
        antebrazo.rotation = Quaternion.Slerp(antebrazo.rotation, rAnte, w);
    }

    void Alternar()
    {
        if (!Encendida && Cuantas <= 0) return;
        Poner(!Encendida);
    }

    void Poner(bool on)
    {
        Encendida = on;
        if (modeloMano != null) modeloMano.SetActive(on);
        if (on)
        {
            if (sonidoEncender != null) fx.PlayOneShot(sonidoEncender, Ajustes.VolEfectos);
            if (fuego != null && sonidoFuego != null && !fuego.isPlaying) fuego.Play();
        }
        else if (fuego != null) fuego.Stop();
    }
}
