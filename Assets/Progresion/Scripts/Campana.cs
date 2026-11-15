using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// La campaña: los jefes van en orden, cada uno con mas vida que el anterior, y cada mision abre el siguiente.
///   1. Bolita (Hamsterlandia, en el salon)      persecucion: no se le puede matar
///   2. Reina Abeja (el arbol)                    1200 de vida
///   3. Reina Hormiga (bajo el jardin)            2000 de vida
///   4. Rey Rata (el trono de la cloaca)          2800 de vida: el jefe final, el mas lejano
/// Un jefe al que aun no le toca esta "sellado": no despierta y no recibe daño (el juego te dice que mision va antes).
/// Las reinas sueltan su pila al morir. Al caer el Rey Rata la cloaca se inunda y la corriente te saca al rincon
/// mas lejano del jardin con la ultima pila. Empieza una cuenta atras (20/15/10/7 min segun la dificultad) para
/// prepararse, y despues... la HECATOMBE: todos los bichos van a por ti mientras corres a la cabina, con musica de
/// violines y situaciones absurdas. Al poner la ultima pila suena la fanfarria de victoria.
/// </summary>
public class Campana : MonoBehaviour
{
    public static Campana Instancia { get; private set; }
    public enum Fase { Historia, Corriente, CuentaAtras, Hecatombe, Victoria }
    public Fase fase { get; private set; } = Fase.Historia;

    public class Jefe
    {
        public string tipo, nombre, mision;
        public RatHealth vida;
        public float vidaObjetivo;
        public MapaMundo.TipoLugar lugar;
        public System.Func<bool> hecho;
    }
    public readonly List<Jefe> Orden = new List<Jefe>();

