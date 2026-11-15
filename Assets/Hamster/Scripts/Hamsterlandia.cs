using System.Collections;
using UnityEngine;

/// <summary>
/// Gestor de Hamsterlandia, el parque de la feria de ciencias en la esquina del salon (lo arma HamsterBuilder).
///  1. Dormido: el parque esta apagado. Tres palancas encienden la caja musical, el carrusel y las luces del
///     mirador; cada una suma una capa al vals tetrico. Bolita no sale... solo se le ve pasar a lo lejos.
///  2. Funcion: con las tres encendidas, la palanca del escenario llama al Gran Bolita: sale de su casita,
///     baja corriendo por los tubos, hace "ta-da" en el escenario... y empieza la persecucion.
///  3. Persecucion: si te alcanza, te mata (te agrega a su coleccion). Su casita tiene la puerta chiquita:
///     si entras, se atora un rato. Adentro esta la pila verde.
///  4. Huida: al tomar la pila se abre una trampilla con un tobogan. La pila no se puede soltar; hay que
///     salir del parque (o dejarse atrapar: igual despiertas en la cabina con ella).
///  5. Calma: Bolita se queda corriendo en su rueda para siempre.
/// </summary>
public class Hamsterlandia : MonoBehaviour
{
    public static Hamsterlandia Instancia { get; private set; }
    public enum Estado { Dormido, Funcion, Persecucion, Huida, Calma }

    [Header("Zona")]
    public Bounds zona;
    public Vector3 centroGuarida;
    public float radioGuarida = 5f, pisoGuarida;
    public Transform puertaGuarida;            // centro de la puertita; forward = hacia afuera
    public Transform bisagraPuerta;            // la puertita (gira al abrirse)
    public Collider colPuerta;
    public Transform bisagraTrampilla;
    public Collider colTrampilla;
    public Vector3[] tobogan;                  // piso del tobogan, de arriba hacia abajo
    public float anchoTobogan = 2.6f, velTobogan = 9f;
    public Transform escenario;                // donde hace "ta-da" (forward = hacia el publico)
    public int[] nodosAmago;                   // a donde corre cuando "se le ve pasar"
    [HideInInspector] public GameObject[] ocultos;   // tiliches del salon que el builder escondio para hacer lugar

    [Header("Piezas")]
    public PalancaFeria[] palancas;            // 0 caja musical, 1 carrusel, 2 mirador
    public PalancaFeria palancaFuncion;
    public AtraccionFeria[] atracciones;       // una por palanca
    public AtraccionFeria carpa, noria, rueda, focos;
    public Transform puntoRueda;               // donde corre Bolita dentro de su rueda
    public Bolita bolita;
    public GameObject bateria;
    public Light focoEscenario;

    [Header("Audio")]
    public AudioSource[] capas;                // vals: caja musical, organillo, theremin
    public AudioSource galope, ambiente, ruedaAudio, fx;
    public AudioClip fanfarria, escape, campana, sonidoPalanca, sonidoTobogan, plop;

    public Estado estado { get; private set; }
    public bool JugadorDentro { get; private set; }

    Transform jugador;
    CharacterController cc;
    PlayerHealth vida;
    float tAtorado = -1f, tFuera, tTobogan, tAmago;
    bool reiniciando, pilaTomada;
    Coroutine rutinaFuncion;
    float[] volCapas;
    float volGalope;
    static readonly string[] Nombres = { "la caja musical", "el carrusel", "las luces del mirador" };

