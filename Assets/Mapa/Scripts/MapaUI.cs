using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// El mapa del cuaderno (tecla M, Controles.Accion.Mapa). Pausa el juego mientras esta abierto.
/// Rueda: zoom.  Arrastrar: mover.  Clic: poner chincheta.  Clic sobre una chincheta o clic derecho: quitarla.
/// C: centrar en el jugador.  M / Esc: cerrar.  Clic en un lugar de la lista: ir a el.
/// </summary>
public class MapaUI : MonoBehaviour
{
    public static bool Abierto { get; private set; }
    static int cuadroCierre = -10;
    /// <summary>El menu de pausa no debe abrirse con el mismo Esc que cierra el mapa.</summary>
    public static bool BloqueaPausa => Abierto || (Time.frameCount >= cuadroCierre && Time.frameCount - cuadroCierre <= 1);

    // sin recarga de dominio, cuadroCierre de un Play anterior (mayor que el frameCount nuevo) bloqueaba Esc
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { Abierto = false; cuadroCierre = -10; }

    const float LadoBase = 840f;        // lado del mapa en la UI con zoom 1
    const float ZoomMin = 1f, ZoomMax = 7f;

    MapaMundo mapa;
    MenuRecursos rec;
    Transform jugador;
    Canvas canvas;
    CanvasGroup grupo;
    RectTransform vista, contenido, capaMarcas, listaLugares, listaChinchetas;
    RawImage imagen;
    Material mat;
    RectTransform flecha;
    TextMeshProUGUI tituloChinchetas, textoCursor;
    AudioSource audioMapa;
    float zoom = 2.2f;

    class Marca { public MapaMundo.Lugar lugar; public RectTransform icono; public TextMeshProUGUI texto; }
    readonly List<Marca> marcas = new List<Marca>();
    readonly Dictionary<MapaMundo.Chincheta, RectTransform> pines = new Dictionary<MapaMundo.Chincheta, RectTransform>();
    // mochilas, botines y ladrones: siempre visibles en el mapa (aunque la zona no este explorada)
    readonly Dictionary<Component, (RectTransform icono, TextMeshProUGUI texto)> bolsas = new Dictionary<Component, (RectTransform, TextMeshProUGUI)>();

    // estado previo al abrir
    float escalaAntes; bool bloqueadoAntes;

    // raton
    bool presionado, arrastrando; Vector2 ratonInicio, panInicio;

    static readonly Color Tinta = new Color(0.2f, 0.17f, 0.14f);
    static readonly Color Rojo = new Color(0.72f, 0.2f, 0.17f);

    // ================================================================== construccion
    void Start()
    {
        mapa = GetComponent<MapaMundo>();
        rec = Resources.Load<MenuRecursos>("MenuRecursos");
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        audioMapa = gameObject.AddComponent<AudioSource>();
        audioMapa.playOnAwake = false; audioMapa.ignoreListenerPause = true; audioMapa.spatialBlend = 0f;
        Construir();
        canvas.gameObject.SetActive(false);
        if (mapa != null) mapa.Cambio += RehacerPines;
    }

    void OnDestroy()
    {
        if (Abierto) { Abierto = false; Time.timeScale = 1f; Controles.Bloqueado = false; }
        if (mat != null) Destroy(mat);
        if (mapa != null) mapa.Cambio -= RehacerPines;
    }

