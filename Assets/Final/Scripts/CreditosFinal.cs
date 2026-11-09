using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Creditos del final. Primero "Tu aventura": estadisticas de la partida con iconos y los retratos de cada
/// bicho con cuantos derrotaste. Despues suben los creditos (logo, equipo, recursos con su autor y pagina,
/// tecnologias, agradecimientos) mientras a los lados pasan fotos comicas de los bichos (Resources/Fotos).
/// Al terminar vuelve al menu principal. Mantener ESC lo salta.
/// </summary>
public class CreditosFinal : MonoBehaviour
{
    public AudioSource musica;

    MenuRecursos rec;
    Canvas canvas;
    RectTransform raiz;
    CanvasGroup grupoStats;
    float tEsc;
    bool saliendo;

    static readonly Color Acento = new Color(0.27f, 0.86f, 0.78f);
    static readonly Color Dorado = new Color(1f, 0.82f, 0.45f);

    // pie de foto de cada foto comica (nombre del archivo en Resources/Fotos)
    static readonly Dictionary<string, string> Pies = new Dictionary<string, string>
    {
        { "01_Ladrona", "¡Devuelve eso!" },
        { "02_Siesta", "Cinco minutitos mas..." },
        { "03_Futbol", "Hormigas F.C. contra la Cochinilla" },
        { "04_Corona", "Su majestad no sabia que estaba ahi" },
        { "05_Antorcha", "Yo tambien quiero ver de noche" },
        { "06_Carrera", "Nadie sabe quien empezo" },
        { "07_Banquete", "Hoy hay carne asada" },
        { "08_HulaHoop", "Mi cama nueva" },
        { "09_Trono", "Esto es mio... ¿verdad?" },
        { "10_Escondite", "Nadie me vera aqui" },
    };

    // ================================================================== inicio
    void Start()
    {
        rec = Resources.Load<MenuRecursos>("MenuRecursos");
        Controles.Bloqueado = true;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        MarcarCompletada();
        Construir();
        StartCoroutine(Secuencia());
    }

    void MarcarCompletada()
    {
        if (Partida.RanuraActual < 0) return;
        var d = Partida.Leer(Partida.RanuraActual);
        if (d == null) return;
        d.completada = true;
        d.stats = Estadisticas.Actual;
        try { Partida.Escribir(Partida.RanuraActual, d, null); } catch (System.Exception e) { Debug.LogWarning("[Final] " + e.Message); }
    }

    void Construir()
    {
        var go = new GameObject("CanvasCreditos");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 995;
        var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.matchWidthOrHeight = 0.5f;
        raiz = (RectTransform)go.transform;
        var fondo = Img(raiz, "Negro", Color.black); Estirar(fondo.rectTransform);
    }

    IEnumerator Secuencia()
    {
        yield return Estadisticas_();
        yield return Rodar();
        yield return Salir();
    }

    void Update()
    {
        bool esc = Input.GetKey(KeyCode.Escape);
        tEsc = esc ? tEsc + Time.unscaledDeltaTime : 0f;
        if (tEsc > 1.2f && !saliendo) { StopAllCoroutines(); StartCoroutine(Salir()); }
    }

