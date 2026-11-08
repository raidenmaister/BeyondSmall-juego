using UnityEngine;

/// <summary>
/// Abeja de la colmena del arbol. Papeles (pacificas al principio):
///   Obrera     : va de celda en celda por las paredes del panal y se queda "trabajando".
///   Nodriza    : igual, pero junto a las larvas de la guarderia.
///   Guardia    : patrulla en circulos frente a la entrada o junto a la reina.
///   Forrajera  : sale por un hueco del tronco, vuela a las flores de afuera, se posa a polinizar y vuelve.
/// Furiosas (golpeaste a una, mataste una larva o cayo la reina) persiguen al jugador y lo pican en picada.
/// Con la antorcha encendida el humo las espanta: no se acercan a menos de Antorcha.Radio.
/// Usa RatHealth, asi el puñetazo del jugador las daña.
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class AbejaColmena : MonoBehaviour
{
    public enum Rol { Obrera, Nodriza, Guardia, Forrajera }

    public Rol rol;
    public Animator animator;
    public Transform visual;
    public Vector3[] puntos;          // lugares de trabajo (obrera/nodriza) o centro de patrulla (guardia)
    public Vector3[] normales;        // hacia donde mira en cada lugar de trabajo
    public Vector3[] ruta;            // forrajera: desde dentro hasta el hueco y afuera
    public float radioPatrulla = 6f;
    public float velocidad = 6f;
    public float velocidadAtaque = 11f;
    public float danio = 7f;
    public GameObject particulasMuerte;

    public bool Hostil { get; private set; }
    public bool Muerta { get; private set; }

    enum Fase { Volar, Trabajar, Ruta, Flor, Regresar, Perseguir, Preparar, Picada, Retirada, Huir }

    RatHealth vida;
    Transform jugador;
    PlayerHealth vidaJugador;
    Fase fase;
    Vector3 vel, objetivo, mirar;
    float timer, timerEsquiva, cd, fasePatrulla, semilla;
    int indice, indiceRuta;
    Vector3 flor, dirPicada;
    string animActual = "";

    void Awake()
    {
        vida = GetComponent<RatHealth>();
        semilla = Random.value * 100f;
        fasePatrulla = Random.value * Mathf.PI * 2f;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.isTrigger = true;
        if (animator != null) animator.cullingMode = AnimatorCullingMode.CullCompletely;
    }

    void OnEnable()
    {
        vida.OnHealthChanged += AlDanio;
        vida.OnDeath += Morir;
        if (Colmena.Instancia != null) Colmena.Instancia.Registrar(this);
    }

    void OnDisable()
    {
        vida.OnHealthChanged -= AlDanio;
        vida.OnDeath -= Morir;
        if (Colmena.Instancia != null) Colmena.Instancia.Quitar(this);
    }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) { jugador = pc.transform; vidaJugador = pc.GetComponent<PlayerHealth>(); }
        if (Colmena.Instancia != null) { Colmena.Instancia.Registrar(this); if (Colmena.Instancia.Furia) Enfurecer(); }
        if (!Hostil) EmpezarRutina();
        Anim("Run");
    }

    void Anim(string s)
    {
        if (animator == null || animActual == s) return;
        animActual = s; animator.CrossFadeInFixedTime(s, 0.1f);
    }

    void EmpezarRutina()
    {
        switch (rol)
        {
            case Rol.Forrajera:
                indiceRuta = 0; fase = Fase.Ruta; break;
            case Rol.Guardia:
                fase = Fase.Volar; break;
            default:
                indice = puntos != null && puntos.Length > 0 ? Random.Range(0, puntos.Length) : 0;
                fase = Fase.Volar; break;
        }
    }

    // ------------------------------------------------------------------ daño y furia
    void AlDanio(float actual, float max)
    {
        if (Muerta || actual <= 0f) return;
        if (!Hostil && Colmena.Instancia != null) Colmena.Instancia.Alarma(transform.position, 22f);
        Enfurecer();
        if (jugador != null) vel += (transform.position - jugador.position).normalized * 9f;   // retroceso
    }

    public void Enfurecer()
    {
        if (Muerta || Hostil) return;
        Hostil = true;
        fase = Fase.Perseguir;
        cd = Random.Range(0.3f, 1.5f);
    }

    void Morir()
    {
        if (Muerta) return;
        Muerta = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        Anim("Death");
        if (Colmena.Instancia != null) Colmena.Instancia.Quitar(this);
        StartCoroutine(Caer());
    }

    System.Collections.IEnumerator Caer()
    {
        float vy = 0f;
        for (float t = 0f; t < 2.5f; t += Time.deltaTime)
        {
            vy -= 22f * Time.deltaTime;
            Vector3 p = transform.position + new Vector3(vel.x * 0.3f, vy, vel.z * 0.3f) * Time.deltaTime;
            if (Physics.Raycast(transform.position + Vector3.up * 0.3f, Vector3.down, out var hit, 0.3f - vy * Time.deltaTime + 0.1f, ~0, QueryTriggerInteraction.Ignore))
            { p.y = hit.point.y; vy = 0f; }
            transform.position = p;
            vel *= 0.95f;
            yield return null;
        }
        if (particulasMuerte != null) Instantiate(particulasMuerte, transform.position + Vector3.up * 0.4f, Quaternion.identity);
        var drops = GetComponent<DropTable>();
        if (drops != null) drops.Drop(transform.position);
        Destroy(gameObject);
    }

    // ------------------------------------------------------------------ bucle
    void Update()
    {
        if (Muerta) return;
        float dt = Time.deltaTime;
        timer -= dt; cd -= dt;

        // el humo de la antorcha gana a todo
        if (jugador != null && Antorcha.Encendida && (Antorcha.Posicion - transform.position).sqrMagnitude < (Antorcha.Radio + 2f) * (Antorcha.Radio + 2f))
        {
            if (fase != Fase.Huir) { fase = Fase.Huir; timer = 0f; }
        }
        else if (fase == Fase.Huir) { fase = Hostil ? Fase.Perseguir : Fase.Volar; if (!Hostil && rol == Rol.Forrajera) fase = Fase.Regresar; }

        switch (fase)
        {
            case Fase.Volar: Volar(dt); break;
            case Fase.Trabajar: Trabajar(dt); break;
            case Fase.Ruta: Ruta(dt, 1); break;
            case Fase.Regresar: Ruta(dt, -1); break;
            case Fase.Flor: Flor(dt); break;
            case Fase.Perseguir: Perseguir(dt); break;
            case Fase.Preparar: Preparar(dt); break;
            case Fase.Picada: Picada(dt); break;
            case Fase.Retirada: Retirada(dt); break;
            case Fase.Huir: Huir(dt); break;
        }
        Mover(dt);
    }

    // rutina de obrera/nodriza/guardia
    void Volar(float dt)
    {
        Anim("Run");
        if (rol == Rol.Guardia)
        {
            Vector3 c = puntos != null && puntos.Length > 0 ? puntos[0] : transform.position;
            fasePatrulla += dt * 0.6f;
            objetivo = c + new Vector3(Mathf.Cos(fasePatrulla) * radioPatrulla, Mathf.Sin(fasePatrulla * 1.7f + semilla) * 1.2f, Mathf.Sin(fasePatrulla) * radioPatrulla);
            mirar = Vector3.zero;
            return;
        }
        if (puntos == null || puntos.Length == 0) return;
        objetivo = puntos[indice];
        mirar = Vector3.zero;
        if ((transform.position - objetivo).sqrMagnitude < 0.8f) { fase = Fase.Trabajar; timer = Random.Range(2.5f, 6f); }
    }

    void Trabajar(float dt)
    {
        // se queda pegada a la celda, moviendose un poco (limpia, deposita miel, alimenta larvas)
        Vector3 n = normales != null && indice < normales.Length ? normales[indice] : Vector3.forward;
        objetivo = puntos[indice] + new Vector3(Mathf.Sin(Time.time * 2.3f + semilla), Mathf.Sin(Time.time * 3.1f + semilla) * 0.5f, Mathf.Cos(Time.time * 1.9f + semilla)) * 0.25f;
        mirar = -n;
        if (timer <= 0f)
        {
            if (rol == Rol.Forrajera) { fase = Fase.Ruta; indiceRuta = 0; return; }   // vuelve a salir a las flores
            int sig = Random.Range(0, puntos.Length);
            indice = sig == indice ? (sig + 1) % puntos.Length : sig;
            fase = Fase.Volar;
        }
    }

    // forrajera: recorre la ruta hacia fuera (dir=1) o de vuelta (dir=-1)
    void Ruta(float dt, int dir)
    {
        Anim("Run");
        mirar = Vector3.zero;
        if (ruta == null || ruta.Length == 0) { fase = Fase.Volar; return; }
        indiceRuta = Mathf.Clamp(indiceRuta, 0, ruta.Length - 1);
        objetivo = ruta[indiceRuta];
        if ((transform.position - objetivo).sqrMagnitude < 4f)
        {
            indiceRuta += dir;
            if (dir > 0 && indiceRuta >= ruta.Length)
            {   // a una flor cercana
                var fl = Colmena.Instancia != null ? Colmena.Instancia.flores : null;
                if (fl != null && fl.Length > 0)
                {
                    flor = fl[Random.Range(0, fl.Length)];
                    fase = Fase.Flor; timer = -1f;
                }
                else { fase = Fase.Regresar; indiceRuta = ruta.Length - 1; }
            }
            else if (dir < 0 && indiceRuta < 0)
            {   // dentro: deja el polen en una celda y vuelve a salir
                fase = Fase.Ruta; indiceRuta = 0;
                if (puntos != null && puntos.Length > 0) { indice = Random.Range(0, puntos.Length); fase = Fase.Trabajar; timer = Random.Range(3f, 7f); }
            }
        }
    }

    void Flor(float dt)
    {
        Vector3 encima = flor + Vector3.up * 0.9f;
        if (timer < 0f)
        {   // volando hacia la flor
            objetivo = encima + Vector3.up * 2f;
            if ((transform.position - objetivo).sqrMagnitude < 3f) timer = Random.Range(3f, 6f);
            return;
        }
        // posada: aleteo corto sobre la flor
        objetivo = encima + new Vector3(Mathf.Sin(Time.time * 4f + semilla) * 0.15f, 0f, Mathf.Cos(Time.time * 3f + semilla) * 0.15f);
        if (timer <= 0f)
        {
            if (Random.value < 0.45f && Colmena.Instancia != null && Colmena.Instancia.flores.Length > 1)
            {   // otra flor cercana
                var fl = Colmena.Instancia.flores;
                Vector3 mejor = flor; float md = float.MaxValue;
                for (int i = 0; i < 6; i++) { var f = fl[Random.Range(0, fl.Length)]; float d = (f - flor).sqrMagnitude; if (d > 4f && d < md) { md = d; mejor = f; } }
                flor = mejor; timer = -1f;
            }
            else { fase = Fase.Regresar; indiceRuta = ruta.Length - 1; }
        }
        // si el timer se agota mientras vuela, se reinicia
        if (timer > 0f && (transform.position - encima).sqrMagnitude > 9f) timer = Mathf.Max(timer, 0.5f);
    }

    // ------------------------------------------------------------------ ataque
    Vector3 PechoJugador => jugador.position + Vector3.up * 1.1f;

    void Perseguir(float dt)
    {
        Anim("Run");
        if (jugador == null) return;
        Vector3 pj = PechoJugador;
        // ronda al jugador a unos metros y busca un hueco para picar
        float ang = Time.time * 1.3f + semilla;
        Vector3 rondar = pj + new Vector3(Mathf.Cos(ang) * 3.2f, 1.2f + Mathf.Sin(ang * 2f) * 0.6f, Mathf.Sin(ang) * 3.2f);
        objetivo = Colmena.Instancia != null && Bloqueado(transform.position, rondar) ? Colmena.Instancia.Paso(transform.position, rondar) : rondar;
        mirar = pj - transform.position;
        if (cd <= 0f && (pj - transform.position).sqrMagnitude < 7f * 7f && !Bloqueado(transform.position, pj))
        { fase = Fase.Preparar; timer = 0.35f; }
    }

    void Preparar(float dt)
    {
        // se echa hacia atras un instante: aviso de la picada
        Vector3 pj = PechoJugador;
        objetivo = transform.position + (transform.position - pj).normalized * 1.5f + Vector3.up * 0.4f;
        mirar = pj - transform.position;
        if (timer <= 0f) { dirPicada = (pj - transform.position).normalized; fase = Fase.Picada; timer = 0.8f; Anim("Attack"); }
    }

    void Picada(float dt)
    {
        Vector3 pj = PechoJugador;
        dirPicada = Vector3.Slerp(dirPicada, (pj - transform.position).normalized, 2.5f * dt);
        vel = dirPicada * velocidadAtaque;
        mirar = dirPicada;
        if ((pj - transform.position).sqrMagnitude < 1.7f * 1.7f)
        {
            if (vidaJugador != null) vidaJugador.TakeDamage(danio);
            StatusEffects.Poison(1.5f, 3f);
            fase = Fase.Retirada; timer = Random.Range(0.8f, 1.4f); cd = Random.Range(1.2f, 2.4f);
            vel = -dirPicada * 7f + Vector3.up * 3f;
        }
        else if (timer <= 0f) { fase = Fase.Retirada; timer = 0.6f; cd = 0.8f; }
    }

    void Retirada(float dt)
    {
        Anim("Run");
        objetivo = transform.position + (transform.position - PechoJugador).normalized * 3f + Vector3.up;
        if (timer <= 0f) fase = Fase.Perseguir;
    }

    void Huir(float dt)
    {
        Anim("Run");
        // se aleja del humo manteniendo la altura (y vuelve a su rutina cuando el humo se va)
        Vector3 lejos = transform.position - Antorcha.Posicion; lejos.y *= 0.3f;
        if (lejos.sqrMagnitude < 0.01f) lejos = Random.onUnitSphere;
        objetivo = transform.position + lejos.normalized * 6f + Vector3.up * 0.5f;
        mirar = Vector3.zero;
    }

    // ------------------------------------------------------------------ movimiento de vuelo
    bool Bloqueado(Vector3 a, Vector3 b)
    {
        return Physics.Linecast(a, b, out _, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    void Mover(float dt)
    {
        if (fase != Fase.Picada)
        {
            Vector3 d = objetivo - transform.position;
            float vmax = Hostil ? velocidadAtaque * 0.8f : velocidad;
            if (fase == Fase.Huir) vmax = velocidadAtaque;
            Vector3 deseada = d.magnitude > 0.05f ? Vector3.ClampMagnitude(d * 2.2f, vmax) : Vector3.zero;
            vel = Vector3.Lerp(vel, deseada, 1f - Mathf.Exp(-4f * dt));
        }
        // esquiva paredes y pisos
        timerEsquiva -= dt;
        if (timerEsquiva <= 0f && vel.sqrMagnitude > 0.5f)
        {
            timerEsquiva = 0.12f;
            if (Physics.SphereCast(transform.position, 0.5f, vel.normalized, out var hit, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                vel = Vector3.ProjectOnPlane(vel, hit.normal) + hit.normal * 2.5f;
        }
        transform.position += vel * dt;
        // orientacion: hacia donde mira o hacia donde vuela, con un leve alabeo
        Vector3 f = mirar.sqrMagnitude > 0.001f ? mirar : vel;
        f.y *= 0.35f;
        if (f.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(f.normalized), 6f * dt);
        if (visual != null) visual.localPosition = new Vector3(0f, Mathf.Sin(Time.time * 5f + semilla) * 0.08f, 0f);
    }
}
