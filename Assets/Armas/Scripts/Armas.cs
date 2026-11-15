using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Armas fusionadas con objetos del salon y de los bichos (Crafteo: dos objetos en CREAR ARMA o el libro de recetas).
/// Cada arma cambia COMO peleas, no solo el numero de daño (Alva Majo: mecanicas que complementan a la principal):
///   Lanza    (lapiz + navaja)            alcance largo, cono estrecho
///   Hacha    (palo + navaja)             mucho daño
///   Mazo     (borrador + chincheta)      golpea a todos los de delante y empuja
///   Latigo   (clip + chincheta doblada)  rapidisimo y de lejos, poco daño
///   Martillo (palo + tuerca)             empuja y aturde
///   Tenazas  (broche + tornillo)         atraviesa caparazones y armaduras
///   y dos fusiones de segundo nivel: Lanza venenosa (lanza + aguijon) y Martillo de cuerno (martillo + cuerno).
/// Se equipa con Q (o eligiendola en la barra rapida); sin arma se pelea a puñetazos.
/// </summary>
public class Armas : MonoBehaviour
{
    public class Def
    {
        public string id, nombre, descripcion, ingredienteA, ingredienteB;
        public float danio = 20f, alcance, cono = 70f, stamina = 30f, empuje = 1f, veneno, escala = 1f;
        public bool area, rompeCorazas;
        public string receta => Catalogo.Nombre(ingredienteA) + " + " + Catalogo.Nombre(ingredienteB);
    }

    public static readonly List<Def> Todas = new List<Def>
    {
        new Def { id = "Lanza", nombre = "Lanza de lapiz", ingredienteA = "Lapiz", ingredienteB = "Navaja", danio = 30f, alcance = 1.4f, cono = 35f, stamina = 26f,
                  descripcion = "Pega desde lejos: perfecta contra arañas y avispas." },
        new Def { id = "Hacha", nombre = "Hacha de navaja", ingredienteA = "Palo", ingredienteB = "Navaja", danio = 44f, alcance = 0.3f, cono = 70f, stamina = 36f,
                  descripcion = "Mucho daño por golpe. Cansa un poco mas." },
        new Def { id = "Mazo", nombre = "Mazo de chinchetas", ingredienteA = "Borrador", ingredienteB = "Chincheta", danio = 32f, alcance = 0.4f, cono = 110f, stamina = 38f, area = true, empuje = 1.8f,
                  descripcion = "Golpea a TODOS los que tengas delante y los aparta." },
        new Def { id = "Latigo", nombre = "Latigo de clip", ingredienteA = "Clips", ingredienteB = "Chincheta_Doblada", danio = 17f, alcance = 1.9f, cono = 55f, stamina = 13f,
                  descripcion = "Muy rapido y de lejos. Gasta poquisima energia." },
        new Def { id = "Martillo", nombre = "Martillo de tuerca", ingredienteA = "Palo", ingredienteB = "Tuerca", danio = 38f, alcance = 0.25f, cono = 70f, stamina = 34f, empuje = 2.3f,
                  descripcion = "Golpe pesado que empuja lejos y aturde." },
        new Def { id = "Tenazas", nombre = "Tenazas de broche", ingredienteA = "broche", ingredienteB = "Tornillo", danio = 26f, alcance = 0.2f, cono = 70f, stamina = 24f, rompeCorazas = true,
                  descripcion = "Muerden a traves de caparazones: escarabajos, cochinillas y jefes acorazados." },
        new Def { id = "PicaVenenosa", nombre = "Lanza venenosa", ingredienteA = "Lanza", ingredienteB = "Aguijon", danio = 32f, alcance = 1.5f, cono = 35f, stamina = 26f, veneno = 7f,
                  descripcion = "La lanza con un aguijon en la punta: envenena (7 de daño por segundo)." },
        new Def { id = "MartilloCuerno", nombre = "Martillo de cuerno", ingredienteA = "Martillo", ingredienteB = "Cuerno", danio = 56f, alcance = 0.35f, cono = 100f, stamina = 40f, area = true, empuje = 2.6f,
                  descripcion = "El arma mas bruta: golpea en area y lo manda todo a volar." },
    };

