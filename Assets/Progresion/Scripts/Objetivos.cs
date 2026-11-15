using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Objetivo actual y señales de las baterias (Alva Majo, "Como diseñar videojuegos con sentido":
/// un mundo abierto en el que no sabes a donde ir solo sirve para perderse; y "Mejora la jugabilidad":
/// el jugador no es adivino, siempre tiene que poder ver hacia donde va).
///
///  1. Al despertar: "Investiga la cabina".
///  2. En la bahia de la cabina se pueden rastrear las señales de energia: aparecen en la brujula
///     las cuatro baterias verdes (con un "?" hasta que descubres el lugar).
///  3. Con una bateria en la mochila el objetivo cambia a "Llevala a la cabina".
///  4. Con las cuatro puestas: "Entra en la cabina".
/// Arriba a la izquierda hay una linea con el objetivo; cuando cambia se escribe sola (TextoVivo).
/// Se puede apagar en Ajustes > Pistas del objetivo.
/// </summary>
public class Objetivos : MonoBehaviour
{
    public static Objetivos Instancia { get; private set; }
    /// <summary>La cabina ya rastreo las señales (se guarda en la partida).</summary>
    public static bool Escaneado;

    public class Senal
    {
        public string nombre, pista;
        public MapaMundo.TipoLugar lugar;
        public System.Func<Vector3> pos;
        public System.Func<bool> hecha;
        /// <summary>Consejo si el jugador llega sin preparacion (null = va bien preparado).</summary>
        public System.Func<string> consejo;
        public bool avisado;
    }
    public readonly List<Senal> senales = new List<Senal>();

    Transform jugador;
    CanvasGroup grupo;
    TextMeshProUGUI titulo, texto;
    string ultimo = "";
    float timer;
    float brillo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { Escaneado = false; Instancia = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= AlCargar;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += AlCargar;
        AlCargar(default, default);
    }

    static void AlCargar(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        if (Instancia != null) return;
        if (Object.FindAnyObjectByType<PlayerController>() == null || Object.FindAnyObjectByType<MaquinaCrecimiento>() == null) return;
        new GameObject("Objetivos").AddComponent<Objetivos>();
    }

    void Awake() { Instancia = this; }
    void OnDestroy() { if (Instancia == this) Instancia = null; }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        CrearSenales();
        Construir();
    }

    // ------------------------------------------------------------------ señales
    void CrearSenales()
    {
        senales.Clear();
        var bh = GameObject.Find("BATERIA_Hormiguero");
        var rh = FindAnyObjectByType<ReinaHormiga>(FindObjectsInactive.Include);
        var h = ZonaJuego.hormigueroEntrada;
        senales.Add(new Senal
        {
            nombre = "Señal bajo tierra", lugar = MapaMundo.TipoLugar.Hormiguero,
            pista = "La señal viene de debajo de la tierra... de un hormiguero. La reina la guarda en su camara.",
            pos = () => new Vector3(h.x, ZonaJuego.Altura(h.x, h.y), h.y),
            hecha = () => Recogida(bh),
            consejo = () => Tiene("Escudo") || Mejoras.TieneGuantes ? null
                : "Las hormigas atacan en grupo. Un <color=#ffd27a>Escudo</color> (Exoesqueleto o Borrador + Ligas) o unos <color=#ffd27a>Guantes</color> te ayudaran."
        });
        var bc = GameObject.Find("BATERIA_Colmena");
        var a = ZonaJuego.arbolCentro;
        senales.Add(new Senal
        {
            nombre = "Señal en lo alto", lugar = MapaMundo.TipoLugar.Arbol,
            pista = "La señal zumba en lo alto de un arbol: una colmena. La antorcha calma a las abejas.",
            pos = () => new Vector3(a.x, ZonaJuego.Altura(a.x, a.y) + 6f, a.y),
            hecha = () => Recogida(bc),
            consejo = () => Tiene("Antorcha") ? null
                : "Sin humo las abejas no perdonan. Crea una <color=#ffd27a>Antorcha</color> (Carbon + Palo) y enciendela con " + Controles.NombreTecla(Controles.Accion.Antorcha) + "."
        });
        var rey = FindAnyObjectByType<ReyRata>(FindObjectsInactive.Include);
        var reyVida = rey != null ? rey.GetComponent<RatHealth>() : null;
        var al = ZonaJuego.alcantarilla;
        senales.Add(new Senal
        {
            nombre = "Señal en la alcantarilla", lugar = MapaMundo.TipoLugar.Cloaca,
            pista = "La señal huele a cloaca. Algo enorme se la comio... el Rey Rata.",
            pos = () => new Vector3(al.x, ZonaJuego.Altura(al.x, al.y), al.y),
            hecha = () => reyVida == null || reyVida.IsDead,
            consejo = () => Tiene("Carne_Cocinada") ? null
                : "El agua de la cloaca enferma y el Rey Rata muerde fuerte. Lleva <color=#ffd27a>carne cocinada</color> para curarte."
        });
        var bb = GameObject.Find("BATERIA_Bolita");
        var hl = FindAnyObjectByType<Hamsterlandia>();
        senales.Add(new Senal
        {
            nombre = "Señal entre musica", lugar = MapaMundo.TipoLugar.Hamsterlandia,
            pista = "La señal suena a feria, en un rincon del salon. Alguien la tiene en su coleccion.",
            pos = () => hl != null ? new Vector3(hl.zona.center.x, 0f, hl.zona.max.z - 6f) : Vector3.zero,
            hecha = () => Recogida(bb)
        });
    }

    static bool Recogida(GameObject g) { return g == null || !g.activeInHierarchy; }
    static bool Tiene(string id) { var inv = ControladorInventario.Instance; return inv != null && inv.GetTotalCount(id) > 0; }

    /// <summary>
    /// Alva Majo (Ocarina of Time te da espada y escudo antes de la primera mazmorra): si el jugador
    /// llega a la zona de un jefe sin lo que necesita, el juego se lo dice antes de que muera sin saber por que.
    /// </summary>
    void Preparacion()
    {
        if (jugador == null || !Ajustes.Pistas) return;
        foreach (var s in senales)
        {
            float d = Vector2.Distance(new Vector2(jugador.position.x, jugador.position.z), new Vector2(s.pos().x, s.pos().z));
            if (d > 110f) { s.avisado = false; continue; }
            if (s.avisado || d > 55f || s.hecha() || s.consejo == null) continue;
            string c = s.consejo();
            s.avisado = true;
            if (c == null) continue;
            Aviso.Mostrar("<temblor>¡Cuidado!</temblor>", new Color(1f, 0.6f, 0.35f), 2.2f);
            SurvivalUI.Aviso(c, new Color(1f, 0.85f, 0.6f));
            brillo = 1f;
            break;
        }
    }

    /// <summary>Señales que aun no se han resuelto (las que salen en la brujula).</summary>
    public IEnumerable<Senal> Pendientes()
    {
        if (!Escaneado) yield break;
        foreach (var s in senales) if (!s.hecha()) yield return s;
    }

    public static bool LugarDescubierto(MapaMundo.TipoLugar t)
    {
        var m = MapaMundo.Instancia;
        if (m == null) return false;
        foreach (var l in m.lugares) if (l.tipo == t && l.descubierto) return true;
        return false;
    }

    /// <summary>La cabina rastrea las baterias: aparecen en la brujula.</summary>
    public static void Escanear()
    {
        if (Escaneado) return;
        Escaneado = true;
        Juice.Sacudir(0.2f);
        Juice.Destello(new Color(0.3f, 1f, 0.55f, 0.35f), 0.9f);
        Aviso.Mostrar("<ola>4 señales de energia</ola>", new Color(0.55f, 1f, 0.65f), 3.5f);
        SurvivalUI.Aviso("Mira la brujula: cada señal es una bateria verde", new Color(0.7f, 1f, 0.75f));
        if (Instancia != null) Instancia.brillo = 1f;
    }

    // ------------------------------------------------------------------ objetivo actual
    string Calcular(out string detalle)
    {
        detalle = "";
        var maq = MaquinaCrecimiento.Instancia;
        if (maq == null) return "";
        if (maq.Encendida) return "Entra en la cabina encendida";
        var inv = ControladorInventario.Instance;
        int llevo = inv != null ? inv.GetTotalCount(MaquinaCrecimiento.ItemBateria) : 0;
        int puestas = maq.Colocadas;
        if (!Escaneado)
        {
            if (puestas == 0 && llevo == 0) { detalle = "Algo hizo ruido... la consola tiene una bahia de energia"; return "Investiga la cabina en la que despertaste"; }
            detalle = "Rastrea las demas en la bahia de la cabina";
        }
        if (llevo > 0) { detalle = $"Baterias puestas: {puestas}/{MaquinaCrecimiento.Necesarias}"; return llevo == 1 ? "Lleva la bateria verde a la cabina" : $"Lleva las {llevo} baterias verdes a la cabina"; }

        // la señal pendiente mas cercana
        Senal mejor = null; float dm = float.MaxValue;
        foreach (var s in Pendientes())
        {
            float d = jugador != null ? Vector3.Distance(jugador.position, s.pos()) : 0f;
            if (d < dm) { dm = d; mejor = s; }
        }
        if (mejor != null) detalle = LugarDescubierto(mejor.lugar) ? mejor.pista : mejor.nombre + ": sigue la brujula";

        // Alva Majo (puzles): todas las piezas claras, o que este claro que falta algo
        var hl = Hamsterlandia.Instancia;
        if (hl != null && jugador != null && hl.zona.Contains(jugador.position))
        {
            int n = 0, bits = hl.PalancasGuardadas;
            for (int i = 0; i < 3; i++) if ((bits & (1 << i)) != 0) n++;
            if (hl.estado == Hamsterlandia.Estado.Dormido)
                detalle = n < 3 ? $"Hamsterlandia esta apagada: enciende las palancas del parque ({n}/3)"
                                : "Todo encendido... falta la palanca del escenario";
            else if (hl.estado == Hamsterlandia.Estado.Persecucion) detalle = "¡Bolita te persigue! Su casita tiene la puerta chiquita: se atora";
            else if (hl.estado == Hamsterlandia.Estado.Huida) detalle = "¡Sal del parque con la pila!";
        }
        return $"Consigue baterias verdes  ({puestas}/{MaquinaCrecimiento.Necesarias})";
    }

    void Update()
    {
        bool visible = Ajustes.Pistas && jugador != null && !IntroDespertar.Activa && !GestorPartida.Muerto && !FinalJuego.Activo
                       && !MapaUI.Abierto && Time.timeScale > 0f;
        grupo.alpha = Mathf.MoveTowards(grupo.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 3f);
        brillo = Mathf.MoveTowards(brillo, 0f, Time.unscaledDeltaTime * 0.8f);
        titulo.color = Color.Lerp(new Color(0.75f, 0.95f, 0.8f, 0.85f), new Color(0.6f, 1f, 0.6f), brillo);

        timer -= Time.unscaledDeltaTime;
        if (timer > 0f) return;
        timer = 0.5f;
        if (visible) Preparacion();
        string detalle;
        string obj = Calcular(out detalle);
        string todo = obj + "\n<size=75%><color=#cfd8cf>" + detalle + "</color></size>";
        // siguiente logro y la tecla del cuaderno de misiones (se actualiza sin reescribir el objetivo)
        var sig = Logros.Siguiente();
        todo += "\n<size=62%><color=#ffd27a>" + (sig != null ? "Siguiente logro: " + sig.titulo + " " + Logros.Valor(sig) + "/" + sig.meta + "   " : "")
                + "[" + Controles.NombreTecla(Controles.Accion.Misiones) + "] misiones y logros</color></size>";
        if (todo != ultimo)
        {
            bool cambioObjetivo = ultimo.Split('\n')[0] != obj;
            ultimo = todo;
            if (cambioObjetivo) { TextoVivo.Escribir(texto, todo, 55f); brillo = 1f; }
            else { var v = texto.GetComponent<TextoVivo>(); if (v != null) v.Poner(todo); v?.Completar(); }
        }
    }

    // ------------------------------------------------------------------ HUD
    void Construir()
    {
        var c = gameObject.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 19;
        var cs = gameObject.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.matchWidthOrHeight = 0.5f;
        grupo = gameObject.AddComponent<CanvasGroup>(); grupo.alpha = 0f; grupo.blocksRaycasts = false; grupo.interactable = false;
        var rec = Resources.Load<MenuRecursos>("MenuRecursos");

        titulo = Txt("Titulo", "OBJETIVO", 20, new Vector2(34, -96), rec != null ? rec.fuenteMenu : null);
        titulo.characterSpacing = 8;
        texto = Txt("Texto", "", 26, new Vector2(34, -122), rec != null ? rec.fuenteTexto : null);
        texto.rectTransform.sizeDelta = new Vector2(620, 150);
        texto.textWrappingMode = TextWrappingModes.Normal;
    }

    TextMeshProUGUI Txt(string n, string s, float tam, Vector2 pos, TMP_FontAsset f)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (f != null) t.font = f;
        t.text = s; t.fontSize = tam; t.alignment = TextAlignmentOptions.TopLeft; t.raycastTarget = false;
        t.outlineWidth = 0.22f; t.outlineColor = new Color32(0, 0, 0, 210);
        var rt = t.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(620, 30);
        return t;
    }
}
