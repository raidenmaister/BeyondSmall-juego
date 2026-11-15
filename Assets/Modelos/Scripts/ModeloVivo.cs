using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Viste a un bicho con un modelo descargado (Sketchfab, CC-BY: ver Assets/Modelos/creditos_sketchfab.json).
/// El modelo viejo hecho por codigo NO se borra: se deja invisible (forceRenderingOff) para que la IA, sus huesos,
/// colisiones y animaciones procedurales sigan funcionando igual. El nuevo se coloca encima, con el mismo tamaño,
/// mirando hacia donde camina el bicho, y se anima solo con sus clips (reposo / caminar / correr segun la velocidad,
/// ataque y muerte por eventos).
/// </summary>
public class ModeloVivo : MonoBehaviour
{
    public class Def
    {
        public string modelo;                 // Resources/<modelo>/<modelo>
        public float giroY;                   // para que la cabeza del modelo mire a +Z
        public float giroX;                   // modelos que vienen de pie (la lombriz trae el cuerpo hacia arriba)
        public float escala = 1f;             // ajuste fino sobre el tamaño del modelo viejo
        public string[] reposo, caminar, correr, ataque, muerte;
        public string[] posado;                         // quieto en el suelo (abeja dormida de noche)
        public float velCaminar = 1.5f, velCorrer = 4f;   // unidades/s en las que el clip se ve a velocidad normal
        public string[] ocultar = new string[0];         // piezas del modelo que sobran (suelos, sombras pegadas)
        public bool volador;
        public bool serpentear;                          // modelo sin animacion: el cuerpo ondula por codigo (lombriz)
        public float tinteR = 1f, tinteG = 1f, tinteB = 1f;   // aclarar o calentar el color del modelo (multiplica su color base)
        public bool alinear;                             // girar el modelo para que su largo vaya en el eje del viejo
    }

    public static readonly Dictionary<string, Def> Defs = new Dictionary<string, Def>
    {
        { "Hormiga", new Def { modelo = "Hormiga", reposo = new[] { "Take 001" }, caminar = new[] { "Take 001" }, velCaminar = 2f } },
        { "ReinaHormiga", new Def { modelo = "ReinaHormiga", reposo = new[] { "idle" }, caminar = new[] { "Walk" }, correr = new[] { "Walk" }, ataque = new[] { "bite", "Attack2", "grab" }, velCaminar = 3f } },
        { "Abeja", new Def { modelo = "Abeja", reposo = new[] { "_bee_hover" }, caminar = new[] { "_bee_hover" }, posado = new[] { "_bee_idle" }, volador = true } },
        { "Rata", new Def { modelo = "Rata", reposo = new[] { "idle_A1", "idle_A2" }, caminar = new[] { "walk_A1" }, correr = new[] { "run_A1" }, velCaminar = 2f, velCorrer = 5f } },
        { "Oruga", new Def { modelo = "Oruga", giroY = 180f, reposo = new[] { "ArmatureAction" }, caminar = new[] { "ArmatureAction" }, velCaminar = 0.8f, ocultar = new[] { "shadow_0" } } },   // trae una sombra falsa plana que sale como motas
        { "Mariposa", new Def { modelo = "Mariposa", reposo = new[] { "|!" }, caminar = new[] { "|2" }, correr = new[] { "|3" }, volador = true } },
        { "Ciempies", new Def { modelo = "Ciempies", giroY = -90f, reposo = new[] { "Animation" }, caminar = new[] { "Animation" }, velCaminar = 2.5f } },
        { "Cochinilla", new Def { modelo = "Cochinilla", reposo = new[] { "Walk" }, caminar = new[] { "Walk" }, velCaminar = 1.2f } },
        { "Arana", new Def { modelo = "Arana", giroY = 180f, reposo = new[] { "idle" }, caminar = new[] { "|walk" }, correr = new[] { "run" }, ataque = new[] { "attack" }, muerte = new[] { "die" }, velCaminar = 1.8f, velCorrer = 4f } },
        { "Escarabajo", new Def { modelo = "Escarabajo", reposo = new[] { "idle" }, caminar = new[] { "walk" }, correr = new[] { "walk" }, ataque = new[] { "attack" }, muerte = new[] { "death" }, velCaminar = 1.5f, velCorrer = 4f } },
        { "Lombriz", new Def { modelo = "Lombriz", giroX = -90f, serpentear = true, velCaminar = 1f } },
        { "Larva", new Def { modelo = "Larva", alinear = true, tinteR = 1.45f, tinteG = 1.35f, tinteB = 1.15f, reposo = new[] { "Larva01" }, caminar = new[] { "Larva01" } } },
        { "Bolita", new Def { modelo = "Bolita", reposo = new[] { "Armature.001" }, caminar = new[] { "Armature.001" }, correr = new[] { "Armature.001" }, velCaminar = 3f, velCorrer = 7f } },
    };

