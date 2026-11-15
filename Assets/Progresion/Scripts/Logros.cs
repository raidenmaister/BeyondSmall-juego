using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Misiones (la historia: una por jefe y la final) y logros (retos con progreso: matar 50 hormigas, crear
/// todas las armas...). Todo se guarda en Estadisticas (que va dentro de la partida).
/// Al cumplirse sale una tarjeta arriba a la derecha con sonido, y las misiones dan recompensa.
/// El panel completo se abre con J (PanelMisiones). El HUD del objetivo muestra el "siguiente logro".
/// </summary>
public class Logros : MonoBehaviour
{
    public class Mision
    {
        public string id, titulo, descripcion, pista;
        public System.Func<bool> hecha, disponible;
        public System.Func<string> progreso;
        public (string id, int n)[] recompensa = new (string, int)[0];
        public bool final;
    }

    public class Logro
    {
        public string id, titulo, descripcion, icono;   // icono: id de un objeto del inventario (o null = trofeo)
        public System.Func<int> valor;
        public int meta = 1;
        public bool secreto;
    }

    public static readonly List<Mision> Misiones = new List<Mision>();
    public static readonly List<Logro> Todos = new List<Logro>();
    public static Logros Instancia { get; private set; }

    static Estadisticas E => Estadisticas.Actual;

    // ------------------------------------------------------------------ contadores propios
    public static void Sumar(string clave, int n = 1)
    {
        var c = E.contadores.Find(x => x.tipo == clave);
        if (c == null) { c = new Estadisticas.Conteo { tipo = clave }; E.contadores.Add(c); }
        c.n += n;
    }
    public static int Contador(string clave) { var c = E.contadores.Find(x => x.tipo == clave); return c != null ? c.n : 0; }
    static bool Creado(string id) => E.fusionados.Contains(id);

    static int Tiene(string id) => ControladorInventario.Instance != null ? ControladorInventario.Instance.GetTotalCount(id) : 0;
    static bool JefeMuerto(string tipo) => E.Bichos(tipo) > 0;

    // ------------------------------------------------------------------ definiciones
    static void Definir()
    {
        Misiones.Clear(); Todos.Clear();
        // ---- misiones (la historia)
        Misiones.Add(new Mision
        {
            id = "despertar", titulo = "Despertar", descripcion = "Despiertas diminuto dentro de una cabina sin energia. Averigua que paso.",
            pista = "Acercate a la bahia de la consola de la cabina y pulsa E para rastrear las señales.",
            hecha = () => Objetivos.Escaneado, recompensa = new[] { ("Carne_Cocinada", 2) }
        });
        Misiones.Add(new Mision
        {
            id = "armas", titulo = "Arma improvisada", descripcion = "Ahi fuera los bichos son enormes. Fusiona dos objetos para crear tu primera arma.",
            pista = "Abre el inventario (I) y mira el libro de RECETAS: lapiz + navaja = lanza, palo + tuerca = martillo...",
            hecha = () => { foreach (var a in Armas.Todas) if (Creado(a.id) || Tiene(a.id) > 0) return true; return false; },
            disponible = () => Objetivos.Escaneado, recompensa = new[] { ("Venda", 2) }
        });
        Misiones.Add(new Mision
        {
            id = "reinaHormiga", titulo = "La reina de la tierra", descripcion = "Una bateria esta en la camara de la Reina Hormiga, bajo el jardin. Derrotala.",
            pista = "Entrada del hormiguero: sigue la señal \"bajo tierra\" de la brujula. Lleva escudo o un mazo para los grupos.",
            hecha = () => JefeMuerto("ReinaHormiga"), disponible = () => Objetivos.Escaneado,
            progreso = () => "Bateria: " + (Recogida("BATERIA_Hormiguero") ? "conseguida" : "en su camara"),
            recompensa = new[] { ("Carne_Cocinada", 3), ("Venda", 1) }
        });
        Misiones.Add(new Mision
        {
            id = "reinaAbeja", titulo = "El enjambre", descripcion = "La Reina Abeja guarda otra bateria en lo alto del arbol. Acaba con ella.",
            pista = "Sube al arbol de la colmena con la antorcha encendida (T): el humo calma a las abejas.",
            hecha = () => JefeMuerto("ReinaAbeja") || (Colmena.Instancia != null && Colmena.Instancia.ReinaMuerta), disponible = () => Objetivos.Escaneado,
            progreso = () => "Bateria: " + (Recogida("BATERIA_Colmena") ? "conseguida" : "en la cupula de cera"),
            recompensa = new[] { ("Carne_Cocinada", 3), ("Venda", 1) }
        });
        Misiones.Add(new Mision
        {
            id = "reyRata", titulo = "El rey de la cloaca", descripcion = "El Rey Rata se trago una bateria. Bajo la alcantarilla te espera su trono.",
            pista = "Entra por la alcantarilla del jardin. No bebas el agua sucia y salta su coletazo.",
            hecha = () => JefeMuerto("ReyRata"), disponible = () => Objetivos.Escaneado,
            recompensa = new[] { ("Carne_Cocinada", 3), ("Venda", 2) }
        });
        Misiones.Add(new Mision
        {
            id = "bolita", titulo = "La funcion de Bolita", descripcion = "En Hamsterlandia, Bolita guarda la ultima pila en su coleccion. Robasela y escapa.",
            pista = "Enciende las tres palancas del parque y luego la del escenario. Su casita tiene la puerta chiquita.",
            hecha = () => E.escapesBolita > 0 || Recogida("BATERIA_Bolita"), disponible = () => Objetivos.Escaneado,
            recompensa = new[] { ("Carne_Cocinada", 2), ("Venda", 2) }
        });
        Misiones.Add(new Mision
        {
            id = "final", titulo = "Volver a crecer", descripcion = "Pon las cuatro baterias verdes en la cabina y entra.", final = true,
            pista = "Lleva cada bateria a la bahia de la consola (E). Con las cuatro, entra en la cabina.",
            hecha = () => FinalJuego.Activo || E.completadaFinal,
            progreso = () => "Baterias puestas: " + (MaquinaCrecimiento.Instancia != null ? MaquinaCrecimiento.Instancia.Colocadas : 0) + "/" + MaquinaCrecimiento.Necesarias,
            disponible = () => Objetivos.Escaneado
        });

        // ---- logros
        L("primeraSangre", "Primera sangre", "Derrota a tu primer bicho.", null, () => E.TotalBichos, 1);
        L("hormigas50", "Exterminador", "Derrota a 50 hormigas.", "Colmillo", () => E.Bichos("Hormiga"), 50);
        L("abejas30", "Apicultor", "Derrota a 30 abejas.", "Aguijon", () => E.Bichos("Abeja"), 30);
        L("ratas20", "Cazarratas", "Derrota a 20 ratas.", "Piel", () => E.Bichos("Rata"), 20);
        L("aranas10", "Aracnofobia", "Derrota a 10 arañas.", "Colmillo", () => E.Bichos("Arana"), 10);
        L("escarabajos5", "Rompecorazas", "Derrota a 5 escarabajos rinoceronte.", "Cuerno", () => E.Bichos("Escarabajo"), 5);
        L("entomologo", "Entomologo", "Derrota a 8 especies de bichos distintas.", "Alas", () =>
        {
            int n = 0; foreach (var t in new[] { "Hormiga", "Abeja", "Arana", "Escarabajo", "Ciempies", "Oruga", "Cochinilla", "Lombriz", "Mariposa", "Rata", "Larva" }) if (E.Bichos(t) > 0) n++; return n;
        }, 8);
        L("plaga", "Plaga", "Derrota a 300 bichos en total.", null, () => E.TotalBichos, 300);
        L("reinas", "Regicida", "Derrota a los tres jefes: las dos reinas y el Rey Rata.", null, () => (JefeMuerto("ReinaHormiga") ? 1 : 0) + (JefeMuerto("ReinaAbeja") ? 1 : 0) + (JefeMuerto("ReyRata") ? 1 : 0), 3);
        L("bolita", "Escapista", "Escapa de Bolita con la pila.", "Bateria", () => E.escapesBolita, 1);
        L("fusion", "Manitas", "Fusiona dos objetos por primera vez.", "Ligas", () => E.fusionados.Count, 1);
        L("armero", "Armero", "Crea las seis armas basicas.", "Hacha", () =>
        {
            int n = 0; foreach (var id in new[] { "Lanza", "Hacha", "Mazo", "Latigo", "Martillo", "Tenazas" }) if (Creado(id)) n++; return n;
        }, 6);
        L("maestro", "Maestro armero", "Crea la lanza venenosa y el martillo de cuerno.", "MartilloCuerno", () => (Creado("PicaVenenosa") ? 1 : 0) + (Creado("MartilloCuerno") ? 1 : 0), 2);
        L("mejoras", "Hecho a medida", "Consigue las cuatro mejoras: guantes, botas, planeador y peto.", "Peto", () =>
            (Creado("Guantes") ? 1 : 0) + (Creado("Botas") ? 1 : 0) + (Creado("Planeador") ? 1 : 0) + (Creado("Peto") ? 1 : 0), 4);
        L("chef", "Buen provecho", "Come 15 veces.", "Carne_Cocinada", () => E.comidas, 15);
        L("enfermero", "Enfermero", "Usa 5 vendas.", "Venda", () => Contador("vendas"), 5);
        L("escudo", "Muralla", "Haz 10 bloqueos perfectos con el escudo.", "Escudo", () => E.bloqueosPerfectos, 10);
        L("planeo", "Vuelo libre", "Planea durante 60 segundos en total.", "Planeador", () => Contador("planeo"), 60);
        L("caminante", "Pequeños pasos", "Recorre 2 km (¡siendo diminuto!).", null, () => Mathf.FloorToInt(E.distancia / 18f), 2000);
        L("cartografo", "Cartografo", "Descubre todos los lugares del mapa.", null, () => LugaresDescubiertos(), System.Enum.GetValues(typeof(MapaMundo.TipoLugar)).Length);
        L("dias", "Superviviente", "Sobrevive 10 dias.", null, () => E.dias, 10);
        L("coleccion", "Pistola de Chejov", "Recoge al menos uno de cada material del juego.", null, () => E.materiales.Count, MaterialesTotales());
        L("sinMorir", "Intocable", "Termina el juego sin morir ni una vez.", null, () => (E.completadaFinal || FinalJuego.Activo) && E.muertes == 0 ? 1 : 0, 1, true);
    }

    static void L(string id, string t, string d, string icono, System.Func<int> v, int meta, bool secreto = false)
    {
        Todos.Add(new Logro { id = id, titulo = t, descripcion = d, icono = icono, valor = v, meta = Mathf.Max(1, meta), secreto = secreto });
    }

    static bool Recogida(string nombre) { var g = GameObject.Find(nombre); return g == null || !g.activeInHierarchy; }

    static int LugaresDescubiertos()
    {
        var m = MapaMundo.Instancia; if (m == null) return 0;
        var tipos = new HashSet<MapaMundo.TipoLugar>();
        foreach (var l in m.lugares) if (l.descubierto) tipos.Add(l.tipo);
        return tipos.Count;
    }
    static int LugaresTotales()
    {
        var m = MapaMundo.Instancia; if (m == null) return 9;
        var tipos = new HashSet<MapaMundo.TipoLugar>();
        foreach (var l in m.lugares) tipos.Add(l.tipo);
        return Mathf.Max(1, tipos.Count);
    }
    static readonly string[] Materiales = { "Palo", "Carbon", "Ligas", "Borrador", "Clips", "Chincheta", "Chincheta_Doblada", "broche", "Tornillo", "Tuerca", "Tela", "Lapiz", "Navaja",
                                            "Hueso", "Piel", "Colmillo", "Aguijon", "Alas", "PataCiempies", "Exoesqueleto", "Cuerno", "Carne" };
    static int MaterialesTotales() => Materiales.Length;

    public static bool Desbloqueado(Logro l) => E.logros.Contains(l.id);
    public static bool Cumplida(Mision m) => E.misiones.Contains(m.id);
    public static bool Disponible(Mision m) => m.disponible == null || m.disponible();
    public static int Valor(Logro l) { try { return Mathf.Min(l.meta, Mathf.Max(0, l.valor())); } catch { return 0; } }

    /// <summary>El logro sin desbloquear mas cerca de cumplirse (para el HUD).</summary>
    public static Logro Siguiente()
    {
        Logro mejor = null; float pm = -1f;
        foreach (var l in Todos)
        {
            if (Desbloqueado(l) || l.secreto) continue;
            float p = (float)Valor(l) / l.meta;
            if (p > pm) { pm = p; mejor = l; }
        }
        return mejor;
    }

    /// <summary>La primera mision de la historia sin cumplir que ya esta disponible.</summary>
    public static Mision MisionActual()
    {
        foreach (var m in Misiones) if (!Cumplida(m) && Disponible(m) && !m.final) return m;
        foreach (var m in Misiones) if (!Cumplida(m) && Disponible(m)) return m;
        return null;
    }

    // ------------------------------------------------------------------ ciclo
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { Instancia = null; }

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
        if (Object.FindAnyObjectByType<PlayerController>() == null) return;
        new GameObject("Logros").AddComponent<Logros>();
    }

    float timer, inicio;
    PlayerController pc;
    readonly Queue<(string titulo, string texto, Sprite icono, bool mision)> cola = new Queue<(string, string, Sprite, bool)>();
    RectTransform tarjeta; CanvasGroup grupoTarjeta; Image iconoTarjeta; TextMeshProUGUI cabTarjeta, titTarjeta, txtTarjeta;
    float tTarjeta = -1f;

    void Awake() { Instancia = this; Definir(); }
    void OnDestroy() { if (Instancia == this) Instancia = null; }

    void Start()
    {
        pc = FindAnyObjectByType<PlayerController>();
        inicio = Time.unscaledTime;
        CrearTarjeta();
        if (FindAnyObjectByType<PanelMisiones>() == null) gameObject.AddComponent<PanelMisiones>();
    }

    void Update()
    {
        // segundos planeando
        if (pc != null && pc.Planeando) { planeo += Time.deltaTime; if (planeo >= 1f) { planeo -= 1f; Sumar("planeo"); } }
        // materiales vistos en la mochila
        timer -= Time.unscaledDeltaTime;
        if (timer <= 0f)
        {
            timer = 0.5f;
            foreach (var id in Materiales) if (!E.materiales.Contains(id) && Tiene(id) > 0) E.materiales.Add(id);
            // los primeros segundos (carga de la partida) no se anuncia nada
            bool anunciar = Time.unscaledTime - inicio > 4f && !IntroDespertar.Activa;
            if (anunciar) Revisar();
        }
        AnimarTarjeta();
    }
    float planeo;

    void Revisar()
    {
        foreach (var m in Misiones)
        {
            if (Cumplida(m) || !Disponible(m)) continue;
            bool h; try { h = m.hecha(); } catch { h = false; }
            if (!h) continue;
            E.misiones.Add(m.id);
            if (m.final) E.completadaFinal = true;
            string premio = "";
            var inv = ControladorInventario.Instance;
            foreach (var r in m.recompensa)
            {
                if (inv != null && inv.AddItem(r.id, inv.GetIconByName(r.id), r.n)) premio += (premio.Length > 0 ? ", " : "") + r.n + " " + Catalogo.Nombre(r.id).ToLower();
            }
            Encolar(m.final ? "¡HISTORIA COMPLETADA!" : "MISION CUMPLIDA", m.titulo, premio.Length > 0 ? "Recompensa: " + premio : m.descripcion, EstiloCuaderno.Check(), true);
            var sig = MisionActual();
            if (sig != null && !m.final) Aviso.Mostrar("<ola>" + m.titulo + "</ola>\n<size=55%>Siguiente: " + sig.titulo + "</size>", new Color(0.6f, 1f, 0.65f), 4f);
        }
        foreach (var l in Todos)
        {
            if (Desbloqueado(l)) continue;
            if (Valor(l) < l.meta) continue;
            E.logros.Add(l.id);
            Encolar("LOGRO DESBLOQUEADO", l.titulo, l.descripcion, Icono(l), false);
        }
    }

    public static Sprite Icono(Logro l)
    {
        if (!string.IsNullOrEmpty(l.icono) && ControladorInventario.Instance != null)
        {
            var s = ControladorInventario.Instance.GetIconByName(l.icono);
            if (s != null) return s;
        }
        return EstiloCuaderno.Trofeo();
    }

    // ------------------------------------------------------------------ tarjeta (toast)
    void Encolar(string cab, string titulo, string texto, Sprite icono, bool mision)
    {
        cola.Enqueue((titulo, cab + "|" + texto, icono, mision));
    }

    void CrearTarjeta()
    {
        var c = EstiloCuaderno.Lienzo("TarjetaLogros", 60);
        c.transform.SetParent(transform, false);
        c.GetComponent<GraphicRaycaster>().enabled = false;
        var papel = EstiloCuaderno.Img(c.transform, "Tarjeta", EstiloCuaderno.Papel(), Color.white);
        papel.type = Image.Type.Sliced;
        tarjeta = papel.rectTransform;
        tarjeta.anchorMin = tarjeta.anchorMax = new Vector2(1f, 1f); tarjeta.pivot = new Vector2(1f, 1f);
        tarjeta.sizeDelta = new Vector2(470f, 128f); tarjeta.anchoredPosition = new Vector2(600f, -90f);
        grupoTarjeta = papel.gameObject.AddComponent<CanvasGroup>();
        iconoTarjeta = EstiloCuaderno.Img(tarjeta, "Icono", null, Color.white); iconoTarjeta.preserveAspect = true;
        EstiloCuaderno.Rect(iconoTarjeta, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, -46), new Vector2(110, 46));
        cabTarjeta = EstiloCuaderno.Txt(tarjeta, "Cab", "", 19, EstiloCuaderno.Rojo);
        EstiloCuaderno.Rect(cabTarjeta, new Vector2(0, 1), new Vector2(1, 1), new Vector2(124, -40), new Vector2(-14, -10));
        titTarjeta = EstiloCuaderno.Txt(tarjeta, "Titulo", "", 32, EstiloCuaderno.Tinta, TextAlignmentOptions.TopLeft, true);
        EstiloCuaderno.Rect(titTarjeta, new Vector2(0, 1), new Vector2(1, 1), new Vector2(124, -78), new Vector2(-14, -34));
        txtTarjeta = EstiloCuaderno.Txt(tarjeta, "Texto", "", 19, EstiloCuaderno.TintaSuave);
        EstiloCuaderno.Rect(txtTarjeta, new Vector2(0, 0), new Vector2(1, 0), new Vector2(124, 8), new Vector2(-14, 52));
        grupoTarjeta.alpha = 0f;
    }

    void AnimarTarjeta()
    {
        if (tarjeta == null) return;
        if (tTarjeta < 0f)
        {
            if (cola.Count == 0) return;
            var x = cola.Dequeue();
            var partes = x.texto.Split('|');
            cabTarjeta.text = partes[0]; titTarjeta.text = x.titulo; txtTarjeta.text = partes.Length > 1 ? partes[1] : "";
            iconoTarjeta.sprite = x.icono; iconoTarjeta.enabled = x.icono != null;
            tTarjeta = 0f;
            SonidosJuice.Tocar(x.mision ? SonidosJuice.Mision : SonidosJuice.Logro, pc != null ? pc.transform.position : Vector3.zero, 0.9f);
            Juice.Destello(x.mision ? new Color(0.4f, 1f, 0.5f, 0.25f) : new Color(1f, 0.85f, 0.3f, 0.25f), 0.7f);
        }
        tTarjeta += Time.unscaledDeltaTime;
        const float dur = 5f;
        float entra = Mathf.Clamp01(tTarjeta / 0.45f), sale = Mathf.Clamp01((dur - tTarjeta) / 0.45f);
        float k = Mathf.Min(entra, sale);
        float ease = 1f - Mathf.Pow(1f - k, 3f);
        // entra desde la derecha con un pequeño rebote
        float rebote = entra < 1f ? Mathf.Sin(entra * Mathf.PI) * 18f : 0f;
        tarjeta.anchoredPosition = new Vector2(Mathf.Lerp(520f, -24f, ease) - rebote, -90f);
        tarjeta.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-6f, -1.5f, ease));
        grupoTarjeta.alpha = k;
        iconoTarjeta.transform.localScale = Vector3.one * (1f + 0.25f * Mathf.Exp(-tTarjeta * 5f) * Mathf.Sin(tTarjeta * 18f));
        if (tTarjeta >= dur) tTarjeta = -1f;
    }
}
