using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Intro al empezar la partida: todo negro (los ojos cerrados). Se oye una maquina que se enciende,
/// zumba cada vez mas fuerte, algo se encoge con un sonido que baja de tono y la maquina se apaga.
/// Silencio, un latido, una respiracion... y el jugador "abre los ojos": dos parpados se separan
/// despacio (con un par de parpadeos), la vista pasa de borrosa y gris a nitida.
/// Mientras dura, el jugador no se mueve. Mantener ENTER o ESC la salta.
/// </summary>
public class IntroDespertar : MonoBehaviour
{
    public static bool Activa { get; private set; }

    public AudioClip encendido, zumbido, carga, encoger, apagado, latido, respiro;
    public TMP_FontAsset fuente;
    [TextArea] public string[] pensamientos = { "...¿que fue ese ruido?", "¿Por que todo se ve tan... enorme?" };

    Canvas canvas;
    Image negro, brillo, parpadoArriba, parpadoAbajo;
    RectTransform rtArriba, rtAbajo;
    TextMeshProUGUI textoSaltar, textoPensamiento;
    AudioSource fuenteZumbido, fuenteFx;
    Volume vol;
    DepthOfField dof;
    ColorAdjustments color;
    Vignette vineta;
    float apertura;          // 0 = ojos cerrados, 1 = abiertos
    bool saltar;
    float tSaltar;
    MonoBehaviour[] camaraInput;

    const float AltoParpado = 900f, Curva = 0.35f;

    void Awake()
    {
        if (!Ajustes.IntroPendiente) { Destroy(gameObject); return; }
        Ajustes.IntroPendiente = false;
        Activa = true;
        Controles.Bloqueado = true;
        AudioListener.pause = true;            // el mundo en silencio; los sonidos de la intro lo ignoran
        CrearUI();
        CrearAudio();
        CrearPost();
        Aplicar();
    }

    void Start()
    {
        // la camara tampoco gira mientras tenemos los ojos cerrados
        var l = new System.Collections.Generic.List<MonoBehaviour>();
        foreach (var mb in FindObjectsByType<MonoBehaviour>())
            if (mb.GetType().Name == "CinemachineInputAxisController" && mb.enabled) { mb.enabled = false; l.Add(mb); }
        camaraInput = l.ToArray();
        StartCoroutine(Secuencia());
    }

