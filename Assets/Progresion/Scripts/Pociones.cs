using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pociones en tapones de botella (tapon + ingrediente) y sus efectos temporales.
/// Cada jefe tiene mas vida que el anterior: las pociones son la forma de "potenciarse" para el siguiente.
///   Pocion de vida      (tapon + carne cocinada)  +80 de vida maxima y cura 60, durante 120 s
///   Pocion de fuerza    (tapon + colmillo)        golpes x1.5 durante 60 s
///   Pocion de caparazon (tapon + exoesqueleto)    recibes un 40% menos de daño durante 60 s
///   Pocion de rapidez   (tapon + pata de ciempies) corres un 35% mas y la energia vuelve el doble de rapido, 60 s
/// Se beben con G (la de la barra rapida si hay una elegida; si no, la primera que no este ya activa)
/// o eligiendolas en la barra rapida. Los efectos activos salen con su tiempo encima de las barras.
/// </summary>
public class Pociones : MonoBehaviour
{
    public class Def { public string id, nombre, ingrediente, efecto; public float segundos; public Color color; }

    public static readonly List<Def> Todas = new List<Def>
    {
        new Def { id = "PocionVida", nombre = "Pocion de vida", ingrediente = "Carne_Cocinada", segundos = 120f, color = new Color(1f, 0.3f, 0.35f), efecto = "+80 de vida maxima y cura 60 (2 min)" },
        new Def { id = "PocionFuerza", nombre = "Pocion de fuerza", ingrediente = "Colmillo", segundos = 60f, color = new Color(1f, 0.6f, 0.15f), efecto = "Golpes x1.5 durante 1 min" },
        new Def { id = "PocionCaparazon", nombre = "Pocion de caparazon", ingrediente = "Exoesqueleto", segundos = 60f, color = new Color(0.35f, 0.55f, 1f), efecto = "Recibes un 40% menos de daño durante 1 min" },
        new Def { id = "PocionRapidez", nombre = "Pocion de rapidez", ingrediente = "PataCiempies", segundos = 60f, color = new Color(0.4f, 1f, 0.45f), efecto = "Corres un 35% mas y la energia vuelve el doble de rapido (1 min)" },
    };
    public const string Tapon = "Tapon";

    public static Def Buscar(string id) { foreach (var d in Todas) if (string.Equals(d.id, id, System.StringComparison.OrdinalIgnoreCase)) return d; return null; }

    // ------------------------------------------------------------------ efectos activos
    static readonly Dictionary<string, float> activos = new Dictionary<string, float>();   // id -> Time.time de fin
    public static bool Activo(string id) { float f; return activos.TryGetValue(id, out f) && Time.time < f; }
    public static float Restante(string id) { float f; return activos.TryGetValue(id, out f) ? Mathf.Max(0f, f - Time.time) : 0f; }

