using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menu de Beyond Small, al estilo de los menus de Portal: lista de opciones a la izquierda,
/// recuadro gris en la opcion elegida y, de fondo, capturas del juego que se van cambiando.
/// Se maneja con teclado (flechas / WASD, Enter, Esc) o con el raton, y suena al moverse.
/// Modo Principal: escena del menu. Modo Pausa: dentro del juego, se abre con Esc.
/// Toda la interfaz se construye por codigo a partir de MenuRecursos.
/// </summary>
public class MenuUI : MonoBehaviour
{
    public enum Modo { Principal, Pausa }

    public MenuRecursos recursos;
    public Modo modo = Modo.Principal;

    // ================================================================== modelo de filas
    enum TipoFila { Boton, Deslizador, Opcion, Tecla }

    class Fila
    {
        public TipoFila tipo;
        public string texto;
        public System.Action accion;
        // deslizador
        public System.Func<float> leer; public System.Action<float> escribir;
        public float min, max, paso; public System.Func<float, string> formato;
        // opcion
        public System.Func<string[]> valores; public System.Func<int> leerIndice; public System.Action<int> escribirIndice;
        // tecla
        public Controles.Accion accionTecla;
        // vista
        public RectTransform rt;
        public Image fondo, marca, barra, relleno;
        public TextMeshProUGUI etiqueta, valor;
        public RectTransform rtBarra;
    }

    class Pantalla
    {
        public string titulo;
        public List<Fila> filas = new List<Fila>();
        public GameObject raiz;
        public RectTransform contenido;
        public float altoVisible;
        public bool esLista;            // lista principal (sin panel) al estilo Portal
        public int pestana = -1;        // pestaña de opciones (-1 = no es de opciones)
        public System.Action alVolver;
        public int indice;
        public TextMeshProUGUI descripcion;      // texto que cambia segun la fila elegida (dificultad)
        public string[] descripciones;
    }

    readonly List<Pantalla> pila = new List<Pantalla>();
    Pantalla Actual => pila.Count > 0 ? pila[pila.Count - 1] : null;

    // ================================================================== estado
    Canvas canvas;
    RectTransform raizUI;
    Image fondoA, fondoB, fundido, oscurecer;
    AspectRatioFitter ajusteA, ajusteB;
    TextMeshProUGUI lugar, textoFps, ayuda;
    CanvasGroup grupoMenu;
    AudioSource audioUI, musica;
    readonly List<RectTransform> esporas = new List<RectTransform>();
    readonly List<Vector4> esporasDatos = new List<Vector4>();
    int fondoActual = -1;
    float tFondo;
    int[] ordenFondos;
    bool abierto, ocupado, esperandoTecla;
    Fila filaTecla;
    float repetir; KeyCode teclaRepetida;
    float fpsAcum; int fpsCuadros; float fpsTimer;
    Pantalla[] pestanas;
    readonly string[] nombresPestanas = { "AUDIO", "VIDEO", "CONTROLES", "JUEGO" };
    RectTransform barraPestanas;
    readonly List<TextMeshProUGUI> textosPestanas = new List<TextMeshProUGUI>();
    Sprite blanco, degradadoIzq, punto;

    float DT => Time.unscaledDeltaTime;

    // ================================================================== ciclo de vida
    void Awake()
    {
        if (recursos == null) recursos = Resources.Load<MenuRecursos>("MenuRecursos");
        Ajustes.Cargar();
        CrearSprites();
        CrearAudio();
        CrearCanvas();
        AsegurarEventSystem();
        if (modo == Modo.Principal)
        {
            CrearFondos();
            AbrirPrincipal();
            abierto = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Time.timeScale = 1f; AudioListener.pause = false;
            StartCoroutine(Fundido(1f, 0f, 1.4f));
            if (musica.clip != null) { musica.volume = 0f; musica.Play(); }
        }
        else
        {
            grupoMenu.alpha = 0f;
            grupoMenu.gameObject.SetActive(false);
            fundido.color = new Color(0, 0, 0, 0);
        }
        Ajustes.Cambiado += AlCambiarAjustes;
    }

    void OnDestroy() { Ajustes.Cambiado -= AlCambiarAjustes; }

    void AlCambiarAjustes()
    {
        if (audioUI != null) audioUI.volume = Ajustes.VolEfectos;
    }

    // ================================================================== construccion base
    void CrearSprites()
    {
        var t = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; t.SetPixels(px); t.Apply();
        blanco = Sprite.Create(t, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
        var g = new Texture2D(256, 1) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < 256; x++) { float u = x / 255f; g.SetPixel(x, 0, new Color(0, 0, 0, Mathf.Pow(1f - u, 1.6f))); }
        g.Apply();
        degradadoIzq = Sprite.Create(g, new Rect(0, 0, 256, 1), Vector2.one * 0.5f);
        var p = new Texture2D(32, 32) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++) { float d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) / 16f; p.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d))); }
        p.Apply();
        punto = Sprite.Create(p, new Rect(0, 0, 32, 32), Vector2.one * 0.5f);
    }

    void CrearAudio()
    {
        audioUI = gameObject.AddComponent<AudioSource>();
        audioUI.playOnAwake = false; audioUI.ignoreListenerPause = true; audioUI.spatialBlend = 0f; audioUI.volume = Ajustes.VolEfectos;
        musica = gameObject.AddComponent<AudioSource>();
        musica.playOnAwake = false; musica.loop = true; musica.spatialBlend = 0f; musica.ignoreListenerPause = true;
        musica.clip = modo == Modo.Principal ? recursos.musica : null;
    }

    void Sonar(AudioClip c, float tono = 1f, float vol = 1f)
    {
        if (c == null) return;
        audioUI.pitch = tono;
        audioUI.PlayOneShot(c, vol);
    }

    void CrearCanvas()
    {
        var go = new GameObject("CanvasMenu");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = modo == Modo.Pausa ? 900 : 100;
        var cs = go.AddComponent<CanvasScaler>();
        cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        cs.referenceResolution = new Vector2(1920, 1080);
        cs.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        raizUI = (RectTransform)go.transform;

        var gm = Rect("Menu", raizUI); Estirar(gm);
        grupoMenu = gm.gameObject.AddComponent<CanvasGroup>();

        // fps (siempre encima)
        textoFps = Texto("Fps", raizUI, "", 22, TextAlignmentOptions.TopRight, recursos.fuenteTexto);
        var rf = textoFps.rectTransform; rf.anchorMin = rf.anchorMax = new Vector2(1, 1); rf.pivot = new Vector2(1, 1);
        rf.anchoredPosition = new Vector2(-24, -18); rf.sizeDelta = new Vector2(200, 40);
        textoFps.color = new Color(1, 1, 1, 0.75f);

        fundido = Imagen("Fundido", raizUI, Color.black); Estirar(fundido.rectTransform);
        fundido.raycastTarget = false;
    }

    void AsegurarEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
        go.AddComponent<StandaloneInputModule>();
