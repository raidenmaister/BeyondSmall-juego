using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Brujula horizontal arriba de la pantalla (estilo Skyrim): puntos cardinales segun hacia donde mira
/// la camara, el icono de la cabina y las chinchetas del mapa con su distancia.
/// Si un objetivo queda fuera de la vista se pega al borde de la barra.
/// </summary>
public class BrujulaUI : MonoBehaviour
{
    const float Ancho = 760f, Alto = 50f, MedioCampo = 95f;   // grados visibles a cada lado
    static readonly string[] Puntos = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };

    MapaMundo mapa;
    MenuRecursos rec;
    Transform jugador;
    CanvasGroup grupo;
    RectTransform barra;
    readonly List<(float ang, RectTransform rt, TextMeshProUGUI t)> marcasFijas = new List<(float, RectTransform, TextMeshProUGUI)>();

    class Objetivo { public RectTransform icono; public TextMeshProUGUI dist; public System.Func<Vector3> pos; public Image img; }
    Objetivo cabina;
    readonly List<Objetivo> pines = new List<Objetivo>();
    // mochilas, botines y bichos ladrones (cambian durante el juego)
    readonly Dictionary<Component, Objetivo> bolsas = new Dictionary<Component, Objetivo>();
    static readonly Color ColorLadron = new Color(1f, 0.55f, 0.45f);

    void Start()
    {
        mapa = GetComponent<MapaMundo>();
        rec = Resources.Load<MenuRecursos>("MenuRecursos");
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        Construir();
        if (mapa != null) { mapa.Cambio += RehacerPines; RehacerPines(); }
        // la barra de vida de los enemigos estaba arriba al centro: baja para dejar sitio a la brujula
        var barraEnemigo = GameObject.Find("PlayerHUD_Canvas/RatHealthBar_Container") as GameObject;
        if (barraEnemigo != null) { var r = (RectTransform)barraEnemigo.transform; r.anchoredPosition = new Vector2(r.anchoredPosition.x, -104f); }
    }

    void OnDestroy() { if (mapa != null) mapa.Cambio -= RehacerPines; }

    void Construir()
    {
        var go = new GameObject("Brujula");
        go.transform.SetParent(transform, false);
        var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 20;
        var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.matchWidthOrHeight = 0.5f;
        grupo = go.AddComponent<CanvasGroup>(); grupo.alpha = 0f; grupo.blocksRaycasts = false; grupo.interactable = false;

        barra = new GameObject("Barra", typeof(RectTransform)).GetComponent<RectTransform>();
        barra.SetParent(go.transform, false);
        barra.anchorMin = barra.anchorMax = new Vector2(0.5f, 1f); barra.pivot = new Vector2(0.5f, 1f);
        barra.anchoredPosition = new Vector2(0, -14); barra.sizeDelta = new Vector2(Ancho, Alto);
        var fondo = Img(barra, "Fondo", Color.white); fondo.sprite = IconosMapa.FondoBrujula(); Estirar(fondo.rectTransform);
        var linea = Img(barra, "Linea", new Color(1, 1, 1, 0.25f)); var rl = linea.rectTransform;
        rl.anchorMin = new Vector2(0.08f, 0); rl.anchorMax = new Vector2(0.92f, 0); rl.sizeDelta = new Vector2(0, 2); rl.anchoredPosition = Vector2.zero;
        barra.gameObject.AddComponent<RectMask2D>().softness = new Vector2Int(90, 0);

        // marcas cada 15 grados; letras cada 45
        for (int g = 0; g < 360; g += 15)
        {
            bool letra = g % 45 == 0;
            TextMeshProUGUI t = null;
            RectTransform rt;
            if (letra)
            {
                t = Txt(barra, "Punto", Puntos[g / 45], g % 90 == 0 ? 30 : 22, rec != null ? rec.fuenteMenu : null);
                t.color = g == 0 ? new Color(1f, 0.45f, 0.35f) : new Color(1, 1, 1, g % 90 == 0 ? 0.95f : 0.7f);
                rt = t.rectTransform; rt.sizeDelta = new Vector2(60, Alto);
            }
            else
            {
                var tick = Img(barra, "Tick", new Color(1, 1, 1, 0.45f));
                rt = tick.rectTransform; rt.sizeDelta = new Vector2(2, 12);
            }
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, letra ? 0.5f : 0.25f);
            marcasFijas.Add((g, rt, t));
        }
        // centro
        var centro = Img(barra, "Centro", rec != null ? rec.acento : new Color(0.27f, 0.86f, 0.78f));
        var rc = centro.rectTransform; rc.anchorMin = rc.anchorMax = new Vector2(0.5f, 0f); rc.pivot = new Vector2(0.5f, 0f); rc.sizeDelta = new Vector2(3, 16);

        cabina = CrearObjetivo(IconosMapa.Cabina(), Color.white, 30f, () => MaquinaCrecimiento.Instancia != null ? MaquinaCrecimiento.Instancia.transform.position : Vector3.zero);
    }

    Objetivo CrearObjetivo(Sprite s, Color color, float tam, System.Func<Vector3> pos)
    {
        var o = new Objetivo { pos = pos };
        o.img = Img(barra.parent, "Objetivo", color); o.img.sprite = s;
        o.icono = o.img.rectTransform; o.icono.anchorMin = o.icono.anchorMax = new Vector2(0.5f, 1f); o.icono.sizeDelta = new Vector2(tam, tam);
        o.dist = Txt(o.icono, "Distancia", "", 17, rec != null ? rec.fuenteTexto : null);
        var rd = o.dist.rectTransform; rd.anchorMin = rd.anchorMax = new Vector2(0.5f, 0f); rd.pivot = new Vector2(0.5f, 1f); rd.anchoredPosition = new Vector2(0, -2); rd.sizeDelta = new Vector2(90, 22);
        o.dist.outlineWidth = 0.25f; o.dist.outlineColor = new Color32(0, 0, 0, 200);
        return o;
    }

    void RehacerPines()
    {
        foreach (var p in pines) if (p.icono != null) Destroy(p.icono.gameObject);
        pines.Clear();
        if (mapa == null || barra == null) return;
        foreach (var c in mapa.chinchetas)
        {
            var cc = c;
            pines.Add(CrearObjetivo(IconosMapa.Chincheta(), MapaMundo.ColoresChincheta[c.color], 26f, () => new Vector3(cc.xz.x, 0f, cc.xz.y)));
        }
    }

    void SincronizarBolsas()
    {
        // quitar las que ya no estan
        var fuera = new List<Component>();
        foreach (var kv in bolsas) if (kv.Key == null || (kv.Key is Mochila m && !Mochila.Todas.Contains(m)) || (kv.Key is Ladron l && !Ladron.Todos.Contains(l))) fuera.Add(kv.Key);
        foreach (var k in fuera) { if (bolsas[k].icono != null) Destroy(bolsas[k].icono.gameObject); bolsas.Remove(k); }
        foreach (var m in Mochila.Todas)
            if (m != null && !bolsas.ContainsKey(m)) { var mm = m; bolsas[m] = CrearObjetivo(IconosMapa.Mochila(), m.tipo == Mochila.Tipo.Mochila ? Color.white : ColorLadron, 30f, () => mm != null ? mm.transform.position : Vector3.zero); }
        foreach (var l in Ladron.Todos)
            if (l != null && !bolsas.ContainsKey(l)) { var ll = l; bolsas[l] = CrearObjetivo(IconosMapa.Mochila(), ColorLadron, 26f, () => ll != null ? ll.transform.position : Vector3.zero); }
    }

    public static string Metros(float unidades)
    {
        // escala del mundo: 1 m real = 18 unidades (el jugador mide ~11 cm)
        float m = unidades / 18f;
        return m < 10f ? m.ToString("0.0") + " m" : Mathf.RoundToInt(m) + " m";
    }

    void LateUpdate()
    {
        bool visible = jugador != null && !MapaUI.Abierto && !IntroDespertar.Activa && !GestorPartida.Muerto && !FinalJuego.Activo && Time.timeScale > 0f;
        grupo.alpha = Mathf.MoveTowards(grupo.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        if (grupo.alpha <= 0.001f) return;

        var cam = Camera.main;
        float rumbo = cam != null ? cam.transform.eulerAngles.y : jugador.eulerAngles.y;
        float pxGrado = Ancho * 0.5f / MedioCampo;

        foreach (var m in marcasFijas)
        {
            float d = Mathf.DeltaAngle(rumbo, m.ang);
            bool dentro = Mathf.Abs(d) <= MedioCampo + 10f;
            m.rt.gameObject.SetActive(dentro);
            if (dentro) m.rt.anchoredPosition = new Vector2(d * pxGrado, 0);
        }
        Ubicar(cabina, rumbo, pxGrado);
        foreach (var p in pines) Ubicar(p, rumbo, pxGrado);
        SincronizarBolsas();
        foreach (var o in bolsas.Values) Ubicar(o, rumbo, pxGrado);
    }

    void Ubicar(Objetivo o, float rumbo, float pxGrado)
    {
        if (o == null || o.icono == null) return;
        Vector3 hacia = o.pos() - jugador.position;
        float distancia = new Vector2(hacia.x, hacia.z).magnitude;
        float ang = Mathf.Atan2(hacia.x, hacia.z) * Mathf.Rad2Deg;
        float d = Mathf.DeltaAngle(rumbo, ang);
        bool fuera = Mathf.Abs(d) > MedioCampo - 6f;
        float x = Mathf.Clamp(d, -(MedioCampo - 6f), MedioCampo - 6f) * pxGrado;
        // barra.anchoredPosition.y = -14: los iconos van centrados en la barra
        o.icono.anchoredPosition = new Vector2(x, -14 - Alto * 0.5f + 4f);
        var c = o.img.color; c.a = fuera ? 0.45f : 1f; o.img.color = c;
        o.dist.text = distancia < 12f ? "" : Metros(distancia);
        o.dist.alpha = fuera ? 0.4f : 0.9f;
        if (distancia < 12f) { c.a *= 0.6f; o.img.color = c; }
    }

    static Image Img(Transform padre, string n, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(padre, false);
        var i = go.AddComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
    }

    static TextMeshProUGUI Txt(Transform padre, string n, string s, float tam, TMP_FontAsset f)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(padre, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (f != null) t.font = f;
        t.text = s; t.fontSize = tam; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        return t;
    }

    static void Estirar(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
}