    IEnumerator Salir()
    {
        if (saliendo) yield break;
        saliendo = true;
        var negro = Img(raiz, "Fin", new Color(0, 0, 0, 0)); Estirar(negro.rectTransform);
        float v0 = musica != null ? musica.volume : 0f;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 3f)
        {
            negro.color = new Color(0, 0, 0, t);
            if (musica != null) musica.volume = v0 * (1f - t);
            yield return null;
        }
        Time.timeScale = 1f; Controles.Bloqueado = false; AudioListener.pause = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        SceneManager.LoadScene(rec != null ? rec.escenaMenu : "MenuPrincipal");
    }

    // ================================================================== tu aventura
    IEnumerator Estadisticas_()
    {
        var e = Estadisticas.Actual;
        var go = new GameObject("Estadisticas", typeof(RectTransform)); var r = (RectTransform)go.transform; r.SetParent(raiz, false); Estirar(r);
        grupoStats = go.AddComponent<CanvasGroup>(); grupoStats.alpha = 0f;

        var titulo = Txt(r, "Titulo", "TU AVENTURA", 76, rec != null ? rec.fuenteMenu : null, TextAlignmentOptions.Center);
        Arriba(titulo.rectTransform, 70, 100); titulo.characterSpacing = 18; titulo.color = Dorado;
        string dif = "<color=" + Partida.ColorHex(Partida.Dif) + ">" + Partida.Nombre(Partida.Dif).ToUpperInvariant() + "</color>";
        var sub = Txt(r, "Sub", "en el mundo diminuto   ·   " + dif, 28, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.Center);
        Arriba(sub.rectTransform, 168, 40); sub.color = new Color(1, 1, 1, 0.75f);

        // columna de estadisticas
        int lugares = 0, totalLugares = 0;
        if (MapaMundo.Instancia != null)
        {
            var vistos = new HashSet<string>();
            foreach (var l in MapaMundo.Instancia.lugares) { if (vistos.Add(l.nombre)) totalLugares++; }
            vistos.Clear();
            foreach (var l in MapaMundo.Instancia.lugares) if (l.descubierto && vistos.Add(l.nombre)) lugares++;
        }
        var filas = new List<(Sprite icono, Color color, string nombre, float valor, System.Func<float, string> texto)>
        {
            (IconoBicho(), new Color(1f, 0.55f, 0.45f), "Bichos derrotados", e.TotalBichos, v => Mathf.RoundToInt(v).ToString()),
            (IconoSol(), new Color(1f, 0.85f, 0.4f), "Dias en el mundo diminuto", e.dias, v => Mathf.RoundToInt(v).ToString()),
            (IconoReloj(), Color.white, "Tiempo jugado", Partida.TiempoJugado, v => Partida.Tiempo(v)),
            (IconoHuella(), new Color(0.85f, 0.75f, 0.6f), "Distancia recorrida", e.distancia / 18f, v => Mathf.RoundToInt(v) + " m"),
            (IconoCorazon(), new Color(1f, 0.4f, 0.45f), "Veces que caiste", e.muertes, v => Mathf.RoundToInt(v).ToString()),
            (IconosMapa.Mochila(), Color.white, "Objetos recogidos", e.objetosRecogidos, v => Mathf.RoundToInt(v).ToString()),
            (Item("Antorcha"), Color.white, "Antorchas creadas", e.antorchasCreadas, v => Mathf.RoundToInt(v).ToString()),
            (Item("Escudo"), Color.white, "Escudos creados", e.escudosCreados, v => Mathf.RoundToInt(v).ToString()),
            (IconoEstrella(), Acento, "Bloqueos perfectos", e.bloqueosPerfectos, v => Mathf.RoundToInt(v).ToString()),
            (Item("Carne"), Color.white, "Bocados", e.comidas, v => Mathf.RoundToInt(v).ToString()),
            (IconosMapa.Chincheta(), new Color(0.9f, 0.3f, 0.25f), "Lugares descubiertos", lugares, v => Mathf.RoundToInt(v) + " de " + totalLugares),
        };
        var col = new GameObject("Columna", typeof(RectTransform)).GetComponent<RectTransform>();
        col.SetParent(r, false); col.anchorMin = col.anchorMax = new Vector2(0.5f, 1f); col.pivot = new Vector2(1f, 1f);
        col.anchoredPosition = new Vector2(-30, -250); col.sizeDelta = new Vector2(760, 720);
        var valores = new List<(TextMeshProUGUI t, float valor, System.Func<float, string> f, CanvasGroup g)>();
        for (int i = 0; i < filas.Count; i++)
        {
            var f = filas[i];
            var fila = new GameObject("Fila", typeof(RectTransform)).GetComponent<RectTransform>();
            fila.SetParent(col, false); fila.anchorMin = new Vector2(0, 1); fila.anchorMax = new Vector2(1, 1); fila.pivot = new Vector2(0.5f, 1);
            fila.anchoredPosition = new Vector2(0, -i * 60); fila.sizeDelta = new Vector2(0, 54);
            var g = fila.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0f;
            var ic = Img(fila, "Icono", f.color); ic.sprite = f.icono; ic.preserveAspect = true;
            var ri = ic.rectTransform; ri.anchorMin = ri.anchorMax = new Vector2(0, 0.5f); ri.sizeDelta = new Vector2(44, 44); ri.anchoredPosition = new Vector2(26, 0);
            var n = Txt(fila, "Nombre", f.nombre, 30, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.MidlineLeft);
            Estirar(n.rectTransform); n.rectTransform.offsetMin = new Vector2(66, 0); n.color = new Color(1, 1, 1, 0.85f);
            var v = Txt(fila, "Valor", "0", 34, rec != null ? rec.fuenteMenu : null, TextAlignmentOptions.MidlineRight);
            Estirar(v.rectTransform); v.color = Color.white;
            var linea = Img(fila, "Linea", new Color(1, 1, 1, 0.08f)); var rl = linea.rectTransform; rl.anchorMin = new Vector2(0, 0); rl.anchorMax = new Vector2(1, 0); rl.sizeDelta = new Vector2(0, 1);
            valores.Add((v, f.valor, f.texto, g));
        }

        // retratos de los bichos con cuantos derrotaste
        var rejilla = new GameObject("Bichos", typeof(RectTransform)).GetComponent<RectTransform>();
        rejilla.SetParent(r, false); rejilla.anchorMin = rejilla.anchorMax = new Vector2(0.5f, 1f); rejilla.pivot = new Vector2(0f, 1f);
        rejilla.anchoredPosition = new Vector2(60, -250); rejilla.sizeDelta = new Vector2(760, 720);
        var tb = Txt(rejilla, "Titulo", "BICHOS", 34, rec != null ? rec.fuenteMenu : null, TextAlignmentOptions.TopLeft);
        Arriba(tb.rectTransform, 0, 44); tb.characterSpacing = 10; tb.color = Acento;
        var retratos = new List<CanvasGroup>();
        for (int i = 0; i < Estadisticas.Tipos.Length; i++)
        {
            var (clave, nombre) = Estadisticas.Tipos[i];
            int n = e.Bichos(clave);
            var celda = new GameObject("Bicho_" + clave, typeof(RectTransform)).GetComponent<RectTransform>();
            celda.SetParent(rejilla, false); celda.anchorMin = celda.anchorMax = new Vector2(0, 1); celda.pivot = new Vector2(0, 1);
            celda.anchoredPosition = new Vector2((i % 4) * 185, -60 - (i / 4) * 165); celda.sizeDelta = new Vector2(175, 160);
            var g = celda.gameObject.AddComponent<CanvasGroup>(); g.alpha = 0f; retratos.Add(g);
            var marco = Img(celda, "Marco", new Color(1, 1, 1, n > 0 ? 0.07f : 0.03f)); Estirar(marco.rectTransform);
            var tex = Resources.Load<Texture2D>("Retratos/" + clave);
            if (tex != null)
            {
                var ri = new GameObject("Retrato", typeof(RectTransform)).AddComponent<RawImage>();
                ri.transform.SetParent(celda, false); ri.texture = tex; ri.raycastTarget = false;
                var rr = ri.rectTransform; rr.anchorMin = new Vector2(0.5f, 1); rr.anchorMax = new Vector2(0.5f, 1); rr.pivot = new Vector2(0.5f, 1); rr.sizeDelta = new Vector2(112, 112); rr.anchoredPosition = new Vector2(0, -4);
                ri.color = n > 0 ? Color.white : new Color(0.15f, 0.15f, 0.17f, 0.9f);     // silueta si no derrotaste ninguno
            }
            var nm = Txt(celda, "Nombre", nombre, 19, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.Bottom);
            Estirar(nm.rectTransform); nm.rectTransform.offsetMin = new Vector2(0, 22); nm.color = new Color(1, 1, 1, n > 0 ? 0.8f : 0.35f);
            var cn = Txt(celda, "Cuenta", n > 0 ? "x " + n : "-", 24, rec != null ? rec.fuenteMenu : null, TextAlignmentOptions.Bottom);
            Estirar(cn.rectTransform); cn.rectTransform.offsetMin = new Vector2(0, 0); cn.color = n > 0 ? Dorado : new Color(1, 1, 1, 0.25f);
        }

        // animacion: aparece, cuentan los numeros, se queda un momento y se va
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.5f) { grupoStats.alpha = t; yield return null; }
        grupoStats.alpha = 1f;
        float inicio = Time.unscaledTime;
        float durFilas = 0.32f, durCuenta = 1.1f;
        float fin = inicio + valores.Count * durFilas + durCuenta;
        while (Time.unscaledTime < fin + 0.2f)
        {
            float ahora = Time.unscaledTime - inicio;
            for (int i = 0; i < valores.Count; i++)
            {
                float k = Mathf.Clamp01((ahora - i * durFilas) / durCuenta);
                valores[i].g.alpha = Mathf.Clamp01((ahora - i * durFilas) / 0.3f);
                valores[i].t.text = valores[i].f(valores[i].valor * (1f - Mathf.Pow(1f - k, 3f)));
            }
            for (int i = 0; i < retratos.Count; i++)
            {
                float k = Mathf.Clamp01((ahora - 0.5f - i * 0.18f) / 0.35f);
                retratos[i].alpha = k; retratos[i].transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1f, 1f - Mathf.Pow(1f - k, 3f));
            }
            yield return null;
        }
        foreach (var v in valores) { v.t.text = v.f(v.valor); v.g.alpha = 1f; }
        foreach (var g in retratos) { g.alpha = 1f; g.transform.localScale = Vector3.one; }
        yield return new WaitForSecondsRealtime(8f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.4f) { grupoStats.alpha = 1f - t; yield return null; }
        Destroy(go);
    }

    // ================================================================== creditos que suben
    IEnumerator Rodar()
    {
        var cont = new GameObject("Rodillo", typeof(RectTransform)).GetComponent<RectTransform>();
        cont.SetParent(raiz, false); cont.anchorMin = cont.anchorMax = new Vector2(0.5f, 0f); cont.pivot = new Vector2(0.5f, 1f);
        cont.sizeDelta = new Vector2(1000, 10);
        float y = 0f;

        void Espacio(float h) { y += h; }
        TextMeshProUGUI Linea(string s, float tam, Color c, TMP_FontAsset f, float alto = -1f, float espaciado = 0f)
        {
            var t = Txt(cont, "Linea", s, tam, f, TextAlignmentOptions.Top);
            var r = t.rectTransform; r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            t.color = c; t.characterSpacing = espaciado; t.textWrappingMode = TextWrappingModes.Normal;
            float h = alto > 0 ? alto : t.GetPreferredValues(s, 1000, 0).y + 6f;
            r.anchoredPosition = new Vector2(0, -y); r.sizeDelta = new Vector2(0, h);
            y += h;
            return t;
        }
        var fMenu = rec != null ? rec.fuenteMenu : null; var fTexto = rec != null ? rec.fuenteTexto : null;
        void Seccion(string titulo) { Espacio(90); Linea(titulo, 30, Acento, fMenu, 44, 12); Espacio(12); }
        void Item_(string s) { Linea(s, 30, Color.white, fTexto); Espacio(4); }
        void Peque(string s) { Linea(s, 22, new Color(1, 1, 1, 0.6f), fTexto); }

        // logo
        if (rec != null && rec.logo != null)
        {
            var lg = Img(cont, "Logo", Color.white); lg.sprite = rec.logo; lg.preserveAspect = true;
            var rl = lg.rectTransform; rl.anchorMin = rl.anchorMax = new Vector2(0.5f, 1); rl.pivot = new Vector2(0.5f, 1);
            float w = 820f; rl.sizeDelta = new Vector2(w, w * rec.logo.rect.height / rec.logo.rect.width); rl.anchoredPosition = new Vector2(0, -y);
            y += rl.sizeDelta.y + 30;
        }
        else Linea("BEYOND SMALL", 96, Color.white, fMenu, 120, 20);
        Linea("Despertaste diminuto en un salon abandonado...\ny volviste a ser grande.", 30, new Color(0.75f, 0.95f, 0.85f), fTexto);

        Seccion("UN JUEGO DE");
        Linea("PARADOX PIXEL", 64, Dorado, fMenu, 84, 14);
        Seccion("EQUIPO DE DESARROLLO");
        foreach (var n in new[] { "Joni", "Axel", "Nolasco", "Yair", "Frank", "Tristan", "Juan Diego" }) { Linea(n, 40, Color.white, fMenu, 54, 4); }

        Seccion("EL PERSONAJE");
        Item_("Modelo e ilustracion del protagonista: Paradox Pixel");
        Peque("Esqueleto y animaciones: Mixamo (Adobe)  ·  mixamo.com");
        Seccion("LOS BICHOS");
        Item_("Hormiga, Oruga, Lombriz, Ciempies, Escarabajo y Mariposa");
        Peque("Poly by Google (CC-BY 3.0)  ·  poly.pizza");
        Item_("Abeja");
        Peque("Quaternius (CC0)  ·  poly.pizza/m/6HsU6GQt3a");
        Peque("Cochinilla, reinas, larvas y objetos que sueltan: hechos por codigo para el juego");
        Seccion("EL MUNDO");
        Item_("Plantas, rocas, troncos, muebles y texturas");
        Peque("Poly Haven (CC0)  ·  polyhaven.com");
        Peque("dandelion_01 · flower_gazania · flower_ursinia · shrub_sorrel_01 · fern_02 · nettle_plant\nrock_07 · rock_09 · namaqualand_boulder_05 · tree_stump_01 · dead_tree_trunk · fir_sapling\nSchoolDesk_01 · SchoolChair_01 · can_rusted · old_tyre · cardboard_box_01 · forest_ground_04");
        Item_("Hormiguero, arbol de la colmena, cabina, mapa y baterias");
        Peque("Generados por codigo para Beyond Small");
        Seccion("SONIDOS");
        Item_("Mas de 50 sonidos de la comunidad de Freesound (CC0)");
        Peque("freesound.org  ·  animales, pasos, golpes, cueva, colmena, antorcha, maquina y menu");
        Peque("496237 · 402450 · 536753 · 470148 · 502211 · 175228 · 467700 · 700418 · 649982 · 800277\n421135 · 686320 · 420670 · 523372 · 818888 · 650379 · 726877 · 757292 · 631985 · 353199\n817927 · 386385 · 422742 · 495117 · 854269 · 844243 · 375925 · 742835 · 636578 · 451948 · 615047");
        Seccion("MUSICA");
        Item_("Tema del final: compuesto para Beyond Small");
        Peque("Musica del menu: Music Ethereal (archivos del proyecto)");
        Seccion("TIPOGRAFIA");
        Item_("Barlow y Barlow Condensed");
        Peque("Jeremy Tribby (SIL Open Font License)  ·  github.com/jpt/barlow");
        Seccion("TECNOLOGIAS");
        Item_("Unity 6  ·  Universal Render Pipeline  ·  Cinemachine");
        Item_("TextMesh Pro  ·  AI Navigation  ·  Shader Graph / HLSL");
        Item_("Blender  ·  Python + NumPy  ·  MCP for Unity  ·  Claude Code");
        Seccion("AGRADECIMIENTOS ESPECIALES");
        Linea("La lap de Axelin", 44, Dorado, fMenu, 60, 6);
        Peque("por aguantar cada compilacion, cada horneado y cada noche de desvelo");
        Espacio(140);
        Linea("A todos los bichos que participaron.", 28, new Color(1, 1, 1, 0.7f), fTexto);
        Peque("Ninguna hormiga resulto herida de verdad.");
        Espacio(380);
        var gracias = Linea("GRACIAS POR JUGAR", 72, Color.white, fMenu, 96, 16);
        float largo = y;
        cont.sizeDelta = new Vector2(1000, largo);

        // fotos comicas a los lados, repartidas a lo largo de los creditos
        var fotos = new List<Texture2D>(Resources.LoadAll<Texture2D>("Fotos"));
        fotos.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        var capaFotos = new GameObject("Fotos", typeof(RectTransform)).GetComponent<RectTransform>();
        capaFotos.SetParent(cont, false); capaFotos.anchorMin = capaFotos.anchorMax = new Vector2(0.5f, 1); capaFotos.pivot = new Vector2(0.5f, 1); capaFotos.sizeDelta = Vector2.zero;
        float tramo = (largo - 900f) / Mathf.Max(1, fotos.Count);
        var rnd = new System.Random(5);
        for (int i = 0; i < fotos.Count; i++)
        {
            bool izq = i % 2 == 0;
            float x = (izq ? -1 : 1) * (680f + (float)rnd.NextDouble() * 40f);
            float yy = 500f + i * tramo + (float)rnd.NextDouble() * tramo * 0.4f;
            Polaroid(capaFotos, fotos[i], new Vector2(x, -yy), (izq ? 1 : -1) * (3f + (float)rnd.NextDouble() * 5f));
        }

        // sube: empieza bajo la pantalla y para cuando "GRACIAS POR JUGAR" queda al centro
        float alturaPantalla = 1080f;
        float yInicio = 0f;                                   // la parte de arriba del rodillo en el borde de abajo
        float yFin = largo - 48f + alturaPantalla * 0.5f;
        float dur = 100f;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            cont.anchoredPosition = new Vector2(0, Mathf.Lerp(yInicio, yFin, t / dur));
            yield return null;
        }
        cont.anchoredPosition = new Vector2(0, yFin);
        // "gracias por jugar" se queda un momento, brillando
        for (float t = 0f; t < 5f; t += Time.unscaledDeltaTime)
        {
            gracias.color = Color.Lerp(Color.white, Dorado, 0.5f + 0.5f * Mathf.Sin(t * 2f));
            yield return null;
        }
    }

    void Polaroid(RectTransform padre, Texture2D foto, Vector2 pos, float giro)
    {
        float ancho = 400f, alto = ancho * foto.height / Mathf.Max(1f, foto.width);
        var sombra = Img(padre, "Sombra", new Color(0, 0, 0, 0.5f));
        var rs = sombra.rectTransform; rs.anchorMin = rs.anchorMax = new Vector2(0.5f, 1f); rs.sizeDelta = new Vector2(ancho + 36, alto + 110); rs.anchoredPosition = pos + new Vector2(8, -10); rs.localEulerAngles = new Vector3(0, 0, giro);
        var marco = Img(padre, "Polaroid", new Color(0.96f, 0.95f, 0.9f));
        var rm = marco.rectTransform; rm.anchorMin = rm.anchorMax = new Vector2(0.5f, 1f); rm.sizeDelta = new Vector2(ancho + 36, alto + 110); rm.anchoredPosition = pos; rm.localEulerAngles = new Vector3(0, 0, giro);
        var ri = new GameObject("Foto", typeof(RectTransform)).AddComponent<RawImage>();
        ri.transform.SetParent(rm, false); ri.texture = foto; ri.raycastTarget = false;
        var rr = ri.rectTransform; rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 1f); rr.pivot = new Vector2(0.5f, 1f); rr.sizeDelta = new Vector2(ancho, alto); rr.anchoredPosition = new Vector2(0, -18);
        string pie;
        if (!Pies.TryGetValue(foto.name, out pie)) pie = "";
        var t = Txt(rm, "Pie", pie, 26, rec != null ? rec.fuenteTexto : null, TextAlignmentOptions.Center);
        var rt = t.rectTransform; rt.anchorMin = new Vector2(0, 0); rt.anchorMax = new Vector2(1, 0); rt.pivot = new Vector2(0.5f, 0); rt.sizeDelta = new Vector2(-30, 80); rt.anchoredPosition = new Vector2(0, 8);
        t.color = new Color(0.2f, 0.18f, 0.16f); t.fontStyle = FontStyles.Italic;
        // cinta adhesiva
        var cinta = Img(rm, "Cinta", new Color(0.95f, 0.9f, 0.7f, 0.75f));
        var rc = cinta.rectTransform; rc.anchorMin = rc.anchorMax = new Vector2(0.5f, 1f); rc.sizeDelta = new Vector2(130, 34); rc.anchoredPosition = new Vector2(0, 6); rc.localEulerAngles = new Vector3(0, 0, -giro * 1.5f);
    }

    // ================================================================== iconos
    static Sprite Item(string id)
    {
        var inv = ControladorInventario.Instance;
        var s = inv != null ? inv.GetIconByName(id) : null;
        return s != null ? s : IconoEstrella();
    }

    static float C(float u, float v, float cx, float cy, float r) => Mathf.Clamp01((r - Mathf.Sqrt((u - cx) * (u - cx) + (v - cy) * (v - cy))) * 40f + 0.5f);
    static float Seg(float u, float v, float ax, float ay, float bx, float by, float g)
    {
        float px = u - ax, py = v - ay, dx = bx - ax, dy = by - ay;
        float h = Mathf.Clamp01((px * dx + py * dy) / (dx * dx + dy * dy));
        float ex = px - dx * h, ey = py - dy * h;
        return Mathf.Clamp01((g - Mathf.Sqrt(ex * ex + ey * ey)) * 40f + 0.5f);
    }
    static Color Blanco(float a) => new Color(1, 1, 1, Mathf.Clamp01(a));

    static Sprite IconoBicho() => IconosMapa.Dibujar("st_bicho", (u, v) =>
    {
        float a = Mathf.Max(C(u, v, 0, 0.42f, 0.22f), C(u, v, 0, 0.02f, 0.28f), C(u, v, 0, -0.45f, 0.36f));
        for (int i = 0; i < 3; i++) { float y = 0.1f - i * 0.22f; a = Mathf.Max(a, Seg(u, v, 0, y, -0.8f, y - 0.25f + i * 0.12f, 0.05f), Seg(u, v, 0, y, 0.8f, y - 0.25f + i * 0.12f, 0.05f)); }
        a = Mathf.Max(a, Seg(u, v, -0.1f, 0.55f, -0.4f, 0.9f, 0.04f), Seg(u, v, 0.1f, 0.55f, 0.4f, 0.9f, 0.04f));
        return Blanco(a);
    });

    static Sprite IconoSol() => IconosMapa.Dibujar("st_sol", (u, v) =>
    {
        float a = C(u, v, 0, 0, 0.38f);
        float ang = Mathf.Atan2(v, u), d = Mathf.Sqrt(u * u + v * v);
        float rayo = Mathf.Clamp01((Mathf.Cos(ang * 8f) - 0.55f) * 6f) * (d > 0.5f && d < 0.88f ? 1f : 0f);
        return Blanco(Mathf.Max(a, rayo));
    });

    static Sprite IconoReloj() => IconosMapa.Dibujar("st_reloj", (u, v) =>
    {
        float d = Mathf.Sqrt(u * u + v * v);
        float aro = Mathf.Clamp01((0.1f - Mathf.Abs(d - 0.72f)) * 40f);
        float agujas = Mathf.Max(Seg(u, v, 0, 0, 0, 0.5f, 0.07f), Seg(u, v, 0, 0, 0.36f, -0.1f, 0.07f));
        return Blanco(Mathf.Max(aro, agujas));
    });

    static Sprite IconoHuella() => IconosMapa.Dibujar("st_huella", (u, v) =>
    {
        float a = Mathf.Max(C(u, v, -0.25f, -0.25f, 0.26f), C(u, v, -0.25f, 0.12f, 0.2f));
        a = Mathf.Max(a, C(u, v, 0.32f, 0.1f, 0.22f), C(u, v, 0.32f, 0.45f, 0.16f));
        for (int i = 0; i < 3; i++) { a = Mathf.Max(a, C(u, v, -0.42f + i * 0.17f, 0.42f, 0.07f), C(u, v, 0.18f + i * 0.14f, 0.72f, 0.055f)); }
        return Blanco(a);
    });

    static Sprite IconoCorazon() => IconosMapa.Dibujar("st_corazon", (u, v) =>
    {
        float x = u * 1.15f, y = v * 1.15f + 0.15f;
        float f = Mathf.Pow(x * x + y * y - 0.6f, 3f) - x * x * y * y * y;
        return Blanco(Mathf.Clamp01(-f * 30f));
    });

    static Sprite IconoEstrella() => IconosMapa.Dibujar("st_estrella", (u, v) =>
    {
        float ang = Mathf.Atan2(v, u) + Mathf.PI / 2f, d = Mathf.Sqrt(u * u + v * v);
        float r = Mathf.Lerp(0.38f, 0.85f, Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2.5f)), 2.2f));
        return Blanco(Mathf.Clamp01((r - d) * 30f));
    });

    // ================================================================== utilidades
    static Image Img(Transform p, string n, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(p, false);
        var i = go.AddComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
    }

    static void Estirar(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    static void Arriba(RectTransform r, float y, float alto)
    {
        r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
        r.anchoredPosition = new Vector2(0, -y); r.sizeDelta = new Vector2(0, alto);
    }

    static TextMeshProUGUI Txt(Transform p, string n, string s, float tam, TMP_FontAsset f, TextAlignmentOptions al)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(p, false);
        var t = go.AddComponent<TextMeshProUGUI>(); if (f != null) t.font = f;
        t.text = s; t.fontSize = tam; t.alignment = al; t.raycastTarget = false; t.color = Color.white;
        return t;
    }
}
