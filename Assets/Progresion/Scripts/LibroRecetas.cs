using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Libro de recetas del inventario (estilo Minecraft): un posit "RECETAS" en la pagina derecha del cuaderno abre
/// todas las fusiones. Cada fila enseña los dos ingredientes y el resultado; si los tienes, la casilla del
/// resultado se pone verde y con un clic se crea sin arrastrar nada. Si te falta algo, la fila te dice QUE
/// falta y DONDE se consigue (Alva Majo: el jugador no es adivino; la lanza que nadie te dijo que necesitabas).
/// </summary>
public class LibroRecetas : MonoBehaviour
{
    ControladorInventario inv;
    RectTransform pagina, contenido, pestanaRt;
    TextMeshProUGUI pestanaTxt, resumen;
    GameObject paginaEquipo;
    bool abierto, visto;
    float refresco;

    class Fila { public Crafteo.Receta r; public GameObject go; public Image a, b, res; public TextMeshProUGUI nombre, estado; public Button boton; public Image fondoBoton; public TextMeshProUGUI txtBoton; }
    readonly List<Fila> filas = new List<Fila>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= AlCargar;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += AlCargar;
        AlCargar(default, default);
    }

    static void AlCargar(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        var inv = Object.FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (inv == null || inv.inventarioUI == null || inv.inventarioUI.GetComponent<LibroRecetas>() != null) return;
        inv.inventarioUI.AddComponent<LibroRecetas>();
    }

    void Awake()
    {
        inv = FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        var eq = transform.Find("MiddlePage_Equipment");
        paginaEquipo = eq != null ? eq.gameObject : null;
        Construir();
        Mostrar(false);
    }

    void OnEnable() { refresco = 0f; }

    // ------------------------------------------------------------------ construir
    void Construir()
    {
        Vector2 baseEq = paginaEquipo != null ? ((RectTransform)paginaEquipo.transform).anchoredPosition : new Vector2(317f, 8f);
        Vector2 tamEq = paginaEquipo != null ? ((RectTransform)paginaEquipo.transform).sizeDelta : new Vector2(398f, 466f);

        // la pagina (encima de la de equipamiento)
        var hoja = EstiloCuaderno.Img(transform, "PaginaRecetas", EstiloCuaderno.Papel(), Color.white);
        hoja.type = Image.Type.Sliced; hoja.raycastTarget = true;
        pagina = hoja.rectTransform;
        pagina.anchorMin = pagina.anchorMax = new Vector2(0.5f, 0.5f);
        pagina.anchoredPosition = baseEq; pagina.sizeDelta = tamEq + new Vector2(10f, 6f);
        pagina.localRotation = Quaternion.Euler(0, 0, 0.6f);

        var tit = EstiloCuaderno.Txt(pagina, "Titulo", "LIBRO DE RECETAS", 28, EstiloCuaderno.Tinta, TextAlignmentOptions.Top, true);
        EstiloCuaderno.Rect(tit, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -50), new Vector2(-20, -12));
        var subr = EstiloCuaderno.Img(pagina, "Subrayado", null, EstiloCuaderno.Rojo);
        EstiloCuaderno.Rect(subr, new Vector2(0.18f, 1), new Vector2(0.82f, 1), new Vector2(0, -53), new Vector2(0, -50));
        resumen = EstiloCuaderno.Txt(pagina, "Resumen", "", 16, EstiloCuaderno.TintaSuave, TextAlignmentOptions.Top);
        EstiloCuaderno.Rect(resumen, new Vector2(0, 1), new Vector2(1, 1), new Vector2(20, -78), new Vector2(-20, -56));

        // lista con scroll
        var vista = EstiloCuaderno.Img(pagina, "Vista", null, new Color(1, 1, 1, 0.001f));
        vista.raycastTarget = true;
        var rv = EstiloCuaderno.Rect(vista, Vector2.zero, Vector2.one, new Vector2(14, 64), new Vector2(-12, -82));
        vista.gameObject.AddComponent<RectMask2D>();
        contenido = new GameObject("Contenido", typeof(RectTransform)).GetComponent<RectTransform>();
        contenido.SetParent(rv, false);
        contenido.anchorMin = new Vector2(0, 1); contenido.anchorMax = new Vector2(1, 1); contenido.pivot = new Vector2(0.5f, 1f);
        contenido.offsetMin = contenido.offsetMax = Vector2.zero;
        var vl = contenido.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 6f; vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false; vl.padding = new RectOffset(2, 2, 2, 10);
        contenido.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var sr = vista.gameObject.AddComponent<ScrollRect>();
        sr.content = contenido; sr.viewport = rv; sr.horizontal = false; sr.scrollSensitivity = 30f; sr.movementType = ScrollRect.MovementType.Clamped;

        foreach (var r in Crafteo.Recetas) filas.Add(CrearFila(r));

        // posit amarillo abajo a la derecha de la pagina: cambia entre RECETAS y EQUIPO
        var pest = EstiloCuaderno.Img(transform, "PositRecetas", null, new Color(1f, 0.88f, 0.35f));
        pest.raycastTarget = true;
        pestanaRt = pest.rectTransform;
        pestanaRt.anchorMin = pestanaRt.anchorMax = new Vector2(0.5f, 0.5f);
        pestanaRt.anchoredPosition = new Vector2(baseEq.x + tamEq.x * 0.5f - 74f, baseEq.y - tamEq.y * 0.5f + 34f);
        pestanaRt.sizeDelta = new Vector2(124f, 46f);
        pestanaRt.localRotation = Quaternion.Euler(0, 0, 4f);
        var sombra = pest.gameObject.AddComponent<Shadow>(); sombra.effectDistance = new Vector2(3, -3); sombra.effectColor = new Color(0, 0, 0, 0.35f);
        pestanaTxt = EstiloCuaderno.Txt(pestanaRt, "Txt", "RECETAS", 21, EstiloCuaderno.Rojo, TextAlignmentOptions.Center, true);
        EstiloCuaderno.Rect(pestanaTxt, Vector2.zero, Vector2.one, new Vector2(4, 2), new Vector2(-4, -2));
        var bt = pest.gameObject.AddComponent<Button>();
        bt.onClick.AddListener(() => { Mostrar(!abierto); SonidosJuice.Tocar(SonidosJuice.Letra, Camera.main != null ? Camera.main.transform.position : Vector3.zero, 1f); });
    }

    Fila CrearFila(Crafteo.Receta r)
    {
        var f = new Fila { r = r };
        var fondo = EstiloCuaderno.Img(contenido, "Receta_" + r.resultado, null, new Color(1f, 1f, 1f, 0.0f));
        f.go = fondo.gameObject;
        var le = f.go.AddComponent<LayoutElement>(); le.preferredHeight = 90f;
        var rt = fondo.rectTransform;

        float x = 2f;
        f.a = Icono(rt, ref x, 40f); Signo(rt, ref x, "+");
        f.b = Icono(rt, ref x, 40f); Signo(rt, ref x, "=");
        f.res = Icono(rt, ref x, 54f);
        // estilo Minecraft: clic en el resultado para crearlo
        var cajaRes = f.res.transform.parent.GetComponent<Image>();
        cajaRes.raycastTarget = true;
        f.fondoBoton = cajaRes;
        f.boton = cajaRes.gameObject.AddComponent<Button>();
        f.boton.transition = Selectable.Transition.None;
        var ff = f;
        f.boton.onClick.AddListener(() => Crear(ff));
        f.txtBoton = EstiloCuaderno.Txt(cajaRes.transform, "Crear", "", 13, EstiloCuaderno.Verde, TextAlignmentOptions.Bottom, true);
        EstiloCuaderno.Rect(f.txtBoton, new Vector2(0, 0), new Vector2(1, 0), new Vector2(-6, -16), new Vector2(6, 2));

        f.nombre = EstiloCuaderno.Txt(rt, "Nombre", "", 19, EstiloCuaderno.Tinta, TextAlignmentOptions.TopLeft, false);
        EstiloCuaderno.Rect(f.nombre, new Vector2(0, 1), new Vector2(1, 1), new Vector2(x + 8, -32), new Vector2(-2, 0));
        f.nombre.fontStyle = FontStyles.Bold;
        f.nombre.textWrappingMode = TextWrappingModes.NoWrap; f.nombre.overflowMode = TextOverflowModes.Ellipsis;
        f.estado = EstiloCuaderno.Txt(rt, "Estado", "", 14f, EstiloCuaderno.TintaSuave, TextAlignmentOptions.TopLeft, false);
        EstiloCuaderno.Rect(f.estado, new Vector2(0, 0), new Vector2(1, 1), new Vector2(x + 8, 0), new Vector2(-2, -29));
        f.estado.overflowMode = TextOverflowModes.Ellipsis;

        // linea de lapiz entre recetas
        var linea = EstiloCuaderno.Img(rt, "Linea", null, EstiloCuaderno.Lapiz);
        EstiloCuaderno.Rect(linea, new Vector2(0, 0), new Vector2(1, 0), new Vector2(4, -3), new Vector2(-4, -2));
        return f;
    }

    static Image Icono(RectTransform padre, ref float x, float tam)
    {
        var caja = EstiloCuaderno.Img(padre, "Caja", EstiloCuaderno.Caja(), Color.white);
        var r = EstiloCuaderno.Rect(caja, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, -tam * 0.5f), new Vector2(x + tam, tam * 0.5f));
        var ic = EstiloCuaderno.Img(r, "Icono", null, Color.white);
        ic.preserveAspect = true;
        EstiloCuaderno.Rect(ic, Vector2.zero, Vector2.one, new Vector2(4, 4), new Vector2(-4, -4));
        x += tam;
        return ic;
    }

    static void Signo(RectTransform padre, ref float x, string s)
    {
        var t = EstiloCuaderno.Txt(padre, "Signo", s, 22, EstiloCuaderno.Tinta, TextAlignmentOptions.Center, true);
        EstiloCuaderno.Rect(t, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, -16), new Vector2(x + 16, 16));
        x += 16f;
    }

    // ------------------------------------------------------------------ uso
    void Mostrar(bool si)
    {
        abierto = si;
        if (pagina != null) pagina.gameObject.SetActive(si);
        if (paginaEquipo != null) paginaEquipo.SetActive(!si);
        if (pestanaTxt != null) pestanaTxt.text = si ? "EQUIPO" : "RECETAS";
        if (si) { visto = true; refresco = 0f; }
    }

    int Cuenta(string id) => inv != null ? inv.GetTotalCount(id) : 0;

    void Update()
    {
        // el posit late hasta que se abre por primera vez; las recetas que se pueden crear laten un poco
        if (pestanaRt != null) pestanaRt.localScale = Vector3.one * (visto ? 1f : 1f + 0.07f * Mathf.Sin(Time.unscaledTime * 5f));
        if (!abierto) return;
        foreach (var f in filas)
            if (f.fondoBoton != null && (f.fondoBoton.GetComponent<Rebote>() == null || !f.fondoBoton.GetComponent<Rebote>().enabled))
                f.fondoBoton.transform.localScale = Vector3.one * (f.boton.interactable ? 1f + 0.05f * Mathf.Sin(Time.unscaledTime * 6f) : 1f);
        refresco -= Time.unscaledDeltaTime;
        if (refresco > 0f) return;
        refresco = 0.3f;
        Refrescar();
    }

    void Refrescar()
    {
        int listas = 0;
        // orden: lo que puedes crear ya, luego lo que te falta poco, luego el resto
        filas.Sort((x, y) => Puntos(y.r).CompareTo(Puntos(x.r)));
        for (int i = 0; i < filas.Count; i++)
        {
            var f = filas[i];
            f.go.transform.SetSiblingIndex(i);
            int na = Cuenta(f.r.a), nb = Cuenta(f.r.b);
            bool puede = na > 0 && nb > 0 && (f.r.a != f.r.b || na > 1);
            if (puede) listas++;
            Poner(f.a, f.r.a, na > 0); Poner(f.b, f.r.b, nb > 0); Poner(f.res, f.r.resultado, true);
            var it = Catalogo.Get(f.r.resultado);
            int ya = Cuenta(f.r.resultado);
            f.nombre.text = Catalogo.Nombre(f.r.resultado) + (ya > 0 ? " <size=75%><color=#2a7a35>(tienes " + ya + ")</color></size>" : "");
            if (puede) f.estado.text = "<color=#2a7a35>¡Clic en la casilla verde para crear!</color>\n" + (it != null ? it.descripcion : "");
            else
            {
                var falta = new List<string>();
                if (na <= 0) falta.Add(Falta(f.r.a));
                if (nb <= 0) falta.Add(Falta(f.r.b));
                f.estado.text = (it != null ? it.descripcion + "\n" : "") + "<color=#b5302a>Falta:</color> " + string.Join(" · ", falta);
            }
            f.boton.interactable = puede;
            // la casilla del resultado se pinta de verde si se puede crear
            f.fondoBoton.color = puede ? EstiloCuaderno.Verde : Color.white;
            f.txtBoton.text = puede ? "CREAR" : "";
        }
        resumen.text = listas > 0 ? "<color=#2a7a35>Puedes crear " + listas + " cosa" + (listas == 1 ? "" : "s") + " ahora mismo</color>"
                                  : "Recoge materiales: cada objeto del mundo sirve para algo";
    }

    int Puntos(Crafteo.Receta r)
    {
        int na = Cuenta(r.a) > 0 ? 1 : 0, nb = Cuenta(r.b) > 0 ? 1 : 0;
        int p = (na + nb) * 10;
        if (Cuenta(r.resultado) > 0) p -= 5;    // lo que ya tienes, un poco mas abajo
        return p;
    }

    static string Falta(string id)
    {
        var it = Catalogo.Get(id);
        return "<b>" + Catalogo.Nombre(id) + "</b>" + (it != null && !string.IsNullOrEmpty(it.donde) ? " <size=90%>(" + it.donde.ToLower() + ")</size>" : "");
    }

    void Poner(Image img, string id, bool tengo)
    {
        var s = inv != null ? inv.GetIconByName(id) : null;
        img.sprite = s; img.enabled = s != null;
        img.color = tengo ? Color.white : new Color(0.25f, 0.22f, 0.2f, 0.45f);   // silueta si no lo tienes
    }

    void Crear(Fila f)
    {
        if (inv == null) return;
        int na = Cuenta(f.r.a), nb = Cuenta(f.r.b);
        if (na <= 0 || nb <= 0) return;
        if (!inv.RemoveItem(f.r.a, 1)) return;
        if (!inv.RemoveItem(f.r.b, 1)) { inv.AddItem(f.r.a, inv.GetIconByName(f.r.a), 1); return; }
        if (!inv.AddItem(f.r.resultado, inv.GetIconByName(f.r.resultado), f.r.cantidad))
        {
            inv.AddItem(f.r.a, inv.GetIconByName(f.r.a), 1); inv.AddItem(f.r.b, inv.GetIconByName(f.r.b), 1);
            SurvivalUI.Aviso("No hay espacio en la mochila", new Color(1f, 0.6f, 0.4f));
            return;
        }
        Estadisticas.Creado(f.r.resultado);
        SurvivalUI.Aviso("¡Creaste: " + Catalogo.Nombre(f.r.resultado) + "!", new Color(1f, 0.75f, 0.35f));
        var donde = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        SonidosJuice.Tocar(SonidosJuice.GolpeFuerte, donde, 0.6f);
        SonidosJuice.Tocar(SonidosJuice.Recoger, donde, 0.9f);
        // la casilla del resultado salta (squash) para que se note
        Juice.Aplastar(f.fondoBoton.transform, 0.35f);
        refresco = 0f;
    }
}
