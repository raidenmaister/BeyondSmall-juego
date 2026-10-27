using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Ciclo de dia y noche: mueve el sol y la luna, pinta el cielo (shader Custom/DayNightSky),
// ajusta luz ambiental, niebla, luces "de dia" de la escena, post-proceso nocturno
// y el sonido de grillos.
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    [Header("Tiempo")]
    [Tooltip("Hora actual del juego (0-24)")]
    [Range(0f, 24f)] public float hora = 17.5f;
    [Tooltip("Minutos reales que dura un dia completo de 24 horas")]
    public float duracionDiaMinutos = 8f;
    [Tooltip("La noche pasa mas rapido que el dia si es < 1")]
    public float multiplicadorNoche = 1f;
    public bool pausado;
    [Tooltip("Mantener esta tecla acelera el tiempo (para probar)")]
    public KeyCode teclaAcelerar = KeyCode.N;
    public float aceleracion = 40f;

    [Header("Luces")]
    public Light sol;
    public Light luna;
    public float intensidadSol = 2.2f;
    public float intensidadLuna = 0.35f;
    [Tooltip("Orientacion horizontal del recorrido del sol")]
    public float azimut = 150f;
    [Tooltip("Inclinacion del recorrido del sol (0 = pasa justo por encima)")]
    public float inclinacion = 25f;
    [Tooltip("Luces de relleno de dia (rayos de sol, etc.). Se apagan de noche")]
    public List<Light> lucesDeDia = new List<Light>();

    [Header("Cielo")]
    public Material cielo;
    public Color cieloArribaDia = new Color(0.24f, 0.5f, 0.9f);
    public Color cieloArribaAtardecer = new Color(0.22f, 0.25f, 0.5f);
    public Color cieloArribaNoche = new Color(0.008f, 0.014f, 0.045f);
    public Color horizonteDia = new Color(0.68f, 0.82f, 0.95f);
    public Color horizonteAtardecer = new Color(1f, 0.48f, 0.22f);
    public Color horizonteNoche = new Color(0.03f, 0.05f, 0.1f);
    public Color colorSolDia = new Color(1f, 0.95f, 0.85f);
    public Color colorSolAtardecer = new Color(1f, 0.5f, 0.25f);
    public Color colorLuna = new Color(0.62f, 0.72f, 1f);

    [Header("Ambiente")]
    public Color ambienteCieloDia = new Color(0.62f, 0.72f, 0.64f);
    public Color ambienteCieloAtardecer = new Color(0.6f, 0.47f, 0.5f);
    public Color ambienteCieloNoche = new Color(0.07f, 0.09f, 0.17f);
    public Color ambienteEcuadorDia = new Color(0.42f, 0.48f, 0.38f);
    public Color ambienteEcuadorAtardecer = new Color(0.46f, 0.33f, 0.29f);
    public Color ambienteEcuadorNoche = new Color(0.035f, 0.05f, 0.09f);
    public Color ambienteSueloDia = new Color(0.22f, 0.2f, 0.14f);
    public Color ambienteSueloAtardecer = new Color(0.13f, 0.1f, 0.09f);
    public Color ambienteSueloNoche = new Color(0.015f, 0.015f, 0.025f);
    public Color nieblaDia = new Color(0.56f, 0.64f, 0.55f);
    public Color nieblaAtardecer = new Color(0.5f, 0.36f, 0.3f);
    public Color nieblaNoche = new Color(0.02f, 0.03f, 0.06f);
    public float densidadNieblaDia = 0.0042f;
    public float densidadNieblaNoche = 0.011f;
    public float reflejosDia = 0.12f;
    public float reflejosNoche = 0.03f;

    [Header("Post-proceso y sonido")]
    [Tooltip("Volumen con el look nocturno (tinte azul, vineta). Su peso sigue a la noche")]
    public Volume volumenNoche;
    public AudioSource grillos;
    public AudioSource grillosLejanos;
    public float volumenGrillos = 0.55f;

    // 0 = pleno dia, 1 = noche cerrada
    public float Noche { get; private set; }
    // 0..1, maximo cuando el sol esta en el horizonte
    public float Atardecer { get; private set; }
    // Entre las 4:30 y el mediodia: las luciernagas usan esto para irse al amanecer
    public bool EsMadrugadaOManana => hora >= 4.5f && hora < 12f;

    readonly Dictionary<Light, float> intensidadOriginal = new Dictionary<Light, float>();
    float siguienteActualizacionGI;
    float fasePitch;

    static readonly int SkyTop = Shader.PropertyToID("_SkyTop");
    static readonly int SkyHorizon = Shader.PropertyToID("_SkyHorizon");
    static readonly int GroundColor = Shader.PropertyToID("_GroundColor");
    static readonly int SunDir = Shader.PropertyToID("_SunDir");
    static readonly int SunColor = Shader.PropertyToID("_SunColor");
    static readonly int SunsetGlow = Shader.PropertyToID("_SunsetGlow");
    static readonly int MoonDir = Shader.PropertyToID("_MoonDir");
    static readonly int MoonColor = Shader.PropertyToID("_MoonColor");
    static readonly int StarIntensity = Shader.PropertyToID("_StarIntensity");
    static readonly int StarRotation = Shader.PropertyToID("_StarRotation");
    static readonly int CloudLit = Shader.PropertyToID("_CloudLit");
    static readonly int CloudShade = Shader.PropertyToID("_CloudShade");

    void Awake()
    {
        Instance = this;
        foreach (var l in lucesDeDia) if (l != null) intensidadOriginal[l] = l.intensity;
        if (cielo != null)
        {
            // instancia propia para no modificar el asset en disco mientras se juega
            cielo = new Material(cielo);
            RenderSettings.skybox = cielo;
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        fasePitch = Random.value * 10f;
    }

    void Start()
    {
        if (grillos != null && !grillos.isPlaying) grillos.Play();
        if (grillosLejanos != null && !grillosLejanos.isPlaying)
        {
            // desfasado para que no se note el loop
            grillosLejanos.Play();
            if (grillosLejanos.clip != null) grillosLejanos.time = grillosLejanos.clip.length * 0.47f;
        }
        Aplicar(true);
    }

    void Update()
    {
        if (!pausado)
        {
            float velocidad = 24f / Mathf.Max(0.01f, duracionDiaMinutos * 60f);
            if (Noche > 0.5f) velocidad *= multiplicadorNoche;
            if (Input.GetKey(teclaAcelerar)) velocidad *= aceleracion;
            hora = Mathf.Repeat(hora + velocidad * Time.deltaTime, 24f);
        }
        Aplicar(false);
    }

    void OnValidate()
    {
        // permite previsualizar la hora en el editor moviendo el slider
        if (!Application.isPlaying && isActiveAndEnabled && sol != null) Aplicar(true);
    }

    public void AplicarAhora() { Aplicar(true); }

    static Color Mezcla(Color noche, Color atardecer, Color dia, float wNoche, float wDia)
    {
        float wAt = Mathf.Max(0f, 1f - wNoche - wDia);
        return noche * wNoche + atardecer * wAt + dia * wDia;
    }

    void Aplicar(bool forzarGI)
    {
        // --- Posicion del sol: 6h sale, 12h cenit, 18h se pone
        float angulo = (hora / 24f) * 360f - 90f;
        Quaternion orbita = Quaternion.Euler(0f, azimut, 0f) * Quaternion.Euler(0f, 0f, inclinacion);
        Quaternion rotSol = orbita * Quaternion.Euler(angulo, 0f, 0f);
        Vector3 haciaSol = -(rotSol * Vector3.forward);
        // la luna va mas o menos opuesta al sol, con otra inclinacion
        Quaternion rotLuna = orbita * Quaternion.Euler(angulo + 180f + 12f, 18f, 0f);
        Vector3 haciaLuna = -(rotLuna * Vector3.forward);

        float elev = haciaSol.y;
        float wDia = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0f, 0.35f, elev));
        float wNoche = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.28f, -0.02f, elev));
        Noche = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.2f, 0.1f, elev));
        Atardecer = Mathf.Clamp01(1f - Mathf.Abs(elev) / 0.3f);

        // --- Sol
        Color colSol = Color.Lerp(colorSolAtardecer, colorSolDia, wDia);
        float iSol = intensidadSol * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.04f, 0.2f, elev));
        if (sol != null)
        {
            sol.transform.rotation = rotSol;
            sol.color = colSol;
            sol.intensity = iSol;
            sol.enabled = iSol > 0.001f;
        }
        // --- Luna
        float iLuna = intensidadLuna * Noche * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.25f, haciaLuna.y));
        if (luna != null)
        {
            luna.transform.rotation = rotLuna;
            luna.color = colorLuna;
            luna.intensity = iLuna;
            luna.enabled = iLuna > 0.001f;
        }
        // la luz principal (con sombras) es la que mas ilumina en ese momento
        RenderSettings.sun = (sol != null && (iSol >= iLuna || luna == null)) ? sol : luna;

        // --- Luces de relleno de dia (rayos de sol entre el techo roto)
        float factorDia = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, 0.3f, elev));
        foreach (var kv in intensidadOriginal)
        {
            if (kv.Key == null) continue;
            kv.Key.intensity = kv.Value * factorDia;
            kv.Key.enabled = factorDia > 0.001f;
        }

        // --- Ambiente y niebla
        RenderSettings.ambientSkyColor = Mezcla(ambienteCieloNoche, ambienteCieloAtardecer, ambienteCieloDia, wNoche, wDia);
        RenderSettings.ambientEquatorColor = Mezcla(ambienteEcuadorNoche, ambienteEcuadorAtardecer, ambienteEcuadorDia, wNoche, wDia);
        RenderSettings.ambientGroundColor = Mezcla(ambienteSueloNoche, ambienteSueloAtardecer, ambienteSueloDia, wNoche, wDia);
        RenderSettings.fogColor = Mezcla(nieblaNoche, nieblaAtardecer, nieblaDia, wNoche, wDia);
        RenderSettings.fogDensity = Mathf.Lerp(densidadNieblaDia, densidadNieblaNoche, Noche);
        RenderSettings.reflectionIntensity = Mathf.Lerp(reflejosDia, reflejosNoche, Noche);
        if (forzarGI || Time.unscaledTime >= siguienteActualizacionGI)
        {
            siguienteActualizacionGI = Time.unscaledTime + 0.2f;
            DynamicGI.UpdateEnvironment();
        }

        // --- Cielo
        if (cielo != null)
        {
            Color horizonte = Mezcla(horizonteNoche, horizonteAtardecer, horizonteDia, wNoche, wDia);
            cielo.SetColor(SkyTop, Mezcla(cieloArribaNoche, cieloArribaAtardecer, cieloArribaDia, wNoche, wDia));
            cielo.SetColor(SkyHorizon, horizonte);
            cielo.SetColor(GroundColor, horizonte * 0.35f);
            cielo.SetVector(SunDir, haciaSol);
            cielo.SetColor(SunColor, colSol);
            cielo.SetFloat(SunsetGlow, Atardecer * 1.2f);
            cielo.SetVector(MoonDir, haciaLuna);
            cielo.SetColor(MoonColor, new Color(0.9f, 0.93f, 1f));
            cielo.SetFloat(StarIntensity, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, Noche)) * 1.6f);
            cielo.SetFloat(StarRotation, hora / 24f * Mathf.PI * 2f);
            cielo.SetColor(CloudLit, Mezcla(new Color(0.12f, 0.14f, 0.2f), new Color(1f, 0.62f, 0.45f), new Color(1f, 1f, 1f), wNoche, wDia));
            cielo.SetColor(CloudShade, Mezcla(new Color(0.03f, 0.04f, 0.07f), new Color(0.42f, 0.28f, 0.35f), new Color(0.6f, 0.66f, 0.75f), wNoche, wDia));
        }

        if (volumenNoche != null) volumenNoche.weight = Noche;

        // --- Grillos: solo de noche, con una leve variacion de tono
        if (Application.isPlaying)
        {
            float vol = volumenGrillos * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.9f, Noche));
            if (grillos != null)
            {
                grillos.volume = vol;
                grillos.pitch = 1f + 0.03f * Mathf.Sin(Time.time * 0.13f + fasePitch);
            }
            if (grillosLejanos != null)
            {
                grillosLejanos.volume = vol * 0.6f;
                grillosLejanos.pitch = 0.9f + 0.03f * Mathf.Sin(Time.time * 0.09f + fasePitch * 2f);
            }
        }
    }
}