    void Construir()
    {
        var go = new GameObject("MapaCuaderno");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 700;
        var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        grupo = go.AddComponent<CanvasGroup>();
        var raiz = (RectTransform)go.transform;

        var fondo = Img(raiz, "Oscurecer", new Color(0.01f, 0.01f, 0.02f, 0.72f)); Estirar(fondo.rectTransform);

        // hoja del cuaderno
        var sombra = Img(raiz, "Sombra", new Color(0, 0, 0, 0.55f));
        Centro(sombra.rectTransform, new Vector2(1600, 990), new Vector2(10, -12));
        var hoja = new GameObject("Hoja", typeof(RectTransform)).AddComponent<RawImage>();
        hoja.transform.SetParent(raiz, false);
        hoja.texture = Resources.Load<Texture2D>("MapaPapel");
        hoja.uvRect = new Rect(0.1f, 0.1f, 0.62f, 0.38f);
        hoja.color = new Color(1f, 0.97f, 0.92f);
        Centro(hoja.rectTransform, new Vector2(1600, 990), Vector2.zero);
        var rh = hoja.rectTransform;
        // espiral
        for (int i = 0; i < 16; i++)
        {
            var a = Img(rh, "Espiral", new Color(0.12f, 0.1f, 0.09f)); a.sprite = IconosMapa.Lugar((MapaMundo.TipoLugar)99);
            var r = a.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0, 1); r.sizeDelta = new Vector2(30, 30);
            r.anchoredPosition = new Vector2(16, -50 - i * 58);
        }
        // titulo
        var titulo = Txt(rh, "Titulo", "MAPA", 72, rec != null ? rec.fuenteMenu : null, TextAlignmentOptions.TopLeft);
        var rt = titulo.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(70, -22); rt.sizeDelta = new Vector2(400, 90);
        titulo.color = Tinta; titulo.characterSpacing = 10; titulo.fontStyle = FontStyles.Italic;
        var sub = Img(rh, "Subrayado", Rojo); var rs = sub.rectTransform;
        rs.anchorMin = rs.anchorMax = new Vector2(0, 1); rs.pivot = new Vector2(0, 0.5f); rs.anchoredPosition = new Vector2(66, -104); rs.sizeDelta = new Vector2(250, 6); rs.localEulerAngles = new Vector3(0, 0, 1.5f);

        // vista del mapa (con mascara)
        vista = new GameObject("Vista", typeof(RectTransform)).GetComponent<RectTransform>();
        vista.SetParent(rh, false);
        vista.anchorMin = new Vector2(0, 0); vista.anchorMax = new Vector2(1, 1);
        vista.offsetMin = new Vector2(66, 56); vista.offsetMax = new Vector2(-380, -128);
        vista.gameObject.AddComponent<RectMask2D>();
        var atrapa = vista.gameObject.AddComponent<Image>(); atrapa.color = new Color(0, 0, 0, 0); atrapa.raycastTarget = true;
        contenido = new GameObject("Contenido", typeof(RectTransform)).GetComponent<RectTransform>();
        contenido.SetParent(vista, false);
        contenido.anchorMin = contenido.anchorMax = new Vector2(0.5f, 0.5f);
        imagen = new GameObject("Dibujo", typeof(RectTransform)).AddComponent<RawImage>();
        imagen.transform.SetParent(contenido, false); Estirar(imagen.rectTransform); imagen.raycastTarget = false;
        imagen.texture = Resources.Load<Texture2D>("MapaBase");
        var plantilla = Resources.Load<Material>("MapaNiebla");
        if (plantilla != null)
        {
            mat = new Material(plantilla);
            mat.SetTexture("_Papel", Resources.Load<Texture2D>("MapaPapel"));
            if (mapa != null) mat.SetTexture("_Niebla", mapa.TexNiebla);
            imagen.material = mat;
        }
        capaMarcas = new GameObject("Marcas", typeof(RectTransform)).GetComponent<RectTransform>();
        capaMarcas.SetParent(contenido, false); Estirar(capaMarcas);
        // marco a lapiz
        foreach (var lado in new[] { 0, 1, 2, 3 })
        {
            var l = Img(rh, "Marco", new Color(Tinta.r, Tinta.g, Tinta.b, 0.85f)); var rl = l.rectTransform;
            bool h = lado < 2;
            rl.anchorMin = h ? new Vector2(0, lado) : new Vector2(lado - 2, 0); rl.anchorMax = h ? new Vector2(1, lado) : new Vector2(lado - 2, 1);
            rl.offsetMin = h ? new Vector2(64, lado == 0 ? 53 : -131) : new Vector2(lado == 2 ? 63 : -383, 54);
            rl.offsetMax = h ? new Vector2(-378, lado == 0 ? 57 : -127) : new Vector2(lado == 2 ? 67 : -379, -127);
        }

        // jugador
        flecha = Img(capaMarcas, "Jugador", new Color(0.78f, 0.15f, 0.12f)).rectTransform;
        ((Image)flecha.GetComponent<Image>()).sprite = IconosMapa.Flecha();
        flecha.sizeDelta = new Vector2(34, 34);

