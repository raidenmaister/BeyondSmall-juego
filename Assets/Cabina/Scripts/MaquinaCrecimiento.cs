using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// La cabina gigante del centro del salon (la que encogio al jugador). Para encenderla hay que
/// meter 4 baterias verdes en la bahia que tiene la consola al ras del suelo.
/// Cada bateria enciende su LED y una franja de la cabina; con las 4 la maquina arranca:
/// zumba, giran los anillos del techo y se ilumina por dentro. alEncender queda para lo que siga
/// (volver a crecer).
/// La construye Herramientas > Cabina > Construir (CabinaBuilder).
/// </summary>
public class MaquinaCrecimiento : MonoBehaviour
{
    public const string ItemBateria = "Bateria";
    public const int Necesarias = 4;

    public static MaquinaCrecimiento Instancia { get; private set; }

    [Header("Bahia de baterias")]
    public Transform bahia;
    public float radioBahia = 5f;
    public GameObject[] bateriasRanura = new GameObject[Necesarias];
    public Renderer[] ledsRanura = new Renderer[Necesarias];

    [Header("Cabina")]
    public float radioInterior = 12f;
    [Tooltip("Piezas que brillan con la energia (anillo del piso, emisor del techo, franjas)")]
    public Renderer[] emisivos;
    [Tooltip("Franjas verticales: se encienden una por bateria")]
    public Renderer[] franjas;
    public Light[] luces;
    public Transform[] anillosGiratorios;
    public TextMeshPro pantalla;
    public Renderer haz;

    [Header("Colores")]
    public Color colorEnergia = new Color(0.25f, 1f, 0.45f);
    public Color colorReposo = new Color(0.9f, 0.18f, 0.08f);

    [Header("Sonido")]
    public AudioClip sonidoInsertar, sonidoEncender, zumbido;

    public UnityEvent alEncender;

    [SerializeField] int colocadas;
    public int Colocadas => colocadas;
    public bool Encendida => colocadas >= Necesarias;

