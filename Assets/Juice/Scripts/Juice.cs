using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// "Juice" del juego (video de Guinxu "JUICE: la clave para que un juego se sienta vivo"):
/// temblor de camara, congelar unos frames al golpear (hitstop), camara lenta, destellos de color
/// en pantalla y chispas en los impactos. Todo pasa por aqui para que la intensidad se regule
/// con una sola opcion (Ajustes.Temblor) y para que dos efectos no se pisen el timeScale.
///
/// Se crea solo la primera vez que se usa. No toca nada si el juego esta en pausa (timeScale 0).
/// </summary>
public class Juice : MonoBehaviour
{
    static Juice instancia;

    // temblor por "trauma": cada golpe suma trauma, el temblor es trauma^2 y se va solo
    float trauma, traumaMax = 1f;
    float semilla;
    Vector3 offsetPos; Quaternion offsetRot = Quaternion.identity;
    Camera camAjustada; Vector3 camPosOriginal; Quaternion camRotOriginal;

    // tiempo: congelado (hitstop) y camara lenta
    float congeladoHasta;           // tiempo real
    float lentaHasta, lentaEscala = 1f, lentaDuracion;
    float escalaPuesta = -1f;       // el timeScale que pusimos nosotros (-1 = ninguno)

    // destello de pantalla
    Image destello; float destelloT, destelloDur; Color destelloColor;

    // chispas
    ParticleSystem chispas, polvo;

    static Juice Get()
    {
        if (instancia != null) return instancia;
        var go = new GameObject("Juice");
        instancia = go.AddComponent<Juice>();
        return instancia;
    }

    static float K => Mathf.Max(0f, Ajustes.Temblor);

    // ------------------------------------------------------------------ API
    /// <summary>Sacude la camara. fuerza 0..1 (0.15 golpe flojo, 0.35 golpe fuerte, 0.7 muerte o caida del jefe).</summary>
    public static void Sacudir(float fuerza)
    {
        if (K <= 0f || fuerza <= 0f) return;
        var j = Get();
        j.trauma = Mathf.Min(j.traumaMax, j.trauma + fuerza * K);
    }

    /// <summary>Sacudida que se debilita con la distancia al jugador (pisadas del jefe, explosiones...).</summary>
    public static void SacudirEn(Vector3 donde, float fuerza, float radio = 18f)
    {
        var cam = Camera.main;
        if (cam == null) return;
        float d = Vector3.Distance(cam.transform.position, donde);
        Sacudir(fuerza * Mathf.Clamp01(1f - d / radio));
    }

    /// <summary>Congela la accion unos instantes (hitstop): el golpe "pesa".</summary>
    public static void Congelar(float segundos)
    {
        if (K <= 0f || segundos <= 0f) return;
        if (Time.timeScale <= 0f) return;          // en pausa: nada
        var j = Get();
        j.congeladoHasta = Mathf.Max(j.congeladoHasta, Time.unscaledTime + segundos * Mathf.Min(1f, K));
        j.AplicarTiempo();
    }

    /// <summary>Camara lenta (al morir: para que veas de donde vino el ultimo golpe).</summary>
    public static void CamaraLenta(float escala, float segundosReales)
    {
        if (Time.timeScale <= 0f) return;
        var j = Get();
        j.lentaEscala = Mathf.Clamp(escala, 0.05f, 1f);
        j.lentaDuracion = segundosReales;
        j.lentaHasta = Time.unscaledTime + segundosReales;
        j.AplicarTiempo();
    }

    /// <summary>Destello de color a pantalla completa que se desvanece (rojo al recibir daño).</summary>
    public static void Destello(Color color, float duracion = 0.35f)
    {
        if (K <= 0f) { color.a *= 0.4f; }
        var j = Get();
        j.CrearDestello();
        j.destelloColor = color;
        j.destelloDur = Mathf.Max(0.05f, duracion);
        j.destelloT = 0f;
    }

    /// <summary>Chispas de impacto (o de "no le hace nada" si el color es gris).</summary>
    public static void Chispas(Vector3 pos, Color color, int cantidad = 14, float velocidad = 4f)
    {
        var j = Get();
        if (j.chispas == null) j.chispas = j.CrearSistema("Chispas", 0.05f, 0.35f, false);
        var p = new ParticleSystem.EmitParams { applyShapeToPosition = true };
        for (int i = 0; i < cantidad; i++)
        {
            p.position = pos;
            p.velocity = (Random.onUnitSphere + Vector3.up * 0.6f).normalized * velocidad * Random.Range(0.5f, 1.2f);
            p.startColor = Color.Lerp(color, Color.white, Random.value * 0.4f);
            p.startSize = Random.Range(0.05f, 0.14f);
            p.startLifetime = Random.Range(0.18f, 0.4f);
            j.chispas.Emit(p, 1);
        }
    }