    static Estadisticas E => Estadisticas.Actual;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { Instancia = null; RatHealth.Inmune = null; }

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
        new GameObject("Campana").AddComponent<Campana>();
    }

    // ------------------------------------------------------------------ jefes
    void Awake()
    {
        Instancia = this;
        var bb = GameObject.Find("BATERIA_Bolita");
        Orden.Add(new Jefe { tipo = "Bolita", nombre = "Bolita", mision = "bolita", lugar = MapaMundo.TipoLugar.Hamsterlandia,
                             hecho = () => E.escapesBolita > 0 || bb == null || !bb.activeInHierarchy });
        var ra = FindAnyObjectByType<ReinaAbeja>(FindObjectsInactive.Include);
        var rh = FindAnyObjectByType<ReinaHormiga>(FindObjectsInactive.Include);
        var rr = FindAnyObjectByType<ReyRata>(FindObjectsInactive.Include);
        Orden.Add(Nuevo("ReinaAbeja", "la Reina Abeja", "reinaAbeja", ra, 1200f, MapaMundo.TipoLugar.Arbol));
        Orden.Add(Nuevo("ReinaHormiga", "la Reina Hormiga", "reinaHormiga", rh, 2000f, MapaMundo.TipoLugar.Hormiguero));
        Orden.Add(Nuevo("ReyRata", "el Rey Rata", "reyRata", rr, 2800f, MapaMundo.TipoLugar.Cloaca));
        // cada jefe con mas vida que el anterior (antes de que la dificultad y la partida guardada la ajusten)
        foreach (var j in Orden)
            if (j.vida != null && j.vidaObjetivo > 0f && j.vida.MaxHealth > 0f) j.vida.EscalarVida(j.vidaObjetivo / j.vida.MaxHealth);
        RatHealth.Inmune = Sellado;
    }

    Jefe Nuevo(string tipo, string nombre, string mision, Component c, float vida, MapaMundo.TipoLugar lugar)
    {
        var rh = c != null ? c.GetComponent<RatHealth>() : null;
        var j = new Jefe { tipo = tipo, nombre = nombre, mision = mision, vida = rh, vidaObjetivo = vida, lugar = lugar };
        j.hecho = () => E.Bichos(tipo) > 0 || (rh != null && rh.IsDead) || (tipo == "ReinaAbeja" && Colmena.Instancia != null && Colmena.Instancia.ReinaMuerta);
        if (rh != null) rh.OnDeath += () => AlMorirJefe(j);
        return j;
    }

    /// <summary>Indice del jefe que toca ahora (Orden.Count = todos derrotados).</summary>
    public int Indice { get { for (int i = 0; i < Orden.Count; i++) if (!Orden[i].hecho()) return i; return Orden.Count; } }
    public Jefe Actual { get { int i = Indice; return i < Orden.Count ? Orden[i] : null; } }
    public bool Hecho(string tipo) { foreach (var j in Orden) if (j.tipo == tipo) return j.hecho(); return false; }
    public MapaMundo.TipoLugar? LugarActual { get { var j = Actual; return j != null ? j.lugar : (MapaMundo.TipoLugar?)null; } }

    /// <summary>¿Este jefe todavia no puede pelear (va despues del actual)?</summary>
    public static bool Sellado(RatHealth rh)
    {
        var c = Instancia;
        if (c == null || rh == null) return false;
        int idx = c.Orden.FindIndex(j => j.vida == rh);
        return idx > c.Indice;
    }

    public static bool LugarSellado(MapaMundo.TipoLugar l)
    {
        var c = Instancia;
        if (c == null) return false;
        int idx = c.Orden.FindIndex(j => j.lugar == l);
        return idx > c.Indice;
    }

    float ultimoAvisoSellado = -99f;
    public static void AvisarSellado(RatHealth rh)
    {
        var c = Instancia;
        if (c == null || Time.time - c.ultimoAvisoSellado < 6f) return;
        c.ultimoAvisoSellado = Time.time;
        var j = c.Orden.Find(x => x.vida == rh);
        var act = c.Actual;
        Aviso.Mostrar("<temblor>¡Aun no!</temblor>", new Color(1f, 0.55f, 0.35f), 2.5f);
        SurvivalUI.Aviso((j != null ? Mayus(j.nombre) : "Este jefe") + " ni se inmuta: es demasiado fuerte para ti todavia. Antes: " + (act != null ? act.nombre : "la mision actual"), new Color(1f, 0.8f, 0.6f));
        SonidosJuice.Tocar(SonidosJuice.Rebote, rh.transform.position, 0.8f);
    }

    static string Mayus(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

    // ------------------------------------------------------------------ muerte de jefes
    void AlMorirJefe(Jefe j)
    {
        if (j.tipo == "ReyRata") { StartCoroutine(Corriente(j.vida.transform.position)); return; }
        StartCoroutine(SoltarPila(j));
    }

    IEnumerator SoltarPila(Jefe j)
    {
        Vector3 donde = j.vida != null ? j.vida.transform.position : Vector3.zero;
        yield return new WaitForSeconds(1.8f);
        if (BateriasEnJuego() >= JefesDerrotados()) yield break;   // partidas viejas: la pila ya estaba recogida
        var prefab = Resources.Load<GameObject>("Drops/Bateria");
        if (prefab == null) yield break;
        RaycastHit hit;
        Vector3 p = donde + Vector3.up * 2f;
        if (Physics.Raycast(donde + Vector3.up * 3f, Vector3.down, out hit, 30f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point + Vector3.up * 1.2f;
        var go = Instantiate(prefab, p, Quaternion.identity);
        go.name = "Drop_Bateria";
        var wi = go.GetComponent<WorldItem>(); if (wi != null) { wi.itemId = MaquinaCrecimiento.ItemBateria; wi.quantity = 1; }
        if (FloorItemPromptManager.Instance != null) FloorItemPromptManager.Instance.RegisterTrackedItem(go);
        go.AddComponent<AparecerLoot>();
        Juice.Destello(new Color(0.4f, 1f, 0.5f, 0.35f), 1f);
        Aviso.Mostrar("<ola>¡Una bateria verde!</ola>\n<size=55%>" + Mayus(j.nombre) + " la guardaba. Llevala a la cabina.</size>", new Color(0.5f, 1f, 0.6f), 4.5f);
    }

    int JefesDerrotados() { int n = 0; foreach (var j in Orden) if (j.hecho()) n++; return n; }

    static int BateriasEnJuego()
    {
        int n = MaquinaCrecimiento.Instancia != null ? MaquinaCrecimiento.Instancia.Colocadas : 0;
        if (ControladorInventario.Instance != null) n += ControladorInventario.Instance.GetTotalCount(MaquinaCrecimiento.ItemBateria);
        foreach (var w in FindObjectsByType<WorldItem>(FindObjectsInactive.Exclude))
            if (w != null && w.itemId == MaquinaCrecimiento.ItemBateria && w.name != "BATERIA_Bolita") n++;
        return n;
    }

    // ------------------------------------------------------------------ la corriente (tras el Rey Rata)
    /// <summary>Donde te deja la corriente: el rincon del jardin mas lejano a la cabina (sobre el trono del Rey).</summary>
    public static readonly Vector2 SalidaCorriente = new Vector2(300f, 225f);

    IEnumerator Corriente(Vector3 trono)
    {
        fase = Fase.Corriente;
        yield return new WaitForSecondsRealtime(2.5f);
        Aviso.Mostrar("<temblor>¡LA CLOACA SE INUNDA!</temblor>", new Color(0.55f, 0.85f, 1f), 3f);
        for (float t = 0f; t < 3f; t += Time.deltaTime) { Juice.Sacudir(0.05f); yield return null; }
        // fundido a negro
        var negro = Fundido();
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f) { negro.color = new Color(0, 0, 0, t); yield return null; }
        negro.color = Color.black;
        // la pila del rey entra en la mochila aunque no la hubieras recogido
        var inv = ControladorInventario.Instance;
        bool tiene = false;
        foreach (var w in FindObjectsByType<WorldItem>(FindObjectsInactive.Exclude))
            if (w != null && w.itemId == MaquinaCrecimiento.ItemBateria && w.name != "BATERIA_Bolita" && Vector3.Distance(w.transform.position, trono) < 60f) { Destroy(w.gameObject); tiene = true; }
        if (inv != null && (tiene || BateriasEnJuego() < 4)) inv.AddItem(MaquinaCrecimiento.ItemBateria, inv.GetIconByName(MaquinaCrecimiento.ItemBateria), 1);
        // a la superficie, lejos de todo
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null)
        {
            Vector3 destino = PuntoSuelo(SalidaCorriente);
            var cc = pc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            Vector3 haciaCabina = MaquinaCrecimiento.Instancia.transform.position - destino; haciaCabina.y = 0f;
            pc.transform.SetPositionAndRotation(destino, Quaternion.LookRotation(haciaCabina.normalized));
            if (cc != null) cc.enabled = true;
        }
        var texto = EstiloCuaderno.Txt(negro.transform, "Texto", "La corriente te arrastra...\n<size=70%>y te escupe en el rincon mas lejano del jardin</size>", 46, Color.white, TextAlignmentOptions.Center, true);
        EstiloCuaderno.Rect(texto, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        yield return new WaitForSecondsRealtime(3f);
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 1.2f) { negro.color = new Color(0, 0, 0, 1f - t); texto.alpha = 1f - t; yield return null; }
        Destroy(negro.canvas.gameObject);
        EmpezarCuentaAtras(MinutosPreparacion() * 60f);
    }

    static Vector3 PuntoSuelo(Vector2 xz)
    {
        for (int i = 0; i < 12; i++)
        {
            Vector2 q = xz + Random.insideUnitCircle * (i * 3f);
            RaycastHit hit;
            if (Physics.Raycast(new Vector3(q.x, 200f, q.y), Vector3.down, out hit, 400f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.7f)
                return hit.point + Vector3.up * 1.05f;
        }
        return new Vector3(xz.x, ZonaJuego.Altura(xz.x, xz.y) + 1.5f, xz.y);
    }

    Image Fundido()
    {
        var c = EstiloCuaderno.Lienzo("FundidoCampana", 900);
        var img = EstiloCuaderno.Img(c.transform, "Negro", null, new Color(0, 0, 0, 0));
        img.raycastTarget = true;
        EstiloCuaderno.Rect(img, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return img;
    }

    // ------------------------------------------------------------------ cuenta atras
    public float Restante { get; private set; }

    public static float MinutosPreparacion()
    {
        switch (Partida.Dif)
        {
            case Dificultad.Pacifico: return 20f;
            case Dificultad.Dificil: return 10f;
            case Dificultad.Extremo: return 7f;
            default: return 15f;
        }
    }

    void EmpezarCuentaAtras(float segundos)
    {
        fase = Fase.CuentaAtras;
        Restante = segundos;
        E.faseFinal = 1; E.restanteFinal = Restante;
        PonerMusica(MusicaSintetica.Tension(), 0.55f);
        Aviso.Mostrar("<temblor>Tienes la ultima pila...</temblor>\n<size=55%>Todos los bichos lo han sentido. En " + Mathf.RoundToInt(segundos / 60f) + " minutos vendran a por ti.\nCurate, crea pociones y armas. Luego, corre a la cabina.</size>", new Color(1f, 0.75f, 0.4f), 8f);
        Juice.Sacudir(0.4f);
    }

    /// <summary>La cabina no acepta la ultima pila durante la cuenta atras (hay que sobrevivir a la hecatombe).</summary>
    public static bool PuedeColocar(int colocadas, out string motivo)
    {
        motivo = null;
        var c = Instancia;
        if (c == null || c.fase != Fase.CuentaAtras || colocadas < MaquinaCrecimiento.Necesarias - 1) return true;
        motivo = "La ultima pila esta inestable: la cabina no la acepta hasta que llegue la tormenta (" + Reloj(c.Restante) + ")";
        return false;
    }

    public static string Reloj(float s) { int t = Mathf.Max(0, Mathf.CeilToInt(s)); return (t / 60) + ":" + (t % 60).ToString("00"); }

    // ------------------------------------------------------------------ ciclo
    float inicio;
    bool restaurado;
    Transform jugador;
    AudioSource musica;
    float volMusica;
    Hecatombe hecatombe;

    void Start()
    {
        inicio = Time.unscaledTime;
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        if (MaquinaCrecimiento.Instancia != null) MaquinaCrecimiento.Instancia.alEncender.AddListener(AlEncenderCabina);
        CrearHud();
    }

    void OnDestroy()
    {
        if (Instancia == this) { Instancia = null; RatHealth.Inmune = null; }
        if (MaquinaCrecimiento.Instancia != null) MaquinaCrecimiento.Instancia.alEncender.RemoveListener(AlEncenderCabina);
    }

    void Update()
    {
        // al cargar una partida: retomar el final donde se quedo
        if (!restaurado && Time.unscaledTime - inicio > 3f)
        {
            restaurado = true;
            if (E.faseFinal == 1 && fase == Fase.Historia) EmpezarCuentaAtras(Mathf.Max(30f, E.restanteFinal));
            else if (E.faseFinal == 2 && fase == Fase.Historia) EmpezarHecatombe();
        }

        if ((fase == Fase.CuentaAtras || fase == Fase.Hecatombe) && jugador != null && MaquinaCrecimiento.Instancia != null && !GestorPartida.Muerto)
        {
            Vector3 a = jugador.position, b = MaquinaCrecimiento.Instancia.transform.position; a.y = b.y = 0f;
            mejorDistancia = Mathf.Min(mejorDistancia, Vector3.Distance(a, b));
        }
        if (fase == Fase.CuentaAtras)
        {
            Restante -= Time.deltaTime;
            E.restanteFinal = Restante;
            // si te acercas a la cabina antes de tiempo, los bichos te ven y empieza ya
            bool cerca = jugador != null && MaquinaCrecimiento.Instancia != null &&
                         Vector2.Distance(new Vector2(jugador.position.x, jugador.position.z), new Vector2(MaquinaCrecimiento.Instancia.transform.position.x, MaquinaCrecimiento.Instancia.transform.position.z)) < 130f;
            if (Restante <= 0f || (cerca && Restante > 0f && Time.unscaledTime - inicio > 6f))
            {
                if (cerca && Restante > 0f) Aviso.Mostrar("<temblor>¡Te han visto!</temblor>", new Color(1f, 0.4f, 0.3f), 2f);
                EmpezarHecatombe();
            }
        }

        if (musica != null)
        {
            float objetivo = (Time.timeScale <= 0f ? 0.25f : 1f) * volMusica * Ajustes.VolMusica;
            musica.volume = Mathf.MoveTowards(musica.volume, objetivo, Time.unscaledDeltaTime * 0.6f);
        }
        ActualizarHud();
    }

    void PonerMusica(AudioClip clip, float vol, bool bucle = true)
    {
        if (musica == null)
        {
            musica = gameObject.AddComponent<AudioSource>();
            musica.spatialBlend = 0f; musica.ignoreListenerPause = false; musica.playOnAwake = false;
        }
        musica.clip = clip; musica.loop = bucle; musica.volume = 0f; volMusica = vol;
        musica.Play();
    }

    // ------------------------------------------------------------------ checkpoint del final
    float mejorDistancia = float.MaxValue;     // lo mas cerca de la cabina que llegaste durante el final

    /// <summary>
    /// Morir en el final no te deja en la cabina (seria ganar gratis) ni te manda al principio (Alva Majo: repetir solo
    /// el ultimo trozo): reapareces en el camino, 60 m por detras de tu mejor marca y nunca a menos de 140 de la cabina.
    /// </summary>
    public bool PuntoReaparicionFinal(out Vector3 pos, out Quaternion rot, out string nombre)
    {
        var maq = MaquinaCrecimiento.Instancia;
        pos = Vector3.zero; rot = Quaternion.identity; nombre = "tu camino hacia la cabina";
        if (maq == null) return false;
        Vector3 cab = maq.transform.position; cab.y = 0f;
        Vector3 ini = new Vector3(SalidaCorriente.x, 0f, SalidaCorriente.y);
        float total = Vector3.Distance(ini, cab);
        float d = Mathf.Clamp(Mathf.Min(mejorDistancia, total) + 60f, 140f, total);
        Vector3 p = cab + (ini - cab).normalized * d;
        pos = PuntoSuelo(new Vector2(p.x, p.z));
        Vector3 mirar = cab - pos; mirar.y = 0f;
        rot = Quaternion.LookRotation(mirar.sqrMagnitude > 0.01f ? mirar.normalized : Vector3.forward);
        return true;
    }

    // ------------------------------------------------------------------ hecatombe y victoria
    void EmpezarHecatombe()
    {
        if (fase == Fase.Hecatombe || fase == Fase.Victoria) return;
        fase = Fase.Hecatombe;
        E.faseFinal = 2;
        PonerMusica(MusicaSintetica.Batalla(), 0.75f);
        hecatombe = gameObject.AddComponent<Hecatombe>();
        Aviso.Mostrar("<temblor>¡HECATOMBE!</temblor>\n<size=55%>Todos los bichos del jardin van a por ti. ¡Corre a la cabina!</size>", new Color(1f, 0.3f, 0.25f), 5f);
        Juice.Sacudir(0.8f);
        Juice.Destello(new Color(1f, 0.1f, 0.05f, 0.5f), 1.2f);
    }

    void AlEncenderCabina()
    {
        if (fase == Fase.Victoria) return;
        bool habiaHecatombe = fase == Fase.Hecatombe || fase == Fase.CuentaAtras;
        fase = Fase.Victoria;
        E.faseFinal = 3;
        if (!habiaHecatombe) return;
        StartCoroutine(Victoria());
    }

    IEnumerator Victoria()
    {
        if (hecatombe != null) hecatombe.Terminar();
        Logros.Sumar("hecatombe");
        PonerMusica(MusicaSintetica.Victoria(), 1f, false);
        musica.volume = Ajustes.VolMusica;
        Juice.CamaraLenta(0.35f, 2.5f);
        Juice.Destello(new Color(1f, 0.95f, 0.6f, 0.6f), 1.5f);
        Aviso.Mostrar("<ola>¡LO LOGRASTE!</ola>\n<size=55%>Los bichos huyen... la cabina por fin tiene energia</size>", new Color(1f, 0.9f, 0.4f), 7f);
        // confeti
        Color[] colores = { new Color(1f, 0.3f, 0.3f), new Color(1f, 0.85f, 0.2f), new Color(0.4f, 1f, 0.5f), new Color(0.4f, 0.7f, 1f), new Color(1f, 0.5f, 1f) };
        for (float t = 0f; t < 6f; t += 0.25f)
        {
            if (jugador != null)
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = jugador.position + new Vector3(Random.Range(-6f, 6f), Random.Range(3f, 7f), Random.Range(-6f, 6f));
                    Juice.Chispas(p, colores[Random.Range(0, colores.Length)], 18, 3.5f);
                }
            yield return new WaitForSecondsRealtime(0.25f);
        }
    }

    // ------------------------------------------------------------------ HUD
    TextMeshProUGUI hudTitulo, hudTexto;
    CanvasGroup hudGrupo;

    void CrearHud()
    {
        var c = EstiloCuaderno.Lienzo("HUD_Final", 21);
        c.transform.SetParent(transform, false);
        c.GetComponent<GraphicRaycaster>().enabled = false;
        hudGrupo = c.gameObject.AddComponent<CanvasGroup>(); hudGrupo.alpha = 0f;
        hudTitulo = EstiloCuaderno.Txt(c.transform, "Titulo", "", 40, Color.white, TextAlignmentOptions.Top, true);
        EstiloCuaderno.Rect(hudTitulo, new Vector2(0.25f, 1f), new Vector2(0.75f, 1f), new Vector2(0, -150), new Vector2(0, -88));
        hudTitulo.outlineWidth = 0.25f; hudTitulo.outlineColor = new Color32(0, 0, 0, 230);
        hudTexto = EstiloCuaderno.Txt(c.transform, "Texto", "", 22, new Color(1f, 0.92f, 0.8f), TextAlignmentOptions.Top);
        EstiloCuaderno.Rect(hudTexto, new Vector2(0.25f, 1f), new Vector2(0.75f, 1f), new Vector2(0, -186), new Vector2(0, -148));
        hudTexto.outlineWidth = 0.25f; hudTexto.outlineColor = new Color32(0, 0, 0, 230);
    }

    void ActualizarHud()
    {
        if (hudGrupo == null) return;
        bool visible = (fase == Fase.CuentaAtras || fase == Fase.Hecatombe) && !MapaUI.Abierto && !GestorPartida.Muerto && !PanelMisiones.Abierto;
        hudGrupo.alpha = Mathf.MoveTowards(hudGrupo.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime * 3f);
        if (!visible) return;
        float dist = 0f;
        if (jugador != null && MaquinaCrecimiento.Instancia != null) dist = Vector3.Distance(jugador.position, MaquinaCrecimiento.Instancia.transform.position);
        if (fase == Fase.CuentaAtras)
        {
            bool urgente = Restante < 60f;
            hudTitulo.text = "Los bichos vendran en " + Reloj(Restante);
            hudTitulo.color = urgente ? Color.Lerp(Color.white, new Color(1f, 0.3f, 0.25f), Mathf.PingPong(Time.time * 2f, 1f)) : new Color(1f, 0.85f, 0.55f);
            hudTexto.text = "Preparate: pociones (G), armas (Q), vendas (H)  ·  Cabina a " + BrujulaUI.Metros(dist);
        }
        else
        {
            hudTitulo.text = "¡HECATOMBE!";
            hudTitulo.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(1f, 0.85f, 0.3f), Mathf.PingPong(Time.time * 3f, 1f));
            hudTitulo.transform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.time * 9f));
            hudTexto.text = "¡Llega a la cabina!  " + BrujulaUI.Metros(dist) + "  ·  Bichos derrotados: " + (hecatombe != null ? hecatombe.Derrotados : 0);
        }
    }
}