#endif
    }

    void CrearFondos()
    {
        var capa = Rect("Fondo", (RectTransform)grupoMenu.transform); Estirar(capa);
        capa.SetAsFirstSibling();
        fondoA = Imagen("FondoA", capa, Color.white); ajusteA = Envolver(fondoA);
        fondoB = Imagen("FondoB", capa, new Color(1, 1, 1, 0)); ajusteB = Envolver(fondoB);
        // oscurecido a la izquierda (detras del menu) y viñeta abajo/arriba
        var izq = Imagen("Degradado", capa, new Color(0, 0, 0, 0.92f)); izq.sprite = degradadoIzq;
        var ri = izq.rectTransform; ri.anchorMin = new Vector2(0, 0); ri.anchorMax = new Vector2(0.72f, 1); ri.offsetMin = ri.offsetMax = Vector2.zero;
        var tinte = Imagen("Tinte", capa, new Color(0.02f, 0.05f, 0.07f, 0.28f)); Estirar(tinte.rectTransform);
        foreach (var arriba in new[] { true, false })
        {
            var v = Imagen(arriba ? "VinetaArriba" : "VinetaAbajo", capa, new Color(0, 0, 0, 0.75f)); v.sprite = degradadoIzq;
            var rv = v.rectTransform; rv.anchorMin = new Vector2(0.5f, arriba ? 1 : 0); rv.anchorMax = rv.anchorMin; rv.pivot = new Vector2(0, 0.5f);
            rv.sizeDelta = new Vector2(260, 2400); rv.localEulerAngles = new Vector3(0, 0, arriba ? -90 : 90);
            rv.anchoredPosition = Vector2.zero;
        }
        // esporas flotando (polvo/esporas brillantes, como en el hormiguero)
        for (int i = 0; i < 46; i++)
        {
            var e = Imagen("Espora", capa, new Color(recursos.acento.r, recursos.acento.g, recursos.acento.b, 0.5f));
            e.sprite = punto; e.raycastTarget = false;
            float s = Random.Range(4f, 13f);
            e.rectTransform.sizeDelta = Vector2.one * s;
            e.rectTransform.anchorMin = e.rectTransform.anchorMax = Vector2.zero;
            e.rectTransform.anchoredPosition = new Vector2(Random.Range(0f, 1920f), Random.Range(0f, 1080f));
            esporas.Add(e.rectTransform);
            esporasDatos.Add(new Vector4(Random.Range(8f, 26f), Random.value * 10f, Random.Range(0.15f, 0.6f), s));
        }
        lugar = Texto("Lugar", capa, "", 22, TextAlignmentOptions.BottomRight, recursos.fuenteTexto);
        var rl = lugar.rectTransform; rl.anchorMin = rl.anchorMax = new Vector2(1, 0); rl.pivot = new Vector2(1, 0);
        rl.anchoredPosition = new Vector2(-40, 34); rl.sizeDelta = new Vector2(700, 40);
        lugar.color = new Color(1, 1, 1, 0f); lugar.characterSpacing = 6;

        int n = recursos.fondos != null ? recursos.fondos.Length : 0;
        ordenFondos = new int[n];
        for (int i = 0; i < n; i++) ordenFondos[i] = i;
        for (int i = n - 1; i > 0; i--) { int j = Random.Range(0, i + 1); int t = ordenFondos[i]; ordenFondos[i] = ordenFondos[j]; ordenFondos[j] = t; }
        if (n > 0) { fondoActual = 0; PonerFondo(fondoA, ajusteA, ordenFondos[0]); }
    }

    AspectRatioFitter Envolver(Image img)
    {
        Estirar(img.rectTransform);
        img.raycastTarget = false;
        var a = img.gameObject.AddComponent<AspectRatioFitter>();
        a.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent; a.aspectRatio = 16f / 9f;
        return a;
    }

    void PonerFondo(Image img, AspectRatioFitter a, int i)
    {
        var s = recursos.fondos[i];
        img.sprite = s;
        a.aspectRatio = s.rect.width / Mathf.Max(1f, s.rect.height);
        if (lugar != null)
            lugar.text = recursos.nombresFondos != null && i < recursos.nombresFondos.Length ? recursos.nombresFondos[i].ToUpperInvariant() : "";
    }

    // ================================================================== pantallas
    void AbrirPrincipal()
    {
        var p = new Pantalla { titulo = "", esLista = true };
        if (modo == Modo.Principal)
        {
            p.filas.Add(Boton("JUGAR", AbrirPartidas));
            p.filas.Add(Boton("OPCIONES", () => AbrirOpciones(0)));
            p.filas.Add(Boton("CONTROLES", () => AbrirOpciones(2)));
            p.filas.Add(Boton("CREDITOS", AbrirCreditos));
            p.filas.Add(Boton("SALIR", () => Confirmar("¿Salir del juego?", Salir)));
            p.alVolver = () => Confirmar("¿Salir del juego?", Salir);
        }
        else
        {
            p.filas.Add(Boton("CONTINUAR", CerrarPausa));
            p.filas.Add(Boton("GUARDAR PARTIDA", GuardarDesdePausa));
            p.filas.Add(Boton("OPCIONES", () => AbrirOpciones(0)));
            p.filas.Add(Boton("CONTROLES", () => AbrirOpciones(2)));
            p.filas.Add(Boton("MENU PRINCIPAL", () => Confirmar("¿Volver al menu principal?\n<size=70%><color=#9aa>" + TextoAlSalir() + "</color></size>", IrAlMenu)));
            p.filas.Add(Boton("SALIR DEL JUEGO", () => Confirmar("¿Salir del juego?\n<size=70%><color=#9aa>" + TextoAlSalir() + "</color></size>", () => { GuardarAlSalir(); Salir(); })));
            p.alVolver = CerrarPausa;
        }
        ConstruirLista(p);
        Empujar(p);
    }

    void ConstruirLista(Pantalla p)
    {
        var raiz = Rect("Principal", (RectTransform)grupoMenu.transform); Estirar(raiz);
        p.raiz = raiz.gameObject;
        if (modo == Modo.Pausa)
        {
            var os = Imagen("Oscurecer", raiz, new Color(0.01f, 0.02f, 0.03f, 0.72f)); Estirar(os.rectTransform);
            var izq = Imagen("Degradado", raiz, new Color(0, 0, 0, 0.7f)); izq.sprite = degradadoIzq;
            var ri = izq.rectTransform; ri.anchorMin = Vector2.zero; ri.anchorMax = new Vector2(0.6f, 1); ri.offsetMin = ri.offsetMax = Vector2.zero;
            var tp = Texto("Pausa", raiz, "PAUSA", 26, TextAlignmentOptions.TopLeft, recursos.fuenteTexto);
            var rtp = tp.rectTransform; rtp.anchorMin = rtp.anchorMax = new Vector2(0, 1); rtp.pivot = new Vector2(0, 1);
            rtp.anchoredPosition = new Vector2(128, -292); rtp.sizeDelta = new Vector2(400, 40); tp.characterSpacing = 18; tp.color = recursos.acento;
        }
        // logo
        if (recursos.logo != null)
        {
            var lg = Imagen("Logo", raiz, Color.white); lg.sprite = recursos.logo; lg.preserveAspect = true; lg.raycastTarget = false;
            var r = lg.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            float w = modo == Modo.Pausa ? 520f : 720f;
            r.sizeDelta = new Vector2(w, w * recursos.logo.rect.height / recursos.logo.rect.width);
            r.anchoredPosition = new Vector2(112, modo == Modo.Pausa ? -110 : -130);
        }
        // opciones
        var lista = Rect("Lista", raiz);
        lista.anchorMin = lista.anchorMax = new Vector2(0, 1); lista.pivot = new Vector2(0, 1);
        lista.anchoredPosition = new Vector2(112, modo == Modo.Pausa ? -360 : -560); lista.sizeDelta = new Vector2(520, 400);
        for (int i = 0; i < p.filas.Count; i++)
        {
            var f = p.filas[i];
            f.rt = Rect("Fila_" + f.texto, lista);
            f.rt.anchorMin = f.rt.anchorMax = new Vector2(0, 1); f.rt.pivot = new Vector2(0, 1);
            f.rt.anchoredPosition = new Vector2(0, -i * 52f); f.rt.sizeDelta = new Vector2(460, 46);
            f.fondo = Imagen("Seleccion", f.rt, new Color(0.62f, 0.64f, 0.66f, 0f)); Estirar(f.fondo.rectTransform);
            f.marca = Imagen("Marca", f.rt, recursos.acento);
            var rm = f.marca.rectTransform; rm.anchorMin = new Vector2(0, 0); rm.anchorMax = new Vector2(0, 1); rm.pivot = new Vector2(0, 0.5f);
            rm.sizeDelta = new Vector2(4, 0); rm.anchoredPosition = Vector2.zero; f.marca.color = new Color(recursos.acento.r, recursos.acento.g, recursos.acento.b, 0f);
            f.etiqueta = Texto("Texto", f.rt, f.texto, 34, TextAlignmentOptions.MidlineLeft, recursos.fuenteMenu);
            var re = f.etiqueta.rectTransform; Estirar(re); re.offsetMin = new Vector2(16, 0);
            f.etiqueta.characterSpacing = 2;
            Raton(f, p);
        }
        // pie: version y ayuda
        var pie = Texto("Pie", raiz, "<color=#46dcc8>—</color>  Un mundo enorme te espera", 22, TextAlignmentOptions.BottomLeft, recursos.fuenteTexto);
        var rp = pie.rectTransform; rp.anchorMin = rp.anchorMax = Vector2.zero; rp.pivot = Vector2.zero; rp.anchoredPosition = new Vector2(128, 34); rp.sizeDelta = new Vector2(700, 40);
        pie.color = new Color(1, 1, 1, 0.65f);
        if (modo == Modo.Pausa) pie.text = "";
        ayuda = Texto("Ayuda", raiz, Ayuda(false), 20, TextAlignmentOptions.BottomLeft, recursos.fuenteTexto);
        var ra = ayuda.rectTransform; ra.anchorMin = ra.anchorMax = Vector2.zero; ra.pivot = Vector2.zero; ra.anchoredPosition = new Vector2(128, 72); ra.sizeDelta = new Vector2(900, 34);
        ayuda.color = new Color(1, 1, 1, 0.4f);
    }

    string Ayuda(bool opciones)
    {
        return opciones ? "<color=#46dcc8>W S</color>  Navegar      <color=#46dcc8>A D</color>  Cambiar      <color=#46dcc8>ENTER</color>  Elegir      <color=#46dcc8>Q E</color>  Pestaña      <color=#46dcc8>ESC</color>  Volver"
                        : "<color=#46dcc8>W S</color>  Navegar      <color=#46dcc8>ENTER</color>  Elegir      <color=#46dcc8>ESC</color>  " + (modo == Modo.Pausa ? "Continuar" : "Salir");
    }

    // ---------------------------------------------------------------- opciones
    void AbrirOpciones(int pestana)
    {
        Sonar(recursos.sonidoAceptar);
        pestanas = new Pantalla[4];
        for (int i = 0; i < 4; i++) { pestanas[i] = CrearPestana(i); pestanas[i].raiz.SetActive(false); }
        var p = pestanas[Mathf.Clamp(pestana, 0, 3)];
        Empujar(p);
    }

    Pantalla CrearPestana(int i)
    {
        var p = new Pantalla { titulo = "OPCIONES", pestana = i };
        switch (i)
        {
            case 0:
                p.filas.Add(Deslizador("Volumen general", () => Ajustes.VolGeneral, v => Ajustes.VolGeneral = v, 0, 1, 0.05f, Porcentaje));
                p.filas.Add(Deslizador("Musica", () => Ajustes.VolMusica, v => Ajustes.VolMusica = v, 0, 1, 0.05f, Porcentaje));
                p.filas.Add(Deslizador("Efectos", () => Ajustes.VolEfectos, v => Ajustes.VolEfectos = v, 0, 1, 0.05f, Porcentaje));
                p.filas.Add(Deslizador("Ambiente", () => Ajustes.VolAmbiente, v => Ajustes.VolAmbiente = v, 0, 1, 0.05f, Porcentaje));
                p.filas.Add(Opcion("Subtitulos", () => SiNo, () => Ajustes.Subtitulos ? 0 : 1, k => Ajustes.Subtitulos = k == 0));
                break;
            case 1:
                p.filas.Add(Opcion("Resolucion", () => Ajustes.Resoluciones().ConvertAll(r => r.width + " x " + r.height).ToArray(), Ajustes.ResolucionActual, k => Ajustes.Resolucion = k));
                p.filas.Add(Opcion("Pantalla completa", () => SiNo, () => Ajustes.PantallaCompleta ? 0 : 1, k => Ajustes.PantallaCompleta = k == 0));
                p.filas.Add(Opcion("Calidad", () => QualitySettings.names, () => Mathf.Clamp(Ajustes.Calidad, 0, QualitySettings.names.Length - 1), k => Ajustes.Calidad = k));
                p.filas.Add(Opcion("Sincronizacion vertical", () => SiNo, () => Ajustes.VSync ? 0 : 1, k => Ajustes.VSync = k == 0));
                p.filas.Add(Opcion("Limite de FPS", () => new[] { "Sin limite", "30", "60", "120", "144" }, () => System.Array.IndexOf(new[] { 0, 30, 60, 120, 144 }, Ajustes.LimiteFps) is int x && x >= 0 ? x : 0, k => Ajustes.LimiteFps = new[] { 0, 30, 60, 120, 144 }[k]));
                p.filas.Add(Deslizador("Brillo", () => Ajustes.Brillo, v => Ajustes.Brillo = v, -1, 1, 0.05f, v => (v >= 0 ? "+" : "") + Mathf.RoundToInt(v * 100)));
                p.filas.Add(Opcion("Mostrar FPS", () => SiNo, () => Ajustes.MostrarFps ? 0 : 1, k => Ajustes.MostrarFps = k == 0));
                break;
            case 2:
                foreach (Controles.Accion a in System.Enum.GetValues(typeof(Controles.Accion)))
                    p.filas.Add(new Fila { tipo = TipoFila.Tecla, texto = Controles.Nombres[a], accionTecla = a });
                p.filas.Add(Boton("Restablecer controles", () => { Controles.Restablecer(); RefrescarValores(Actual); Sonar(recursos.sonidoAceptar); }));
                break;
            case 3:
                p.filas.Add(Deslizador("Sensibilidad de la camara", () => Ajustes.Sensibilidad, v => Ajustes.Sensibilidad = v, 0.2f, 3f, 0.1f, v => v.ToString("0.0") + "x"));
                p.filas.Add(Opcion("Invertir eje vertical", () => SiNo, () => Ajustes.InvertirY ? 0 : 1, k => Ajustes.InvertirY = k == 0));
                p.filas.Add(Opcion("Autoguardado", () => NombresAutoguardado, () => System.Array.IndexOf(MinutosAutoguardado, Ajustes.AutoguardadoMin) is int x && x >= 0 ? x : 5, k => Ajustes.AutoguardadoMin = MinutosAutoguardado[k]));
                p.filas.Add(Boton("Restablecer opciones", () => { Ajustes.Restablecer(); RefrescarValores(Actual); Sonar(recursos.sonidoAceptar); }));
                break;
        }
        p.alVolver = () => { Ajustes.Guardar(); };
        ConstruirPanel(p, 980, 700, true);
        return p;
    }

    static readonly string[] SiNo = { "Si", "No" };
    static readonly int[] MinutosAutoguardado = { 0, 1, 2, 3, 5, 10, 15, 20, 30 };
    static readonly string[] NombresAutoguardado = { "Desactivado", "Cada minuto", "Cada 2 min", "Cada 3 min", "Cada 5 min", "Cada 10 min", "Cada 15 min", "Cada 20 min", "Cada 30 min" };
    static string Porcentaje(float v) { return Mathf.RoundToInt(v * 100) + "%"; }

    void CambiarPestana(int d)
    {
        var p = Actual;
        if (p == null || p.pestana < 0) return;
        int n = (p.pestana + d + 4) % 4;
        Sonar(recursos.sonidoMover, 0.9f);
        p.raiz.SetActive(false);
        pila[pila.Count - 1] = pestanas[n];
        pestanas[n].raiz.SetActive(true);
        pestanas[n].indice = 0;
        Resaltar(pestanas[n], true);
    }

    // ---------------------------------------------------------------- partidas (ranuras y dificultad)
    const float AltoRanura = 190f, SepRanura = 14f;

    void AbrirPartidas()
    {
        Sonar(recursos.sonidoAceptar);
        var p = new Pantalla { titulo = "PARTIDAS" };
        for (int i = 0; i < Partida.Ranuras; i++) { int r = i; p.filas.Add(Boton("Partida " + (i + 1), () => ElegirRanura(r))); }
        p.filas.Add(Boton("Volver", () => Atras()));
        ConstruirPanel(p, 1240, 880, false);
        float y = 0f;
        for (int i = 0; i < p.filas.Count; i++)
        {
            var f = p.filas[i];
            bool ranura = i < Partida.Ranuras;
            f.rt.anchoredPosition = new Vector2(0, -y);
            f.rt.sizeDelta = new Vector2(0, ranura ? AltoRanura : 50f);
            if (ranura) DecorarRanura(f, i);
            y += ranura ? AltoRanura + SepRanura : 54f;
        }
        p.contenido.sizeDelta = new Vector2(0, y);
        p.altoVisible = 99999f;     // todo cabe: sin desplazamiento
        Empujar(p);
    }

    void DecorarRanura(Fila f, int r)
    {
        var d = Partida.Leer(r);
        float altoMini = AltoRanura - 28f, anchoMini = altoMini * 16f / 9f;
        var marco = Imagen("Marco", f.rt, new Color(0f, 0f, 0f, 0.55f));
        var rm = marco.rectTransform; rm.anchorMin = rm.anchorMax = new Vector2(0, 0.5f); rm.pivot = new Vector2(0, 0.5f);
        rm.anchoredPosition = new Vector2(22, 0); rm.sizeDelta = new Vector2(anchoMini, altoMini);
        var ri = new GameObject("Miniatura", typeof(RectTransform)).AddComponent<RawImage>();
        ri.transform.SetParent(rm, false); ri.raycastTarget = false;
        var rr = ri.rectTransform; Estirar(rr); rr.offsetMin = new Vector2(2, 2); rr.offsetMax = new Vector2(-2, -2);
        var tex = d != null ? Partida.Miniatura(r) : null;
        if (tex != null) ri.texture = tex;
        else
        {
            ri.color = new Color(0.08f, 0.1f, 0.12f);
            var v = Texto("Vacia", rm, d != null ? "SIN IMAGEN" : "+", d != null ? 22 : 64, TextAlignmentOptions.Center, recursos.fuenteMenu);
            Estirar(v.rectTransform); v.color = new Color(1, 1, 1, 0.25f);
        }
        float x0 = 22f + anchoMini + 30f;
        var dif = d != null ? (Dificultad)d.dificultad : Dificultad.Normal;
        f.etiqueta.text = d != null
            ? "PARTIDA " + (r + 1) + "   <size=70%><color=" + Partida.ColorHex(dif) + ">" + Partida.Nombre(dif).ToUpperInvariant() + "</color>" + (d.completada ? "   <color=#ffd070>COMPLETADA</color>" : "") + "</size>"
            : "PARTIDA " + (r + 1) + "   <size=70%><color=#ffffff66>VACIA</color></size>";
        f.etiqueta.fontSize = 36;
        var re = f.etiqueta.rectTransform; re.anchorMin = new Vector2(0, 1); re.anchorMax = new Vector2(1, 1); re.pivot = new Vector2(0, 1);
        re.offsetMin = new Vector2(x0, -66); re.offsetMax = new Vector2(-20, -16);
        string det = d != null
            ? "<color=#46dcc8>Tiempo jugado</color>   " + Partida.Tiempo(d.tiempo) +
              "\n<color=#46dcc8>Guardada</color>   " + d.fecha +
              (string.IsNullOrEmpty(d.lugar) ? "" : "\n<color=#46dcc8>Lugar</color>   " + d.lugar)
            : "Ranura libre. Eligela para empezar una partida nueva\ny escoger la dificultad.";
        var t = Texto("Detalle", f.rt, det, 25, TextAlignmentOptions.TopLeft, recursos.fuenteTexto);
        var rt = t.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(x0, 14); rt.offsetMax = new Vector2(-20, -70);
        t.color = new Color(0.85f, 0.88f, 0.88f, d != null ? 0.95f : 0.6f);
        t.lineSpacing = 8f;
    }

    void ElegirRanura(int r)
    {
        if (Partida.Existe(r)) AbrirRanura(r);
        else AbrirDificultad(r);
    }

    void AbrirRanura(int r)
    {
        Sonar(recursos.sonidoAceptar);
        var p = new Pantalla { titulo = "PARTIDA " + (r + 1) };
        p.filas.Add(Boton("Continuar", () =>
        {
            if (Partida.Continuar(r)) CargarJuego();
            else Confirmar("No se pudo leer esta partida.\n<size=70%><color=#9aa>¿Borrarla?</color></size>", () => { Partida.Borrar(r); RefrescarPartidas(); });
        }));
        p.filas.Add(Boton("Nueva partida", () => Confirmar("¿Empezar de nuevo en la Partida " + (r + 1) + "?\n<size=70%><color=#9aa>Se borrara lo que tenias guardado.</color></size>", () => AbrirDificultad(r))));
        p.filas.Add(Boton("Borrar partida", () => Confirmar("¿Borrar la Partida " + (r + 1) + "?\n<size=70%><color=#9aa>No se puede deshacer.</color></size>", () => { Partida.Borrar(r); RefrescarPartidas(); })));
        p.filas.Add(Boton("Volver", () => Atras()));
        ConstruirPanel(p, 720, 420, false);
        Empujar(p);
    }

    void AbrirDificultad(int r)
    {
        Sonar(recursos.sonidoAceptar);
        var p = new Pantalla { titulo = "DIFICULTAD" };
        var difs = new[] { Dificultad.Pacifico, Dificultad.Normal, Dificultad.Dificil, Dificultad.Extremo };
        var desc = new List<string>();
        foreach (var d in difs)
        {
            var dd = d;
            p.filas.Add(Boton(Partida.Nombre(d), () => { Partida.Nueva(r, dd); CargarJuego(); }));
            desc.Add("<color=" + Partida.ColorHex(d) + "><b>" + Partida.Nombre(d).ToUpperInvariant() + "</b></color>\n" + Partida.Descripcion(d));
        }
        p.filas.Add(Boton("Volver", () => Atras()));
        desc.Add("");
        ConstruirPanel(p, 980, 720, false);
        for (int i = 0; i < difs.Length; i++) p.filas[i].etiqueta.text = "<color=" + Partida.ColorHex(difs[i]) + ">" + Partida.Nombre(difs[i]).ToUpperInvariant() + "</color>";
        var caja = (RectTransform)p.raiz.transform.Find("Caja");
        var t = Texto("Descripcion", caja, "", 27, TextAlignmentOptions.TopLeft, recursos.fuenteTexto);
        var rt = t.rectTransform; rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0);
        rt.anchoredPosition = new Vector2(0, 66); rt.sizeDelta = new Vector2(-100, 190);
        t.color = new Color(0.86f, 0.9f, 0.9f); t.lineSpacing = 6f;
        var linea = Imagen("Linea", caja, new Color(1, 1, 1, 0.12f));
        var rl = linea.rectTransform; rl.anchorMin = new Vector2(0, 0); rl.anchorMax = new Vector2(1, 0); rl.pivot = new Vector2(0.5f, 0); rl.anchoredPosition = new Vector2(0, 268); rl.sizeDelta = new Vector2(-80, 2);
        p.descripcion = t; p.descripciones = desc.ToArray();
        p.indice = 1;      // normal
        Empujar(p);
    }

    /// <summary>Vuelve a construir la lista de partidas (despues de borrar una).</summary>
    void RefrescarPartidas()
    {
        while (pila.Count > 1) { var q = Actual; pila.RemoveAt(pila.Count - 1); if (q.raiz != null) Destroy(q.raiz); }
        AbrirPartidas();
    }

    void GuardarDesdePausa()
    {
        var g = GestorPartida.Instancia;
        if (g != null) g.GuardarManual();
    }

    static string TextoAlSalir()
    {
        if (Partida.RanuraActual < 0) return "Es una partida de prueba: no se guardara.";
        return "Se guardara la partida.";
    }

    static void GuardarAlSalir()
    {
        var g = GestorPartida.Instancia;
        if (g != null && !GestorPartida.Muerto) g.Guardar(false);
    }

    // ---------------------------------------------------------------- creditos y confirmacion
    void AbrirCreditos()
    {
        Sonar(recursos.sonidoAceptar);
        var p = new Pantalla { titulo = "CREDITOS" };
        p.filas.Add(Boton("Volver", () => Atras()));
        ConstruirPanel(p, 980, 760, false);
        var t = Texto("Texto", p.contenido,
            "<size=130%><b>BEYOND SMALL</b></size>\n<color=#9fb>Despiertas diminuto en un salon abandonado. ¿Que fue esa maquina?</color>\n\n" +
            "<color=#46dcc8>DESARROLLO</color>\nEquipo de Beyond Small — proyecto de Graficacion\n\n" +
            "<color=#46dcc8>MODELOS Y TEXTURAS</color>\nPoly Haven (CC0): plantas, rocas, troncos, texturas de suelo y tierra\n\n" +
            "<color=#46dcc8>SONIDOS</color>\nFreesound.org (CC0): animales, pasos, golpes, ambiente, cueva, maquina\n\n" +
            "<color=#46dcc8>TIPOGRAFIA</color>\nBarlow, por Jeremy Tribby (SIL Open Font License)\n\n" +
            "<color=#46dcc8>MOTOR</color>\nUnity 6 · Universal Render Pipeline · Cinemachine",
            26, TextAlignmentOptions.Top, recursos.fuenteTexto);
        var r = t.rectTransform; r.anchorMin = new Vector2(0, 0); r.anchorMax = new Vector2(1, 1); r.offsetMin = new Vector2(40, 80); r.offsetMax = new Vector2(-40, -20);
        t.color = new Color(0.88f, 0.9f, 0.9f);
        // el boton "Volver" va al fondo del panel
        var f = p.filas[0]; f.rt.anchorMin = f.rt.anchorMax = new Vector2(0.5f, 0); f.rt.pivot = new Vector2(0.5f, 0); f.rt.anchoredPosition = new Vector2(0, 10);
        Empujar(p);
    }

    void Confirmar(string pregunta, System.Action siAcepta)
    {
        Sonar(recursos.sonidoAceptar, 0.95f);
        var p = new Pantalla { titulo = "" };
        p.filas.Add(Boton("Si", () => { pila.Remove(p); Destroy(p.raiz); siAcepta(); }));
        p.filas.Add(Boton("No", () => Atras()));
        ConstruirPanel(p, 640, 330, false);
        var t = Texto("Pregunta", p.contenido.parent as RectTransform, pregunta, 34, TextAlignmentOptions.Center, recursos.fuenteTexto);
        var r = t.rectTransform; r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1); r.anchoredPosition = new Vector2(0, -40); r.sizeDelta = new Vector2(-60, 130);
        // botones en horizontal
        for (int i = 0; i < 2; i++)
        {
            var f = p.filas[i];
            f.rt.anchorMin = f.rt.anchorMax = new Vector2(0.5f, 0); f.rt.pivot = new Vector2(0.5f, 0);
            f.rt.sizeDelta = new Vector2(220, 52); f.rt.anchoredPosition = new Vector2(i == 0 ? -125 : 125, 0);
            f.etiqueta.alignment = TextAlignmentOptions.Center;
        }
        p.indice = 1;   // por seguridad, "No" seleccionado
        Empujar(p);
    }

    // ---------------------------------------------------------------- panel generico (opciones, creditos, dialogos)
    void ConstruirPanel(Pantalla p, float ancho, float alto, bool conPestanas)
    {
        var raiz = Rect("Panel_" + p.titulo, (RectTransform)grupoMenu.transform); Estirar(raiz);
        p.raiz = raiz.gameObject;
        var tapa = Imagen("Tapa", raiz, new Color(0, 0, 0, 0.55f)); Estirar(tapa.rectTransform);
        var caja = Imagen("Caja", raiz, new Color(0.06f, 0.075f, 0.085f, 0.94f));
        var rc = caja.rectTransform; rc.anchorMin = rc.anchorMax = new Vector2(0.5f, 0.5f); rc.sizeDelta = new Vector2(ancho, alto);
        var borde = Imagen("Borde", rc, recursos.acento); var rb = borde.rectTransform;
        rb.anchorMin = new Vector2(0, 1); rb.anchorMax = new Vector2(1, 1); rb.pivot = new Vector2(0.5f, 1); rb.sizeDelta = new Vector2(0, 3);
        float arriba = 30f;
        if (!string.IsNullOrEmpty(p.titulo))
        {
            var t = Texto("Titulo", rc, p.titulo, 40, TextAlignmentOptions.TopLeft, recursos.fuenteMenu);
            var rt = t.rectTransform; rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(40, -24); rt.sizeDelta = new Vector2(-80, 50); t.characterSpacing = 8;
            arriba = 90f;
        }
        if (conPestanas)
        {
            var barra = Rect("Pestanas", rc); barra.anchorMin = new Vector2(0, 1); barra.anchorMax = new Vector2(1, 1); barra.pivot = new Vector2(0, 1);
            barra.anchoredPosition = new Vector2(40, -arriba); barra.sizeDelta = new Vector2(-80, 44);
            float x = 0f;
            for (int i = 0; i < nombresPestanas.Length; i++)
            {
                int k = i;
                var tt = Texto("Pestana_" + i, barra, nombresPestanas[i], 26, TextAlignmentOptions.MidlineLeft, recursos.fuenteMenu);
                var r = tt.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0, 0.5f); r.pivot = new Vector2(0, 0.5f); r.anchoredPosition = new Vector2(x, 0); r.sizeDelta = new Vector2(190, 44);
                tt.characterSpacing = 6;
                bool sel = i == p.pestana;
                tt.color = sel ? recursos.acento : new Color(1, 1, 1, 0.45f);
                if (sel)
                {
                    var sub = Imagen("Subrayado", r, recursos.acento); var rs = sub.rectTransform;
                    rs.anchorMin = new Vector2(0, 0); rs.anchorMax = new Vector2(0, 0); rs.pivot = Vector2.zero; rs.sizeDelta = new Vector2(tt.preferredWidth, 3); rs.anchoredPosition = new Vector2(0, -2);
                }
                tt.raycastTarget = true;
                var cl = tt.gameObject.AddComponent<EventoRaton>();
                cl.alClic = () => { if (Actual == p && k != p.pestana) CambiarPestana(k - p.pestana); };
                x += 200f;
            }
            arriba += 60f;
        }
        // zona de filas con mascara (desplazable)
        var vista = Rect("Vista", rc);
        vista.anchorMin = new Vector2(0, 0); vista.anchorMax = new Vector2(1, 1);
        vista.offsetMin = new Vector2(40, conPestanas ? 70 : 30); vista.offsetMax = new Vector2(-40, -arriba);
        vista.gameObject.AddComponent<RectMask2D>();
        p.altoVisible = alto - arriba - (conPestanas ? 70 : 30);
        var cont = Rect("Contenido", vista); cont.anchorMin = new Vector2(0, 1); cont.anchorMax = new Vector2(1, 1); cont.pivot = new Vector2(0.5f, 1); cont.sizeDelta = new Vector2(0, 0);
        p.contenido = cont;
        for (int i = 0; i < p.filas.Count; i++)
        {
            var f = p.filas[i];
            f.rt = Rect("Fila_" + f.texto, cont);
            f.rt.anchorMin = new Vector2(0, 1); f.rt.anchorMax = new Vector2(1, 1); f.rt.pivot = new Vector2(0.5f, 1);
            f.rt.anchoredPosition = new Vector2(0, -i * 54f); f.rt.sizeDelta = new Vector2(0, 50);
            f.fondo = Imagen("Seleccion", f.rt, new Color(0.62f, 0.64f, 0.66f, 0f)); Estirar(f.fondo.rectTransform);
            f.marca = Imagen("Marca", f.rt, new Color(recursos.acento.r, recursos.acento.g, recursos.acento.b, 0f));
            var rm = f.marca.rectTransform; rm.anchorMin = new Vector2(0, 0); rm.anchorMax = new Vector2(0, 1); rm.pivot = new Vector2(0, 0.5f); rm.sizeDelta = new Vector2(4, 0);
            f.etiqueta = Texto("Texto", f.rt, f.tipo == TipoFila.Boton ? f.texto.ToUpperInvariant() : f.texto, f.tipo == TipoFila.Boton ? 30 : 28,
                TextAlignmentOptions.MidlineLeft, f.tipo == TipoFila.Boton ? recursos.fuenteMenu : recursos.fuenteTexto);
            var re = f.etiqueta.rectTransform; Estirar(re); re.offsetMin = new Vector2(18, 0); re.offsetMax = new Vector2(-18, 0);
            if (f.tipo != TipoFila.Boton)
            {
                f.valor = Texto("Valor", f.rt, "", 28, TextAlignmentOptions.MidlineRight, recursos.fuenteTexto);
                var rv = f.valor.rectTransform; Estirar(rv); rv.offsetMin = new Vector2(18, 0); rv.offsetMax = new Vector2(-18, 0);
                if (f.tipo == TipoFila.Deslizador)
                {
                    rv.offsetMax = new Vector2(-18, 0);
                    f.rtBarra = Rect("Barra", f.rt);
                    f.rtBarra.anchorMin = f.rtBarra.anchorMax = new Vector2(1, 0.5f); f.rtBarra.pivot = new Vector2(1, 0.5f);
                    f.rtBarra.anchoredPosition = new Vector2(-110, 0); f.rtBarra.sizeDelta = new Vector2(300, 8);
                    f.barra = Imagen("Fondo", f.rtBarra, new Color(1, 1, 1, 0.16f)); Estirar(f.barra.rectTransform);
                    f.relleno = Imagen("Relleno", f.rtBarra, recursos.acento);
                    var rr = f.relleno.rectTransform; rr.anchorMin = new Vector2(0, 0); rr.anchorMax = new Vector2(0, 1); rr.pivot = new Vector2(0, 0.5f); rr.sizeDelta = Vector2.zero;
                }
            }
            Raton(f, p);
        }
        float total = p.filas.Count * 54f;
        cont.sizeDelta = new Vector2(0, total);
        // pie de ayuda dentro del panel
        var ay = Texto("Ayuda", rc, conPestanas ? Ayuda(true) : "<color=#46dcc8>W S</color>  Navegar      <color=#46dcc8>ENTER</color>  Elegir      <color=#46dcc8>ESC</color>  Volver", 19, TextAlignmentOptions.Center, recursos.fuenteTexto);
        var ra = ay.rectTransform; ra.anchorMin = new Vector2(0, 0); ra.anchorMax = new Vector2(1, 0); ra.pivot = new Vector2(0.5f, 0); ra.anchoredPosition = new Vector2(0, 20); ra.sizeDelta = new Vector2(-40, 30);
        ay.color = new Color(1, 1, 1, 0.4f);
        if (!conPestanas && p.filas.Count <= 2) ay.gameObject.SetActive(false);
        RefrescarValores(p);
    }

    // ---------------------------------------------------------------- fabricas de filas
    static Fila Boton(string t, System.Action a) { return new Fila { tipo = TipoFila.Boton, texto = t, accion = a }; }

    static Fila Deslizador(string t, System.Func<float> l, System.Action<float> e, float min, float max, float paso, System.Func<float, string> fmt)
    {
        return new Fila { tipo = TipoFila.Deslizador, texto = t, leer = l, escribir = e, min = min, max = max, paso = paso, formato = fmt };
    }

    static Fila Opcion(string t, System.Func<string[]> v, System.Func<int> l, System.Action<int> e)
    {
        return new Fila { tipo = TipoFila.Opcion, texto = t, valores = v, leerIndice = l, escribirIndice = e };
    }

    void RefrescarValores(Pantalla p)
    {
        if (p == null) return;
        foreach (var f in p.filas)
        {
            if (f.valor == null) continue;
            switch (f.tipo)
            {
                case TipoFila.Deslizador:
                    float v = f.leer();
                    f.valor.text = f.formato(v);
                    f.relleno.rectTransform.sizeDelta = new Vector2(f.rtBarra.sizeDelta.x * Mathf.InverseLerp(f.min, f.max, v), 0);
                    break;
                case TipoFila.Opcion:
                    var vals = f.valores(); int k = Mathf.Clamp(f.leerIndice(), 0, Mathf.Max(0, vals.Length - 1));
                    f.valor.text = vals.Length > 0 ? "<color=#ffffff55>‹</color>   " + vals[k] + "   <color=#ffffff55>›</color>" : "-";
                    break;
                case TipoFila.Tecla:
                    f.valor.text = esperandoTecla && f == filaTecla ? "<color=#46dcc8>Presiona una tecla...</color>" : "[ " + Controles.NombreTecla(f.accionTecla) + " ]";
                    break;
            }
        }
    }

    // ================================================================== pila de pantallas
    void Empujar(Pantalla p)
    {
        if (Actual != null && !Actual.esLista) Actual.raiz.SetActive(false);
        pila.Add(p);
        p.raiz.SetActive(true);
        Resaltar(p, true);
        StartCoroutine(Aparecer(p.raiz));
    }

    void Atras()
    {
        var p = Actual;
        if (p == null) return;
        if (pila.Count == 1)
        {
            p.alVolver?.Invoke();
            return;
        }
        Sonar(recursos.sonidoAtras);
        p.alVolver?.Invoke();
        pila.RemoveAt(pila.Count - 1);
        // las pestañas de opciones se destruyen juntas
        if (p.pestana >= 0 && pestanas != null) { foreach (var q in pestanas) if (q != null) Destroy(q.raiz); pestanas = null; }
        else Destroy(p.raiz);
        Actual.raiz.SetActive(true);
        Resaltar(Actual, true);
    }

    IEnumerator Aparecer(GameObject go)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        var rt = (RectTransform)go.transform;
        for (float t = 0f; t < 1f; t += DT / 0.18f)
        {
            if (go == null) yield break;
            cg.alpha = t; rt.localScale = Vector3.one * Mathf.Lerp(0.985f, 1f, t);
            yield return null;
        }
        if (go != null) { cg.alpha = 1f; rt.localScale = Vector3.one; }
    }

    // ================================================================== seleccion y entrada
    void Resaltar(Pantalla p, bool inmediato) { Desplazar(p); }

    void Desplazar(Pantalla p)
    {
        if (p.contenido == null || p.altoVisible <= 0f) return;
        float y = p.indice * 54f;
        float actual = p.contenido.anchoredPosition.y;
        float min = y + 54f - p.altoVisible, max = y;
        float objetivo = Mathf.Clamp(actual, min, max);
        objetivo = Mathf.Clamp(objetivo, 0f, Mathf.Max(0f, p.filas.Count * 54f - p.altoVisible));
        p.contenido.anchoredPosition = new Vector2(0, objetivo);
    }

    void Mover(int d)
    {
        var p = Actual; if (p == null || p.filas.Count == 0) return;
        p.indice = (p.indice + d + p.filas.Count) % p.filas.Count;
        Sonar(recursos.sonidoMover, Random.Range(0.97f, 1.03f), 0.8f);
        Desplazar(p);
    }

    void Seleccionar(Pantalla p, int i)
    {
        if (Actual != p || i == p.indice) return;
        p.indice = i;
        Sonar(recursos.sonidoMover, Random.Range(0.97f, 1.03f), 0.6f);
        Desplazar(p);
    }

    void Cambiar(Fila f, int d)
    {
        switch (f.tipo)
        {
            case TipoFila.Deslizador:
                float v = Mathf.Clamp(f.leer() + d * f.paso, f.min, f.max);
                v = Mathf.Round(v / f.paso) * f.paso;
                if (Mathf.Abs(v - f.leer()) < 0.0001f) return;
                f.escribir(v);
                break;
            case TipoFila.Opcion:
                int n = f.valores().Length; if (n == 0) return;
                f.escribirIndice((f.leerIndice() + d + n) % n);
                break;
            default: return;
        }
        Ajustes.Aplicar();
        if (musica != null && musica.isPlaying) musica.volume = Ajustes.VolMusica * 0.6f;
        Sonar(recursos.sonidoAjuste, 1f + 0.15f * d, 0.7f);
        RefrescarValores(Actual);
    }

    void Activar(Fila f)
    {
        switch (f.tipo)
        {
            case TipoFila.Boton: f.accion?.Invoke(); break;
            case TipoFila.Opcion: Cambiar(f, 1); break;
            case TipoFila.Deslizador: break;
            case TipoFila.Tecla:
                esperandoTecla = true; filaTecla = f;
                Sonar(recursos.sonidoAceptar);
                RefrescarValores(Actual);
                break;
        }
    }

    void Raton(Fila f, Pantalla p)
    {
        f.fondo.raycastTarget = true;
        var ev = f.fondo.gameObject.AddComponent<EventoRaton>();
        ev.alEntrar = () => { if (!esperandoTecla) Seleccionar(p, p.filas.IndexOf(f)); };
        ev.alClic = () =>
        {
            if (Actual != p || esperandoTecla || ocupado) return;
            p.indice = p.filas.IndexOf(f);
            if (f.tipo == TipoFila.Deslizador) return;
            if (f.tipo != TipoFila.Tecla && f.tipo != TipoFila.Opcion) Sonar(recursos.sonidoAceptar);
            Activar(f);
        };
        if (f.tipo == TipoFila.Deslizador)
        {
            ev.alArrastrar = pos =>
            {
                if (Actual != p || f.rtBarra == null) return;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(f.rtBarra, pos, null, out var local);
                float u = Mathf.Clamp01((local.x + f.rtBarra.rect.width) / f.rtBarra.rect.width);
                float v = Mathf.Round(Mathf.Lerp(f.min, f.max, u) / f.paso) * f.paso;
                if (Mathf.Abs(v - f.leer()) < 0.0001f) return;
                f.escribir(v); Ajustes.Aplicar(); RefrescarValores(Actual);
                if (musica != null && musica.isPlaying) musica.volume = Ajustes.VolMusica * 0.6f;
                Sonar(recursos.sonidoAjuste, 1f, 0.4f);
            };
        }
    }

    bool Tecla(KeyCode k)
    {
        if (Input.GetKeyDown(k)) { teclaRepetida = k; repetir = 0.38f; return true; }
        if (teclaRepetida == k && Input.GetKey(k))
        {
            repetir -= DT;
            if (repetir <= 0f) { repetir = 0.075f; return true; }
        }
        return false;
    }

    void Update()
    {
        ActualizarFps();
        if (modo == Modo.Principal) ActualizarFondo();
        else if (ActualizarPausa()) { ActualizarVisuales(); return; }   // el mismo Esc que la abre no debe cerrarla
        if (!abierto || ocupado) { ActualizarVisuales(); return; }

        if (esperandoTecla) { CapturarTecla(); ActualizarVisuales(); return; }

        var p = Actual;
        if (p != null)
        {
            if (Tecla(KeyCode.UpArrow) || Tecla(KeyCode.W)) Mover(-1);
            if (Tecla(KeyCode.DownArrow) || Tecla(KeyCode.S)) Mover(1);
            var f = p.filas.Count > 0 ? p.filas[p.indice] : null;
            bool horizontal = p.filas.Count == 2 && p.pestana < 0 && !p.esLista && p.titulo == "";
            if (Tecla(KeyCode.LeftArrow) || Tecla(KeyCode.A)) { if (horizontal) Mover(-1); else if (f != null) Cambiar(f, -1); }
            if (Tecla(KeyCode.RightArrow) || Tecla(KeyCode.D)) { if (horizontal) Mover(1); else if (f != null) Cambiar(f, 1); }
            if (p.pestana >= 0)
            {
                if (Input.GetKeyDown(KeyCode.Q)) CambiarPestana(-1);
                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Tab)) CambiarPestana(1);
            }
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) && f != null)
            {
                if (f.tipo == TipoFila.Boton) Sonar(recursos.sonidoAceptar);
                Activar(f);
            }
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace))
            {
                if (pila.Count == 1 && modo == Modo.Pausa) CerrarPausa();
                else Atras();
            }
        }
        ActualizarVisuales();
    }

    void CapturarTecla()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { esperandoTecla = false; Sonar(recursos.sonidoAtras); RefrescarValores(Actual); return; }
        foreach (KeyCode k in System.Enum.GetValues(typeof(KeyCode)))
        {
            if (k == KeyCode.None || (int)k >= (int)KeyCode.JoystickButton0) continue;
            if (!Input.GetKeyDown(k) || Controles.Reservada(k)) continue;
            Controles.Asignar(filaTecla.accionTecla, k);
            esperandoTecla = false;
            Sonar(recursos.sonidoAceptar, 1.1f);
            RefrescarValores(Actual);
            return;
        }
    }

    void ActualizarVisuales()
    {
        var p = Actual;
        if (p == null) return;
        if (p.descripcion != null && p.descripciones != null)
            p.descripcion.text = p.indice >= 0 && p.indice < p.descripciones.Length ? p.descripciones[p.indice] : "";
        float k = 1f - Mathf.Exp(-DT * 18f);
        for (int i = 0; i < p.filas.Count; i++)
        {
            var f = p.filas[i];
            bool sel = i == p.indice && abierto;
            if (f.fondo == null) continue;
            var cf = f.fondo.color; cf.a = Mathf.Lerp(cf.a, sel ? (p.esLista ? 0.38f : 0.2f) : 0f, k); f.fondo.color = cf;
            var cm = f.marca.color; cm.a = Mathf.Lerp(cm.a, sel ? 1f : 0f, k); f.marca.color = cm;
            var ct = sel ? Color.white : new Color(0.8f, 0.82f, 0.83f, p.esLista ? 0.85f : 0.75f);
            f.etiqueta.color = Color.Lerp(f.etiqueta.color, ct, k);
            if (p.esLista)
            {
                var re = f.etiqueta.rectTransform;
                re.offsetMin = new Vector2(Mathf.Lerp(re.offsetMin.x, sel ? 24f : 16f, k), 0);
            }
        }
    }

    // ================================================================== fondo animado
    void ActualizarFondo()
    {
        if (fondoA == null || recursos.fondos == null || recursos.fondos.Length == 0) return;
        const float dur = 8f, cruce = 1.8f;
        tFondo += DT;
        float u = tFondo / dur;
        // Ken Burns: zoom y paneo lentos
        float z = Mathf.Lerp(1.04f, 1.14f, u);
        float px = (fondoActual % 2 == 0 ? 1f : -1f) * Mathf.Lerp(-18f, 18f, u);
        fondoA.rectTransform.localScale = Vector3.one * z;
        fondoA.rectTransform.anchoredPosition = new Vector2(px, Mathf.Lerp(6f, -6f, u));
        float aL = Mathf.Clamp01(tFondo / 1.2f) * Mathf.Clamp01((dur - tFondo) / 1.2f);
        if (lugar != null) lugar.color = new Color(1, 1, 1, 0.55f * aL);
        if (tFondo > dur - cruce && recursos.fondos.Length > 1)
        {
            if (fondoB.color.a <= 0.001f)
            {
                int sig = ordenFondos[(fondoActual + 1) % ordenFondos.Length];
                PonerFondo(fondoB, ajusteB, sig);
                if (lugar != null) lugar.text = recursos.nombresFondos != null && sig < recursos.nombresFondos.Length ? recursos.nombresFondos[sig].ToUpperInvariant() : "";
            }
            float a = Mathf.Clamp01((tFondo - (dur - cruce)) / cruce);
            fondoB.color = new Color(1, 1, 1, a);
            fondoB.rectTransform.localScale = Vector3.one * 1.04f;
            fondoB.rectTransform.anchoredPosition = new Vector2(((fondoActual + 1) % 2 == 0 ? -18f : 18f), 6f);
        }
        if (tFondo >= dur)
        {
            // B pasa a ser A
            fondoActual = (fondoActual + 1) % ordenFondos.Length;
            var tmp = fondoA; fondoA = fondoB; fondoB = tmp;
            var ta = ajusteA; ajusteA = ajusteB; ajusteB = ta;
            fondoB.color = new Color(1, 1, 1, 0);
            fondoA.transform.SetSiblingIndex(0); fondoB.transform.SetSiblingIndex(1);
            tFondo = 0f;
        }
        // esporas
        for (int i = 0; i < esporas.Count; i++)
        {
            var d = esporasDatos[i]; var r = esporas[i];
            var p = r.anchoredPosition;
            p.y += d.x * DT;
            p.x += Mathf.Sin(Time.unscaledTime * 0.4f + d.y) * 10f * DT;
            if (p.y > 1100f) { p.y = -20f; p.x = Random.Range(0f, 1920f); }
            r.anchoredPosition = p;
            var img = r.GetComponent<Image>();
            var c = img.color; c.a = d.z * (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 1.3f + d.y * 3f)); img.color = c;
        }
        if (musica != null && musica.isPlaying && !ocupado) musica.volume = Mathf.MoveTowards(musica.volume, Ajustes.VolMusica * 0.6f, DT * 0.25f);
    }

    void ActualizarFps()
    {
        if (textoFps == null) return;
        if (!Ajustes.MostrarFps) { if (textoFps.text.Length > 0) textoFps.text = ""; return; }
        fpsAcum += Time.unscaledDeltaTime; fpsCuadros++; fpsTimer -= Time.unscaledDeltaTime;
        if (fpsTimer <= 0f)
        {
            fpsTimer = 0.5f;
            textoFps.text = Mathf.RoundToInt(fpsCuadros / Mathf.Max(0.0001f, fpsAcum)) + " FPS";
            fpsAcum = 0f; fpsCuadros = 0;
        }
    }

    // ================================================================== acciones
    void CargarJuego()
    {
        if (ocupado) return;
        ocupado = true;
        StartCoroutine(RutinaJugar());
    }

    IEnumerator RutinaJugar()
    {
        Ajustes.Guardar();
        float v0 = musica.volume;
        for (float t = 0f; t < 1f; t += DT / 1.3f)
        {
            fundido.color = new Color(0, 0, 0, t);
            musica.volume = v0 * (1f - t);
            yield return null;
        }
        fundido.color = Color.black;
        musica.Stop();
        var op = SceneManager.LoadSceneAsync(recursos.escenaJuego);
        while (!op.isDone) yield return null;
    }

    void Salir()
    {
        Ajustes.Guardar();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    IEnumerator Fundido(float de, float a, float dur)
    {
        for (float t = 0f; t < 1f; t += DT / dur)
        {
            fundido.color = new Color(0, 0, 0, Mathf.Lerp(de, a, Mathf.SmoothStep(0f, 1f, t)));
            yield return null;
        }
        fundido.color = new Color(0, 0, 0, a);
    }

    // ---------------------------------------------------------------- pausa
    bool bloqueadoAntes;

    /// <summary>Abre la pausa con Esc. Devuelve true si la abrio en este cuadro.</summary>
    bool ActualizarPausa()
    {
        if (abierto || ocupado) return false;
        if (!Input.GetKeyDown(KeyCode.Escape)) return false;
        if (IntroDespertar.Activa || GestorPartida.Muerto || MapaUI.BloqueaPausa || FinalJuego.Activo) return false;
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null && pc.InventarioAbierto) return false;
        if (ControladorInventario.Instance != null && ControladorInventario.Instance.IsOpen) return false;
        AbrirPausa();
        return true;
    }

    void AbrirPausa()
    {
        abierto = true;
        bloqueadoAntes = Controles.Bloqueado; Controles.Bloqueado = true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        Time.timeScale = 0f; AudioListener.pause = true;
        grupoMenu.gameObject.SetActive(true);
        if (pila.Count == 0) AbrirPrincipal();
        Actual.indice = 0;
        Sonar(recursos.sonidoAceptar, 0.9f);
        if (fundiendo != null) StopCoroutine(fundiendo);
        fundiendo = StartCoroutine(FundirMenu(0f, 1f));
    }
    Coroutine fundiendo;

    void CerrarPausa()
    {
        if (!abierto) return;
        // vuelve a la lista principal
        while (pila.Count > 1) { var p = Actual; pila.RemoveAt(pila.Count - 1); if (p.pestana >= 0 && pestanas != null) { foreach (var q in pestanas) if (q != null) Destroy(q.raiz); pestanas = null; } else Destroy(p.raiz); }
        Ajustes.Guardar();
        abierto = false;
        Sonar(recursos.sonidoAtras);
        Time.timeScale = 1f; AudioListener.pause = false;
        Controles.Bloqueado = bloqueadoAntes;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        if (fundiendo != null) StopCoroutine(fundiendo);
        fundiendo = StartCoroutine(FundirMenu(1f, 0f));
    }

    IEnumerator FundirMenu(float de, float a)
    {
        for (float t = 0f; t < 1f; t += DT / 0.18f) { grupoMenu.alpha = Mathf.Lerp(de, a, t); yield return null; }
        grupoMenu.alpha = a;
        if (a <= 0f && !abierto) grupoMenu.gameObject.SetActive(false);
    }

    void IrAlMenu()
    {
        ocupado = true;
        StartCoroutine(RutinaIrAlMenu());
    }

    IEnumerator RutinaIrAlMenu()
    {
        Ajustes.Guardar();
        GuardarAlSalir();
        for (float t = 0f; t < 1f; t += DT / 0.8f) { fundido.color = new Color(0, 0, 0, t); yield return null; }
        Time.timeScale = 1f; AudioListener.pause = false; Controles.Bloqueado = false;
        SceneManager.LoadScene(recursos.escenaMenu);
    }

    // ================================================================== utilidades de UI
    static RectTransform Rect(string n, RectTransform padre)
    {
        var go = new GameObject(n, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(padre, false);
        return rt;
    }

    static void Estirar(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    Image Imagen(string n, RectTransform padre, Color c)
    {
        var rt = Rect(n, padre);
        var i = rt.gameObject.AddComponent<Image>();
        i.sprite = blanco; i.color = c; i.raycastTarget = false;
        return i;
    }

    TextMeshProUGUI Texto(string n, RectTransform padre, string texto, float tam, TextAlignmentOptions al, TMP_FontAsset fuente)
    {
        var rt = Rect(n, padre);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (fuente != null) t.font = fuente;
        t.text = texto; t.fontSize = tam; t.alignment = al; t.color = Color.white; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }
}

/// <summary>Reenvia los eventos del raton de una fila del menu.</summary>
public class EventoRaton : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler, IPointerDownHandler, IDragHandler
{
    public System.Action alEntrar, alClic;
    public System.Action<Vector2> alArrastrar;
    public void OnPointerEnter(PointerEventData e) { alEntrar?.Invoke(); }
    public void OnPointerClick(PointerEventData e) { alClic?.Invoke(); }
    public void OnPointerDown(PointerEventData e) { alArrastrar?.Invoke(e.position); }
    public void OnDrag(PointerEventData e) { alArrastrar?.Invoke(e.position); }
}