        // columna derecha: lugares y chinchetas
        var col = new GameObject("Columna", typeof(RectTransform)).GetComponent<RectTransform>();
        col.SetParent(rh, false); col.anchorMin = new Vector2(1, 0); col.anchorMax = new Vector2(1, 1); col.pivot = new Vector2(1, 1);
        col.offsetMin = new Vector2(-350, 56); col.offsetMax = new Vector2(-40, -40);
        var tl = Txt(col, "Lugares", "LUGARES", 34, rec != null ? rec.fuenteMenu : null, TextAlignmentOptions.TopLeft);
        Arriba(tl.rectTransform, 0, 44); tl.color = Tinta; tl.characterSpacing = 6;
        var sl = Img(col, "Sub", Rojo); Arriba(sl.rectTransform, 44, 4); sl.rectTransform.offsetMax = new Vector2(-150, sl.rectTransform.offsetMax.y);
        listaLugares = new GameObject("ListaLugares", typeof(RectTransform)).GetComponent<RectTransform>();
        listaLugares.SetParent(col, false); Arriba(listaLugares, 60, 380);
        tituloChinchetas = Txt(col, "Chinchetas", "CHINCHETAS", 34, rec != null ? rec.fuenteMenu : null, TextAlignmentOptions.TopLeft);
        Arriba(tituloChinchetas.rectTransform, 460, 44); tituloChinchetas.color = Tinta; tituloChinchetas.characterSpacing = 6;
        var sc = Img(col, "Sub", Rojo); Arriba(sc.rectTransform, 504, 4); sc.rectTransform.offsetMax = new Vector2(-110, sc.rectTransform.offsetMax.y);
        listaChinchetas = new GameObject("ListaChinchetas", typeof(RectTransform)).GetComponent<RectTransform>();
        listaChinchetas.SetParent(col, false); Arriba(listaChinchetas, 520, 300);