    public string clave;
    public float ajusteEscala = 1f;
    [Tooltip("Mas lejos que esto de la camara el modelo no se dibuja ni se anima (rendimiento: hay cientos de bichos)")]
    public float distanciaVisible = 85f;
    [Tooltip("Si el propio juego ya tumba al bicho al morir (CreatureVisual, HormigaNido...), el modelo no rueda por su cuenta")]
    public bool rodarAlMorir = true;
    /// <summary>Piezas del modelo viejo que se conservan y pasan a la cabeza del nuevo (corona del Rey, carga de las ratas).</summary>
    public static readonly string[] Conservar = { "Corona", "BrilloOjos", "Carga" };
    /// <summary>Sin corte por distancia (el fotografo del final retrata bichos lejos de la camara del jugador).</summary>
    public static bool SiempreVisible;
    /// <summary>0..1: el modelo camina o corre aunque no se mueva (poses de las fotos del final). -1 = segun su movimiento.</summary>
    [HideInInspector] public float paseoForzado = -1f;
    float tMuerte = -1f;
    GameObject modelo;
    Def def;
    PlayableGraph grafo;
    AnimationMixerPlayable mezcla;
    readonly List<AnimationClipPlayable> clips = new List<AnimationClipPlayable>();
    int iReposo = -1, iCaminar = -1, iCorrer = -1, iAtaque = -1, iMuerte = -1, iPosado = -1;
    /// <summary>Lo pone la IA: el bicho esta posado y quieto (usa la animacion "posado" si el modelo la tiene).</summary>
    [HideInInspector] public bool posado;
    Vector3 posAnterior;
    float velSuave, tAtaque = -1f, signo = 1f, calibrado;
    // rendimiento: el grafo se evalua a mano, solo si se ve y menos veces por segundo cuanto mas lejos
    float acumEval;
    int evaluaciones; bool apoyado;
    static Mesh horneado;
    Renderer principal;
    float[] pesos;
    bool muerto;
    Transform giro;

    /// <summary>Pone el modelo a un bicho. Devuelve false si no hay modelo para esa clave.</summary>
    public static ModeloVivo Vestir(GameObject bicho, string clave, float ajuste = 1f, bool rodar = true, float distancia = 85f)
    {
        if (bicho == null || !Defs.ContainsKey(clave)) return null;
        var mv = bicho.GetComponent<ModeloVivo>();
        if (mv != null) return mv;
        mv = bicho.AddComponent<ModeloVivo>();
        mv.clave = clave; mv.ajusteEscala = ajuste; mv.rodarAlMorir = rodar; mv.distanciaVisible = distancia;
        return mv;
    }

    static bool EsConservada(Transform t, Transform raiz)
    {
        for (var x = t; x != null && x != raiz; x = x.parent)
            foreach (var n in Conservar) if (x.name == n) return true;
        return false;
    }

