using UnityEngine;

// Una luciernaga: vuela deambulando alrededor de un punto "hogar", aletea,
// y su abdomen emite una luz verde que pulsa. Llega al anochecer saliendo
// de la vegetacion y se va volando hacia el cielo al amanecer.
public class Firefly : MonoBehaviour
{
    public enum Estado { Oculta, Llegando, Volando, Yendose }

    [Header("Partes del modelo")]
    public Transform alaIzq;
    public Transform alaDer;
    public Renderer linterna;
    public Renderer halo;
    public Light luz;

    [Header("Luz")]
    public float intensidadLuz = 6f;
    public float rangoLuz = 9f;
    [Tooltip("Brillo minimo entre destellos (0-1). Alto = ilumina mas constante")]
    [Range(0f, 1f)] public float brilloBase = 0.45f;

    [Header("Vuelo")]
    public float velocidad = 2.2f;
    public float alturaMin = 0.6f;
    public float alturaMax = 4.5f;
    public float frecuenciaAleteo = 22f;

    public Estado EstadoActual { get; private set; } = Estado.Oculta;

    // El enjambre asigna el hogar; si sigueA no es null, el hogar se mueve con ese transform
    [System.NonSerialized] public Vector3 hogar;
    [System.NonSerialized] public Transform sigueA;
    [System.NonSerialized] public Vector3 desplazamientoHogar;
    [System.NonSerialized] public float radio = 6f;