    Transform jugador;
    AudioSource fuenteZumbido, fuenteFx;
    MaterialPropertyBlock mpb;
    float energia;            // 0..1, sube despacio al encender
    float[] intensidadLuz;
    static readonly int Emision = Shader.PropertyToID("_EmissionColor");
    static readonly int ColorBase = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        Instancia = this;
        mpb = new MaterialPropertyBlock();
        if (luces != null)
        {
            intensidadLuz = new float[luces.Length];
            for (int i = 0; i < luces.Length; i++) if (luces[i] != null) intensidadLuz[i] = luces[i].intensity;
        }
        fuenteFx = gameObject.AddComponent<AudioSource>();
        fuenteFx.playOnAwake = false; fuenteFx.spatialBlend = 0.6f; fuenteFx.minDistance = 20f; fuenteFx.maxDistance = 200f;
        fuenteZumbido = gameObject.AddComponent<AudioSource>();
        fuenteZumbido.playOnAwake = false; fuenteZumbido.loop = true; fuenteZumbido.clip = zumbido;
        fuenteZumbido.spatialBlend = 1f; fuenteZumbido.rolloffMode = AudioRolloffMode.Linear; fuenteZumbido.minDistance = 15f; fuenteZumbido.maxDistance = 160f;
        energia = Encendida ? 1f : 0f;
    }

    void OnDestroy() { if (Instancia == this) Instancia = null; }

    void Start()
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        for (int i = 0; i < bateriasRanura.Length; i++) if (bateriasRanura[i] != null) bateriasRanura[i].SetActive(i < colocadas);
        if (Encendida && zumbido != null) fuenteZumbido.Play();
        Pintar();
    }

    void Update()
    {
        if (jugador != null && !IntroDespertar.Activa) Interaccion();

        float objetivo = Encendida ? 1f : 0f;
        if (!Mathf.Approximately(energia, objetivo)) energia = Mathf.MoveTowards(energia, objetivo, Time.deltaTime / 3.5f);
        if (anillosGiratorios != null)
            for (int i = 0; i < anillosGiratorios.Length; i++)
                if (anillosGiratorios[i] != null)
                    anillosGiratorios[i].Rotate(0f, (i % 2 == 0 ? 1f : -1f) * (6f + 70f * energia * energia) * (1f + i * 0.35f) * Time.deltaTime, 0f, Space.Self);
        if (fuenteZumbido.isPlaying)
        {
            fuenteZumbido.volume = Mathf.Lerp(0f, 0.55f, energia) * Ajustes.VolEfectos;
            fuenteZumbido.pitch = Mathf.Lerp(0.6f, 1f, energia);
        }
        Pintar();
    }

    void Interaccion()
    {
        var inv = ControladorInventario.Instance;
        if (inv != null && inv.IsOpen) return;
        Vector3 p = jugador.position;

        // bahia de baterias (al ras del suelo, en la consola)
        if (bahia != null && !Encendida)
        {
            Vector3 d = p - bahia.position; d.y = 0f;
            if (d.magnitude <= radioBahia && Mathf.Abs(p.y - bahia.position.y) < 6f)
            {
                int tengo = inv != null ? inv.GetTotalCount(ItemBateria) : 0;
                string tecla = Controles.NombreTecla(Controles.Accion.Interactuar);
                if (tengo > 0)
                {
                    SurvivalUI.Indicacion(this, $"[{tecla}] Colocar bateria verde   ({colocadas}/{Necesarias})");
                    if (Controles.Bajada(Controles.Accion.Interactuar) && inv.RemoveItem(ItemBateria, 1)) Colocar();
                }
                else
                    SurvivalUI.Indicacion(this, $"Bahia de energia: {colocadas}/{Necesarias} baterias verdes");
                return;
            }
        }

        // dentro de la cabina
        Vector3 c = p - transform.position;
        float alto = c.y; c.y = 0f;
        if (c.magnitude < radioInterior && alto > -1f && alto < 20f)
        {
            if (Encendida) SurvivalUI.Indicacion(this, "La cabina tiene energia de nuevo...");
            else SurvivalUI.Indicacion(this, $"La cabina esta sin energia: faltan {Necesarias - colocadas} baterias verdes");
        }
    }

    /// <summary>Mete una bateria en la siguiente ranura libre.</summary>
    public void Colocar()
    {
        if (Encendida) return;
        if (colocadas < bateriasRanura.Length && bateriasRanura[colocadas] != null) bateriasRanura[colocadas].SetActive(true);
        colocadas++;
        if (sonidoInsertar != null) fuenteFx.PlayOneShot(sonidoInsertar, 0.7f * Ajustes.VolEfectos);
        if (Encendida)
        {
            if (sonidoEncender != null) fuenteFx.PlayOneShot(sonidoEncender, 0.9f * Ajustes.VolEfectos);
            if (zumbido != null) { fuenteZumbido.volume = 0f; fuenteZumbido.Play(); }
            SurvivalUI.Aviso("¡La cabina se encendio!", colorEnergia);
            alEncender?.Invoke();
        }
        else
            SurvivalUI.Aviso($"Bateria colocada ({colocadas}/{Necesarias})", colorEnergia);
    }

    void Pintar()
    {
        float pulso = 0.85f + 0.15f * Mathf.Sin(Time.time * (2f + 6f * energia));
        Color reposo = colorReposo * 0.6f * (0.7f + 0.3f * Mathf.Sin(Time.time * 1.3f));
        Color activo = colorEnergia * Mathf.Lerp(0.2f, 3.2f, energia) * pulso;
        Color general = Color.Lerp(reposo, activo, Mathf.Clamp01(energia * 1.5f + colocadas * 0.08f));
        if (emisivos != null) foreach (var r in emisivos) Emitir(r, general);

        if (franjas != null)
            for (int i = 0; i < franjas.Length; i++)
            {
                // cada bateria enciende una parte de las franjas
                float k = Mathf.Clamp01(colocadas / (float)Necesarias * franjas.Length - i);
                Emitir(franjas[i], Color.Lerp(colorReposo * 0.25f, colorEnergia * (1.4f + 1.8f * energia) * pulso, k));
            }

        if (ledsRanura != null)
            for (int i = 0; i < ledsRanura.Length; i++)
                Emitir(ledsRanura[i], i < colocadas ? colorEnergia * 4f : colorReposo * (1.2f + 0.8f * Mathf.Sin(Time.time * 4f + i)));

        if (haz != null)
        {
            haz.enabled = energia > 0.01f;
            if (haz.enabled)
            {
                Color c = colorEnergia; c.a = 0.1f * energia * pulso;
                mpb.Clear(); mpb.SetColor(ColorBase, c); mpb.SetColor(Emision, colorEnergia * 1.5f * energia);
                haz.SetPropertyBlock(mpb);
            }
        }

        if (luces != null)
            for (int i = 0; i < luces.Length; i++)
            {
                if (luces[i] == null) continue;
                luces[i].color = Color.Lerp(colorReposo, colorEnergia, Mathf.Clamp01(energia * 2f));
                luces[i].intensity = intensidadLuz[i] * Mathf.Lerp(0.12f + 0.06f * colocadas, 1f, energia) * pulso;
            }

        if (pantalla != null)
        {
            pantalla.text = Encendida ? "<size=70%>ENERGIA</size>\nLISTA" : $"<size=70%>ENERGIA</size>\n{colocadas}/{Necesarias}";
            pantalla.color = Encendida ? colorEnergia : Color.Lerp(colorReposo, colorEnergia, colocadas / (float)Necesarias);
        }
    }

    void Emitir(Renderer r, Color c)
    {
        if (r == null) return;
        r.GetPropertyBlock(mpb);
        mpb.SetColor(Emision, c);
        r.SetPropertyBlock(mpb);
    }
}