    void Start()
    {
        def = Defs[clave];
        var prefab = Resources.Load<GameObject>(def.modelo + "/" + def.modelo);
        if (prefab == null) { Debug.LogWarning("[ModeloVivo] falta el modelo " + def.modelo); enabled = false; return; }

        // si es una copia de un bicho ya vestido (hecatombe, plantillas), se quita el modelo copiado y se rehace
        var copia = transform.Find("ModeloVivo");
        if (copia != null) { copia.SetParent(null); Destroy(copia.gameObject); }
        // tamaño y apoyo del modelo viejo (antes de ocultarlo)
        var viejos = new List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (!(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer) && r.GetComponentInParent<Canvas>() == null
                && !EsConservada(r.transform, transform)) viejos.Add(r);
        foreach (var r in viejos) r.forceRenderingOff = false;      // medir con todo visible
        Bounds bv = Medir(viejos, false);   // los viejos son de codigo: sin piezas sobrantes (y a veces el cuerpo es la unica pieza grande)

        // el nuevo modelo dentro de un pivote (para girarlo hacia donde camina)
        giro = new GameObject("ModeloVivo").transform;
        giro.SetParent(transform, false);
        modelo = Instantiate(prefab, giro);
        modelo.name = def.modelo;
        // misma capa que el bicho (las fotos del final dibujan solo una capa)
        foreach (var t in modelo.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
        foreach (var o in def.ocultar) foreach (var t in modelo.GetComponentsInChildren<Transform>(true)) if (t.name == o) t.gameObject.SetActive(false);
        modelo.transform.localRotation = Quaternion.Euler(0f, def.giroY, 0f) * Quaternion.Euler(def.giroX, 0f, 0f);
        bool tinte = def.tinteR != 1f || def.tinteG != 1f || def.tinteB != 1f;
        foreach (var r in modelo.GetComponentsInChildren<Renderer>(true))
        {
            if (tinte)
            {
                var mpb = new MaterialPropertyBlock();
                for (int k = 0; k < r.sharedMaterials.Length; k++)
                {
                    var mat = r.sharedMaterials[k];
                    if (mat == null || !mat.HasProperty("baseColorFactor")) continue;
                    var c0 = mat.GetColor("baseColorFactor");
                    r.GetPropertyBlock(mpb, k);
                    mpb.SetColor("baseColorFactor", new Color(c0.r * def.tinteR, c0.g * def.tinteG, c0.b * def.tinteB, c0.a));
                    r.SetPropertyBlock(mpb, k);
                }
            }
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) smr.updateWhenOffscreen = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        // escala: el largo horizontal del nuevo igual al del viejo
        Bounds bn = Medir(new List<Renderer>(modelo.GetComponentsInChildren<Renderer>()));
        // bichos alargados sin animacion (lombriz): el cuerpo en el mismo eje que el del modelo viejo
        if ((def.serpentear || def.alinear) && (bv.size.x > bv.size.z) != (bn.size.x > bn.size.z))
        {
            modelo.transform.Rotate(0f, 90f, 0f, Space.World);
            bn = Medir(new List<Renderer>(modelo.GetComponentsInChildren<Renderer>()));
        }
        float largoViejo = Mathf.Max(bv.size.x, bv.size.z), largoNuevo = Mathf.Max(bn.size.x, bn.size.z);
        if (largoViejo > 0.01f && largoNuevo > 0.0001f)
            modelo.transform.localScale *= largoViejo / largoNuevo * def.escala * ajusteEscala;
        // apoyarlo donde estaba el viejo (mismo centro en planta y misma base)
        bn = Medir(new List<Renderer>(modelo.GetComponentsInChildren<Renderer>()));
        Vector3 delta = new Vector3(bv.center.x - bn.center.x, bv.min.y - bn.min.y, bv.center.z - bn.center.z);
        if (def.volador) delta.y = bv.center.y - bn.center.y;
        modelo.transform.position += delta;

        // esconder el viejo: sigue existiendo (IA, huesos, colisiones) pero no se dibuja
        foreach (var r in viejos) r.forceRenderingOff = true;
        // ya medido: las mallas con huesos usan su caja (un poco holgada) en vez de recalcularla cada cuadro,
        // asi lo que queda fuera de la pantalla no se deforma ni se anima
        float mayor = 0f;
        foreach (var r in modelo.GetComponentsInChildren<Renderer>())
        {
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) { var lb = smr.localBounds; lb.Expand(lb.size.magnitude * 0.35f); smr.updateWhenOffscreen = false; smr.localBounds = lb; }
            float t = r.bounds.size.sqrMagnitude; if (t > mayor) { mayor = t; principal = r; }
        }

        Animar();
        if (def.serpentear) PrepararOndas();
        PasarConservadas();
        var vida = GetComponentInParent<RatHealth>();
        if (vida != null) vida.OnDeath += AlMorir;
        posAnterior = transform.position;
    }