    public static float MultDanio => Activo("PocionFuerza") ? 1.5f : 1f;
    public static float MultRecibido => Activo("PocionCaparazon") ? 0.6f : 1f;
    public static float MultVelocidad => Activo("PocionRapidez") ? 1.35f : 1f;
    public static float MultEnergia => Activo("PocionRapidez") ? 2f : 1f;
    const float VidaExtra = 80f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { activos.Clear(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Recetas()
    {
        foreach (var p in Todas)
        {
            bool esta = false;
            foreach (var x in Crafteo.Recetas) if (x.resultado == p.id) esta = true;
            if (!esta) Crafteo.Recetas.Add(new Crafteo.Receta { a = Tapon, b = p.ingrediente, resultado = p.id, cantidad = 1 });
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= AlCargar;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += AlCargar;
        AlCargar(default, default);
    }

    static void AlCargar(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        var p = Object.FindAnyObjectByType<PlayerController>();
        if (p != null && p.GetComponent<Pociones>() == null) p.gameObject.AddComponent<Pociones>();
    }

    PlayerHealth vida;
    bool vidaExtraPuesta;
    RectTransform fila;
    readonly Dictionary<string, (GameObject go, Image barra, TextMeshProUGUI txt)> iconos = new Dictionary<string, (GameObject, Image, TextMeshProUGUI)>();
    bool avisado;

    void Start()
    {
        vida = GetComponent<PlayerHealth>();
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed += AlUsarRapido;
        CrearHud();
    }

    void OnDestroy() { if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed -= AlUsarRapido; }

    void AlUsarRapido(int slot, string itemId)
    {
        var d = Buscar(itemId);
        if (d == null) return;
        Beber(d);
        if (QuickAccessBar.Instance != null && QuickAccessBar.Instance.SelectedIndex == slot) QuickAccessBar.Instance.Select(slot);
    }

    static int Cuantas(string id) => ControladorInventario.Instance != null ? ControladorInventario.Instance.GetTotalCount(id) : 0;

    public void Beber(Def d)
    {
        if (d == null || vida == null || vida.IsDead) return;
        if (Cuantas(d.id) <= 0 || !ControladorInventario.Instance.RemoveItem(d.id, 1)) return;
        activos[d.id] = Mathf.Max(Time.time, activos.ContainsKey(d.id) ? activos[d.id] : 0f) + d.segundos;
        if (d.id == "PocionVida")
        {
            if (!vidaExtraPuesta) { vida.SetHealth(vida.CurrentHealth + VidaExtra, vida.MaxHealth + VidaExtra); vidaExtraPuesta = true; }
            vida.Heal(60f);
        }
        // glu glu: sonido, destello del color de la pocion y burbujas
        SonidosJuice.Tocar(SonidosJuice.Recoger, transform.position, 0.9f);
        Juice.Destello(new Color(d.color.r, d.color.g, d.color.b, 0.35f), 0.8f);
        Juice.Chispas(transform.position + Vector3.up * 1.2f, d.color, 22, 2.5f);
        Juice.Aplastar(transform, 0.12f);
        SurvivalUI.Aviso(d.nombre + ": " + d.efecto, d.color);
        Logros.Sumar("pociones");
    }

    void Update()
    {
        if (!avisado)
            foreach (var d in Todas)
                if (Cuantas(d.id) > 0) { avisado = true; SurvivalUI.Aviso("Tienes una pocion: pulsa [" + Controles.NombreTecla(Controles.Accion.Beber) + "] para beberla", new Color(1f, 0.85f, 0.6f)); break; }

        if (Controles.Bajada(Controles.Accion.Beber) && (ControladorInventario.Instance == null || !ControladorInventario.Instance.IsOpen))
        {
            Def elegida = null;
            var q = QuickAccessBar.Instance;
            if (q != null && q.SelectedIndex >= 0) elegida = Buscar(q.GetItemId(q.SelectedIndex));
            if (elegida == null) foreach (var d in Todas) if (Cuantas(d.id) > 0 && !Activo(d.id)) { elegida = d; break; }
            if (elegida == null) foreach (var d in Todas) if (Cuantas(d.id) > 0) { elegida = d; break; }
            if (elegida != null) Beber(elegida);
            else SurvivalUI.Aviso("No tienes pociones: tapon + ingrediente en el libro de RECETAS", new Color(1f, 0.7f, 0.5f));
        }

        // fin de la pocion de vida: la vida maxima vuelve a la normal
        if (vidaExtraPuesta && !Activo("PocionVida") && vida != null)
        {
            vidaExtraPuesta = false;
            float max = Mathf.Max(1f, vida.MaxHealth - VidaExtra);
            vida.SetHealth(Mathf.Min(vida.CurrentHealth, max), max);
            SurvivalUI.Aviso("Se acabo el efecto de la pocion de vida", new Color(1f, 0.6f, 0.6f));
        }
        ActualizarHud();
    }

    // ------------------------------------------------------------------ HUD de efectos
    void CrearHud()
    {
        var c = EstiloCuaderno.Lienzo("HUD_Efectos", 17);
        c.transform.SetParent(transform, false);
        c.GetComponent<GraphicRaycaster>().enabled = false;
        fila = new GameObject("Fila", typeof(RectTransform)).GetComponent<RectTransform>();
        fila.SetParent(c.transform, false);
        fila.anchorMin = fila.anchorMax = new Vector2(0.5f, 0f); fila.pivot = new Vector2(0.5f, 0f);
        fila.anchoredPosition = new Vector2(0f, 236f); fila.sizeDelta = new Vector2(600f, 60f);
        var hl = fila.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 10f; hl.childAlignment = TextAnchor.MiddleCenter; hl.childControlWidth = false; hl.childControlHeight = false;
    }

    void ActualizarHud()
    {
        if (fila == null) return;
        var inv = ControladorInventario.Instance;
        foreach (var d in Todas)
        {
            bool on = Activo(d.id);
            (GameObject go, Image barra, TextMeshProUGUI txt) x;
            if (!iconos.TryGetValue(d.id, out x))
            {
                if (!on) continue;
                var go = new GameObject("Efecto_" + d.id, typeof(RectTransform));
                go.transform.SetParent(fila, false);
                ((RectTransform)go.transform).sizeDelta = new Vector2(54f, 54f);
                var fondo = EstiloCuaderno.Img(go.transform, "Fondo", null, new Color(0f, 0f, 0f, 0.45f));
                EstiloCuaderno.Rect(fondo, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var barra = EstiloCuaderno.Img(go.transform, "Tiempo", null, new Color(d.color.r, d.color.g, d.color.b, 0.55f));
                barra.type = Image.Type.Filled; barra.fillMethod = Image.FillMethod.Radial360; barra.sprite = IconosMapa.Blanco();
                EstiloCuaderno.Rect(barra, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                var ic = EstiloCuaderno.Img(go.transform, "Icono", inv != null ? inv.GetIconByName(d.id) : null, Color.white);
                ic.preserveAspect = true;
                EstiloCuaderno.Rect(ic, Vector2.zero, Vector2.one, new Vector2(4, 8), new Vector2(-4, -4));
                var txt = EstiloCuaderno.Txt(go.transform, "Seg", "", 15, Color.white, TextAlignmentOptions.Bottom);
                txt.outlineWidth = 0.3f; txt.outlineColor = new Color32(0, 0, 0, 230);
                EstiloCuaderno.Rect(txt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, -4), new Vector2(0, 18));
                x = (go, barra, txt);
                iconos[d.id] = x;
                Juice.Aplastar(go.transform, -0.4f);
            }
            if (!on) { Destroy(x.go); iconos.Remove(d.id); continue; }
            float r = Restante(d.id);
            x.barra.fillAmount = r / d.segundos;
            x.txt.text = Mathf.CeilToInt(r) + "s";
            // parpadea los ultimos 5 segundos
            x.go.GetComponent<RectTransform>().localScale = Vector3.one * (r < 5f ? 1f + 0.08f * Mathf.Sin(Time.time * 14f) : 1f);
        }
    }
}
