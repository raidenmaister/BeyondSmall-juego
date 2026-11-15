using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cuaderno de MISIONES y LOGROS (tecla J). Dos paginas: a la izquierda la historia (una mision por jefe y la final),
/// con la actual marcada y su pista; a la derecha los logros con su barra de progreso y el "siguiente logro".
/// Pausa el juego mientras esta abierto.
/// </summary>
public class PanelMisiones : MonoBehaviour
{
    public static bool Abierto { get; private set; }
    static int cuadroCierre = -10;
    /// <summary>El Esc que cierra este cuaderno no debe abrir tambien el menu de pausa.</summary>
    public static bool BloqueaPausa => Abierto || Time.frameCount - cuadroCierre <= 1;

    Canvas lienzo;
    RectTransform libro, listaMisiones, listaLogros;
    TextMeshProUGUI cabLogros, siguiente;
    float escalaAntes = 1f;
    bool bloqueadoAntes;
    float anim;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { Abierto = false; }

    void Start() { Construir(); lienzo.gameObject.SetActive(false); }

    void Update()
    {
        bool tecla = Input.GetKeyDown(Controles.Tecla(Controles.Accion.Misiones));
        if (Abierto)
        {
            if (tecla || Input.GetKeyDown(KeyCode.Escape)) Cerrar();
            else Animar();
            return;
        }
        if (!tecla || Controles.Bloqueado || Time.timeScale <= 0f) return;
        if (IntroDespertar.Activa || GestorPartida.Muerto || FinalJuego.Activo || MapaUI.Abierto) return;
        if (ControladorInventario.Instance != null && ControladorInventario.Instance.IsOpen) return;
        Abrir();
    }

    void Abrir()
    {
        Abierto = true;
        escalaAntes = Time.timeScale; bloqueadoAntes = Controles.Bloqueado;
        Time.timeScale = 0f; Controles.Bloqueado = true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        lienzo.gameObject.SetActive(true);
        Rellenar();
        anim = 0f;
        SonidosJuice.Tocar(SonidosJuice.Letra, Camera.main != null ? Camera.main.transform.position : Vector3.zero, 1f);
    }

    void Cerrar()
    {
        Abierto = false;
        cuadroCierre = Time.frameCount;
        lienzo.gameObject.SetActive(false);
        Time.timeScale = escalaAntes > 0f ? escalaAntes : 1f; Controles.Bloqueado = bloqueadoAntes;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }

    void OnDestroy() { if (Abierto) { Time.timeScale = 1f; Controles.Bloqueado = false; Abierto = false; } }