    /// <summary>La corona del Rey Rata, su brillo de ojos o la carne que cargan las ratas: a la cabeza del modelo nuevo.</summary>
    void PasarConservadas()
    {
        Transform cabeza = null;
        foreach (var t in modelo.GetComponentsInChildren<Transform>())
        {
            string n = t.name.ToLower();
            if (n.Contains("head") || n.Contains("cabeza")) { cabeza = t; if (n.Contains("end")) break; }
        }
        if (cabeza == null) return;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t.IsChildOf(modelo.transform) || System.Array.IndexOf(Conservar, t.name) < 0) continue;
            Vector3 escalaMundo = t.lossyScale;
            t.SetParent(cabeza, true);
            t.position = cabeza.position + Vector3.up * 0.1f * transform.lossyScale.y;
        }
    }

    static Bounds Medir(List<Renderer> rs, bool filtrar = true)
    {
        // descarta piezas descomunales (suelos o sombras que traen algunos modelos)
        var tam = new List<float>();
        foreach (var r in rs) tam.Add(r.bounds.size.magnitude);
        tam.Sort();
        float mediana = tam.Count > 0 ? tam[tam.Count / 2] : 1f;
        Bounds b = new Bounds(); bool primero = true;
        foreach (var r in rs)
        {
            if (!r.gameObject.activeInHierarchy) continue;
            if (filtrar && r.bounds.size.magnitude > mediana * 25f) continue;
            if (primero) { b = r.bounds; primero = false; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    void Animar()
    {
        var animator = modelo.GetComponentInChildren<Animator>();
        if (animator == null) animator = modelo.AddComponent<Animator>();
        animator.applyRootMotion = false;
        var todos = Resources.LoadAll<AnimationClip>(def.modelo + "/" + def.modelo);
        if (todos.Length == 0) return;
        grafo = PlayableGraph.Create("ModeloVivo_" + clave);
        grafo.SetTimeUpdateMode(DirectorUpdateMode.Manual);   // lo avanza Update (ver Evaluar)
        mezcla = AnimationMixerPlayable.Create(grafo, 0);
        iReposo = Añadir(todos, def.reposo, true);
        iPosado = Añadir(todos, def.posado, true);
        iCaminar = Añadir(todos, def.caminar, true);
        iCorrer = Añadir(todos, def.correr, true);
        iAtaque = Añadir(todos, def.ataque, false);
        iMuerte = Añadir(todos, def.muerte, false);
        if (iReposo < 0) iReposo = Añadir(todos, new[] { todos[0].name }, true);
        var salida = AnimationPlayableOutput.Create(grafo, "Salida", animator);
        salida.SetSourcePlayable(mezcla);
        grafo.Play();
    }

    int Añadir(AnimationClip[] todos, string[] nombres, bool bucle)
    {
        if (nombres == null) return -1;
        foreach (var n in nombres)
            foreach (var c in todos)
                if (c.name.Contains(n))
                {
                    var p = AnimationClipPlayable.Create(grafo, c);
                    p.SetApplyFootIK(false);
                    if (!bucle) p.SetDuration(c.length);
                    int i = mezcla.GetInputCount();
                    mezcla.SetInputCount(i + 1);
                    grafo.Connect(p, 0, mezcla, i);
                    mezcla.SetInputWeight(i, 0f);
                    // cada bicho empieza en un punto distinto del ciclo (que no vayan todos sincronizados)
                    p.SetTime(Random.value * c.length);
                    clips.Add(p);
                    return i;
                }
        return -1;
    }

    // ------------------------------------------------------------------ ondulado (modelos sin huesos)
    Mesh ondas;
    Vector3[] ondas0, ondasTrabajo;
    int ejeLargo, ejeLado;
    float ondasMin, ondasLargo, ondasFase;

    /// <summary>Copia la malla para deformarla: el cuerpo ondula a lo largo de su eje mas largo.</summary>
    void PrepararOndas()
    {
        var mf = modelo.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return;
        ondas = Instantiate(mf.sharedMesh);
        ondas.MarkDynamic();
        mf.sharedMesh = ondas;
        ondas0 = ondas.vertices;
        ondasTrabajo = new Vector3[ondas0.Length];
        var b = ondas.bounds;
        ejeLargo = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : (b.size.z >= b.size.y ? 2 : 1);
        // el lado: el otro eje que este en horizontal en el mundo (el que menos mire hacia arriba)
        int a1 = (ejeLargo + 1) % 3, a2 = (ejeLargo + 2) % 3;
        Vector3 v1 = Vector3.zero, v2 = Vector3.zero; v1[a1] = 1f; v2[a2] = 1f;
        ejeLado = Mathf.Abs(Vector3.Dot(mf.transform.TransformDirection(v1).normalized, Vector3.up)) < Mathf.Abs(Vector3.Dot(mf.transform.TransformDirection(v2).normalized, Vector3.up)) ? a1 : a2;
        ondasMin = b.min[ejeLargo]; ondasLargo = Mathf.Max(b.size[ejeLargo], 0.0001f);
        ondasFase = Random.value * 10f;
    }

    void Ondular()
    {
        if (muerto) return;                     // quieta al morir (el propio juego la tumba)
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float vel = new Vector3(transform.position.x - posAnterior.x, 0f, transform.position.z - posAnterior.z).magnitude / dt;
        velSuave = Mathf.Lerp(velSuave, vel, 1f - Mathf.Exp(-dt * 8f));
        // mas rapido cuando se arrastra, un vaiven lento cuando esta quieta
        ondasFase += dt * (1.6f + Mathf.Clamp(velSuave, 0f, 4f) * 2.2f);
        float amp = ondasLargo * 0.06f;
        for (int i = 0; i < ondas0.Length; i++)
        {
            var p = ondas0[i];
            float u = (p[ejeLargo] - ondasMin) / ondasLargo;           // 0 cola .. 1 cabeza
            float env = 0.35f + 0.65f * Mathf.Sin(u * Mathf.PI);      // las puntas se mueven menos que el medio
            p[ejeLado] += Mathf.Sin(u * Mathf.PI * 3f - ondasFase) * amp * env;
            // y se estira y encoge un poco, como al reptar
            p[ejeLargo] += Mathf.Sin(u * Mathf.PI * 2f - ondasFase * 0.8f) * ondasLargo * 0.015f;
            ondasTrabajo[i] = p;
        }
        ondas.vertices = ondasTrabajo;
        ondas.RecalculateNormals();
    }

    /// <summary>
    /// Una sola vez, con la animacion ya puesta: el punto mas bajo de la malla deformada se apoya en el suelo que tiene
    /// debajo. El modelo se coloca con su pose de reposo y al animarse las patas se recogen: quedaba flotando 20-55 cm.
    /// </summary>
    /// <summary>Vuelve a apoyar el modelo en el suelo (tras recolocar el bicho a mano, p. ej. en las fotos).</summary>
    public void ReApoyar() { apoyado = false; evaluaciones = 0; }

    void Apoyar()
    {
        apoyado = true;
        if (def.volador || def.serpentear || muerto || principal == null) return;
        float minY = float.MaxValue, maxY = float.MinValue; Vector3 bajo = Vector3.zero;
        foreach (var r in modelo.GetComponentsInChildren<Renderer>())
        {
            Vector3[] vs;
            var smr = r as SkinnedMeshRenderer;
            if (smr != null) { if (horneado == null) horneado = new Mesh(); smr.BakeMesh(horneado, true); vs = horneado.vertices; }
            else { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue; vs = mf.sharedMesh.vertices; }
            var tr = r.transform;
            for (int i = 0; i < vs.Length; i += 3)
            {
                var w = tr.TransformPoint(vs[i]);
                if (w.y < minY) { minY = w.y; bajo = w; }
                if (w.y > maxY) maxY = w.y;
            }
        }
        if (minY == float.MaxValue) return;
        // el suelo justo debajo de ese punto (sin contar bichos, jugador ni disparadores)
        float suelo = float.NaN;
        foreach (var h in Physics.RaycastAll(new Vector3(bajo.x, maxY + 0.5f, bajo.z), Vector3.down, (maxY - minY) + 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<RatHealth>() != null || h.collider.GetComponentInParent<PlayerController>() != null) continue;
            if (h.point.y <= maxY && (float.IsNaN(suelo) || h.point.y > suelo)) suelo = h.point.y;
        }
        if (float.IsNaN(suelo)) return;
        float hueco = minY - suelo;
        // solo correcciones razonables (si el bicho esta saltando o trepando no se toca)
        if (Mathf.Abs(hueco) < 0.04f || Mathf.Abs(hueco) > Mathf.Max(1.2f, (maxY - minY) * 0.6f)) return;
        modelo.transform.position -= Vector3.up * hueco;
    }

    /// <summary>Para las IA: lanza el ataque del modelo que lleve el bicho (en el o en sus hijos), si lo hay.</summary>
    public static void AtacarEn(Component bicho)
    {
        if (bicho == null) return;
        var mv = bicho.GetComponentInChildren<ModeloVivo>();
        if (mv != null) mv.Atacar();
    }

    /// <summary>Lanza la animacion de ataque (si el modelo la tiene).</summary>
    public void Atacar()
    {
        if (iAtaque < 0 || muerto) return;
        tAtaque = 0f;
        clips[iAtaque].SetTime(0);
    }

    void AlMorir()
    {
        muerto = true;
        tMuerte = 0f;
        if (iMuerte >= 0) clips[iMuerte].SetTime(0);
    }

    void Update()
    {
        if (modelo == null) return;
        // lejos de la camara: ni se dibuja ni se anima
        var cam = Camera.main;
        bool cerca = SiempreVisible || cam == null || (cam.transform.position - transform.position).sqrMagnitude < distanciaVisible * distanciaVisible;
        if (modelo.activeSelf != cerca)
        {
            modelo.SetActive(cerca);
            if (grafo.IsValid()) { if (cerca) grafo.Play(); else grafo.Stop(); }
        }
        if (!cerca) { posAnterior = transform.position; return; }
        if (ondas != null) { Ondular(); posAnterior = transform.position; return; }
        if (!grafo.IsValid()) { posAnterior = transform.position; return; }
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        // sin animacion de muerte: se cae de lado, patas arriba
        if (muerto && iMuerte < 0 && tMuerte >= 0f && rodarAlMorir)
        {
            tMuerte += dt;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tMuerte / 0.45f));
            modelo.transform.localRotation = Quaternion.Euler(0f, def.giroY, 0f) * Quaternion.Euler(def.giroX, 0f, 0f) * Quaternion.Euler(0f, 0f, 160f * k);
            if (grafo.IsPlaying()) grafo.Stop();
            return;
        }
        Vector3 d = transform.position - posAnterior; posAnterior = transform.position;
        Vector3 dPlano = new Vector3(d.x, 0f, d.z);
        float vel = dPlano.magnitude / dt;
        if (paseoForzado >= 0f) { vel = paseoForzado * def.velCorrer; velSuave = vel; }
        velSuave = Mathf.Lerp(velSuave, vel, 1f - Mathf.Exp(-dt * 8f));

        // el modelo mira hacia donde avanza el bicho (algunas IA giran la raiz al reves)
        if (vel > 0.4f)
        {
            float z = Vector3.Dot(dPlano.normalized, transform.forward);
            if (Mathf.Abs(z) > 0.6f) calibrado += (z > 0f ? 1f : -1f) * dt;
            if (calibrado > 0.5f) signo = 1f; else if (calibrado < -0.5f) signo = -1f;
            calibrado = Mathf.Clamp(calibrado, -1f, 1f);
        }
        giro.localRotation = Quaternion.Slerp(giro.localRotation, Quaternion.Euler(0f, signo > 0f ? 0f : 180f, 0f), 1f - Mathf.Exp(-dt * 10f));

        // pesos: muerte > ataque > correr/caminar/reposo
        int n = mezcla.GetInputCount();
        if (pesos == null || pesos.Length != n) pesos = new float[n];
        var w = pesos; System.Array.Clear(w, 0, n);
        if (muerto && iMuerte >= 0) w[iMuerte] = 1f;
        else if (posado && iPosado >= 0) w[iPosado] = 1f;
        else if (tAtaque >= 0f && iAtaque >= 0)
        {
            tAtaque += dt;
            float largo = (float)clips[iAtaque].GetAnimationClip().length;
            w[iAtaque] = 1f;
            if (tAtaque > largo) tAtaque = -1f;
        }
        else
        {
            float kCam = iCaminar >= 0 ? Mathf.Clamp01(velSuave / Mathf.Max(0.1f, def.velCaminar * 0.4f)) : 0f;
            float kCor = iCorrer >= 0 && iCorrer != iCaminar ? Mathf.Clamp01((velSuave - def.velCaminar) / Mathf.Max(0.1f, def.velCorrer - def.velCaminar)) : 0f;
            if (iReposo >= 0) w[iReposo] += 1f - kCam;
            if (iCaminar >= 0) w[iCaminar] += kCam * (1f - kCor);
            if (iCorrer >= 0) w[iCorrer] += kCam * kCor;
            // la velocidad del ciclo sigue a la del bicho (sin patinar)
            if (iCaminar >= 0 && iCaminar != iReposo) clips[iCaminar].SetSpeed(Mathf.Clamp(velSuave / def.velCaminar, 0.4f, 2.5f));
            if (iCorrer >= 0 && iCorrer != iCaminar) clips[iCorrer].SetSpeed(Mathf.Clamp(velSuave / def.velCorrer, 0.6f, 2f));
        }
        for (int i = 0; i < n; i++) mezcla.SetInputWeight(i, w[i]);
        // la muerte se queda en su ultimo cuadro
        if (muerto && iMuerte >= 0)
        {
            var c = clips[iMuerte];
            if (c.GetTime() >= c.GetAnimationClip().length) c.SetSpeed(0);
        }
        Evaluar(cam);
    }

    /// <summary>Avanza la animacion: cada cuadro de cerca, a 24 por segundo a media distancia y a 12 de lejos;
    /// si no se ve en ninguna camara solo acumula el tiempo (sigue su ciclo sin gastar en huesos).</summary>
    void Evaluar(Camera cam)
    {
        acumEval += Time.deltaTime;
        float d2 = cam != null ? (cam.transform.position - transform.position).sqrMagnitude : 0f;
        float paso = SiempreVisible || d2 < 22f * 22f ? 0f : d2 < 45f * 45f ? 1f / 24f : 1f / 12f;
        bool seVe = SiempreVisible || principal == null || principal.isVisible;
        if (!seVe) { if (acumEval > 2f) acumEval = 2f; return; }
        if (acumEval < paso) return;
        grafo.Evaluate(acumEval);
        if (!apoyado && ++evaluaciones >= 2) Apoyar();
        acumEval = 0f;
    }

    void OnDestroy()
    {
        if (grafo.IsValid()) grafo.Destroy();
        if (ondas != null) Destroy(ondas);
        var vida = GetComponentInParent<RatHealth>();
        if (vida != null) vida.OnDeath -= AlMorir;
    }
}