    Vector3 vel;
    float semilla, fade, tiempoEstado, proximoDestello, destello, periodoDestello;
    Vector3 direccionHuida;
    Quaternion rotBaseIzq, rotBaseDer;
    float signoIzq = 1f, signoDer = -1f;
    MaterialPropertyBlock mpb;
    static readonly int GlowId = Shader.PropertyToID("_Glow");
    static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    const int MascaraSuelo = ~(1 << 2); // todo menos "Ignore Raycast"

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        semilla = Random.value * 1000f;
        periodoDestello = Random.Range(2.2f, 5f);
        proximoDestello = Random.Range(0f, periodoDestello);
        if (alaIzq != null) { rotBaseIzq = alaIzq.localRotation; signoIzq = Mathf.Sign(alaIzq.localPosition.x == 0 ? 1 : alaIzq.localPosition.x); }
        if (alaDer != null) { rotBaseDer = alaDer.localRotation; signoDer = Mathf.Sign(alaDer.localPosition.x == 0 ? -1 : alaDer.localPosition.x); }
        AplicarBrillo(0f);
    }

    Vector3 HogarActual => sigueA != null ? sigueA.position + desplazamientoHogar : hogar;

    public void Llegar(float retraso)
    {
        gameObject.SetActive(true);
        Vector3 h = HogarActual;
        // aparece entre la hierba, cerca de su hogar
        Vector3 p = h + new Vector3(Random.Range(-radio, radio), 0f, Random.Range(-radio, radio));
        if (Physics.Raycast(p + Vector3.up * 8f, Vector3.down, out var hit, 40f, MascaraSuelo, QueryTriggerInteraction.Ignore)) p = hit.point + Vector3.up * 0.15f;
        transform.position = p;
        vel = Vector3.up * 0.5f;
        fade = 0f;
        tiempoEstado = -retraso;
        EstadoActual = Estado.Llegando;
        AplicarBrillo(0f);
    }

    public void Irse(float retraso)
    {
        if (EstadoActual == Estado.Oculta) return;
        Vector2 r = Random.insideUnitCircle.normalized;
        direccionHuida = new Vector3(r.x, Random.Range(0.7f, 1.4f), r.y).normalized;
        tiempoEstado = -retraso;
        EstadoActual = Estado.Yendose;
    }

    public void OcultarYa()
    {
        EstadoActual = Estado.Oculta;
        gameObject.SetActive(false);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        tiempoEstado += dt;
        float t = Time.time + semilla;

        // --- Aleteo (las alas se ven borrosas por la velocidad, con un pequeño temblor)
        float ang = Mathf.Sin(t * frecuenciaAleteo * Mathf.PI * 2f) * 55f + 15f;
        if (alaIzq != null) alaIzq.localRotation = Quaternion.AngleAxis(ang * signoIzq, Vector3.forward) * rotBaseIzq;
        if (alaDer != null) alaDer.localRotation = Quaternion.AngleAxis(ang * signoDer, Vector3.forward) * rotBaseDer;

        // Esperando su turno (retraso escalonado)
        if (tiempoEstado < 0f)
        {
            if (EstadoActual == Estado.Llegando) { AplicarBrillo(0f); return; }
        }

        Vector3 pos = transform.position;
        Vector3 objetivo;
        float vMax = velocidad;

        switch (EstadoActual)
        {
            case Estado.Llegando:
                fade = Mathf.Clamp01(tiempoEstado / 3f);
                objetivo = PuntoDeambular(t);
                if (fade >= 1f) EstadoActual = Estado.Volando;
                break;
            case Estado.Yendose:
                if (tiempoEstado < 0f) { objetivo = PuntoDeambular(t); break; }
                fade = Mathf.Clamp01(1f - tiempoEstado / 5f);
                objetivo = pos + direccionHuida * 10f + new Vector3(Mathf.Sin(t * 1.3f), 0f, Mathf.Cos(t * 1.1f)) * 2f;
                vMax = velocidad * (1f + tiempoEstado * 0.8f);
                if (fade <= 0f) { OcultarYa(); return; }
                break;
            default:
                fade = 1f;
                objetivo = PuntoDeambular(t);
                break;
        }

        // --- Direccion suave hacia el objetivo, con algo de "zumbido" aleatorio
        Vector3 deseada = objetivo - pos;
        if (deseada.sqrMagnitude > vMax * vMax) deseada = deseada.normalized * vMax;
        Vector3 jitter = new Vector3(Mathf.PerlinNoise(t * 1.7f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t * 1.9f) - 0.5f, Mathf.PerlinNoise(t * 1.5f, 5.1f) - 0.5f) * 1.6f;
        vel = Vector3.Lerp(vel, deseada + jitter, 1f - Mathf.Exp(-dt * 1.8f));

        // esquiva obstaculos al frente
        if (vel.sqrMagnitude > 0.01f && Physics.SphereCast(pos, 0.15f, vel.normalized, out var obst, 1.2f, MascaraSuelo, QueryTriggerInteraction.Ignore))
            vel = Vector3.Reflect(vel, obst.normal) * 0.7f + obst.normal * 0.4f;

        pos += vel * dt;

        // mantener una altura razonable sobre el suelo (salvo cuando se va)
        if (EstadoActual != Estado.Yendose && Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.down, out var suelo, 20f, MascaraSuelo, QueryTriggerInteraction.Ignore))
        {
            float altura = pos.y - suelo.point.y;
            float minimo = EstadoActual == Estado.Llegando ? Mathf.Lerp(0.1f, alturaMin, fade) : alturaMin;
            if (altura < minimo) { pos.y = suelo.point.y + minimo; if (vel.y < 0f) vel.y = 0f; }
        }
        transform.position = pos;

        if (vel.sqrMagnitude > 0.0004f)
        {
            Vector3 plano = new Vector3(vel.x, vel.y * 0.4f, vel.z);
            Quaternion mirar = Quaternion.LookRotation(plano.normalized, Vector3.up);
            float alabeo = Mathf.Clamp(Vector3.Dot(Vector3.Cross(transform.forward, vel), Vector3.up) * -25f, -35f, 35f);
            transform.rotation = Quaternion.Slerp(transform.rotation, mirar * Quaternion.Euler(0f, 0f, alabeo), 1f - Mathf.Exp(-dt * 4f));
        }

        // --- Destellos: subida rapida y apagado lento, como las luciernagas reales
        proximoDestello -= dt;
        if (proximoDestello <= 0f) { destello = 1f; proximoDestello = periodoDestello * Random.Range(0.7f, 1.3f); }
        destello = Mathf.Max(0f, destello - dt * 1.1f);
        float pulso = Mathf.SmoothStep(0f, 1f, destello);
        float respiracion = 0.85f + 0.15f * Mathf.Sin(t * 2.3f);
        float brillo = Mathf.Lerp(brilloBase * respiracion, 1f, pulso) * fade;
        AplicarBrillo(brillo);
    }

    Vector3 PuntoDeambular(float t)
    {
        Vector3 h = HogarActual;
        float s = t * 0.07f;
        float x = (Mathf.PerlinNoise(s, semilla) - 0.5f) * 2f * radio;
        float z = (Mathf.PerlinNoise(semilla, s) - 0.5f) * 2f * radio;
        float y = Mathf.Lerp(alturaMin, alturaMax, Mathf.PerlinNoise(s * 1.6f, semilla * 0.5f));
        Vector3 p = new Vector3(h.x + x, h.y, h.z + z);
        // la altura se mide desde el suelo debajo del punto
        if (Physics.Raycast(p + Vector3.up * 8f, Vector3.down, out var hit, 40f, MascaraSuelo, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
        p.y += y;
        return p;
    }

    void AplicarBrillo(float b)
    {
        if (luz != null)
        {
            luz.intensity = intensidadLuz * b;
            luz.range = rangoLuz;
            luz.enabled = b > 0.01f;
        }
        if (linterna != null)
        {
            linterna.GetPropertyBlock(mpb);
            mpb.SetFloat(GlowId, 0.08f + b);
            linterna.SetPropertyBlock(mpb);
        }
        if (halo != null)
        {
            halo.GetPropertyBlock(mpb);
            mpb.SetFloat(IntensityId, b);
            halo.SetPropertyBlock(mpb);
            halo.enabled = b > 0.01f;
        }
    }
}