    void Awake() { Instancia = this; }
    void OnDestroy() { if (Instancia == this) Instancia = null; }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) { jugador = pc.transform; cc = pc.GetComponent<CharacterController>(); vida = pc.GetComponent<PlayerHealth>(); }
        if (vida != null) vida.OnDeath += AlMorirJugador;
        if (bolita != null) { bolita.alAtrapar = Atrapado; bolita.Esconder(); }
        volCapas = new float[capas != null ? capas.Length : 0];
        // las capas del vals suenan juntas desde el principio (en silencio) para que empalmen
        double cuando = AudioSettings.dspTime + 0.2;
        if (capas != null) foreach (var c in capas) if (c != null) { c.volume = 0f; c.loop = true; c.PlayScheduled(cuando); }
        if (ambiente != null) { ambiente.volume = 0f; ambiente.loop = true; ambiente.Play(); }
        if (ruedaAudio != null) { ruedaAudio.volume = 0f; ruedaAudio.loop = true; ruedaAudio.Play(); }
        Puerta(false, true); Trampilla(false, true);
        if (focoEscenario != null) focoEscenario.enabled = false;
        estado = Estado.Dormido;
        if (BateriaTomada) EntrarCalma(true);
    }

    void OnDisable() { if (vida != null) vida.OnDeath -= AlMorirJugador; }

    bool BateriaTomada => bateria == null || !bateria.activeInHierarchy;

    /// <summary>Palancas encendidas como bits (para guardar la partida).</summary>
    public int PalancasGuardadas
    {
        get { int m = 0; if (palancas != null) for (int i = 0; i < palancas.Length; i++) if (palancas[i] != null && palancas[i].activada) m |= 1 << i; return m; }
    }

    public void Restaurar(int mascara)
    {
        if (palancas == null) return;
        for (int i = 0; i < palancas.Length; i++)
        {
            bool on = (mascara & (1 << i)) != 0;
            if (palancas[i] != null) palancas[i].Poner(on, true);
            if (atracciones != null && i < atracciones.Length && atracciones[i] != null) atracciones[i].encendida = on;
        }
        if (Encendidas == 3) { if (noria != null) noria.encendida = true; if (focos != null) focos.encendida = true; }
    }

    int Encendidas { get { int n = 0; if (palancas != null) foreach (var p in palancas) if (p != null && p.activada) n++; return n; } }

    public bool DentroGuarida(Vector3 p)
    {
        Vector3 d = p - centroGuarida; d.y = 0f;
        return d.magnitude < radioGuarida && p.y > pisoGuarida - 0.6f && p.y < pisoGuarida + 5f;
    }

    // ================================================================== palancas
    public bool PuedeJalar(PalancaFeria p, out string motivo)
    {
        motivo = "";
        if (p == palancaFuncion)
        {
            if (estado == Estado.Calma) { motivo = "La funcion ya termino. Bolita esta muy ocupado en su rueda."; return false; }
            if (estado != Estado.Dormido) { motivo = "¡La funcion ya empezo! ¡CORRE!"; return false; }
            int n = Encendidas;
            if (n < 3) { motivo = $"LA GRAN FUNCION: enciende las 3 atracciones primero ({n}/3)"; return false; }
        }
        return true;
    }

    public void Jalar(PalancaFeria p)
    {
        p.Poner(true, false);
        if (fx != null && sonidoPalanca != null) fx.PlayOneShot(sonidoPalanca, 0.9f * Ajustes.VolEfectos);
        if (p == palancaFuncion) { rutinaFuncion = StartCoroutine(Funcion()); return; }
        int i = System.Array.IndexOf(palancas, p);
        if (i >= 0 && atracciones != null && i < atracciones.Length && atracciones[i] != null) atracciones[i].encendida = true;
        int n = Encendidas;
        if (n >= 3)
        {
            if (noria != null) noria.encendida = true;
            if (focos != null) focos.encendida = true;
            Aviso.Mostrar("Todas las atracciones estan encendidas...\n<size=60%>Algo se mueve en la carpa. La palanca de la funcion esta lista.</size>", new Color(1f, 0.8f, 0.35f), 5f);
        }
        else
        {
            SurvivalUI.Aviso("Se encendio " + (i >= 0 ? Nombres[Mathf.Clamp(i, 0, 2)] : "algo") + $"  ({n}/3)", new Color(1f, 0.8f, 0.35f));
            if (estado == Estado.Dormido) StartCoroutine(Amago(n));
        }
    }

    // a Bolita "se le ve pasar" por los tubos lejanos y vuelve a su casa
    IEnumerator Amago(int n)
    {
        yield return new WaitForSeconds(2.2f);
        if (estado != Estado.Dormido || bolita == null || bolita.Visible || nodosAmago == null || nodosAmago.Length == 0 || jugador == null) yield break;
        int mejor = nodosAmago[0]; float lejos = -1f;
        foreach (var nd in nodosAmago)
        {
            float d = Vector3.Distance(bolita.nodos[nd], jugador.position);
            if (d > lejos) { lejos = d; mejor = nd; }
        }
        Puerta(true, false);
        bolita.AparecerEn(bolita.nodoPuerta, puertaGuarida != null ? puertaGuarida.forward : Vector3.forward);
        bolita.Chillar();
        bool volvio = false;
        bolita.IrA(mejor, 10f, () => { bolita.Chillar(); bolita.IrA(bolita.nodoPuerta, 10f, () => volvio = true); });
        float t = 0f;
        while (!volvio && t < 25f && estado == Estado.Dormido) { t += Time.deltaTime; yield return null; }
        if (estado == Estado.Dormido) { bolita.Esconder(); Puerta(false, false); }
        if (n == 1) SurvivalUI.Aviso("...¿algo paso corriendo por los tubos?", new Color(0.85f, 0.8f, 0.75f));
    }

    // ================================================================== la funcion
    IEnumerator Funcion()
    {
        estado = Estado.Funcion;
        if (atracciones != null) foreach (var a in atracciones) if (a != null) a.extra = 1.6f;
        if (carpa != null) { carpa.encendida = true; carpa.extra = 1.6f; }
        if (focos != null) focos.extra = 1.8f;
        if (focoEscenario != null) focoEscenario.enabled = true;
        if (fx != null && campana != null) fx.PlayOneShot(campana, 0.9f * Ajustes.VolEfectos);
        Aviso.Mostrar("¡Damas y caballeros, bichos y bichas!\n<size=60%>Con ustedes... ¡EL GRAN BOLITA!</size>", new Color(1f, 0.82f, 0.3f), 5f);
        yield return new WaitForSeconds(1.4f);
        Puerta(true, false);
        bolita.AparecerEn(bolita.nodoPuerta, puertaGuarida != null ? puertaGuarida.forward : Vector3.forward);
        bolita.Chillar();
        bool llego = false;
        bolita.IrA(bolita.nodoEscenario, 9.5f, () => llego = true);
        float t = 0f;
        while (!llego && t < 14f) { t += Time.deltaTime; yield return null; }
        if (!llego) bolita.AparecerEn(bolita.nodoEscenario, escenario != null ? escenario.forward : Vector3.forward);
        // mira al publico (o al jugador si esta cerca) y se para en dos patas
        Vector3 mirar = escenario != null ? escenario.forward : Vector3.forward;
        if (jugador != null && Vector3.Distance(jugador.position, bolita.Posicion) < 25f) mirar = jugador.position - bolita.Posicion;
        bolita.AparecerEn(bolita.nodoEscenario, mirar);
        bolita.Tada();
        if (fx != null && fanfarria != null) fx.PlayOneShot(fanfarria, 1f * Ajustes.VolEfectos);
        yield return new WaitForSeconds(2.6f);
        Aviso.Mostrar("¡CORRE!", new Color(1f, 0.3f, 0.25f), 2.2f);
        bolita.Quieto(); bolita.Chillar(true);
        yield return new WaitForSeconds(1f);
        Empezar(Estado.Persecucion);
    }

    void Empezar(Estado e)
    {
        estado = e;
        tAtorado = -1f; tFuera = 0f;
        if (galope != null)
        {
            if (!galope.isPlaying) { galope.time = 0f; galope.Play(); }
            galope.pitch = e == Estado.Huida ? 1.07f : 1f;
        }
        if (bolita != null && jugador != null) bolita.Perseguir(jugador, VelocidadBase);
    }

    float VelocidadBase
    {
        get
        {
            float v = estado == Estado.Huida ? 4.35f : 3.95f;
            switch (Partida.Dif)
            {
                case Dificultad.Pacifico: v *= 0.88f; break;
                case Dificultad.Dificil: v *= 1.04f; break;
                case Dificultad.Extremo: v *= 1.08f; break;
            }
            return v;
        }
    }

    // ================================================================== bucle
    void Update()
    {
        if (jugador == null) return;
        Vector3 p = jugador.position;
        JugadorDentro = zona.Contains(p);
        float dt = Time.deltaTime;

        if (estado == Estado.Dormido && !reiniciando && BateriaTomada) EntrarCalma(true);   // partida cargada con la pila ya tomada

        if (estado == Estado.Persecucion || estado == Estado.Huida)
        {
            if (!pilaTomada && BateriaTomada) TomarPila();
            Perseguir(p, dt);
        }
        Tobogan(p, dt);
        Musica(p, dt);
    }

    void Perseguir(Vector3 p, float dt)
    {
        if (bolita == null || reiniciando) return;
        // liga elastica: si se queda muy atras, corre mas
        if (bolita.modo == Bolita.Modo.Perseguir)
        {
            float d = bolita.DistanciaRuta;
            bolita.Velocidad = VelocidadBase + (d > 34f ? 1.8f : d > 22f ? 0.7f : 0f);
        }

        // la puertita: Bolita se atora si el jugador esta adentro de su casa (y para eso va directo a la puerta)
        bool adentro = DentroGuarida(p);
        bolita.nodoForzado = adentro ? bolita.nodoPuerta : -1;
        if (tAtorado < 0f && adentro && bolita.modo == Bolita.Modo.Perseguir && puertaGuarida != null &&
            Vector3.Distance(bolita.Posicion, bolita.nodos[bolita.nodoPuerta]) < 3.5f)
        {
            tAtorado = estado == Estado.Huida ? 4.5f : 6.5f;
            bolita.Atorar(puertaGuarida.position, -puertaGuarida.forward);
            SurvivalUI.Aviso("¡Bolita se atoro en la puerta!", new Color(1f, 0.85f, 0.4f));
        }
        if (tAtorado >= 0f)
        {
            tAtorado -= dt;
            // si sales por la puerta mientras esta ahi, te agarra
            if (puertaGuarida != null && Vector3.Distance(p, puertaGuarida.position) < 2.2f) { tAtorado = -1f; Atrapado(); return; }
            if (tAtorado < 0f)
            {
                if (adentro)
                {
                    if (fx != null && plop != null) fx.PlayOneShot(plop, Ajustes.VolEfectos);
                    SurvivalUI.Aviso("¡PLOP!", new Color(1f, 0.6f, 0.4f));
                    Atrapado(); return;
                }
                bolita.Desatorar();
                bolita.Chillar(true);
                bolita.Perseguir(jugador, VelocidadBase);
            }
            return;
        }

        // salir del parque = escapar
        if (!JugadorDentro)
        {
            tFuera += dt;
            if (tFuera > 0.6f) Escapo();
        }
        else tFuera = 0f;
    }

    void TomarPila()
    {
        pilaTomada = true;
        Trampilla(true, false);
        Aviso.Mostrar("¡La pila verde es tuya!\n<size=60%>Se pego a tu inventario: no se puede soltar.\nY Bolita lo sabe. ¡Sal de Hamsterlandia!</size>", new Color(0.4f, 1f, 0.55f), 5.5f);
        if (bolita != null) bolita.Chillar(true);
        estado = Estado.Huida;
        if (galope != null) galope.pitch = 1.07f;
        if (tAtorado > 2.5f) tAtorado = 2.5f;     // del coraje se desatora antes
    }

    void Tobogan(Vector3 p, float dt)
    {
        if (tobogan == null || tobogan.Length < 2 || cc == null || !cc.enabled) return;
        if (colTrampilla != null && colTrampilla.enabled) return;
        float mejor = float.MaxValue; Vector3 dir = Vector3.zero; float yPiso = 0f;
        for (int i = 0; i < tobogan.Length - 1; i++)
        {
            Vector3 a = tobogan[i], b = tobogan[i + 1];
            Vector3 ab = b - a; Vector3 abh = new Vector3(ab.x, 0f, ab.z);
            float u = Mathf.Clamp01(Vector3.Dot(new Vector3(p.x - a.x, 0f, p.z - a.z), abh) / Mathf.Max(0.001f, abh.sqrMagnitude));
            Vector3 q = a + ab * u;
            float dh = new Vector2(p.x - q.x, p.z - q.z).magnitude;
            if (dh < mejor && p.y > q.y - 0.8f && p.y < q.y + 1.3f) { mejor = dh; dir = abh.normalized; yPiso = q.y; }
        }
        if (mejor > anchoTobogan) return;
        cc.Move(dir * velTobogan * dt);
        tTobogan -= dt;
        if (tTobogan <= 0f && fx != null && sonidoTobogan != null) { fx.PlayOneShot(sonidoTobogan, 0.8f * Ajustes.VolEfectos); tTobogan = 1.4f; }
    }

    void Musica(Vector3 p, float dt)
    {
        // se oye fuerte adentro y se apaga con la distancia afuera
        Vector3 c = zona.ClosestPoint(p);
        float cerca = Mathf.Clamp01(1f - Vector3.Distance(c, p) / 45f);
        bool caza = estado == Estado.Persecucion || estado == Estado.Huida;
        if (capas != null)
            for (int i = 0; i < capas.Length; i++)
            {
                if (capas[i] == null) continue;
                bool on = i < palancas.Length && palancas[i] != null && palancas[i].activada;
                float meta = caza ? 0f : (on ? (estado == Estado.Calma ? 0.45f : 0.6f) : 0f) * cerca;
                volCapas[i] = Mathf.MoveTowards(volCapas[i], meta, dt * (caza ? 1.5f : 0.4f));
                capas[i].volume = volCapas[i] * Ajustes.VolMusica;
            }
        if (galope != null)
        {
            volGalope = Mathf.MoveTowards(volGalope, caza ? 0.85f * Mathf.Max(cerca, 0.35f) : 0f, dt * (caza ? 2f : 0.5f));
            galope.volume = volGalope * Ajustes.VolMusica;
            if (volGalope <= 0f && galope.isPlaying && !caza) galope.Stop();
        }
        if (ambiente != null) ambiente.volume = Mathf.MoveTowards(ambiente.volume, (caza ? 0f : 0.5f * cerca) * Ajustes.VolAmbiente, dt * 0.5f);
        if (rueda != null && estado != Estado.Calma)
        {
            // la rueda se mueve sola de vez en cuando... (da miedito)
            tAmago -= dt;
            float v = tAmago > 0f ? 70f : 0f;
            if (tAmago < -Random.Range(14f, 26f)) tAmago = Random.Range(1.2f, 2.5f);
            rueda.velocidadManual = Mathf.MoveTowards(Mathf.Max(0f, rueda.velocidadManual), v, dt * 60f);
            if (ruedaAudio != null) ruedaAudio.volume = Mathf.Clamp01(rueda.velocidadManual / 70f) * 0.35f * cerca * Ajustes.VolEfectos;
        }
        else if (ruedaAudio != null) ruedaAudio.volume = 0.3f * cerca * Ajustes.VolEfectos;
    }

    // ================================================================== finales de la persecucion
    void Atrapado()
    {
        if (reiniciando || (estado != Estado.Persecucion && estado != Estado.Huida)) return;
        reiniciando = true;
        tAtorado = -1f;
        Estadisticas.Actual.atrapadoBolita++;
        if (bolita != null) bolita.Festejar();
        if (!Partida.Agresivos)
        {
            // pacifico: no hace daño, solo te saca de su parque
            if (cc != null)
            {
                cc.enabled = false;
                jugador.position = new Vector3(zona.center.x, 0.2f, zona.max.z + 4f);
                cc.enabled = true;
            }
            Aviso.Mostrar("Bolita te saco de su parque...\n<size=60%>" + (BateriaTomada ? "pero la pila verde sigue contigo." : "y te cerro la puerta.") + "</size>", new Color(1f, 0.8f, 0.4f), 4f);
            StartCoroutine(Reiniciar(1.5f));
            return;
        }
        GestorPartida.CausaMuerte = BateriaTomada ? "Bolita te agrego a su coleccion... pero la pila verde sigue contigo." : "Bolita te agrego a su coleccion.";
        if (vida != null) vida.DanioEntorno(99999f);
        StartCoroutine(Reiniciar(3f));
    }

    void AlMorirJugador()
    {
        if (estado == Estado.Persecucion || estado == Estado.Huida || estado == Estado.Funcion)
        {
            if (!reiniciando) { reiniciando = true; StartCoroutine(Reiniciar(3f)); }
        }
    }

    IEnumerator Reiniciar(float espera)
    {
        yield return new WaitForSeconds(espera);
        if (rutinaFuncion != null) { StopCoroutine(rutinaFuncion); rutinaFuncion = null; }
        if (bolita != null) bolita.Esconder();
        tAtorado = -1f;
        if (BateriaTomada) EntrarCalma(false);
        else
        {
            estado = Estado.Dormido;
            if (palancaFuncion != null) palancaFuncion.Poner(false, true);
            Puerta(false, false);
            if (carpa != null) { carpa.encendida = false; carpa.extra = 1f; }
            if (atracciones != null) foreach (var a in atracciones) if (a != null) a.extra = 1f;
            if (focos != null) focos.extra = 1f;
            if (focoEscenario != null) focoEscenario.enabled = false;
        }
        reiniciando = false;
    }

    void Escapo()
    {
        bool conPila = BateriaTomada;
        if (conPila)
        {
            if (fx != null && escape != null) fx.PlayOneShot(escape, Ajustes.VolEfectos);
            Aviso.Mostrar("¡Escapaste de Hamsterlandia!\n<size=60%>con la pila verde. Bolita se quedo haciendo berrinche.</size>", new Color(0.45f, 1f, 0.6f), 5f);
            Estadisticas.Actual.escapesBolita++;
            EntrarCalma(false);
        }
        else
        {
            SurvivalUI.Aviso("Bolita se rindio... por ahora", new Color(1f, 0.8f, 0.4f));
            reiniciando = true;
            if (bolita != null) { bolita.Chillar(true); bolita.IrA(bolita.nodoPuerta, 6f, () => bolita.Esconder()); }
            estado = Estado.Dormido;
            StartCoroutine(Reiniciar(6f));
        }
    }

    void EntrarCalma(bool inmediato)
    {
        estado = Estado.Calma;
        pilaTomada = true;
        Puerta(true, inmediato); Trampilla(true, inmediato);
        if (palancas != null) foreach (var p in palancas) if (p != null) p.Poner(true, inmediato);
        if (palancaFuncion != null) palancaFuncion.Poner(true, inmediato);
        if (atracciones != null) foreach (var a in atracciones) if (a != null) { a.encendida = true; a.extra = 1f; }
        if (noria != null) noria.encendida = true;
        if (focos != null) { focos.encendida = true; focos.extra = 1f; }
        if (carpa != null) { carpa.encendida = true; carpa.extra = 1f; }
        if (focoEscenario != null) focoEscenario.enabled = true;
        if (rueda != null) rueda.velocidadManual = 170f;
        if (bolita != null && puntoRueda != null) bolita.EnRueda(rueda != null ? rueda.transform : null, puntoRueda.position, puntoRueda.forward);
    }

    // ================================================================== puertas
    void Puerta(bool abierta, bool inmediato)
    {
        if (colPuerta != null) colPuerta.enabled = !abierta;
        if (bisagraPuerta == null) return;
        if (inmediato || !isActiveAndEnabled) bisagraPuerta.localRotation = Quaternion.Euler(0f, abierta ? -105f : 0f, 0f);
        else StartCoroutine(GirarPuerta(bisagraPuerta, abierta ? -105f : 0f, true));
    }

    void Trampilla(bool abierta, bool inmediato)
    {
        if (colTrampilla != null) colTrampilla.enabled = !abierta;
        if (bisagraTrampilla == null) return;
        if (inmediato || !isActiveAndEnabled) bisagraTrampilla.localRotation = Quaternion.Euler(abierta ? -100f : 0f, 0f, 0f);
        else
        {
            StartCoroutine(GirarPuerta(bisagraTrampilla, abierta ? -100f : 0f, false));
            if (abierta) SurvivalUI.Aviso("Se abrio una trampilla en el piso...", new Color(0.8f, 1f, 0.8f));
        }
    }

    IEnumerator GirarPuerta(Transform t, float meta, bool enY)
    {
        Quaternion a = t.localRotation, b = enY ? Quaternion.Euler(0f, meta, 0f) : Quaternion.Euler(meta, 0f, 0f);
        for (float k = 0f; k < 1f; k += Time.deltaTime / 0.6f) { t.localRotation = Quaternion.Slerp(a, b, Mathf.SmoothStep(0f, 1f, k)); yield return null; }
        t.localRotation = b;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.8f, 0.6f);
        Gizmos.DrawWireCube(zona.center, zona.size);
        Gizmos.DrawWireSphere(centroGuarida, radioGuarida);
        if (tobogan != null) for (int i = 0; i < tobogan.Length - 1; i++) Gizmos.DrawLine(tobogan[i], tobogan[i + 1]);
    }
}