        // ayuda y coordenadas del cursor
        var ayuda = Txt(rh, "Ayuda",
            "<b>Clic</b> poner chincheta    <b>Clic der.</b> quitar    <b>Rueda</b> zoom    <b>Arrastrar</b> mover    <b>C</b> centrar    <b>" + Controles.NombreTecla(Controles.Accion.Mapa) + " / Esc</b> cerrar",
            22, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.BottomLeft);
        var ra = ayuda.rectTransform; ra.anchorMin = new Vector2(0, 0); ra.anchorMax = new Vector2(1, 0); ra.pivot = new Vector2(0, 0);
        ra.anchoredPosition = new Vector2(70, 14); ra.sizeDelta = new Vector2(-120, 34); ayuda.color = new Color(Tinta.r, Tinta.g, Tinta.b, 0.75f);
        textoCursor = Txt(rh, "Cursor", "", 22, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.TopRight);
        var rc = textoCursor.rectTransform; rc.anchorMin = rc.anchorMax = new Vector2(1, 1); rc.pivot = new Vector2(1, 1);
        rc.anchoredPosition = new Vector2(-384, -96); rc.sizeDelta = new Vector2(520, 30); textoCursor.color = new Color(Tinta.r, Tinta.g, Tinta.b, 0.7f);
    }

    // ================================================================== abrir / cerrar
    void Update()
    {
        if (!Abierto)
        {
            if (Controles.Bajada(Controles.Accion.Mapa) && PuedeAbrir()) Abrir();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(Controles.Tecla(Controles.Accion.Mapa))) { Cerrar(); return; }
        if (Input.GetKeyDown(KeyCode.C)) CentrarEn(jugador != null ? jugador.position.ToXZ() : MapaMundo.Centro);
        Raton();
        Teclas();
        Colocar();
    }

    bool PuedeAbrir()
    {
        if (IntroDespertar.Activa || GestorPartida.Muerto || FinalJuego.Activo || Time.timeScale <= 0f) return false;
        if (ControladorInventario.Instance != null && ControladorInventario.Instance.IsOpen) return false;
        return Resources.Load<Texture2D>("MapaBase") != null;
    }

    void Abrir()
    {
        Abierto = true;
        escalaAntes = Time.timeScale; bloqueadoAntes = Controles.Bloqueado;
        Time.timeScale = 0f; Controles.Bloqueado = true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        canvas.gameObject.SetActive(true);
        grupo.alpha = 1f;
        Sonar(rec != null ? rec.sonidoAceptar : null, 0.8f);
        if (mapa != null) mapa.Revisar();
        RehacerLugares();
        RehacerPines();
        RehacerBolsas();
        CentrarEn(jugador != null ? jugador.position.ToXZ() : MapaMundo.Centro);
        Colocar();
    }

    void Cerrar()
    {
        Abierto = false; cuadroCierre = Time.frameCount;
        Time.timeScale = escalaAntes > 0f ? escalaAntes : 1f; Controles.Bloqueado = bloqueadoAntes;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        canvas.gameObject.SetActive(false);
        Sonar(rec != null ? rec.sonidoAtras : null, 0.8f);
    }

    void Sonar(AudioClip c, float tono)
    {
        if (c == null) return;
        audioMapa.pitch = tono; audioMapa.PlayOneShot(c, 0.8f * Ajustes.VolEfectos);
    }

    // ================================================================== entrada
    float Lado => LadoBase * zoom;

    Vector2 RatonLocal(RectTransform r)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(r, Input.mousePosition, null, out var p);
        return p;
    }

    bool RatonEnVista() => RectTransformUtility.RectangleContainsScreenPoint(vista, Input.mousePosition, null);

    void Raton()
    {
        // zoom alrededor del cursor
        float rueda = Input.mouseScrollDelta.y;
        if (Mathf.Abs(rueda) > 0.01f && RatonEnVista())
        {
            Vector2 antes = RatonLocal(contenido) / Lado;              // punto del mapa bajo el cursor (en lados)
            zoom = Mathf.Clamp(zoom * Mathf.Pow(1.18f, rueda), ZoomMin, ZoomMax);
            contenido.sizeDelta = Vector2.one * Lado;
            Vector2 despues = RatonLocal(contenido) / Lado;
            contenido.anchoredPosition += (despues - antes) * Lado;
            Limitar();
        }

        if (Input.GetMouseButtonDown(0) && RatonEnVista())
        {
            presionado = true; arrastrando = false;
            ratonInicio = Input.mousePosition; panInicio = contenido.anchoredPosition;
        }
        if (presionado && Input.GetMouseButton(0))
        {
            Vector2 d = (Vector2)Input.mousePosition - ratonInicio;
            if (!arrastrando && d.magnitude > 6f) arrastrando = true;
            if (arrastrando)
            {
                contenido.anchoredPosition = panInicio + d / canvas.scaleFactor;
                Limitar();
            }
        }
        if (presionado && Input.GetMouseButtonUp(0))
        {
            presionado = false;
            if (!arrastrando && RatonEnVista()) Clic(false);
        }
        if (Input.GetMouseButtonDown(1) && RatonEnVista()) Clic(true);

        // coordenadas bajo el cursor
        if (RatonEnVista())
        {
            Vector2 uv = RatonLocal(contenido) / Lado + Vector2.one * 0.5f;
            var c = PinBajoCursor();
            textoCursor.text = c != null ? "Chincheta: clic para quitarla" : uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1 ? "Clic para poner una chincheta" : "";
        }
        else textoCursor.text = "";
    }

    void Teclas()
    {
        Vector2 m = Vector2.zero;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) m.y -= 1;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) m.y += 1;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) m.x += 1;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) m.x -= 1;
        if (m != Vector2.zero) { contenido.anchoredPosition += m * 700f * Time.unscaledDeltaTime; Limitar(); }
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) Zoom(1.3f);
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) Zoom(1f / 1.3f);
    }

    void Zoom(float k)
    {
        Vector2 centro = -contenido.anchoredPosition / Lado;
        zoom = Mathf.Clamp(zoom * k, ZoomMin, ZoomMax);
        contenido.sizeDelta = Vector2.one * Lado;
        contenido.anchoredPosition = -centro * Lado;
        Limitar();
    }

    MapaMundo.Chincheta PinBajoCursor()
    {
        Vector2 p = RatonLocal(capaMarcas);
        MapaMundo.Chincheta mejor = null; float md = 26f;
        foreach (var kv in pines)
        {
            float d = Vector2.Distance(p, kv.Value.anchoredPosition + new Vector2(0, 6));
            if (d < md) { md = d; mejor = kv.Key; }
        }
        return mejor;
    }

    void Clic(bool derecho)
    {
        if (mapa == null) return;
        var pin = PinBajoCursor();
        if (pin != null) { mapa.QuitarChincheta(pin); Sonar(rec != null ? rec.sonidoAtras : null, 1.2f); return; }
        if (derecho) return;
        Vector2 uv = RatonLocal(contenido) / Lado + Vector2.one * 0.5f;
        if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return;
        if (mapa.PonerChincheta(MapaMundo.AMundo(uv))) Sonar(rec != null ? rec.sonidoAjuste : null, 1.1f);
        else Sonar(rec != null ? rec.sonidoAtras : null, 0.7f);
    }

    void CentrarEn(Vector2 xz)
    {
        contenido.sizeDelta = Vector2.one * Lado;
        Vector2 uv = MapaMundo.AUV(xz);
        contenido.anchoredPosition = -(uv - Vector2.one * 0.5f) * Lado;
        Limitar();
    }

    void Limitar()
    {
        Vector2 v = vista.rect.size, p = contenido.anchoredPosition;
        float mx = Mathf.Max(0f, (Lado - v.x) * 0.5f), my = Mathf.Max(0f, (Lado - v.y) * 0.5f);
        contenido.anchoredPosition = new Vector2(Mathf.Clamp(p.x, -mx, mx), Mathf.Clamp(p.y, -my, my));
    }

    // ================================================================== marcas
    Vector2 PosMapa(Vector2 xz) => (MapaMundo.AUV(xz) - Vector2.one * 0.5f) * Lado;

    void Colocar()
    {
        contenido.sizeDelta = Vector2.one * Lado;
        if (jugador != null)
        {
            flecha.anchoredPosition = PosMapa(jugador.position.ToXZ());
            var cam = Camera.main;
            float yaw = cam != null ? cam.transform.eulerAngles.y : jugador.eulerAngles.y;
            flecha.localEulerAngles = new Vector3(0, 0, -yaw);
            flecha.SetAsLastSibling();
        }
        foreach (var m in marcas)
        {
            Vector2 p = PosMapa(m.lugar.pos.ToXZ());
            m.icono.anchoredPosition = p;
            m.texto.rectTransform.anchoredPosition = p + new Vector2(0, m.lugar.tipo == MapaMundo.TipoLugar.Salon ? 10f : -27f * m.icono.localScale.x);
            float k = Mathf.Lerp(0.85f, 1.1f, Mathf.InverseLerp(ZoomMin, ZoomMax, zoom));
            m.icono.localScale = Vector3.one * k;
        }
        foreach (var kv in pines) kv.Value.anchoredPosition = PosMapa(kv.Key.xz);
        foreach (var kv in bolsas)
        {
            if (kv.Key == null) continue;
            Vector2 p = PosMapa(kv.Key.transform.position.ToXZ());
            kv.Value.icono.anchoredPosition = p;
            kv.Value.texto.rectTransform.anchoredPosition = p + new Vector2(0, -22);
        }
    }

    void RehacerBolsas()
    {
        foreach (var kv in bolsas) { if (kv.Value.icono != null) Destroy(kv.Value.icono.gameObject); if (kv.Value.texto != null) Destroy(kv.Value.texto.gameObject); }
        bolsas.Clear();
        var todas = new List<(Component c, string nombre, Color color)>();
        foreach (var m in Mochila.Todas) if (m != null) todas.Add((m, m.titulo, m.tipo == Mochila.Tipo.Mochila ? Color.white : new Color(1f, 0.55f, 0.45f)));
        foreach (var l in Ladron.Todos) if (l != null) todas.Add((l, "Ladron: " + l.Nombre, new Color(1f, 0.55f, 0.45f)));
        foreach (var b in todas)
        {
            var ic = Img(capaMarcas, "Bolsa", b.color); ic.sprite = IconosMapa.Mochila(); ic.rectTransform.sizeDelta = new Vector2(40, 40);
            var tx = Txt(capaMarcas, "NombreBolsa", b.nombre, 20, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.Top);
            tx.rectTransform.sizeDelta = new Vector2(300, 34); tx.rectTransform.pivot = new Vector2(0.5f, 1f);
            tx.color = new Color(0.55f, 0.12f, 0.08f); tx.fontStyle = FontStyles.Italic | FontStyles.Bold;
            bolsas[b.c] = (ic.rectTransform, tx);
            // tambien en la lista de la derecha (clic = ir alli)
            int fila = listaLugares.childCount;
            var e = Txt(listaLugares, "FilaBolsa", b.nombre, 25, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.MidlineLeft);
            var re = e.rectTransform; re.anchorMin = new Vector2(0, 1); re.anchorMax = new Vector2(1, 1); re.pivot = new Vector2(0, 1);
            re.anchoredPosition = new Vector2(42, -fila * 40); re.sizeDelta = new Vector2(-42, 38);
            e.color = new Color(0.55f, 0.12f, 0.08f); e.raycastTarget = true;
            var mini = Img(re, "Icono", b.color); mini.sprite = IconosMapa.Mochila();
            var rm = mini.rectTransform; rm.anchorMin = rm.anchorMax = new Vector2(0, 0.5f); rm.sizeDelta = new Vector2(30, 30); rm.anchoredPosition = new Vector2(-22, 0);
            var objetivo = b.c;
            var ev = e.gameObject.AddComponent<EventoRaton>();
            ev.alClic = () => { if (objetivo != null) CentrarEn(objetivo.transform.position.ToXZ()); };
        }
        if (flecha != null) flecha.SetAsLastSibling();
    }

    void RehacerLugares()
    {
        foreach (var m in marcas) { if (m.icono != null) Destroy(m.icono.gameObject); if (m.texto != null) Destroy(m.texto.gameObject); }
        marcas.Clear();
        for (int i = listaLugares.childCount - 1; i >= 0; i--) { var t = listaLugares.GetChild(i); t.SetParent(null); Destroy(t.gameObject); }
        if (mapa == null) return;
        int fila = 0;
        foreach (var l in mapa.lugares)
        {
            if (!l.descubierto) continue;
            bool grande = l.tipo != MapaMundo.TipoLugar.Salon;
            var ic = Img(capaMarcas, "Lugar_" + l.nombre, Color.white);
            ic.sprite = IconosMapa.Lugar(l.tipo);
            ic.rectTransform.sizeDelta = grande ? new Vector2(46, 46) : Vector2.zero;
            var tx = Txt(capaMarcas, "Nombre_" + l.nombre, l.nombre, l.tipo == MapaMundo.TipoLugar.Salon ? 30 : 22, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.Top);
            tx.rectTransform.sizeDelta = new Vector2(300, 40); tx.rectTransform.pivot = new Vector2(0.5f, 1f);
            tx.color = Tinta; tx.fontStyle = FontStyles.Italic | FontStyles.Bold;
            tx.outlineWidth = 0.2f; tx.outlineColor = new Color32(235, 222, 196, 200);
            marcas.Add(new Marca { lugar = l, icono = ic.rectTransform, texto = tx });

            // entrada en la lista (clic = centrar ahi); la cerca solo una vez
            if (l.tipo == MapaMundo.TipoLugar.Cerca && fila > 0 && EnLista("La cerca")) continue;
            var e = Txt(listaLugares, "Fila", l.nombre, 25, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.MidlineLeft);
            var re = e.rectTransform; re.anchorMin = new Vector2(0, 1); re.anchorMax = new Vector2(1, 1); re.pivot = new Vector2(0, 1);
            re.anchoredPosition = new Vector2(42, -fila * 40); re.sizeDelta = new Vector2(-42, 38);
            e.color = Tinta; e.raycastTarget = true;
            var mini = Img(re, "Icono", Color.white); mini.sprite = IconosMapa.Lugar(l.tipo);
            var rm = mini.rectTransform; rm.anchorMin = rm.anchorMax = new Vector2(0, 0.5f); rm.sizeDelta = new Vector2(30, 30); rm.anchoredPosition = new Vector2(-22, 0);
            var lugar = l;
            var ev = e.gameObject.AddComponent<EventoRaton>();
            ev.alClic = () => { CentrarEn(lugar.pos.ToXZ()); Sonar(rec != null ? rec.sonidoMover : null, 1f); };
            fila++;
        }
        flecha.SetAsLastSibling();
    }

    bool EnLista(string nombre)
    {
        foreach (Transform t in listaLugares) { var x = t.GetComponent<TextMeshProUGUI>(); if (x != null && x.text == nombre) return true; }
        return false;
    }

    void RehacerPines()
    {
        if (capaMarcas == null || mapa == null) return;
        foreach (var kv in pines) if (kv.Value != null) Destroy(kv.Value.gameObject);
        pines.Clear();
        foreach (Transform t in listaChinchetas) Destroy(t.gameObject);
        int i = 0;
        foreach (var c in mapa.chinchetas)
        {
            var img = Img(capaMarcas, "Chincheta", MapaMundo.ColoresChincheta[c.color]);
            img.sprite = IconosMapa.Chincheta();
            img.rectTransform.sizeDelta = new Vector2(38, 38);
            pines[c] = img.rectTransform;

            float dist = jugador != null ? Vector2.Distance(c.xz, jugador.position.ToXZ()) : 0f;
            var e = Txt(listaChinchetas, "Fila", "Chincheta " + (i + 1) + "   <size=80%><color=#00000080>" + BrujulaUI.Metros(dist) + "</color></size>", 24, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.MidlineLeft);
            var re = e.rectTransform; re.anchorMin = new Vector2(0, 1); re.anchorMax = new Vector2(1, 1); re.pivot = new Vector2(0, 1);
            re.anchoredPosition = new Vector2(42, -i * 32); re.sizeDelta = new Vector2(-42, 30);
            e.color = Tinta; e.raycastTarget = true;
            var mini = Img(re, "Icono", MapaMundo.ColoresChincheta[c.color]); mini.sprite = IconosMapa.Chincheta();
            var rm = mini.rectTransform; rm.anchorMin = rm.anchorMax = new Vector2(0, 0.5f); rm.sizeDelta = new Vector2(28, 28); rm.anchoredPosition = new Vector2(-22, 0);
            var cc = c;
            var ev = e.gameObject.AddComponent<EventoRaton>();
            ev.alClic = () => CentrarEn(cc.xz);
            i++;
        }
        if (i == 0)
        {
            var e = Txt(listaChinchetas, "Vacio", "Haz clic en el mapa para marcar un lugar. La brujula te guiara hasta tus chinchetas.", 22, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.TopLeft);
            var re = e.rectTransform; re.anchorMin = new Vector2(0, 1); re.anchorMax = new Vector2(1, 1); re.pivot = new Vector2(0, 1); re.sizeDelta = new Vector2(0, 110);
            e.color = new Color(Tinta.r, Tinta.g, Tinta.b, 0.65f); e.fontStyle = FontStyles.Italic;
        }
        tituloChinchetas.text = "CHINCHETAS  <size=70%>" + mapa.chinchetas.Count + "/" + MapaMundo.MaxChinchetas + "</size>";
        if (flecha != null) flecha.SetAsLastSibling();
        if (Abierto) Colocar();
    }

    // ================================================================== utilidades
    static Image Img(Transform padre, string n, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(padre, false);
        var i = go.AddComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
    }

    static TextMeshProUGUI Txt(Transform padre, string n, string s, float tam, TMP_FontAsset f, TextAlignmentOptions al)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(padre, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (f != null) t.font = f;
        t.text = s; t.fontSize = tam; t.alignment = al; t.raycastTarget = false;
        return t;
    }

    static void Estirar(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
    static void Centro(RectTransform r, Vector2 tam, Vector2 pos) { r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = tam; r.anchoredPosition = pos; }
    static void Arriba(RectTransform r, float y, float alto)
    {
        r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(0, -y); r.sizeDelta = new Vector2(0, alto);
    }
}
