using System.Collections;
using UnityEngine;

/// <summary>
/// IA de la araña enemiga. Reutiliza RatHealth como componente de vida para que el
/// ataque del jugador (PlayerController) la detecte igual que a la rata.
/// Estados: Idle, Run (persigue), Attack, HitRecovery, Return (vuelve a su origen), Dead.
/// El Animator solo necesita los estados "Idle", "Run", "Attack" y "Death".
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class SpiderAI : MonoBehaviour
{
    public enum SpiderState { Idle, Run, Attack, HitRecovery, Return, Dead }

    [Header("Referencias")]
    public Transform player;
    public Animator animator;
    public RatHealth health;
    public PlayerHealth playerHealth;

    [Header("Movimiento")]
    public float runSpeed = 5.5f;
    public float returnSpeed = 4f;
    public float rotationSpeed = 10f;
    [Tooltip("Si el modelo mira hacia -Z local, activa esto.")]
    public bool invertForward = false;

    [Header("Detección")]
    public float detectionDistance = 10f;
    public float loseDistance = 16f;

    [Header("Ataque")]
    public float attackRange = 1.6f;
    public float attackDamage = 20f;
    public float attackInterval = 1.1f;

    [Header("Golpe recibido")]
    public float knockbackDistance = 1.2f;
    public float knockbackDuration = 0.2f;
    public float hitRecoveryTime = 0.8f;

    [Header("Muerte")]
    public GameObject deathParticlesPrefab;
    public float fallbackDeathDuration = 1.0f;

    [Header("Barra de vida (sobre la araña)")]
    public float healthBarDistance = 14f;
    public float healthBarHeight = 1.4f;

    private SpiderState state = SpiderState.Idle;
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private float attackTimer;
    private float recoveryTimer;
    private Vector3 knockbackVelocity;
    private string currentAnim = "";
    private float groundY;

    public SpiderState CurrentState => state;

    private Vector3 ModelForward => invertForward ? -transform.forward : transform.forward;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (health == null) health = GetComponent<RatHealth>();
        homePosition = transform.position;
        homeRotation = transform.rotation;
        groundY = transform.position.y;
    }

    private void OnEnable()
    {
        if (health != null) health.OnDeath += Die;
    }

    private void OnDisable()
    {
        if (health != null) health.OnDeath -= Die;
    }

    private void Start()
    {
        FindPlayer();
        Play("Idle");
    }

    private void FindPlayer()
    {
        if (player == null)
        {
            var pc = Object.FindAnyObjectByType<PlayerController>();
            if (pc != null) player = pc.transform;
        }
        if (playerHealth == null)
        {
            if (player != null) playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth == null) playerHealth = Object.FindAnyObjectByType<PlayerHealth>();
        }
    }

    private void Play(string stateName)
    {
        if (animator == null || currentAnim == stateName) return;
        currentAnim = stateName;
        animator.CrossFadeInFixedTime(stateName, 0.12f);
    }

    private void Update()
    {
        if (state == SpiderState.Dead) return;
        if (health != null && health.IsDead) { Die(); return; }

        FindPlayer();
        if (player == null) return;

        float dt = Time.deltaTime;
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;

        switch (state)
        {
            case SpiderState.Idle:
                if (dist <= detectionDistance) SetState(SpiderState.Run);
                break;

            case SpiderState.Run:
                if (dist > loseDistance) { SetState(SpiderState.Return); break; }
                RotateTowards(toPlayer, dt);
                if (dist <= attackRange) { SetState(SpiderState.Attack); break; }
                Move(ModelForward * (runSpeed * dt));
                break;

            case SpiderState.Attack:
                RotateTowards(toPlayer, dt);
                if (dist > attackRange * 1.4f) { SetState(SpiderState.Run); break; }
                attackTimer -= dt;
                if (attackTimer <= 0f)
                {
                    attackTimer = attackInterval;
                    currentAnim = ""; // fuerza reinicio de la animación de ataque
                    Play("Attack");
                    if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
                }
                break;

            case SpiderState.HitRecovery:
                if (knockbackVelocity.sqrMagnitude > 0.01f)
                {
                    Move(knockbackVelocity * dt);
                    knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero,
                        (knockbackDistance / (knockbackDuration * knockbackDuration)) * dt);
                }
                RotateTowards(toPlayer, dt);
                recoveryTimer -= dt;
                if (recoveryTimer <= 0f)
                    SetState(dist <= attackRange ? SpiderState.Attack : SpiderState.Run);
                break;

            case SpiderState.Return:
                if (dist <= detectionDistance) { SetState(SpiderState.Run); break; }
                Vector3 toHome = homePosition - transform.position;
                toHome.y = 0f;
                if (toHome.magnitude <= 0.3f)
                {
                    transform.rotation = homeRotation;
                    SetState(SpiderState.Idle);
                    break;
                }
                RotateTowards(toHome, dt);
                Move(ModelForward * (returnSpeed * dt));
                break;
        }
    }

    private void Move(Vector3 delta)
    {
        Vector3 next = transform.position + delta;
        next.y = groundY;
        transform.position = next;
    }

    private void RotateTowards(Vector3 dir, float dt)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        Vector3 fwd = invertForward ? -dir.normalized : dir.normalized;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(fwd), rotationSpeed * dt);
    }

    private void SetState(SpiderState s)
    {
        state = s;
        switch (s)
        {
            case SpiderState.Idle: Play("Idle"); break;
            case SpiderState.Run:
            case SpiderState.Return: Play("Run"); break;
            case SpiderState.Attack: attackTimer = 0f; break;
            case SpiderState.HitRecovery: Play("Idle"); break;
        }
    }

    /// <summary>Llamado por PlayerController cuando el jugador golpea a la araña.</summary>
    public void TakeHit(Vector3 knockbackDirection)
    {
        if (state == SpiderState.Dead || (health != null && health.IsDead)) return;

        knockbackDirection.y = 0f;
        if (knockbackDirection.sqrMagnitude < 0.001f) knockbackDirection = -ModelForward;
        knockbackDirection.Normalize();

        knockbackVelocity = knockbackDirection * (knockbackDuration > 0f ? knockbackDistance / knockbackDuration : 0f);
        recoveryTimer = hitRecoveryTime;
        SetState(SpiderState.HitRecovery);
    }

    private void Die()
    {
        if (state == SpiderState.Dead) return;
        state = SpiderState.Dead;
        knockbackVelocity = Vector3.zero;

        currentAnim = "Death";
        if (animator != null) animator.CrossFadeInFixedTime("Death", 0.05f);

        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        StartCoroutine(DeathRoutine());
    }

    private IEnumerator DeathRoutine()
    {
        float wait = fallbackDeathDuration;
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name.IndexOf("death", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    wait = clip.length;
                    break;
                }
            }
        }
        yield return new WaitForSeconds(wait + 0.5f);

        var rends = GetComponentsInChildren<Renderer>();
        Vector3 center = transform.position;
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            center = b.center;
        }

        if (deathParticlesPrefab != null) Instantiate(deathParticlesPrefab, center, Quaternion.identity);
        foreach (var r in rends) r.enabled = false;

        yield return new WaitForSeconds(0.15f);
        Destroy(gameObject);
    }

    // ---------- Barra de vida simple (IMGUI, independiente del Canvas) ----------
    private void OnGUI()
    {
        if (state == SpiderState.Dead || health == null || player == null) return;
        if (Vector3.Distance(player.position, transform.position) > healthBarDistance) return;

        var cam = Camera.main;
        if (cam == null) return;

        Vector3 world = transform.position + Vector3.up * healthBarHeight;
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;

        const float w = 90f, h = 10f;
        float x = sp.x - w * 0.5f;
        float y = Screen.height - sp.y - h * 0.5f;

        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(x - 1, y - 1, w + 2, h + 2), Texture2D.whiteTexture);
        GUI.color = new Color(0.85f, 0.1f, 0.1f, 1f);
        GUI.DrawTexture(new Rect(x, y, w * health.HealthPercent, h), Texture2D.whiteTexture);
        GUI.color = prev;

        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
        GUI.Label(new Rect(x - 20, y - 18, w + 40, 16), health.EnemyName, style);
    }
}