    // ------------------------------------------------------------------ construccion
    void CrearUI()
    {
        var go = new GameObject("CanvasIntro");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 800;
        var cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.matchWidthOrHeight = 0.5f;
        var raiz = (RectTransform)go.transform;

        var tex = TexturaParpado();
        var spr = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f));
        parpadoArriba = Img("ParpadoArriba", raiz, Color.black); parpadoArriba.sprite = spr;
        rtArriba = parpadoArriba.rectTransform;
        rtArriba.anchorMin = rtArriba.anchorMax = new Vector2(0.5f, 0.5f); rtArriba.pivot = new Vector2(0.5f, 0f);
        rtArriba.sizeDelta = new Vector2(1920f * 1.5f, AltoParpado);
        parpadoAbajo = Img("ParpadoAbajo", raiz, Color.black); parpadoAbajo.sprite = spr;
        rtAbajo = parpadoAbajo.rectTransform;
        rtAbajo.anchorMin = rtAbajo.anchorMax = new Vector2(0.5f, 0.5f); rtAbajo.pivot = new Vector2(0.5f, 0f);
        rtAbajo.sizeDelta = new Vector2(1920f * 1.5f, AltoParpado);
        rtAbajo.localScale = new Vector3(1f, -1f, 1f);

        // luz que se cuela por los parpados cerrados (rojiza)
        brillo = Img("Brillo", raiz, new Color(0.35f, 0.04f, 0.02f, 0f)); Estirar(brillo.rectTransform);
        negro = Img("Negro", raiz, Color.black); Estirar(negro.rectTransform);

        textoSaltar = Txt("Saltar", raiz, "Manten ENTER para saltar", 20, TextAlignmentOptions.BottomRight);
        var rs = textoSaltar.rectTransform; rs.anchorMin = rs.anchorMax = new Vector2(1, 0); rs.pivot = new Vector2(1, 0);
        rs.anchoredPosition = new Vector2(-40, 34); rs.sizeDelta = new Vector2(600, 40);
        textoSaltar.color = new Color(1, 1, 1, 0);
        textoPensamiento = Txt("Pensamiento", raiz, "", 34, TextAlignmentOptions.Center);
        var rp = textoPensamiento.rectTransform; rp.anchorMin = new Vector2(0, 0); rp.anchorMax = new Vector2(1, 0); rp.pivot = new Vector2(0.5f, 0);
        rp.anchoredPosition = new Vector2(0, 300); rp.sizeDelta = new Vector2(-200, 60);
        textoPensamiento.fontStyle = FontStyles.Italic;
        textoPensamiento.color = new Color(1, 1, 1, 0);
    }

    // parpado: negro, con el borde inferior en arco (almendra) y difuminado
    static Texture2D TexturaParpado()
    {
        int w = 512, h = 512;
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (w - 1f) * 2f - 1f, v = y / (h - 1f);
                float borde = Curva * (1f - u * u);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(borde - 0.035f, borde + 0.035f, v));
                px[y * w + x] = new Color32(0, 0, 0, (byte)(a * 255));
            }
        t.SetPixels32(px); t.Apply();
        return t;
    }

    void CrearAudio()
    {
        fuenteZumbido = gameObject.AddComponent<AudioSource>();
        fuenteZumbido.clip = zumbido; fuenteZumbido.loop = true; fuenteZumbido.volume = 0f; fuenteZumbido.ignoreListenerPause = true; fuenteZumbido.playOnAwake = false;
        fuenteFx = gameObject.AddComponent<AudioSource>();
        fuenteFx.ignoreListenerPause = true; fuenteFx.playOnAwake = false;
    }

    void CrearPost()
    {
        var go = new GameObject("VolumenIntro");
        go.transform.SetParent(transform, false);
        vol = go.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 100; vol.weight = 1f;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        dof = p.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(0f); dof.gaussianEnd.Override(0.5f); dof.gaussianMaxRadius.Override(1.5f); dof.highQualitySampling.Override(true);
        color = p.Add<ColorAdjustments>(true);
        color.saturation.Override(-75f); color.postExposure.Override(-0.6f);
        vineta = p.Add<Vignette>(true);
        vineta.intensity.Override(0.55f); vineta.smoothness.Override(0.9f); vineta.color.Override(Color.black);
        vol.sharedProfile = p;
    }

    // ------------------------------------------------------------------ secuencia
    void Fx(AudioClip c, float vol, float tono = 1f) { if (c == null) return; fuenteFx.pitch = tono; fuenteFx.PlayOneShot(c, vol * Ajustes.VolEfectos); }

    IEnumerator Esperar(float s) { for (float t = 0f; t < s && !saltar; t += Time.unscaledDeltaTime) yield return null; }

    IEnumerator Secuencia()
    {
        yield return Esperar(1.2f);
        // 1. la maquina se enciende
        Fx(encendido, 0.8f);
        StartCoroutine(Destellos(1.4f, 0.22f));
        yield return Esperar(1.3f);
        if (zumbido != null) { fuenteZumbido.pitch = 0.75f; fuenteZumbido.Play(); }
        float t0 = Time.unscaledTime;
        // 2. el zumbido crece y sube de tono
        while (Time.unscaledTime - t0 < 4.2f && !saltar)
        {
            float u = (Time.unscaledTime - t0) / 4.2f;
            fuenteZumbido.volume = Mathf.Lerp(0f, 0.7f, Mathf.SmoothStep(0f, 1f, u * 1.6f)) * Ajustes.VolEfectos;
            fuenteZumbido.pitch = Mathf.Lerp(0.75f, 1.15f, u);
            Brillo(0.08f + 0.12f * u + 0.05f * Mathf.Sin(Time.unscaledTime * 22f));
            if (u > 0.55f && u - Time.unscaledDeltaTime / 4.2f <= 0.55f) Fx(carga, 0.75f);
            yield return null;
        }
        // 3. algo se encoge
        Fx(encoger, 1f);
        t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 2.6f && !saltar)
        {
            float u = (Time.unscaledTime - t0) / 2.6f;
            fuenteZumbido.pitch = Mathf.Lerp(1.15f, 0.45f, u * u);
            fuenteZumbido.volume = Mathf.Lerp(0.85f, 0.5f, u) * Ajustes.VolEfectos;
            Brillo(Mathf.Lerp(0.45f, 0.05f, u) * (0.8f + 0.2f * Mathf.Sin(Time.unscaledTime * 40f)));
            yield return null;
        }
        // 4. la maquina se apaga
        Fx(apagado, 0.7f);
        t0 = Time.unscaledTime;
        float v0 = fuenteZumbido.volume;
        while (Time.unscaledTime - t0 < 1.6f && !saltar)
        {
            float u = (Time.unscaledTime - t0) / 1.6f;
            fuenteZumbido.volume = v0 * (1f - u);
            fuenteZumbido.pitch = Mathf.Lerp(0.45f, 0.25f, u);
            Brillo(0.05f * (1f - u));
            yield return null;
        }
        fuenteZumbido.Stop();
        Brillo(0f);
        // 5. silencio... latido, respiracion
        yield return Esperar(1.6f);
        Fx(latido, 0.8f);
        yield return Esperar(1.7f);
        Fx(respiro, 0.7f);
        yield return Esperar(0.6f);

        // 6. abrir los ojos
        AudioListener.pause = false;
        negro.color = new Color(0, 0, 0, 0);
        yield return Parpadear(0f, 0.18f, 1.3f);
        yield return Parpadear(0.18f, 0f, 0.35f);
        yield return Esperar(0.25f);
        yield return Parpadear(0f, 0.55f, 1.4f);
        yield return Parpadear(0.55f, 0.3f, 0.3f);
        yield return Esperar(0.15f);
        yield return Parpadear(0.3f, 1f, 2.2f);
        Terminar();
        // un pensamiento como pista de lo que paso
        if (Ajustes.Subtitulos && pensamientos != null)
            foreach (var p in pensamientos) yield return Pensar(p);
        Destroy(gameObject, 0.5f);
    }

    IEnumerator Parpadear(float de, float a, float dur)
    {
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / dur)
        {
            if (saltar) yield break;
            apertura = Mathf.Lerp(de, a, Mathf.SmoothStep(0f, 1f, t));
            Aplicar();
            yield return null;
        }
        apertura = a; Aplicar();
    }

    IEnumerator Destellos(float dur, float fuerza)
    {
        for (float t = 0f; t < dur && !saltar; t += Time.unscaledDeltaTime)
        {
            Brillo(Random.value < 0.25f ? fuerza * Random.Range(0.5f, 1f) : 0.02f);
            yield return new WaitForSecondsRealtime(0.05f);
        }
    }

    IEnumerator Pensar(string s)
    {
        textoPensamiento.text = s;
        for (float t = 0f; t < 3.4f; t += Time.unscaledDeltaTime)
        {
            float a = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((3.4f - t) / 0.8f);
            textoPensamiento.color = new Color(0.9f, 0.95f, 0.95f, a);
            yield return null;
        }
        textoPensamiento.color = new Color(1, 1, 1, 0);
        yield return new WaitForSecondsRealtime(0.4f);
    }

    void Brillo(float a) { brillo.color = new Color(0.35f, 0.04f, 0.02f, Mathf.Clamp01(a)); }

    // posicion de los parpados y del post-proceso segun la apertura
    void Aplicar()
    {
        float alto = 1080f;
        float cerrado = -Curva * AltoParpado;            // el arco tapa el centro
        float abierto = alto * 0.5f + 40f;
        float y = Mathf.Lerp(cerrado, abierto, apertura);
        rtArriba.anchoredPosition = new Vector2(0f, y);
        rtAbajo.anchoredPosition = new Vector2(0f, -y);
        // vista borrosa, oscura y sin color hasta despertar del todo
        float k = Mathf.SmoothStep(0f, 1f, apertura);
        dof.gaussianEnd.Override(Mathf.Lerp(0.3f, 60f, k * k));
        dof.gaussianMaxRadius.Override(Mathf.Lerp(1.5f, 0.5f, k));
        color.saturation.Override(Mathf.Lerp(-75f, 0f, k));
        color.postExposure.Override(Mathf.Lerp(-0.6f, 0f, k));
        vineta.intensity.Override(Mathf.Lerp(0.55f, 0.15f, k));
        vol.weight = apertura >= 0.999f ? 0f : 1f;
    }

    void Update()
    {
        if (!Activa) return;
        // mantener ENTER/ESC para saltar
        bool tecla = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.KeypadEnter);
        tSaltar = tecla ? tSaltar + Time.unscaledDeltaTime : 0f;
        float aviso = Time.timeSinceLevelLoad > 2f ? 0.45f : 0f;
        textoSaltar.color = new Color(1, 1, 1, Mathf.MoveTowards(textoSaltar.color.a, tecla ? 0.9f : aviso, Time.unscaledDeltaTime * 2f));
        if (tSaltar > 0.9f && !saltar)
        {
            saltar = true;
            StopAllCoroutines();
            StartCoroutine(Saltar());
        }
    }

    IEnumerator Saltar()
    {
        AudioListener.pause = false;
        if (fuenteZumbido != null) fuenteZumbido.Stop();
        Brillo(0f);
        negro.color = new Color(0, 0, 0, 0);
        float de = apertura;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f)
        {
            apertura = Mathf.Lerp(de, 1f, Mathf.SmoothStep(0f, 1f, t)); Aplicar();
            yield return null;
        }
        apertura = 1f; Aplicar();
        Terminar();
        Destroy(gameObject, 0.2f);
    }

    void Terminar()
    {
        if (!Activa) return;
        Activa = false;
        Controles.Bloqueado = false;
        if (camaraInput != null) foreach (var mb in camaraInput) if (mb != null) mb.enabled = true;
        textoSaltar.gameObject.SetActive(false);
        parpadoArriba.enabled = parpadoAbajo.enabled = false;
        vol.weight = 0f;
    }

    void OnDestroy()
    {
        if (Activa) { Activa = false; Controles.Bloqueado = false; AudioListener.pause = false; }
    }

    // ------------------------------------------------------------------ utilidades
    static Image Img(string n, RectTransform padre, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        var i = go.AddComponent<Image>(); i.color = c; i.raycastTarget = false;
        return i;
    }

    static void Estirar(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    TextMeshProUGUI Txt(string n, RectTransform padre, string s, float tam, TextAlignmentOptions al)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(padre, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (fuente != null) t.font = fuente;
        t.text = s; t.fontSize = tam; t.alignment = al; t.raycastTarget = false;
        return t;
    }
}
