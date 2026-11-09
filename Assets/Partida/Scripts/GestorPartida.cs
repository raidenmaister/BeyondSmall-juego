using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Vive en la escena del juego (lo crea solo al cargarla). Se encarga de:
///  - registrar los objetos recogibles y los bichos de la escena con una clave estable (su ruta en la jerarquia);
///  - guardar la partida (jugador, inventario, hora, objetos recogidos o soltados, bichos vivos/muertos,
///    baterias de la cabina, colonias furiosas) con miniatura de lo que se esta viendo;
///  - cargarla al entrar desde el menu;
///  - autoguardado cada Ajustes.AutoguardadoMin minutos y guardado manual (F5 / Controles.Accion.Guardar);
///  - dificultad: vida extra de los bichos, bichos nocturnos (SpawnerNocturno) y muerte (extremo = todo se borra).
/// </summary>
[DefaultExecutionOrder(-200)]
public class GestorPartida : MonoBehaviour
{
    public static GestorPartida Instancia { get; private set; }
    public static bool Muerto { get; private set; }

    readonly Dictionary<string, GameObject> items = new Dictionary<string, GameObject>();
    readonly HashSet<GameObject> itemsEscena = new HashSet<GameObject>();
    readonly Dictionary<string, RatHealth> enemigos = new Dictionary<string, RatHealth>();
    readonly Dictionary<string, Sprite> iconos = new Dictionary<string, Sprite>();

    PlayerController pc;
    PlayerHealth vida;
    Vector3 inicioJugador; Quaternion rotInicio;
    MenuRecursos recursos;
    float tAuto;
    bool listo;

    // aviso de guardado (arriba a la derecha)
    Canvas canvasAviso;
    CanvasGroup grupoAviso;
    TextMeshProUGUI textoAviso;
    RectTransform giro;
    float tAviso;

    // pantalla de muerte
    GameObject pantallaMuerte;

    const int AnchoMini = 480, AltoMini = 270;

    // ================================================================== creacion
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Enganchar()
    {
        Instancia = null; Muerto = false;
        SceneManager.sceneLoaded -= AlCargarEscena;     // sin recarga de dominio no se duplica
        SceneManager.sceneLoaded += AlCargarEscena;
    }

    static void AlCargarEscena(Scene s, LoadSceneMode m) { Preparar(); }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void PrimeraEscena() { Preparar(); }

    static void Preparar()
    {
        if (Instancia != null) return;
        if (FindAnyObjectByType<PlayerController>() == null) return;      // solo en la escena del juego
        var go = new GameObject("GestorPartida");
        Instancia = go.AddComponent<GestorPartida>();
        Instancia.Registrar();
    }

    void OnDestroy()
    {
        if (Instancia == this) { Instancia = null; Muerto = false; }
    }

    static string Clave(Transform t)
    {
        var sb = new StringBuilder();
        for (var x = t; x != null; x = x.parent) sb.Insert(0, "/" + x.name + "#" + x.GetSiblingIndex());
        return sb.ToString();
    }

    /// <summary>Se llama al cargar la escena, antes de cualquier Start: nada se ha movido todavia.</summary>
    void Registrar()
    {
        Muerto = false;
        recursos = Resources.Load<MenuRecursos>("MenuRecursos");
        pc = FindAnyObjectByType<PlayerController>();
        vida = pc != null ? pc.GetComponent<PlayerHealth>() : null;
        if (pc != null) { inicioJugador = pc.transform.position; rotInicio = pc.transform.rotation; }
        if (vida != null) vida.OnDeath += AlMorir;

        // objetos recogibles de la escena
        var fm = FloorItemPromptManager.Instance != null ? FloorItemPromptManager.Instance : FindAnyObjectByType<FloorItemPromptManager>();
        var candidatos = new List<GameObject>();
        if (fm != null && fm.floorObjects != null) candidatos.AddRange(fm.floorObjects);
        foreach (var wi in FindObjectsByType<WorldItem>(FindObjectsInactive.Exclude))
            if (wi.transform.parent == null && !candidatos.Contains(wi.gameObject)) candidatos.Add(wi.gameObject);
        foreach (var go in candidatos)
        {
            if (go == null || !go.activeInHierarchy) continue;
            string k = Clave(go.transform);
            if (items.ContainsKey(k)) continue;
            items[k] = go; itemsEscena.Add(go);
        }

        // bichos (todos usan RatHealth) y vida segun la dificultad (tambien plantillas de hordas)
        float k2 = Partida.MultiplicadorVida;
        foreach (var rh in FindObjectsByType<RatHealth>(FindObjectsInactive.Include))
        {
            if (rh.gameObject.scene != gameObject.scene) continue;
            rh.EscalarVida(k2);
            if (!rh.gameObject.activeInHierarchy) continue;
            string k = Clave(rh.transform);
            if (!enemigos.ContainsKey(k)) enemigos[k] = rh;
        }

        // iconos conocidos (para devolver el icono correcto al cargar el inventario)
        foreach (var wi in FindObjectsByType<WorldItem>(FindObjectsInactive.Include)) Icono(wi.itemIcon);
        foreach (var g in Resources.LoadAll<GameObject>("Drops")) { var wi = g.GetComponent<WorldItem>(); if (wi != null) Icono(wi.itemIcon); }
        if (ControladorInventario.Instance != null) foreach (var s in ControladorInventario.Instance.iconDatabase) Icono(s);

        gameObject.AddComponent<MapaMundo>();
        // ninguna particula (humo de antorcha/fogata, chispas...) puede ocupar mas de un pedazo de la pantalla:
        // Unity las deja crecer hasta media pantalla cuando la camara pasa a traves de ellas (destello blanco)
        foreach (var pr in FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include))
            pr.maxParticleSize = Mathf.Min(pr.maxParticleSize, 0.12f);
        gameObject.AddComponent<FinalJuego>();
        gameObject.AddComponent<Trucos>();

