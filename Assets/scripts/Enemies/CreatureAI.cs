using System.Collections;
using UnityEngine;

/// <summary>
/// IA de los enemigos de "Beyond Small" (oruga, lombriz, hormigas, ciempies, escarabajo, abeja, cochinilla, mariposa).
/// Reutiliza RatHealth como vida para que el ataque del jugador (PlayerController) los detecte igual que a la rata.
/// Cada tipo tiene su mecanica propia:
///   Caterpillar : escupe acido a distancia y mantiene la distancia (lenta, muy dañina).
///   Worm        : vive enterrada bajo un monticulo, emerge al acercarte y se entierra para esquivar golpes.
///   Ants        : enjambre de 3 hormigas; las feromonas alertan a otros enjambres; mueren de una en una.
///   Centipede   : veloz, avanza en zigzag y su mordida envenena.
///   Beetle      : embestida con aviso; si falla se aturde y recibe mas daño.
///   Bee         : vuela, orbita al jugador, pica en picada y se retira.
///   Woodlouse   : se enrolla en bola (casi inmune) y rueda contra el jugador.
///   Butterfly   : inofensiva, revolotea y huye del jugador.
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class CreatureAI : MonoBehaviour
{
    public enum Kind { Caterpillar, Worm, Ants, Centipede, Beetle, Bee, Woodlouse, Butterfly }
    enum S { Idle, Chase, Windup, Action, Recover, Return, Burrowed, Stunned, Flee, Dead }

    [Header("Tipo")]
    public Kind kind;
    public CreatureVisual[] visuals;

    [Header("Movimiento")]
    public float chaseSpeed = 3f;
    public float returnSpeed = 3f;
    public float rotationSpeed = 9f;

    [Header("Deteccion")]
    public float detectionDistance = 10f;
    public float loseDistance = 18f;

    [Header("Ataque")]
    public float attackRange = 2f;
    public float attackDamage = 10f;
    public float attackInterval = 1f;

    [Header("Golpe recibido")]
    public float knockbackDistance = 1f;
    public float knockbackDuration = 0.2f;
    public float hitRecoveryTime = 0.5f;

    [Header("Vuelo")]
    public float flyHeight = 1.7f;

    [Header("Barra de vida")]
    public float barHeight = 2f;
    public float barDistance = 16f;

    [Header("Efectos")]
    public GameObject deathParticlesPrefab;

    // limites del salon para que las embestidas no atraviesen las paredes
    static readonly Vector2 roomMin = new Vector2(-69f, -92f), roomMax = new Vector2(69f, 80f);

    RatHealth health;
    Transform player;
    PlayerHealth playerHealth;
    Animator animator;
    S state = S.Idle;
    Vector3 home;
    Quaternion homeRot;
    float groundY;
    float timer, attackCd, stateTime;
    Vector3 knockVel;
    Vector3 lockedDir;
    bool hitDone, melee;
    float wanderTimer;
    Vector3 wanderTarget;
    float orbitAngle, orbitDir = 1f;
    float seed;
    bool dying;

    // gusano
    GameObject mound;
    Vector3 burrowTarget;
    // hormigas
    Vector3[] antOffsets;
    int antsAlive = 3;
    // cochinilla
    float curlTimer;
    // oruga
    bool spitDone;

    public bool IsDead { get { return state == S.Dead; } }
    /// <summary>Lombriz enterrada: no se muestra su barra de vida.</summary>
    public bool IsHidden { get { return kind == Kind.Worm && (state == S.Burrowed || (Main != null && Main.sink < -0.3f)); } }

    CreatureVisual Main { get { return visuals != null && visuals.Length > 0 ? visuals[0] : null; } }

    void Awake()
    {
        // Se mueven por transform: un collider sólido empuja al CharacterController del jugador a través del piso
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.isTrigger = true;
        health = GetComponent<RatHealth>();
        animator = GetComponentInChildren<Animator>();
        home = transform.position; homeRot = transform.rotation; groundY = transform.position.y;
        seed = Random.value * 100f;
        if (visuals == null || visuals.Length == 0) visuals = GetComponentsInChildren<CreatureVisual>();
        health.DamageFilter = FilterDamage;
    }

    void OnEnable() { if (health != null) health.OnDeath += Die; }
    void OnDisable() { if (health != null) health.OnDeath -= Die; }

    void Start()
    {
        FindPlayer();
        attackCd = Random.Range(0.5f, 2f);
        if (kind == Kind.Ants) SetupAnts();
        if (kind == Kind.Worm) SetupWorm();
        if (kind == Kind.Bee) { var p = transform.position; p.y = groundY + flyHeight; transform.position = p; }
        if (kind == Kind.Butterfly) { wanderTarget = home + Vector3.up * flyHeight; var p = transform.position; p.y = groundY + flyHeight; transform.position = p; }
        SetAnim("Idle");
    }

    void FindPlayer()
    {
        if (player == null)
        {
            var pc = Object.FindAnyObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;
        }
        if (playerHealth == null && player != null) playerHealth = player.GetComponent<PlayerHealth>();
    }

    // ------------------------------------------------------------------ filtro de daño (mecanicas defensivas)
    float FilterDamage(float amount)
    {
        if (kind == Kind.Worm && (state == S.Burrowed || (Main != null && Main.sink < -0.3f))) return 0f;
        if (kind == Kind.Woodlouse && Main != null && Main.curl > 0.5f) return amount * 0.25f;
        if (kind == Kind.Beetle && state == S.Stunned) return amount * 1.5f;
        return amount;
    }

    // ------------------------------------------------------------------ preparacion por tipo
    void SetupAnts()
    {
        antOffsets = new Vector3[visuals.Length];
        for (int i = 0; i < visuals.Length; i++)
        {
            float a = i * 120f * Mathf.Deg2Rad + 0.4f;
            antOffsets[i] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 1.1f;
            visuals[i].offset = antOffsets[i];
        }
        antsAlive = visuals.Length;
    }

    void SetupWorm()
    {
        mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        mound.name = "WormMound";
        Destroy(mound.GetComponent<Collider>());
        mound.transform.SetParent(transform, false);
        mound.transform.localScale = new Vector3(2.4f, 0.55f, 2.4f);
        mound.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh != null ? sh : Shader.Find("Sprites/Default"));
        m.color = new Color(0.25f, 0.17f, 0.1f);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", m.color);
        mound.GetComponent<Renderer>().sharedMaterial = m;
        state = S.Burrowed;
        SetSink(-1.6f);
    }

    void SetSink(float s) { foreach (var v in visuals) v.sink = s; if (mound != null) mound.SetActive(s < -0.15f); }

    void SetAnim(string name)
    {
        foreach (var v in visuals) v.Play(name);
    }

    // ------------------------------------------------------------------ utilidades de movimiento
    Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    void Face(Vector3 dir, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), rotationSpeed * dt);
    }

    void Step(Vector3 dir, float speed, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Vector3 p = transform.position + dir.normalized * speed * dt;
        p.x = Mathf.Clamp(p.x, roomMin.x, roomMax.x);
        p.z = Mathf.Clamp(p.z, roomMin.y, roomMax.y);
        p.y = (kind == Kind.Bee || kind == Kind.Butterfly) ? p.y : groundY;
        transform.position = p;
    }

    void SetMove(float v) { foreach (var c in visuals) c.move = v; }

    void SetState(S s) { state = s; stateTime = 0f; hitDone = false; }

    // ------------------------------------------------------------------ bucle principal
    void Update()
    {
        if (state == S.Dead) return;
        if (health.IsDead) { Die(); return; }
        FindPlayer();
        if (player == null) return;

        float dt = Time.deltaTime;
        stateTime += dt;
        attackCd -= dt;
        Vector3 toPlayer = Flat(player.position - transform.position);
        float dist = toPlayer.magnitude;

        switch (kind)
        {
            case Kind.Caterpillar: UpdateCaterpillar(dt, toPlayer, dist); break;
            case Kind.Worm: UpdateWorm(dt, toPlayer, dist); break;
            case Kind.Ants: UpdateAnts(dt, toPlayer, dist); break;
            case Kind.Centipede: UpdateCentipede(dt, toPlayer, dist); break;
            case Kind.Beetle: UpdateBeetle(dt, toPlayer, dist); break;
            case Kind.Bee: UpdateBee(dt, toPlayer, dist); break;
            case Kind.Woodlouse: UpdateWoodlouse(dt, toPlayer, dist); break;
            case Kind.Butterfly: UpdateButterfly(dt, toPlayer, dist); break;
        }
    }

    // comun: reposo con paseo corto + volver a casa
    bool IdleOrReturn(float dt, float dist, float wanderSpeed, S chaseState = S.Chase)
    {
        if (state == S.Idle)
        {
            if (dist <= detectionDistance) { OnDetect(); SetState(chaseState); return false; }
            wanderTimer -= dt;
            if (wanderTimer <= 0f)
            {
                wanderTimer = Random.Range(2.5f, 5f);
                wanderTarget = home + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
            }
            Vector3 d = Flat(wanderTarget - transform.position);
            if (d.magnitude > 0.4f) { Face(d, dt); Step(d, wanderSpeed, dt); SetMove(0.3f); }
            else SetMove(0f);
            return true;
        }
        if (state == S.Return)
        {
            if (dist <= detectionDistance) { OnDetect(); SetState(chaseState); return false; }
            Vector3 d = Flat(home - transform.position);
            if (d.magnitude <= 0.4f) { SetState(S.Idle); SetMove(0f); return true; }
            Face(d, dt); Step(d, returnSpeed, dt); SetMove(0.6f);
            return true;
        }
        return false;
    }

    void OnDetect()
    {
        if (kind == Kind.Ants) AlertNearbyAnts();
    }

    // ------------------------------------------------------------------ ORUGA: escupe acido
    void UpdateCaterpillar(float dt, Vector3 toPlayer, float dist)
    {
        var cv = Main;
        switch (state)
        {
            case S.Idle:
            case S.Return:
                IdleOrReturn(dt, dist, chaseSpeed * 0.3f);
                break;

            case S.Chase:
                if (dist > loseDistance) { SetState(S.Return); break; }
                Face(toPlayer, dt);
                if (dist < 4f) { Step(-toPlayer, chaseSpeed * 0.7f, dt); SetMove(0.6f); }      // mantiene la distancia
                else if (dist > 9.5f) { Step(toPlayer, chaseSpeed, dt); SetMove(1f); }
                else SetMove(0.1f);
                if (attackCd <= 0f && dist <= 11f) { SetState(S.Windup); spitDone = false; SetMove(0f); }
                break;

            case S.Windup:
                Face(toPlayer, dt);
                cv.pitch = Mathf.Lerp(0f, 38f, stateTime / 0.9f);
                cv.shake = stateTime / 0.9f;
                if (stateTime >= 0.9f)
                {
                    cv.Attack(0.4f); cv.shake = 0f;
                    Vector3 mouth = transform.position + transform.forward * 1.8f + Vector3.up * 1.2f;
                    EnemyProjectile.Spawn(mouth, player.position + Vector3.up * 1f, 15f, attackDamage, new Color(0.45f, 0.95f, 0.1f), 1f, 0f, 0.7f);
                    attackCd = attackInterval;
                    SetState(S.Recover);
                }
                break;

            case S.Recover:
                cv.pitch = Mathf.MoveTowards(cv.pitch, 0f, 80f * dt);
                knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, 12f * dt);
                if (knockVel.sqrMagnitude > 0.01f) Step(knockVel, knockVel.magnitude, dt);
                if (stateTime >= Mathf.Max(0.5f, hitRecoveryTime)) SetState(S.Chase);
                break;
        }
    }

    // ------------------------------------------------------------------ LOMBRIZ: emerge y se entierra
    void UpdateWorm(float dt, Vector3 toPlayer, float dist)
    {
        var cv = Main;
        switch (state)
        {
            case S.Burrowed:
                // moviendose bajo tierra hacia el jugador, o esperando emboscada
                if (burrowTarget != Vector3.zero)
                {
                    Vector3 d = Flat(burrowTarget - transform.position);
                    if (d.magnitude > 0.6f && stateTime < 1.6f) { Step(d, 7.5f, dt); SetSink(Mathf.MoveTowards(cv.sink, -1.6f, 8f * dt)); break; }
                    burrowTarget = Vector3.zero; SetState(S.Action); break;   // emerge
                }
                SetSink(-1.6f);
                if (dist <= detectionDistance * 0.65f) SetState(S.Action);
                break;

            case S.Action: // emergiendo
                Face(toPlayer, dt);
                SetSink(Mathf.MoveTowards(cv.sink, 0f, 5f * dt));
                if (cv.sink >= -0.01f) { SetSink(0f); SetState(S.Chase); }
                break;

            case S.Chase:
                if (dist > loseDistance) { burrowTarget = Vector3.zero; SetState(S.Return); break; }
                Face(toPlayer, dt);
                if (dist > attackRange) { Step(toPlayer, chaseSpeed, dt); SetMove(1f); }
                else
                {
                    SetMove(0f);
                    if (attackCd <= 0f) { attackCd = attackInterval; cv.Attack(0.25f); if (playerHealth != null) playerHealth.TakeDamage(attackDamage); }
                }
                break;

            case S.Return:
                if (dist <= detectionDistance * 0.65f) { SetState(S.Chase); break; }
                Vector3 h = Flat(home - transform.position);
                if (h.magnitude <= 0.5f) { SetState(S.Burrowed); break; }
                Step(h, returnSpeed, dt); SetMove(0.6f);
                break;

            case S.Recover: // golpeada sin enterrarse
                knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, 14f * dt);
                if (knockVel.sqrMagnitude > 0.01f) Step(knockVel, knockVel.magnitude, dt);
                if (stateTime >= hitRecoveryTime) SetState(S.Chase);
                break;
        }
    }

    // ------------------------------------------------------------------ HORMIGAS: enjambre con feromonas
    void UpdateAnts(float dt, Vector3 toPlayer, float dist)
    {
        // las hormigas se mueven con ligera variacion para parecer un enjambre
        for (int i = 0; i < visuals.Length; i++)
        {
            if (!visuals[i].gameObject.activeSelf) continue;
            float n1 = Mathf.PerlinNoise(seed + i * 7.1f, Time.time * 0.9f) - 0.5f;
            float n2 = Mathf.PerlinNoise(seed + i * 3.3f + 50f, Time.time * 0.9f) - 0.5f;
            visuals[i].offset = antOffsets[i] + new Vector3(n1, 0f, n2) * 0.9f;
        }

        // al perder vida mueren hormigas del enjambre
        int target = health.HealthPercent > 0.66f ? 3 : (health.HealthPercent > 0.33f ? 2 : 1);
        target = Mathf.Min(target, visuals.Length);
        while (antsAlive > target) { antsAlive--; StartCoroutine(KillAnt(visuals[antsAlive])); }

        switch (state)
        {
            case S.Idle:
            case S.Return:
                IdleOrReturn(dt, dist, chaseSpeed * 0.3f);
                break;
            case S.Chase:
                if (dist > loseDistance) { SetState(S.Return); break; }
                Face(toPlayer, dt);
                if (dist > attackRange) { Step(toPlayer, chaseSpeed, dt); SetMove(1f); }
                else
                {
                    SetMove(0.2f);
                    if (attackCd <= 0f)
                    {
                        attackCd = attackInterval;
                        for (int i = 0; i < antsAlive; i++) visuals[i].Attack(0.3f);
                        if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
                    }
                }
                break;
            case S.Recover:
                knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, 14f * dt);
                if (knockVel.sqrMagnitude > 0.01f) Step(knockVel, knockVel.magnitude, dt);
                Face(toPlayer, dt);
                if (stateTime >= hitRecoveryTime) SetState(S.Chase);
                break;
        }
    }

    IEnumerator KillAnt(CreatureVisual ant)
    {
        ant.Die();
        yield return new WaitForSeconds(0.9f);
        if (ant != null) ant.gameObject.SetActive(false);
    }

    void AlertNearbyAnts()
    {
        foreach (var other in Object.FindObjectsByType<CreatureAI>())
        {
            if (other == this || other.kind != Kind.Ants || other.state == S.Dead) continue;
            if ((other.transform.position - transform.position).sqrMagnitude > 30f * 30f) continue;
            if (other.state == S.Idle || other.state == S.Return) { other.SetState(S.Chase); other.SetMove(1f); }
        }
    }

    // ------------------------------------------------------------------ CIENPIES: zigzag + veneno
    void UpdateCentipede(float dt, Vector3 toPlayer, float dist)
    {
        var cv = Main;
        switch (state)
        {
            case S.Idle:
            case S.Return:
                IdleOrReturn(dt, dist, chaseSpeed * 0.25f);
                break;
            case S.Chase:
                if (dist > loseDistance) { SetState(S.Return); break; }
                float weave = Mathf.Sin(Time.time * 5f + seed) * Mathf.Clamp01((dist - 2.5f) / 6f) * 35f;
                Vector3 dir = Quaternion.Euler(0f, weave, 0f) * toPlayer;
                Face(dir, dt);
                if (dist > attackRange) { Step(transform.forward, chaseSpeed, dt); SetMove(1f); }
                else
                {
                    SetMove(0.15f);
                    if (attackCd <= 0f)
                    {
                        attackCd = attackInterval;
                        cv.Attack(0.3f);
                        if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
                        StatusEffects.Poison(4f, 4f);
                    }
                }
                break;
            case S.Recover:
                knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, 14f * dt);
                if (knockVel.sqrMagnitude > 0.01f) Step(knockVel, knockVel.magnitude, dt);
                Face(toPlayer, dt);
                if (stateTime >= hitRecoveryTime) SetState(S.Chase);
                break;
        }
    }

    // ------------------------------------------------------------------ ESCARABAJO: embestida
    void UpdateBeetle(float dt, Vector3 toPlayer, float dist)
    {
        var cv = Main;
        switch (state)
        {
            case S.Idle:
            case S.Return:
                IdleOrReturn(dt, dist, chaseSpeed * 0.3f);
                break;

            case S.Chase:
                cv.pitch = Mathf.MoveTowards(cv.pitch, 0f, 60f * dt);
                if (dist > loseDistance) { SetState(S.Return); break; }
                Face(toPlayer, dt);
                if (dist > 2.6f) { Step(toPlayer, chaseSpeed, dt); SetMove(1f); } else SetMove(0.1f);
                if (attackCd <= 0f && dist >= 4.5f && dist <= 12f) { melee = false; SetState(S.Windup); SetMove(0f); }   // embestida
                else if (attackCd <= 0f && dist < 2.8f) { melee = true; SetState(S.Windup); SetMove(0f); }          // cornada
                break;

            case S.Windup:
                Face(toPlayer, dt);
                float wu = melee ? 0.45f : 1.0f;
                cv.pitch = -Mathf.Lerp(0f, 22f, stateTime / wu);      // agacha la cabeza
                cv.shake = stateTime / wu;
                if (stateTime >= wu)
                {
                    cv.shake = 0f;
                    if (melee)
                    {   // cornada cuerpo a cuerpo
                        cv.Attack(0.35f); cv.pitch = 18f;
                        if (dist < 3.4f && playerHealth != null) playerHealth.TakeDamage(attackDamage);
                        attackCd = attackInterval; SetState(S.Recover);
                    }
                    else { lockedDir = toPlayer.normalized; SetState(S.Action); }
                }
                break;

            case S.Action: // embestida en linea recta
                cv.pitch = -10f;
                Step(lockedDir, 17f, dt); SetMove(1f);
                transform.rotation = Quaternion.LookRotation(lockedDir);
                if (!hitDone && dist <= 2.1f)
                {
                    hitDone = true;
                    if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
                    attackCd = attackInterval; cv.Attack(0.3f); cv.pitch = 20f;
                    SetState(S.Recover); hitDone = true;
                    break;
                }
                Vector3 p = transform.position;
                bool wall = p.x <= roomMin.x + 0.1f || p.x >= roomMax.x - 0.1f || p.z <= roomMin.y + 0.1f || p.z >= roomMax.y - 0.1f;
                if (stateTime >= 0.9f || wall) { attackCd = attackInterval; SetState(S.Stunned); }   // fallo: se aturde
                break;

            case S.Stunned:
                SetMove(0f);
                cv.pitch = -14f; cv.shake = 0.5f;
                if (stateTime >= 1.9f) { cv.shake = 0f; cv.pitch = 0f; SetState(S.Chase); }
                break;

            case S.Recover:
                cv.pitch = Mathf.MoveTowards(cv.pitch, 0f, 50f * dt);
                knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, 12f * dt);
                if (knockVel.sqrMagnitude > 0.01f) Step(knockVel, knockVel.magnitude, dt);
                if (stateTime >= Mathf.Max(0.7f, hitRecoveryTime)) SetState(S.Chase);
                break;
        }
    }

    // ------------------------------------------------------------------ ABEJA: orbita, pica y se retira
    void UpdateBee(float dt, Vector3 toPlayer, float dist)
    {
        var cv = Main;
        float targetY = groundY + flyHeight + Mathf.Sin(Time.time * 3f + seed) * 0.25f;
        var pos = transform.position;
        pos.y = Mathf.MoveTowards(pos.y, targetY, 3f * dt);
        transform.position = pos;

        switch (state)
        {
            case S.Idle:
                SetAnim("Run");
                if (dist <= detectionDistance) { orbitAngle = Mathf.Atan2(-toPlayer.x, -toPlayer.z); orbitDir = Random.value < 0.5f ? 1f : -1f; SetState(S.Chase); break; }
                wanderTimer -= dt;
                if (wanderTimer <= 0f) { wanderTimer = Random.Range(1.5f, 3f); wanderTarget = home + new Vector3(Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f)); }
                Vector3 d = Flat(wanderTarget - transform.position);
                if (d.magnitude > 0.5f) { Face(d, dt); Step(d, 2f, dt); }
                break;

            case S.Chase: // zumba en circulos alrededor del jugador
                SetAnim("Run");
                if (dist > loseDistance) { SetState(S.Return); break; }
                orbitAngle += orbitDir * 1.6f * dt;
                Vector3 want = player.position + new Vector3(Mathf.Sin(orbitAngle), 0f, Mathf.Cos(orbitAngle)) * 4.5f;
                Vector3 to = Flat(want - transform.position);
                Step(to, chaseSpeed, dt);
                Face(toPlayer, dt);
                if (stateTime > 1.1f && attackCd <= 0f) SetState(S.Windup);
                break;

            case S.Windup: // se echa hacia atras un instante antes de la picada
                Face(toPlayer, dt);
                Step(-toPlayer, 2.5f, dt);
                cv.shake = 0.8f;
                if (stateTime >= 0.4f) { cv.shake = 0f; lockedDir = (player.position + Vector3.up * 0.9f - transform.position); SetState(S.Action); SetAnim("Attack"); }
                break;

            case S.Action: // picada en linea recta hacia el jugador
                Vector3 dir = player.position + Vector3.up * 0.9f - transform.position;
                float d3 = dir.magnitude;
                Face(Flat(dir), dt * 2f);
                var np = transform.position + dir.normalized * 15f * dt;
                np.x = Mathf.Clamp(np.x, roomMin.x, roomMax.x); np.z = Mathf.Clamp(np.z, roomMin.y, roomMax.y);
                transform.position = np;
                if (d3 <= 1.7f && !hitDone)
                {
                    hitDone = true;
                    if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
                    attackCd = attackInterval; SetState(S.Recover); hitDone = true; lockedDir = -toPlayer.normalized; SetAnim("Run");
                    break;
                }
                if (stateTime > 1.2f) { attackCd = 0.4f; SetState(S.Recover); SetAnim("Run"); lockedDir = -toPlayer.normalized; }
                break;

            case S.Recover: // se retira y vuelve a orbitar
                var rp = transform.position + (lockedDir.sqrMagnitude > 0.01f ? lockedDir : -toPlayer.normalized) * 7f * dt;
                rp.x = Mathf.Clamp(rp.x, roomMin.x, roomMax.x); rp.z = Mathf.Clamp(rp.z, roomMin.y, roomMax.y);
                rp.y = Mathf.MoveTowards(rp.y, groundY + flyHeight + 0.8f, 2f * dt);
                transform.position = rp;
                knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, 14f * dt);
                if (knockVel.sqrMagnitude > 0.01f) Step(knockVel, knockVel.magnitude, dt);
                if (stateTime >= Mathf.Max(0.9f, hitRecoveryTime)) { SetState(S.Chase); orbitAngle = Mathf.Atan2(-toPlayer.x, -toPlayer.z); }
                break;

            case S.Return:
                SetAnim("Run");
                Vector3 h = Flat(home - transform.position);
                if (dist <= detectionDistance) { SetState(S.Chase); break; }
                if (h.magnitude <= 0.8f) { SetState(S.Idle); break; }
                Face(h, dt); Step(h, returnSpeed, dt);
                break;
        }
    }

    // ------------------------------------------------------------------ COCHINILLA: bola rodante
    void UpdateWoodlouse(float dt, Vector3 toPlayer, float dist)
    {
        var cv = Main;
        switch (state)
        {
            case S.Idle:
            case S.Return:
                cv.curl = Mathf.MoveTowards(cv.curl, 0f, 3f * dt);
                IdleOrReturn(dt, dist, chaseSpeed * 0.3f);
                break;

            case S.Chase:
                cv.curl = Mathf.MoveTowards(cv.curl, 0f, 3f * dt);
                if (dist > loseDistance) { SetState(S.Return); break; }
                Face(toPlayer, dt);
                if (dist > 2.6f) { Step(toPlayer, chaseSpeed, dt); SetMove(1f); } else SetMove(0.1f);
                if (attackCd <= 0f && dist >= 3f && dist <= 11f) { SetState(S.Windup); SetMove(0f); }
                break;

            case S.Windup: // se enrolla
                Face(toPlayer, dt);
                cv.curl = Mathf.MoveTowards(cv.curl, 1f, 3f * dt);
                if (stateTime >= 0.5f) { lockedDir = toPlayer.normalized; SetState(S.Action); }
                break;

            case S.Action: // rueda hacia el jugador
                cv.curl = 1f;
                Vector3 want = Vector3.Slerp(lockedDir, toPlayer.normalized, 0.8f * dt);
                lockedDir = want.sqrMagnitude > 0.01f ? want.normalized : lockedDir;
                Step(lockedDir, 9f, dt);
                transform.rotation = Quaternion.LookRotation(lockedDir);
                cv.spin += 9f / 0.9f * Mathf.Rad2Deg * dt;
                if (!hitDone && dist <= 2.0f)
                {
                    hitDone = true;
                    if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
                }
                Vector3 p = transform.position;
                bool wall = p.x <= roomMin.x + 0.1f || p.x >= roomMax.x - 0.1f || p.z <= roomMin.y + 0.1f || p.z >= roomMax.y - 0.1f;
                if (stateTime >= 1.5f || wall) { attackCd = attackInterval; SetState(S.Recover); }
                break;

            case S.Recover: // se desenrolla y queda vulnerable
                cv.spin = Mathf.Repeat(cv.spin, 360f);
                if (cv.spin > 1f) cv.spin = Mathf.MoveTowards(cv.spin > 180f ? cv.spin - 360f : cv.spin, 0f, 360f * dt);
                cv.curl = Mathf.MoveTowards(cv.curl, 0f, 1.4f * dt);
                knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, 14f * dt);
                if (knockVel.sqrMagnitude > 0.01f) Step(knockVel, knockVel.magnitude, dt);
                if (stateTime >= 1.2f) { cv.spin = 0f; SetState(S.Chase); }
                break;

            case S.Stunned: // defensa: enrollada al ser golpeada
                curlTimer -= dt;
                cv.curl = Mathf.MoveTowards(cv.curl, 1f, 5f * dt);
                if (curlTimer <= 0f) SetState(S.Chase);
                break;
        }
    }

    // ------------------------------------------------------------------ MARIPOSA: huye
    void UpdateButterfly(float dt, Vector3 toPlayer, float dist)
    {
        var cv = Main;
        var pos = transform.position;
        float targetY = groundY + flyHeight + Mathf.Sin(Time.time * 2.3f + seed) * 0.5f;
        pos.y = Mathf.MoveTowards(pos.y, targetY, 2f * dt);
        transform.position = pos;
        SetMove(1f);

        bool scared = dist < 9f;
        if (scared)
        {
            // huye dando bandazos
            Vector3 away = -toPlayer.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, away) * Mathf.Sin(Time.time * 4f + seed) * 0.9f;
            Vector3 dir = (away + side).normalized;
            Face(dir, dt * 2f);
            Step(dir, 4.6f, dt);
            return;
        }
        // revolotea por su zona
        wanderTimer -= dt;
        Vector3 d = Flat(wanderTarget - transform.position);
        if (wanderTimer <= 0f || d.magnitude < 0.8f)
        {
            wanderTimer = Random.Range(1.5f, 3.5f);
            wanderTarget = home + new Vector3(Random.Range(-9f, 9f), 0f, Random.Range(-9f, 9f));
        }
        Face(d, dt * 2f);
        Step(d, 2.2f, dt);
    }

    // ------------------------------------------------------------------ golpe recibido
    /// <summary>Llamado por PlayerController cuando el jugador golpea a esta criatura.</summary>
    public void TakeHit(Vector3 knockbackDirection)
    {
        if (state == S.Dead || health.IsDead) return;
        knockbackDirection.y = 0f;
        if (knockbackDirection.sqrMagnitude < 0.001f) knockbackDirection = -transform.forward;
        knockbackDirection.Normalize();

        if (kind == Kind.Ants) AlertNearbyAnts();
        if (state == S.Idle || state == S.Return) { if (kind != Kind.Worm && kind != Kind.Butterfly) SetState(S.Chase); }

        switch (kind)
        {
            case Kind.Worm:
                if (state == S.Burrowed || (Main != null && Main.sink < -0.3f)) return;
                if (Random.value < 0.55f)
                {   // se entierra y reaparece junto al jugador
                    FindPlayer();
                    Vector2 c = Random.insideUnitCircle.normalized * 3.2f;
                    burrowTarget = player.position + new Vector3(c.x, 0f, c.y);
                    SetState(S.Burrowed);
                    return;
                }
                break;
            case Kind.Beetle:
                if (state == S.Windup || state == S.Action) return;     // en plena embestida no se detiene
                if (state == S.Stunned) return;
                break;
            case Kind.Woodlouse:
                if (state == S.Action) return;
                if (state != S.Stunned && state != S.Windup) { curlTimer = 2.4f; SetState(S.Stunned); SetMove(0f); return; }
                break;
            case Kind.Butterfly:
                return;
            case Kind.Bee:
                knockVel = knockbackDirection * (knockbackDistance / Mathf.Max(0.05f, knockbackDuration)) * 1.5f;
                SetState(S.Recover); lockedDir = knockbackDirection;
                return;
        }

        knockVel = knockbackDirection * (knockbackDistance / Mathf.Max(0.05f, knockbackDuration));
        SetState(S.Recover);
        foreach (var v in visuals) v.shake = 0f;
    }

    // ------------------------------------------------------------------ muerte
    void Die()
    {
        if (state == S.Dead) return;
        state = S.Dead;
        knockVel = Vector3.zero;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var v in visuals) { v.Die(); v.shake = 0f; }
        if (kind == Kind.Bee) SetAnim("Death");
        if (mound != null) Destroy(mound);
        StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        float t = 0f;
        bool flyer = kind == Kind.Bee || kind == Kind.Butterfly;
        float startY = transform.position.y;
        while (t < 1.3f)
        {
            t += Time.deltaTime;
            if (flyer)
            {   // cae al suelo
                var p = transform.position;
                p.y = Mathf.Max(groundY + 0.15f, p.y - 7f * Time.deltaTime);
                transform.position = p;
            }
            yield return null;
        }

        var rends = GetComponentsInChildren<Renderer>();
        Vector3 center = transform.position;
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            center = b.center;
        }
        if (deathParticlesPrefab != null) Instantiate(deathParticlesPrefab, center, Quaternion.identity);
        foreach (var r in rends) r.enabled = false;

        var drops = GetComponent<DropTable>();
        if (drops != null) drops.Drop(new Vector3(center.x, groundY, center.z));

        yield return new WaitForSeconds(0.15f);
        Destroy(gameObject);
    }
}