    /// <summary>Nube de polvo a ras de suelo (aterrizar, caer algo pesado).</summary>
    public static void Polvo(Vector3 pos, float tamano = 1f, int cantidad = 10)
    {
        var j = Get();
        if (j.polvo == null) j.polvo = j.CrearSistema("Polvo", 0.3f, 1.1f, true);
        var p = new ParticleSystem.EmitParams();
        for (int i = 0; i < cantidad; i++)
        {
            Vector2 c = Random.insideUnitCircle.normalized;
            p.position = pos + new Vector3(c.x * 0.25f * tamano, 0.18f, c.y * 0.25f * tamano);
            p.velocity = new Vector3(c.x, Random.Range(0.05f, 0.35f), c.y) * Random.Range(0.8f, 1.6f) * tamano;
            p.startColor = new Color(0.78f, 0.72f, 0.62f, 0.55f);
            p.startSize = Random.Range(0.25f, 0.5f) * tamano;
            p.startLifetime = Random.Range(0.45f, 0.8f);
            j.polvo.Emit(p, 1);
        }
    }

    /// <summary>Aplasta y estira un objeto (squash &amp; stretch) y lo devuelve a su forma con un rebote.</summary>
    public static void Aplastar(Transform t, float fuerza = 0.3f, float duracion = 0.35f)
    {
        if (t == null) return;
        var r = t.GetComponent<Rebote>();
        if (r == null) r = t.gameObject.AddComponent<Rebote>();
        r.Golpe(fuerza, duracion);
    }

    // ------------------------------------------------------------------ interno
    void Awake()
    {
        semilla = Random.value * 100f;
        RenderPipelineManager.beginCameraRendering += AntesDeRender;
        RenderPipelineManager.endCameraRendering += DespuesDeRender;
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= AntesDeRender;
        RenderPipelineManager.endCameraRendering -= DespuesDeRender;
        if (escalaPuesta >= 0f && Mathf.Approximately(Time.timeScale, escalaPuesta)) Time.timeScale = 1f;
        if (instancia == this) instancia = null;
    }