    void Animar()
    {
        anim += Time.unscaledDeltaTime;
        float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(anim / 0.35f), 3f);
        libro.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, k);
        libro.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-4f, -0.6f, k));
    }

    // ------------------------------------------------------------------ construir
    void Construir()
    {
        lienzo = EstiloCuaderno.Lienzo("CuadernoMisiones", 70);
        lienzo.transform.SetParent(transform, false);
        var velo = EstiloCuaderno.Img(lienzo.transform, "Velo", null, new Color(0.05f, 0.04f, 0.03f, 0.72f));
        velo.raycastTarget = true;
        EstiloCuaderno.Rect(velo, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        libro = new GameObject("Libro", typeof(RectTransform)).GetComponent<RectTransform>();
        libro.SetParent(lienzo.transform, false);
        libro.sizeDelta = new Vector2(1560f, 900f);

        var izq = Hoja(libro, "PaginaMisiones", new Vector2(-392f, 0f));
        var der = Hoja(libro, "PaginaLogros", new Vector2(392f, 0f));
        // espiral del cuaderno
        for (int i = 0; i < 14; i++)
        {
            var aro = EstiloCuaderno.Img(libro, "Aro", IconosMapa.Dibujar("aro", (u, v) =>
            {
                float d = Mathf.Abs(Mathf.Sqrt(u * u + v * v) - 0.6f);
                return new Color(0.55f, 0.56f, 0.6f, Mathf.Clamp01((0.18f - d) * 12f));
            }), Color.white);
            var r = aro.rectTransform; r.sizeDelta = new Vector2(46, 46); r.anchoredPosition = new Vector2(0, 390 - i * 60);
        }

        // ---- misiones
        Titulo(izq, "MISIONES");
        var sub = EstiloCuaderno.Txt(izq, "Sub", "La historia: una bateria por cada jefe", 19, EstiloCuaderno.TintaSuave, TextAlignmentOptions.Top);
        EstiloCuaderno.Rect(sub, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -112), new Vector2(-40, -84));
        listaMisiones = Lista(izq, 120f);

        // ---- logros
        Titulo(der, "LOGROS");
        cabLogros = EstiloCuaderno.Txt(der, "Cab", "", 19, EstiloCuaderno.TintaSuave, TextAlignmentOptions.Top);
        EstiloCuaderno.Rect(cabLogros, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -112), new Vector2(-40, -84));
        siguiente = EstiloCuaderno.Txt(der, "Siguiente", "", 18, EstiloCuaderno.Rojo, TextAlignmentOptions.Top);
        EstiloCuaderno.Rect(siguiente, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -140), new Vector2(-40, -112));
        listaLogros = Lista(der, 148f);

        var pie = EstiloCuaderno.Txt(libro, "Pie", "[" + Controles.NombreTecla(Controles.Accion.Misiones) + "] o [Esc] para cerrar", 18, new Color(0.9f, 0.86f, 0.78f), TextAlignmentOptions.Center);
        EstiloCuaderno.Rect(pie, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -44), new Vector2(0, -10));
    }

    static RectTransform Hoja(RectTransform padre, string n, Vector2 pos)
    {
        var h = EstiloCuaderno.Img(padre, n, EstiloCuaderno.Papel(), Color.white);
        h.type = Image.Type.Sliced; h.raycastTarget = true;
        var r = h.rectTransform; r.sizeDelta = new Vector2(760f, 880f); r.anchoredPosition = pos;
        r.localRotation = Quaternion.Euler(0, 0, pos.x < 0 ? 0.5f : -0.4f);
        return r;
    }

    static void Titulo(RectTransform hoja, string s)
    {
        var t = EstiloCuaderno.Txt(hoja, "Titulo", s, 46, EstiloCuaderno.Tinta, TextAlignmentOptions.Top, true);
        EstiloCuaderno.Rect(t, new Vector2(0, 1), new Vector2(1, 1), new Vector2(40, -80), new Vector2(-40, -18));
        var l = EstiloCuaderno.Img(hoja, "Subrayado", null, EstiloCuaderno.Rojo);
        EstiloCuaderno.Rect(l, new Vector2(0.3f, 1), new Vector2(0.7f, 1), new Vector2(0, -84), new Vector2(0, -80));
    }

    static RectTransform Lista(RectTransform hoja, float arriba)
    {
        var vista = EstiloCuaderno.Img(hoja, "Vista", null, new Color(1, 1, 1, 0.001f));
        vista.raycastTarget = true;
        var rv = EstiloCuaderno.Rect(vista, Vector2.zero, Vector2.one, new Vector2(70, 30), new Vector2(-30, -arriba));
        vista.gameObject.AddComponent<RectMask2D>();
        var c = new GameObject("Contenido", typeof(RectTransform)).GetComponent<RectTransform>();
        c.SetParent(rv, false);
        c.anchorMin = new Vector2(0, 1); c.anchorMax = new Vector2(1, 1); c.pivot = new Vector2(0.5f, 1f);
        c.offsetMin = c.offsetMax = Vector2.zero;
        var vl = c.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 10f; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
        c.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var sr = vista.gameObject.AddComponent<ScrollRect>();
        sr.content = c; sr.viewport = rv; sr.horizontal = false; sr.scrollSensitivity = 35f; sr.movementType = ScrollRect.MovementType.Clamped;
        return c;
    }

    // ------------------------------------------------------------------ rellenar
    void Rellenar()
    {
        foreach (Transform t in listaMisiones) Destroy(t.gameObject);
        foreach (Transform t in listaLogros) Destroy(t.gameObject);

        var actual = Logros.MisionActual();
        int n = 1;
        foreach (var m in Logros.Misiones) FilaMision(m, n++, m == actual);

        int hechos = 0; foreach (var l in Logros.Todos) if (Logros.Desbloqueado(l)) hechos++;
        cabLogros.text = "Desbloqueados: <b>" + hechos + " / " + Logros.Todos.Count + "</b>";
        var sig = Logros.Siguiente();
        siguiente.text = sig != null ? "Siguiente logro: <b>" + sig.titulo + "</b>  (" + Logros.Valor(sig) + "/" + sig.meta + ")" : "¡Todos los logros conseguidos!";
        // primero los conseguidos? no: primero los que estan cerca, luego los hechos al final
        var orden = new List<Logros.Logro>(Logros.Todos);
        orden.Sort((a, b) =>
        {
            int da = Logros.Desbloqueado(a) ? 1 : 0, db = Logros.Desbloqueado(b) ? 1 : 0;
            if (da != db) return da.CompareTo(db);
            return ((float)Logros.Valor(b) / b.meta).CompareTo((float)Logros.Valor(a) / a.meta);
        });
        foreach (var l in orden) FilaLogro(l);
    }

    void FilaMision(Logros.Mision m, int numero, bool actual)
    {
        bool hecha = Logros.Cumplida(m), disp = Logros.Disponible(m);
        var go = new GameObject("Mision_" + m.id, typeof(RectTransform));
        go.transform.SetParent(listaMisiones, false);
        var r = (RectTransform)go.transform;
        float alto = actual ? 150f : 88f;
        go.AddComponent<LayoutElement>().preferredHeight = alto;

        if (actual)
        {
            // resaltado de marcador amarillo
            var fondo = EstiloCuaderno.Img(r, "Marcador", null, new Color(1f, 0.9f, 0.3f, 0.28f));
            EstiloCuaderno.Rect(fondo, Vector2.zero, Vector2.one, new Vector2(-8, 0), new Vector2(0, 0));
        }
        var caja = EstiloCuaderno.Img(r, "Casilla", EstiloCuaderno.Caja(), Color.white);
        EstiloCuaderno.Rect(caja, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -44), new Vector2(40, -4));
        if (hecha)
        {
            var c = EstiloCuaderno.Img(caja.transform, "Check", EstiloCuaderno.Check(), Color.white);
            EstiloCuaderno.Rect(c, Vector2.zero, Vector2.one, new Vector2(-6, -2), new Vector2(10, 14));
        }
        string titulo = disp || hecha ? m.titulo : "???";
        string tit = (m.final ? "Final: " : numero + ". ") + titulo;
        var t = EstiloCuaderno.Txt(r, "Titulo", hecha ? "<s>" + tit + "</s>" : tit, 28, hecha ? EstiloCuaderno.TintaSuave : EstiloCuaderno.Tinta, TextAlignmentOptions.TopLeft, true);
        EstiloCuaderno.Rect(t, new Vector2(0, 1), new Vector2(1, 1), new Vector2(54, -40), new Vector2(0, -2));
        string desc = disp || hecha ? m.descripcion : "Se desbloquea al avanzar en la historia.";
        if (hecha) desc = "<color=#2a7a35>Cumplida</color>  ·  " + desc;
        else if (m.progreso != null && disp) desc += "\n<color=#7a5a20>" + m.progreso() + "</color>";
        var d = EstiloCuaderno.Txt(r, "Desc", desc, 18, EstiloCuaderno.TintaSuave);
        EstiloCuaderno.Rect(d, new Vector2(0, 1), new Vector2(1, 1), new Vector2(54, -alto + 8), new Vector2(0, -40));
        if (actual && !string.IsNullOrEmpty(m.pista))
        {
            d.text += "\n<color=#b5302a>→ " + m.pista + "</color>";
        }
        if (!hecha && disp && m.recompensa.Length > 0)
        {
            var premio = "";
            foreach (var x in m.recompensa) premio += (premio.Length > 0 ? ", " : "") + x.n + " " + Catalogo.Nombre(x.id).ToLower();
            d.text += "\n<size=85%>Recompensa: " + premio + "</size>";
        }
    }

    void FilaLogro(Logros.Logro l)
    {
        bool hecho = Logros.Desbloqueado(l);
        bool oculto = l.secreto && !hecho;
        var go = new GameObject("Logro_" + l.id, typeof(RectTransform));
        go.transform.SetParent(listaLogros, false);
        var r = (RectTransform)go.transform;
        go.AddComponent<LayoutElement>().preferredHeight = 84f;

        var caja = EstiloCuaderno.Img(r, "Caja", EstiloCuaderno.Caja(), Color.white);
        EstiloCuaderno.Rect(caja, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, -36), new Vector2(72, 36));
        var ic = EstiloCuaderno.Img(caja.transform, "Icono", oculto ? EstiloCuaderno.Candado() : Logros.Icono(l), hecho ? Color.white : new Color(0.3f, 0.28f, 0.26f, 0.5f));
        ic.preserveAspect = true;
        EstiloCuaderno.Rect(ic, Vector2.zero, Vector2.one, new Vector2(8, 8), new Vector2(-8, -8));
        if (hecho)
        {
            var tr = EstiloCuaderno.Img(caja.transform, "Trofeo", EstiloCuaderno.Trofeo(), Color.white);
            EstiloCuaderno.Rect(tr, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, -6), new Vector2(6, 24));
        }

        var t = EstiloCuaderno.Txt(r, "Titulo", oculto ? "Logro secreto" : l.titulo, 25, hecho ? EstiloCuaderno.Verde : EstiloCuaderno.Tinta, TextAlignmentOptions.TopLeft, true);
        EstiloCuaderno.Rect(t, new Vector2(0, 1), new Vector2(1, 1), new Vector2(86, -34), new Vector2(-90, -2));
        var d = EstiloCuaderno.Txt(r, "Desc", oculto ? "???" : l.descripcion, 17, EstiloCuaderno.TintaSuave);
        EstiloCuaderno.Rect(d, new Vector2(0, 0), new Vector2(1, 1), new Vector2(86, 22), new Vector2(-90, -34));

        // barra de progreso dibujada a lapiz
        int v = Logros.Valor(l);
        float p = hecho ? 1f : (float)v / l.meta;
        var marco = EstiloCuaderno.Img(r, "Barra", EstiloCuaderno.Caja(), Color.white);
        marco.type = Image.Type.Simple;
        EstiloCuaderno.Rect(marco, new Vector2(0, 0), new Vector2(1, 0), new Vector2(86, 4), new Vector2(-90, 20));
        var lleno = EstiloCuaderno.Img(marco.transform, "Lleno", null, hecho ? new Color(0.3f, 0.65f, 0.3f, 0.85f) : new Color(0.85f, 0.55f, 0.15f, 0.8f));
        EstiloCuaderno.Rect(lleno, Vector2.zero, new Vector2(Mathf.Max(0.01f, p), 1f), new Vector2(2, 2), new Vector2(-2, -2));
        var num = EstiloCuaderno.Txt(r, "Num", hecho ? "¡Hecho!" : v + " / " + l.meta, 19, hecho ? EstiloCuaderno.Verde : EstiloCuaderno.Tinta, TextAlignmentOptions.Right, true);
        EstiloCuaderno.Rect(num, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-88, 0), new Vector2(0, 30));
    }
}