        if (Partida.NochePeligrosa)
        {
            var sp = new GameObject("BichosNocturnos").AddComponent<SpawnerNocturno>();
            sp.transform.SetParent(transform, false);
            sp.Preparar(enemigos.Values);
        }
        Debug.Log($"[Partida] ranura {Partida.RanuraActual + 1}, {Partida.Nombre(Partida.Dif)}: {items.Count} objetos y {enemigos.Count} bichos registrados");
    }

    void Icono(Sprite s) { if (s != null && !iconos.ContainsKey(s.name)) iconos[s.name] = s; }

    public Sprite BuscarIcono(string nombre, string id)
    {
        Sprite s;
        if (!string.IsNullOrEmpty(nombre) && iconos.TryGetValue(nombre, out s)) return s;
        if (ControladorInventario.Instance != null) { s = ControladorInventario.Instance.GetIconByName(id); if (s != null) return s; }
        if (!string.IsNullOrEmpty(nombre))
            foreach (var sp in Resources.FindObjectsOfTypeAll<Sprite>()) if (sp.name == nombre) { iconos[nombre] = sp; return sp; }
        return null;
    }

    // ================================================================== ciclo
    void Start()
    {
        CrearAviso();
        StartCoroutine(Inicio());
    }

    IEnumerator Inicio()
    {
        yield return null;          // que todos los Start() hayan corrido
        yield return null;
        if (Partida.Pendiente != null)
        {
            try { Aplicar(Partida.Pendiente); }
            catch (System.Exception e) { Debug.LogError("[Partida] Error al cargar: " + e); }
            Partida.Pendiente = null;
            listo = true;
            yield break;
        }
        listo = true;
        // partida nueva: primer guardado al terminar de despertar, asi la ranura ya queda ocupada
        if (Partida.RanuraActual >= 0)
        {
            while (IntroDespertar.Activa) yield return null;
            yield return new WaitForSeconds(1.5f);
            if (!Muerto) Guardar(false);
        }
    }

    void Update()
    {
        if (Time.timeScale > 0f && !Muerto && !IntroDespertar.Activa && !FinalJuego.Activo) Partida.TiempoJugado += Time.unscaledDeltaTime;
        ActualizarAviso();
        if (!listo || Muerto || FinalJuego.Activo) return;
        ContarDiasYPasos();

        if (Controles.Bajada(Controles.Accion.Guardar)) GuardarManual();

        if (Ajustes.AutoguardadoMin > 0 && Partida.RanuraActual >= 0 && !IntroDespertar.Activa && Time.timeScale > 0f)
        {
            tAuto += Time.unscaledDeltaTime;
            if (tAuto >= Ajustes.AutoguardadoMin * 60f) { tAuto = 0f; Guardar(true); }
        }
    }

    // ---------------------------------------------------------------- estadisticas: dias y distancia
    float horaAntes = -1f; Vector3 posAntes; bool hayPos;
    void ContarDiasYPasos()
    {
        var dn = DayNightCycle.Instance;
        if (dn != null)
        {
            if (horaAntes >= 0f && dn.hora < horaAntes - 12f) Estadisticas.Actual.dias++;   // paso la medianoche
            horaAntes = dn.hora;
        }
        if (pc != null)
        {
            Vector3 p = pc.transform.position;
            if (hayPos)
            {
                float d = new Vector2(p.x - posAntes.x, p.z - posAntes.z).magnitude;
                if (d < 5f) Estadisticas.Actual.distancia += d;          // los saltos grandes son teletransportes
            }
            posAntes = p; hayPos = true;
        }
    }

    // ================================================================== guardar
    /// <summary>Guardado pedido por el jugador (F5 o menu de pausa).</summary>
    public bool GuardarManual()
    {
        if (Partida.RanuraActual < 0)
        {
            MostrarAviso("Partida de prueba: entra desde el menu para poder guardar", false);
            return false;
        }
        if (Muerto || IntroDespertar.Activa) { MostrarAviso("Ahora no se puede guardar", false); return false; }
        return Guardar(false);
    }

    public bool Guardar(bool automatico)
    {
        if (Partida.RanuraActual < 0 || Muerto || !listo) return false;
        if (vida != null && vida.IsDead) return false;
        try
        {
            var d = Capturar();
            Partida.Escribir(Partida.RanuraActual, d, Miniatura());
            tAuto = 0f;
            MostrarAviso(automatico ? "Autoguardado" : "Partida guardada", true);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Partida] No se pudo guardar: " + e);
            MostrarAviso("No se pudo guardar la partida", false);
            return false;
        }
    }

    DatosPartida Capturar()
    {
        var d = new DatosPartida
        {
            dificultad = (int)Partida.Dif,
            tiempo = Partida.TiempoJugado,
            fecha = System.DateTime.Now.ToString("dd/MM/yyyy  HH:mm"),
            lugar = Lugar(),
        };
        if (pc != null)
        {
            d.posJugador = pc.transform.position;
            d.rotJugador = pc.transform.eulerAngles.y;
        }
        if (vida != null) { d.vida = vida.CurrentHealth; d.vidaMax = vida.MaxHealth; }
        if (PlayerHunger.Instance != null) d.comida = PlayerHunger.Instance.CurrentFood;
        if (DayNightCycle.Instance != null) d.hora = DayNightCycle.Instance.hora;

        // inventario (mochila, mesa de crafteo y acceso rapido)
        foreach (var (grupo, lista) in GruposInventario())
            for (int i = 0; i < lista.Count; i++)
            {
                var s = lista[i];
                if (s == null || s.IsEmpty) continue;
                d.inventario.Add(new SlotGuardado { grupo = grupo, indice = i, id = s.ItemId, cantidad = s.Quantity, icono = s.CurrentIcon != null ? s.CurrentIcon.name : "" });
            }

        // objetos de la escena ya recogidos
        foreach (var kv in items) if (kv.Value == null || !kv.Value.activeSelf) d.recogidos.Add(kv.Key);
        // objetos soltados en el suelo durante la partida (drops de bichos)
        var vistos = new HashSet<GameObject>();
        foreach (var wi in FindObjectsByType<WorldItem>(FindObjectsInactive.Exclude))
        {
            var go = wi.gameObject;
            if (go.transform.parent != null || itemsEscena.Contains(go) || !vistos.Add(go)) continue;
            if (go.GetComponentInParent<SpawnerNocturno>() != null) continue;
            string id = string.IsNullOrEmpty(wi.itemId) ? ControladorInventario.NormalizeItemName(go.name.Replace("Drop_", "")) : wi.itemId;
            d.dinamicos.Add(new ItemDinamico { id = id, pos = go.transform.position, rot = go.transform.eulerAngles.y });
        }

        // bichos de la escena
        foreach (var kv in enemigos)
        {
            var rh = kv.Value;
            bool muerto = rh == null || rh.IsDead;
            var e = new EnemigoGuardado { clave = kv.Key, muerto = muerto };
            if (!muerto) { e.pos = rh.transform.position; e.rot = rh.transform.eulerAngles.y; e.vida = rh.CurrentHealth; }
            d.enemigos.Add(e);
        }

        if (MaquinaCrecimiento.Instancia != null) d.baterias = MaquinaCrecimiento.Instancia.Colocadas;
        if (Hormiguero.Instancia != null) d.hormigueroFuria = Hormiguero.Instancia.Furia;
        if (Colmena.Instancia != null) { d.colmenaFuria = Colmena.Instancia.Furia; d.colmenaReinaMuerta = Colmena.Instancia.ReinaMuerta; }
        if (MapaMundo.Instancia != null) MapaMundo.Instancia.Exportar(d);
        d.stats = Estadisticas.Actual;
        var previo = Partida.Leer(Partida.RanuraActual); if (previo != null) d.completada = previo.completada;

        // mochilas y botines en el suelo; bichos que cargan cosas robadas
        foreach (var m in Mochila.Todas)
            if (m != null && m.items.Count > 0) d.mochilas.Add(new MochilaGuardada { pos = m.transform.position, tipo = (int)m.tipo, titulo = m.titulo, items = new List<SlotGuardado>(m.items) });
        foreach (var l in Ladron.Todos)
        {
            if (l == null || l.items.Count == 0) continue;
            string clave = null;
            foreach (var kv in enemigos) if (kv.Value != null && kv.Value.gameObject == l.gameObject) { clave = kv.Key; break; }
            if (clave != null) d.ladrones.Add(new LadronGuardado { clave = clave, items = new List<SlotGuardado>(l.items) });
            else d.mochilas.Add(new MochilaGuardada { pos = l.transform.position, tipo = (int)Mochila.Tipo.Botin, titulo = "Lo que robo " + l.Nombre, items = new List<SlotGuardado>(l.items) });
        }
        return d;
    }

    IEnumerable<(string, List<InventorySlotUI>)> GruposInventario()
    {
        var inv = ControladorInventario.Instance;
        if (inv != null)
        {
            inv.InitializeSlotsIfNeeded();
            yield return ("inv", inv.slots);
            if (inv.craftInputSlots != null) yield return ("crafteo", inv.craftInputSlots);
        }
        if (QuickAccessBar.Instance != null && QuickAccessBar.Instance.pageSlots != null)
            yield return ("rapido", new List<InventorySlotUI>(QuickAccessBar.Instance.pageSlots));
    }

    string Lugar()
    {
        if (pc == null) return "";
        Vector3 p = pc.transform.position;
        if (Hormiguero.Instancia != null && Hormiguero.Instancia.JugadorDentro) return "Hormiguero";
        if (Colmena.Instancia != null && Colmena.Instancia.JugadorDentro) return "Colmena";
        if (MaquinaCrecimiento.Instancia != null)
        {
            Vector3 c = p - MaquinaCrecimiento.Instancia.transform.position; c.y = 0f;
            if (c.magnitude < 16f) return "La cabina";
        }
        return ZonaJuego.EnEdificio(p) ? "El salon" : "Afuera";
    }

    /// <summary>Lo que ve la camara del juego, sin el HUD, en pequeño.</summary>
    byte[] Miniatura()
    {
        var cam = Camera.main;
        if (cam == null) return null;
        var rt = RenderTexture.GetTemporary(AnchoMini, AltoMini, 24, RenderTextureFormat.ARGB32);
        var antes = cam.targetTexture;
        var activa = RenderTexture.active;
        Texture2D tex = null;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(AnchoMini, AltoMini, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, AnchoMini, AltoMini), 0, 0);
            tex.Apply();
            return tex.EncodeToPNG();
        }
        catch (System.Exception e) { Debug.LogWarning("[Partida] Sin miniatura: " + e.Message); return null; }
        finally
        {
            cam.targetTexture = antes;
            RenderTexture.active = activa;
            RenderTexture.ReleaseTemporary(rt);
            if (tex != null) Destroy(tex);
        }
    }

    // ================================================================== cargar
    void Aplicar(DatosPartida d)
    {
        // jugador
        if (pc != null)
        {
            var cc = pc.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            pc.transform.SetPositionAndRotation(d.posJugador + Vector3.up * 0.05f, Quaternion.Euler(0f, d.rotJugador, 0f));
            if (cc != null) cc.enabled = true;
            AjustarCamara(d.rotJugador);
        }
        if (vida != null && d.vidaMax > 0f) vida.SetHealth(Mathf.Max(1f, d.vida), d.vidaMax);
        if (PlayerHunger.Instance != null && d.comida >= 0f) PlayerHunger.Instance.SetFood(d.comida);
        if (DayNightCycle.Instance != null && d.hora >= 0f) { DayNightCycle.Instance.hora = d.hora; DayNightCycle.Instance.AplicarAhora(); }

        // inventario
        var grupos = new Dictionary<string, List<InventorySlotUI>>();
        foreach (var (g, l) in GruposInventario()) { grupos[g] = l; foreach (var s in l) if (s != null) s.Clear(); }
        foreach (var s in d.inventario)
        {
            List<InventorySlotUI> l;
            if (!grupos.TryGetValue(s.grupo, out l) || s.indice < 0 || s.indice >= l.Count || l[s.indice] == null) continue;
            l[s.indice].SetItem(s.id, BuscarIcono(s.icono, s.id), s.cantidad);
        }

        // objetos recogidos
        var fm = FloorItemPromptManager.Instance;
        foreach (var k in d.recogidos)
        {
            GameObject go;
            if (!items.TryGetValue(k, out go) || go == null) continue;
            if (fm != null) fm.UnregisterTrackedItem(go);
            go.SetActive(false);
        }
        // objetos soltados
        foreach (var it in d.dinamicos)
        {
            var pref = Resources.Load<GameObject>("Drops/" + it.id);
            if (pref == null) continue;
            var go = Instantiate(pref, it.pos, Quaternion.Euler(0f, it.rot, 0f));
            go.name = "Drop_" + it.id;
            var wi = go.GetComponent<WorldItem>(); if (wi != null) { wi.itemId = it.id; wi.quantity = 1; }
            var rb = go.GetComponent<Rigidbody>(); if (rb != null) { rb.linearVelocity = Vector3.zero; rb.Sleep(); }
            if (fm != null) fm.RegisterTrackedItem(go);
        }

        // bichos
        foreach (var e in d.enemigos)
        {
            RatHealth rh;
            if (!enemigos.TryGetValue(e.clave, out rh) || rh == null) continue;
            if (e.muerto) { Retirar(rh); continue; }
            rh.FijarVida(e.vida);
            if ((rh.transform.position - e.pos).sqrMagnitude > 1f)
            {
                var ag = rh.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (ag != null && ag.enabled && ag.isOnNavMesh) ag.Warp(e.pos);
                else rh.transform.position = e.pos;
                rh.transform.rotation = Quaternion.Euler(0f, e.rot, 0f);
            }
        }

        if (MaquinaCrecimiento.Instancia != null) MaquinaCrecimiento.Instancia.Restaurar(d.baterias);
        if (Hormiguero.Instancia != null && d.hormigueroFuria) Hormiguero.Instancia.RestaurarFuria();
        if (Colmena.Instancia != null && (d.colmenaFuria || d.colmenaReinaMuerta)) Colmena.Instancia.Restaurar(d.colmenaFuria, d.colmenaReinaMuerta);
        if (MapaMundo.Instancia != null) MapaMundo.Instancia.Importar(d);
        if (d.mochilas != null)
            foreach (var m in d.mochilas) Mochila.Crear(m.pos, (Mochila.Tipo)m.tipo, m.titulo, new List<SlotGuardado>(m.items));
        if (d.ladrones != null)
            foreach (var l in d.ladrones)
            {
                RatHealth rh;
                if (enemigos.TryGetValue(l.clave, out rh) && rh != null && rh.gameObject.activeInHierarchy && !rh.IsDead) Ladron.Asignar(rh, l.items);
                else if (rh != null) Mochila.Crear(rh.transform.position, Mochila.Tipo.Botin, "Tus cosas", new List<SlotGuardado>(l.items));
            }

        Debug.Log($"[Partida] cargada ranura {Partida.RanuraActual + 1}: {d.inventario.Count} casillas, {d.recogidos.Count} recogidos, {d.dinamicos.Count} soltados, {d.enemigos.FindAll(x => x.muerto).Count} bichos muertos");
    }

    // un bicho que ya estaba muerto: desaparece sin soltar nada ni avisar a nadie
    void Retirar(RatHealth rh)
    {
        var h = rh.GetComponent<HormigaNido>();
        if (h != null && Hormiguero.Instancia != null) Hormiguero.Instancia.Quitar(h);
        var a = rh.GetComponent<AbejaColmena>();
        if (a != null && Colmena.Instancia != null) Colmena.Instancia.Quitar(a);
        if (RatHealthBarUI.jefe == rh) RatHealthBarUI.jefe = null;
        rh.gameObject.SetActive(false);
    }

    void AjustarCamara(float yaw)
    {
        if (yaw > 180f) yaw -= 360f;
        foreach (var of in FindObjectsByType<CinemachineOrbitalFollow>(FindObjectsInactive.Exclude))
        {
            of.HorizontalAxis.Value = yaw;
            var cam = of.GetComponent<CinemachineCamera>();
            if (cam != null) cam.PreviousStateIsValid = false;
        }
    }

    // ================================================================== muerte
    void AlMorir()
    {
        if (Muerto) return;
        Muerto = true;
        Controles.Bloqueado = true;
        Estadisticas.Actual.muertes++;
        if (!Partida.UnaVida) { StartCoroutine(RutinaRevivir()); return; }
        // extremo: la partida se borra y todo empieza de cero
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (Partida.RanuraActual >= 0) Partida.Borrar(Partida.RanuraActual);
        StartCoroutine(RutinaMuerte());
    }

    // ---------------------------------------------------------------- morir y despertar en la cabina
    IEnumerator RutinaRevivir()
    {
        Vector3 donde = pc != null ? pc.transform.position : Vector3.zero;
        var c = CrearCanvas("Muerte", 960);
        var negro = Img(c.transform, "Negro", new Color(0, 0, 0, 0)); Estirar(negro.rectTransform);
        var rojo = Img(c.transform, "Rojo", new Color(0.35f, 0f, 0f, 0f)); Estirar(rojo.rectTransform);
        var titulo = Txt(c.transform, "Titulo", "HAS MUERTO", 96, recursos != null ? recursos.fuenteMenu : null);
        var rt = titulo.rectTransform; rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(1, 0.64f); rt.offsetMin = rt.offsetMax = Vector2.zero;
        titulo.characterSpacing = 14; titulo.color = new Color(0.85f, 0.2f, 0.18f, 0f);
        var texto = Txt(c.transform, "Texto", "", 30, recursos != null ? recursos.fuenteTexto : null);
        var rx = texto.rectTransform; rx.anchorMin = new Vector2(0.15f, 0.32f); rx.anchorMax = new Vector2(0.85f, 0.56f); rx.offsetMin = rx.offsetMax = Vector2.zero;
        texto.color = new Color(1, 1, 1, 0); texto.lineSpacing = 10f;
        Volume vol; ColorAdjustments color; DepthOfField dof;
        CrearPost(out vol, out color, out dof);
        var audio = gameObject.AddComponent<AudioSource>(); audio.ignoreListenerPause = true; audio.playOnAwake = false;
        if (recursos != null && recursos.sonidoLatido != null) audio.PlayOneShot(recursos.sonidoLatido, 0.8f * Ajustes.VolEfectos);

        // 1. se apaga todo: rojo, borroso, sin color y al final negro
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 2.6f)
        {
            float k = Mathf.SmoothStep(0f, 1f, t);
            rojo.color = new Color(0.35f, 0f, 0f, 0.55f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.4f)));
            negro.color = new Color(0, 0, 0, Mathf.Pow(k, 1.6f));
            vol.weight = k; color.saturation.Override(Mathf.Lerp(0f, -100f, k)); dof.gaussianEnd.Override(Mathf.Lerp(60f, 0.5f, k));
            titulo.color = new Color(0.85f, 0.2f, 0.18f, Mathf.Clamp01(t * 2f - 0.6f));
            yield return null;
        }
        negro.color = Color.black; rojo.color = Color.clear;
        yield return new WaitForSecondsRealtime(1.2f);

        // 2. con la pantalla en negro: las cosas segun la dificultad y de vuelta a la cabina
        string msg = "";
        try { msg = InventarioMuerte.AlMorir(donde); }
        catch (System.Exception e) { Debug.LogError("[Partida] inventario al morir: " + e); }
        Reaparecer();
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f) { titulo.color = new Color(0.85f, 0.2f, 0.18f, 1f - t); yield return null; }
        titulo.color = Color.clear;
        texto.text = "<size=120%>Despiertas en la cabina...</size>\n<color=#cfc6b4>" + msg + "</color>";
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f) { texto.color = new Color(1, 1, 1, t); yield return null; }
        for (float t = 0f; t < 4.5f; t += Time.unscaledDeltaTime)
        {
            if (t > 1f && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape))) break;
            yield return null;
        }
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f) { texto.color = new Color(1, 1, 1, 1f - t); yield return null; }
        texto.color = Color.clear;

        // 3. despertar: parpadeos con la vista borrosa que se va aclarando
        if (recursos != null && recursos.sonidoRespiro != null) audio.PlayOneShot(recursos.sonidoRespiro, 0.7f * Ajustes.VolEfectos);
        float[] pasos = { 1f, 0.7f, 1f, 0.35f, 0.65f, 0f };
        float[] tiempos = { 0f, 1.1f, 0.25f, 1.1f, 0.3f, 1.8f };
        for (int i = 1; i < pasos.Length; i++)
        {
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / tiempos[i])
            {
                float a = Mathf.Lerp(pasos[i - 1], pasos[i], Mathf.SmoothStep(0f, 1f, t));
                negro.color = new Color(0, 0, 0, a);
                float claro = (float)i / (pasos.Length - 1) * 0.7f + (1f - a) * 0.3f;
                vol.weight = 1f - claro; color.saturation.Override(Mathf.Lerp(-100f, 0f, claro)); dof.gaussianEnd.Override(Mathf.Lerp(0.5f, 60f, claro * claro));
                yield return null;
            }
        }
        Destroy(c.gameObject); Destroy(vol.gameObject); Destroy(audio);
        Muerto = false;
        Controles.Bloqueado = false;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        Guardar(true);
    }

    /// <summary>De vuelta a la cabina, con la vida llena y sin veneno.</summary>
    void Reaparecer()
    {
        if (pc == null) return;
        Vector3 pos = inicioJugador; Quaternion rot = rotInicio;
        var maq = MaquinaCrecimiento.Instancia;
        if (maq != null) { pos = maq.transform.TransformPoint(new Vector3(0f, 1.05f, -4f)); rot = maq.transform.rotation; }
        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pc.transform.SetPositionAndRotation(pos, rot);
        if (cc != null) cc.enabled = true;
        AjustarCamara(rot.eulerAngles.y);
        if (vida != null) vida.SetHealth(vida.MaxHealth, vida.MaxHealth);
        if (PlayerHunger.Instance != null && PlayerHunger.Instance.CurrentFood < 40f) PlayerHunger.Instance.SetFood(40f);
        StatusEffects.Limpiar();
    }

    void CrearPost(out Volume vol, out ColorAdjustments color, out DepthOfField dof)
    {
        var go = new GameObject("VolumenMuerte");
        go.transform.SetParent(transform, false);
        vol = go.AddComponent<Volume>();
        vol.isGlobal = true; vol.priority = 100; vol.weight = 0f;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        dof = p.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(0f); dof.gaussianEnd.Override(60f); dof.gaussianMaxRadius.Override(1.5f); dof.highQualitySampling.Override(true);
        color = p.Add<ColorAdjustments>(true);
        color.saturation.Override(0f);
        var vin = p.Add<Vignette>(true); vin.intensity.Override(0.5f); vin.smoothness.Override(0.9f); vin.color.Override(Color.black);
        vol.sharedProfile = p;
    }

    IEnumerator RutinaMuerte()
    {
        var c = CrearCanvas("PantallaMuerte", 960);
        pantallaMuerte = c.gameObject;
        var fondo = Img(c.transform, "Fondo", new Color(0.12f, 0f, 0f, 0f)); Estirar(fondo.rectTransform);
        var titulo = Txt(c.transform, "Titulo", "HAS MUERTO", 110, recursos != null ? recursos.fuenteMenu : null);
        var rt = titulo.rectTransform; rt.anchorMin = new Vector2(0, 0.55f); rt.anchorMax = new Vector2(1, 0.75f); rt.offsetMin = rt.offsetMax = Vector2.zero;
        titulo.color = new Color(1f, 0.3f, 0.25f, 0f); titulo.characterSpacing = 14;
        bool extremo = Partida.UnaVida;
        string sub = extremo
            ? "<color=#ff5050>MODO EXTREMO</color>\nSolo tenias una vida. La partida se borro y todo vuelve a empezar desde cero."
            : Partida.RanuraActual >= 0 ? "Puedes volver a tu ultimo guardado." : "Partida de prueba.";
        var texto = Txt(c.transform, "Texto", sub, 34, recursos != null ? recursos.fuenteTexto : null);
        var rx = texto.rectTransform; rx.anchorMin = new Vector2(0.15f, 0.4f); rx.anchorMax = new Vector2(0.85f, 0.55f); rx.offsetMin = rx.offsetMax = Vector2.zero;
        texto.color = new Color(1f, 1f, 1f, 0f);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 2.2f)
        {
            fondo.color = new Color(0.12f, 0f, 0f, Mathf.Lerp(0f, 0.82f, t));
            titulo.color = new Color(1f, 0.3f, 0.25f, Mathf.Clamp01(t * 1.6f - 0.3f));
            texto.color = new Color(1f, 1f, 1f, Mathf.Clamp01(t * 1.6f - 0.6f));
            yield return null;
        }

        if (extremo)
        {
            yield return new WaitForSecondsRealtime(4.5f);
            if (Partida.RanuraActual >= 0) Partida.Nueva(Partida.RanuraActual, Dificultad.Extremo);
            else Ajustes.IntroPendiente = true;
            Recargar();
            yield break;
        }

        bool hayGuardado = Partida.Existe(Partida.RanuraActual);
        var b1 = Boton(c.transform, hayGuardado ? "CARGAR ULTIMO GUARDADO" : "VOLVER A EMPEZAR", new Vector2(0, -60), () =>
        {
            if (hayGuardado) Partida.Continuar(Partida.RanuraActual);
            else if (Partida.RanuraActual >= 0) Partida.Nueva(Partida.RanuraActual, Partida.Dif);
            else Ajustes.IntroPendiente = true;
            Recargar();
        });
        Boton(c.transform, "MENU PRINCIPAL", new Vector2(0, -140), () => SceneManager.LoadScene(recursos != null ? recursos.escenaMenu : "MenuPrincipal"));
        var ayuda = Txt(c.transform, "Ayuda", "ENTER  " + (hayGuardado ? "Cargar" : "Empezar") + "      ESC  Menu principal", 22, recursos != null ? recursos.fuenteTexto : null);
        var ra = ayuda.rectTransform; ra.anchorMin = new Vector2(0, 0.2f); ra.anchorMax = new Vector2(1, 0.26f); ra.offsetMin = ra.offsetMax = Vector2.zero;
        ayuda.color = new Color(1, 1, 1, 0.45f);
        while (true)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { b1.onClick.Invoke(); yield break; }
            if (Input.GetKeyDown(KeyCode.Escape)) { SceneManager.LoadScene(recursos != null ? recursos.escenaMenu : "MenuPrincipal"); yield break; }
            yield return null;
        }
    }

    void Recargar()
    {
        Time.timeScale = 1f; AudioListener.pause = false; Controles.Bloqueado = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // ================================================================== aviso de guardado
    void CrearAviso()
    {
        canvasAviso = CrearCanvas("AvisoGuardado", 950);
        var raiz = new GameObject("Caja", typeof(RectTransform)).GetComponent<RectTransform>();
        raiz.SetParent(canvasAviso.transform, false);
        raiz.anchorMin = raiz.anchorMax = new Vector2(1, 1); raiz.pivot = new Vector2(1, 1);
        raiz.anchoredPosition = new Vector2(-36, -30); raiz.sizeDelta = new Vector2(520, 52);
        grupoAviso = raiz.gameObject.AddComponent<CanvasGroup>(); grupoAviso.alpha = 0f;
        var fondo = Img(raiz, "Fondo", new Color(0.04f, 0.06f, 0.07f, 0.72f));
        var rf = fondo.rectTransform; rf.anchorMin = new Vector2(1, 0); rf.anchorMax = new Vector2(1, 1); rf.pivot = new Vector2(1, 0.5f); rf.sizeDelta = new Vector2(300, 0);
        var acento = recursos != null ? recursos.acento : new Color(0.27f, 0.86f, 0.78f);
        var barra = Img(fondo.rectTransform, "Barra", acento);
        var rb = barra.rectTransform; rb.anchorMin = new Vector2(0, 0); rb.anchorMax = new Vector2(0, 1); rb.pivot = new Vector2(0, 0.5f); rb.sizeDelta = new Vector2(4, 0);
        giro = Img(fondo.rectTransform, "Giro", acento).rectTransform;
        giro.anchorMin = giro.anchorMax = new Vector2(0, 0.5f); giro.anchoredPosition = new Vector2(30, 0); giro.sizeDelta = new Vector2(18, 18);
        textoAviso = Txt(fondo.rectTransform, "Texto", "", 24, recursos != null ? recursos.fuenteTexto : null);
        textoAviso.alignment = TextAlignmentOptions.MidlineLeft;
        var rt = textoAviso.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(56, 0); rt.offsetMax = new Vector2(-14, 0);
    }

    void MostrarAviso(string msg, bool ok)
    {
        if (textoAviso == null) return;
        textoAviso.text = msg;
        textoAviso.color = ok ? Color.white : new Color(1f, 0.75f, 0.55f);
        // ajustar el ancho de la caja al texto
        var caja = (RectTransform)textoAviso.transform.parent;
        caja.sizeDelta = new Vector2(Mathf.Clamp(textoAviso.preferredWidth + 80f, 220f, 900f), 0);
        tAviso = ok ? 2.6f : 3.4f;
    }

    void ActualizarAviso()
    {
        if (grupoAviso == null) return;
        if (tAviso > 0f) tAviso -= Time.unscaledDeltaTime;
        float objetivo = tAviso > 0f ? 1f : 0f;
        grupoAviso.alpha = Mathf.MoveTowards(grupoAviso.alpha, objetivo, Time.unscaledDeltaTime * (objetivo > 0 ? 6f : 2f));
        if (giro != null) giro.localRotation = Quaternion.Euler(0, 0, -Time.unscaledTime * 240f);
    }

    // ================================================================== utilidades de UI
    Canvas CrearCanvas(string n, int orden)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var c = go.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = orden;
        var cs = go.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920, 1080); cs.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        return c;
    }

    static Image Img(Transform padre, string n, Color c)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(padre, false);
        var i = go.AddComponent<Image>(); i.color = c; i.raycastTarget = false; return i;
    }

    static void Estirar(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    static TextMeshProUGUI Txt(Transform padre, string n, string s, float tam, TMP_FontAsset f)
    {
        var go = new GameObject(n, typeof(RectTransform)); go.transform.SetParent(padre, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (f != null) t.font = f;
        t.text = s; t.fontSize = tam; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        return t;
    }

    Button Boton(Transform padre, string texto, Vector2 pos, System.Action accion)
    {
        var img = Img(padre, "Boton_" + texto, new Color(1f, 1f, 1f, 0.08f));
        img.raycastTarget = true;
        var r = img.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.4f); r.sizeDelta = new Vector2(560, 64); r.anchoredPosition = pos;
        var b = img.gameObject.AddComponent<Button>();
        var cols = b.colors; cols.normalColor = new Color(1, 1, 1, 0.08f); cols.highlightedColor = new Color(1, 1, 1, 0.25f); cols.pressedColor = new Color(1, 1, 1, 0.4f); b.colors = cols;
        b.targetGraphic = img;
        b.onClick.AddListener(() => accion());
        var t = Txt(r, "Texto", texto, 32, recursos != null ? recursos.fuenteMenu : null);
        Estirar(t.rectTransform);
        return b;
    }
}