    void Update()
    {
        // el temblor sigue aunque el tiempo vaya lento, pero se congela en pausa
        if (Time.timeScale > 0f || escalaPuesta >= 0f)
        {
            trauma = Mathf.Max(0f, trauma - Time.unscaledDeltaTime * 1.6f);
            float s = trauma * trauma;
            float t = Time.unscaledTime * 22f;
            offsetPos = new Vector3(Mathf.PerlinNoise(semilla, t) - 0.5f, Mathf.PerlinNoise(semilla + 7f, t) - 0.5f, 0f) * 0.5f * s;
            offsetRot = Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(semilla + 13f, t) - 0.5f) * 7f * s);
        }
        AplicarTiempo();

        if (destello != null && destello.enabled)
        {
            destelloT += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(destelloT / destelloDur);
            var c = destelloColor; c.a *= k * k;
            destello.color = c;
            if (destelloT >= destelloDur) destello.enabled = false;
        }
    }

    void AplicarTiempo()
    {
        // si alguien puso otro timeScale (menu de pausa, mapa, inventario) no lo tocamos
        if (escalaPuesta >= 0f && !Mathf.Approximately(Time.timeScale, escalaPuesta))
        {
            escalaPuesta = -1f; congeladoHasta = 0f; lentaHasta = 0f;
            return;
        }
        if (escalaPuesta < 0f && !Mathf.Approximately(Time.timeScale, 1f)) return;

        float ahora = Time.unscaledTime;
        float escala = 1f;
        if (ahora < lentaHasta)
        {
            // al final de la camara lenta se vuelve poco a poco a velocidad normal
            float resta = (lentaHasta - ahora) / Mathf.Max(0.01f, lentaDuracion);
            escala = Mathf.Lerp(1f, lentaEscala, Mathf.Clamp01(resta * 2.5f));
        }
        if (ahora < congeladoHasta) escala = Mathf.Min(escala, 0.03f);

        if (escala < 0.999f)
        {
            Time.timeScale = escala;
            escalaPuesta = escala;
        }
        else if (escalaPuesta >= 0f)
        {
            Time.timeScale = 1f;
            escalaPuesta = -1f;
        }
    }

    void AntesDeRender(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != Camera.main || camAjustada != null) return;
        if (trauma <= 0.001f) return;
        camAjustada = cam;
        camPosOriginal = cam.transform.position; camRotOriginal = cam.transform.rotation;
        cam.transform.SetPositionAndRotation(camPosOriginal + cam.transform.rotation * offsetPos, camRotOriginal * offsetRot);
    }

    void DespuesDeRender(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam != camAjustada) return;
        cam.transform.SetPositionAndRotation(camPosOriginal, camRotOriginal);
        camAjustada = null;
    }

    void CrearDestello()
    {
        if (destello != null) { destello.enabled = true; return; }
        var go = new GameObject("DestelloPantalla");
        go.transform.SetParent(transform, false);
        var c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 940;
        var img = new GameObject("Color", typeof(RectTransform)).AddComponent<Image>();
        img.transform.SetParent(go.transform, false);
        var rt = img.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        img.raycastTarget = false;
        img.sprite = SpriteVineta();
        destello = img;
    }

    // degradado radial: el destello pinta sobre todo los bordes y deja ver el centro
    static Sprite vineta;
    static Sprite SpriteVineta()
    {
        if (vineta != null) return vineta;
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Lerp(0.35f, 1f, Mathf.SmoothStep(0.2f, 1f, d))));
            }
        tex.Apply();
        vineta = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        return vineta;
    }

    ParticleSystem CrearSistema(string nombre, float tamano, float vida, bool suave)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startSize = tamano; main.startLifetime = vida; main.startSpeed = 0f;
        main.gravityModifier = suave ? -0.05f : 1.4f;
        main.maxParticles = 600;
        main.useUnscaledTime = false;
        var em = ps.emission; em.enabled = false;
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(suave ? 0.8f : 1f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, suave ? AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f) : AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
        if (suave) { var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 0.4f; lim.dampen = 0.15f; }
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.material = MaterialParticula(suave);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    static readonly Dictionary<bool, Material> materiales = new Dictionary<bool, Material>();
    static Material MaterialParticula(bool suave)
    {
        Material m;
        if (materiales.TryGetValue(suave, out m) && m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        m = new Material(sh);
        // textura redonda suave hecha a mano
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = suave ? Mathf.Clamp01(1f - d) : Mathf.Clamp01((1f - d) * 3f);
                tex.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        tex.Apply();
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        m.mainTexture = tex;
        // transparente con mezcla alfa (o aditiva para las chispas)
        if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
        if (m.HasProperty("_Blend")) m.SetFloat("_Blend", suave ? 0f : 2f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", suave ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.One);
        m.SetInt("_ZWrite", 0);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        materiales[suave] = m;
        return m;
    }
}

/// <summary>Squash &amp; stretch: aplasta el objeto y lo devuelve a su escala con un muelle amortiguado.</summary>
public class Rebote : MonoBehaviour
{
    Vector3 escalaBase;
    bool tieneBase;
    float valor, velocidad;     // deformacion actual (+ aplastado, - estirado)

    public void Golpe(float fuerza, float duracion)
    {
        if (!tieneBase) { escalaBase = transform.localScale; tieneBase = true; }
        valor = fuerza;
        velocidad = 0f;
        enabled = true;
    }

    void LateUpdate()
    {
        // muelle: la deformacion oscila y se apaga (gelatina)
        // en pausa (inventario abierto) sigue animandose con el tiempo real
        float dt = Mathf.Min(Time.timeScale > 0f ? Time.deltaTime : Time.unscaledDeltaTime, 0.05f);
        float rigidez = 260f, amort = 14f;
        velocidad += (-rigidez * valor - amort * velocidad) * dt;
        valor += velocidad * dt;
        float y = 1f - valor, xz = 1f + valor * 0.5f;
        transform.localScale = new Vector3(escalaBase.x * xz, escalaBase.y * y, escalaBase.z * xz);
        if (Mathf.Abs(valor) < 0.002f && Mathf.Abs(velocidad) < 0.02f)
        {
            transform.localScale = escalaBase;
            enabled = false;
        }
    }

    void OnDisable() { if (tieneBase) transform.localScale = escalaBase; }
}