    public static Def Buscar(string id)
    {
        foreach (var d in Todas) if (string.Equals(d.id, id, System.StringComparison.OrdinalIgnoreCase)) return d;
        return null;
    }

    /// <summary>Arma en la mano (null = puños).</summary>
    public static Def Equipada { get; private set; }
    public static Armas Instancia { get; private set; }

    Transform mano;
    GameObject modelo;
    string modeloId;
    readonly HashSet<string> conocidas = new HashSet<string>();
    bool primeraVez = true;
    float timer;
    PlayerController pc;

    // HUD
    CanvasGroup hud; Image hudIcono; TextMeshProUGUI hudNombre, hudTecla;
    float hudPulso;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { Equipada = null; Instancia = null; }

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
        if (p != null && p.GetComponent<Armas>() == null) p.gameObject.AddComponent<Armas>();
    }

    void Awake() { Instancia = this; }

    void Start()
    {
        pc = GetComponent<PlayerController>();
        var anim = GetComponent<Animator>();
        if (anim != null && anim.isHuman) mano = anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (mano == null) foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == "mixamorig:RightHand") mano = t;
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed += AlUsarRapido;
        // iconos de las armas y piezas (Resources/Armas/Iconos) en la base de iconos del inventario
        var inv = ControladorInventario.Instance != null ? ControladorInventario.Instance : FindAnyObjectByType<ControladorInventario>(FindObjectsInactive.Include);
        if (inv != null)
            foreach (var sp in Resources.LoadAll<Sprite>("Armas/Iconos"))
                if (!inv.iconDatabase.Exists(x => x != null && x.name == sp.name)) inv.iconDatabase.Insert(0, sp);
        CrearHud();
    }

    void OnDestroy()
    {
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed -= AlUsarRapido;
        if (Instancia == this) { Instancia = null; Equipada = null; }
    }

    void AlUsarRapido(int slot, string itemId)
    {
        var d = Buscar(itemId);
        if (d != null) Equipar(Equipada == d ? null : d);
    }

    static int Cuantas(string id) => ControladorInventario.Instance != null ? ControladorInventario.Instance.GetTotalCount(id) : 0;

    public void Equipar(Def d)
    {
        if (d != null && Cuantas(d.id) <= 0) d = null;
        if (Equipada == d) return;
        Equipada = d;
        Estadisticas.Actual.armaEquipada = d != null ? d.id : "";
        hudPulso = 1f;
        SonidosJuice.Tocar(SonidosJuice.Recoger, transform.position, 0.5f);
        if (d != null) SurvivalUI.Aviso(d.nombre + ": " + d.descripcion, new Color(1f, 0.85f, 0.55f));
    }

    void Update()
    {
        if (Controles.Bajada(Controles.Accion.CambiarArma) && (ControladorInventario.Instance == null || !ControladorInventario.Instance.IsOpen))
            Siguiente();

        timer -= Time.unscaledDeltaTime;
        if (timer <= 0f)
        {
            timer = 0.25f;
            if (Equipada != null && Cuantas(Equipada.id) <= 0) Equipar(null);
            // al cargar una partida se vuelve a empuñar el arma que llevabas (sin anuncios)
            if (Equipada == null && !string.IsNullOrEmpty(Estadisticas.Actual.armaEquipada))
            {
                var d0 = Buscar(Estadisticas.Actual.armaEquipada);
                if (d0 != null && Cuantas(d0.id) > 0) Equipada = d0;
            }
            // arma nueva: se equipa sola y se explica como cambiarla
            foreach (var d in Todas)
            {
                bool tiene = Cuantas(d.id) > 0;
                if (!tiene) { conocidas.Remove(d.id); continue; }
                if (conocidas.Contains(d.id)) continue;
                conocidas.Add(d.id);
                if (primeraVez) continue;
                Equipar(d);
                Aviso.Mostrar("<ola>¡" + d.nombre + "!</ola>\n<size=55%>" + d.descripcion + "\n[" + Controles.NombreTecla(Controles.Accion.CambiarArma) + "] cambia de arma</size>", new Color(1f, 0.82f, 0.4f), 4.5f);
                Juice.Destello(new Color(1f, 0.85f, 0.4f, 0.3f), 0.6f);
            }
            primeraVez = false;
        }
        ActualizarHud();
    }

    void Siguiente()
    {
        var lista = new List<Def> { null };
        foreach (var d in Todas) if (Cuantas(d.id) > 0) lista.Add(d);
        if (lista.Count == 1) { SurvivalUI.Aviso("No tienes armas: fusiona objetos en el inventario (libro de RECETAS)", new Color(1f, 0.8f, 0.6f)); return; }
        int i = lista.IndexOf(Equipada);
        Equipar(lista[(i + 1) % lista.Count]);
    }

    // ------------------------------------------------------------------ el arma en la mano
    void LateUpdate()
    {
        string quiero = Equipada != null && !GestorPartida.Muerto ? Equipada.id : null;
        if (quiero != modeloId)
        {
            if (modelo != null) Destroy(modelo);
            modelo = null; modeloId = quiero;
            if (quiero != null)
            {
                modelo = ModelosArmas.Crear(quiero);
                if (modelo != null)
                {
                    modelo.transform.SetParent(transform, false);
                    modelo.transform.localScale = Vector3.one * Escala;
                    foreach (var r in modelo.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    Juice.Aplastar(modelo.transform, -0.3f);   // aparece con un estiron
                }
            }
        }
        if (modelo == null || mano == null) return;

        // tajo: al empezar un ataque el arma barre de arriba-atras hacia delante-abajo y vuelve
        bool atacando = pc != null && pc.IsAttacking;
        if (atacando && !atacandoAntes) tAtaque = 0f;
        atacandoAntes = atacando;
        tAtaque += Time.deltaTime;
        float barrido;
        if (tAtaque < 0.12f) barrido = Mathf.Lerp(0f, -55f, tAtaque / 0.12f);                       // levantar
        else if (tAtaque < 0.26f) barrido = Mathf.Lerp(-55f, 95f, Mathf.SmoothStep(0f, 1f, (tAtaque - 0.12f) / 0.14f));   // golpe
        else barrido = Mathf.Lerp(95f, 0f, Mathf.Clamp01((tAtaque - 0.26f) / 0.3f));               // volver
        if (tAtaque > 0.6f) barrido = 0f;

        // en reposo el arma va casi vertical y un poco hacia delante, agarrada por el mango
        Vector3 eje = (transform.forward * 0.42f + Vector3.up * 0.9f).normalized;
        eje = Quaternion.AngleAxis(barrido, transform.right) * eje;
        Quaternion rot = Quaternion.LookRotation(eje, -transform.forward) * Quaternion.Euler(90f, 0f, 0f);
        modelo.transform.SetPositionAndRotation(mano.position - eje * (0.12f * Escala), rot);
    }

    float Escala => (Equipada != null ? Equipada.escala : 1f) * 0.85f / Mathf.Max(0.001f, transform.lossyScale.x);
    bool atacandoAntes;
    float tAtaque = 9f;

    // ------------------------------------------------------------------ HUD
    void CrearHud()
    {
        var go = new GameObject("HUD_Arma");
        go.transform.SetParent(transform, false);
        var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 18;
        var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.matchWidthOrHeight = 0.5f;
        hud = go.AddComponent<CanvasGroup>(); hud.blocksRaycasts = false; hud.interactable = false;
        var rec = Resources.Load<MenuRecursos>("MenuRecursos");

        var marco = new GameObject("Marco", typeof(RectTransform)).AddComponent<Image>();
        marco.transform.SetParent(go.transform, false);
        marco.color = new Color(0.08f, 0.07f, 0.06f, 0.55f); marco.raycastTarget = false;
        var rm = marco.rectTransform; rm.anchorMin = rm.anchorMax = new Vector2(0.5f, 0f); rm.pivot = new Vector2(0f, 0f);
        rm.anchoredPosition = new Vector2(290f, 92f); rm.sizeDelta = new Vector2(250f, 74f);

        hudIcono = new GameObject("Icono", typeof(RectTransform)).AddComponent<Image>();
        hudIcono.transform.SetParent(marco.transform, false); hudIcono.raycastTarget = false; hudIcono.preserveAspect = true;
        var ri = hudIcono.rectTransform; ri.anchorMin = ri.anchorMax = new Vector2(0f, 0.5f); ri.pivot = new Vector2(0f, 0.5f);
        ri.anchoredPosition = new Vector2(6f, 0f); ri.sizeDelta = new Vector2(64f, 64f);

        hudNombre = Txt(marco.transform, "Nombre", 22, new Vector2(78f, 12f), rec != null ? rec.fuenteMenu : null);
        hudTecla = Txt(marco.transform, "Tecla", 16, new Vector2(78f, -14f), rec != null ? rec.fuenteTexto : null);
        hudTecla.color = new Color(1f, 1f, 1f, 0.7f);
    }

    static TextMeshProUGUI Txt(Transform p, string n, float tam, Vector2 pos, TMP_FontAsset f)
    {
        var t = new GameObject(n, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(p, false);
        if (f != null) t.font = f;
        t.fontSize = tam; t.alignment = TextAlignmentOptions.Left; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        var r = t.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f); r.pivot = new Vector2(0f, 0.5f);
        r.anchoredPosition = pos; r.sizeDelta = new Vector2(166f, 28f);
        return t;
    }

    Sprite iconoPuño;
    void ActualizarHud()
    {
        if (hud == null) return;
        bool visible = !IntroDespertar.Activa && !GestorPartida.Muerto && !FinalJuego.Activo && !MapaUI.Abierto && Time.timeScale > 0f;
        hud.alpha = Mathf.MoveTowards(hud.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        hudPulso = Mathf.MoveTowards(hudPulso, 0f, Time.unscaledDeltaTime * 2.5f);
        hud.transform.GetChild(0).localScale = Vector3.one * (1f + 0.12f * hudPulso);
        var inv = ControladorInventario.Instance;
        if (Equipada != null)
        {
            hudIcono.sprite = inv != null ? inv.GetIconByName(Equipada.id) : null;
            hudIcono.enabled = hudIcono.sprite != null;
            hudNombre.text = Equipada.nombre;
        }
        else
        {
            if (iconoPuño == null) iconoPuño = IconosMapa.Dibujar("puño", (u, v) =>
            {
                float d = Mathf.Sqrt(u * u * 1.1f + (v + 0.05f) * (v + 0.05f));
                float a = Mathf.Clamp01((0.62f - d) * 30f);
                float nudillos = Mathf.Clamp01((0.06f - Mathf.Abs(v - 0.25f)) * 30f) * (Mathf.Abs(Mathf.Repeat(u * 3.2f, 1f) - 0.5f) < 0.06f ? 1f : 0f);
                return Color.Lerp(new Color(0.95f, 0.8f, 0.68f, a), new Color(0.4f, 0.28f, 0.2f, a), nudillos);
            });
            hudIcono.sprite = iconoPuño; hudIcono.enabled = true;
            hudNombre.text = "Puños";
        }
        int n = 0; foreach (var d in Todas) if (Cuantas(d.id) > 0) n++;
        hudTecla.text = n > 0 ? "[" + Controles.NombreTecla(Controles.Accion.CambiarArma) + "] cambiar arma (" + n + ")" : "Fusiona objetos para crear armas";
    }
}

/// <summary>Veneno en un bicho (lanza venenosa): daño cada medio segundo y burbujas verdes.</summary>
public class VenenoBicho : MonoBehaviour
{
    float dps, quedan;
    RatHealth vida;

    public static void Aplicar(RatHealth objetivo, float dps, float segundos)
    {
        if (objetivo == null || objetivo.IsDead) return;
        var v = objetivo.GetComponent<VenenoBicho>();
        if (v == null) v = objetivo.gameObject.AddComponent<VenenoBicho>();
        v.vida = objetivo; v.dps = Mathf.Max(v.dps, dps); v.quedan = Mathf.Max(v.quedan, segundos);
        v.enabled = true;
        v.CancelInvoke(); v.InvokeRepeating(nameof(Tick), 0.5f, 0.5f);
    }

    void Tick()
    {
        if (vida == null || vida.IsDead || quedan <= 0f) { CancelInvoke(); enabled = false; return; }
        quedan -= 0.5f;
        float hecho = vida.TakeDamage(dps * 0.5f);
        Juice.Chispas(vida.transform.position + Vector3.up * 0.6f, new Color(0.4f, 1f, 0.3f), 4, 1.2f);
        if (hecho > 0f) NumerosDanio.Mostrar(vida.transform.position + Vector3.up * 1f, hecho, false);
    }
}
