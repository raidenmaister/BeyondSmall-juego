using UnityEngine;

public class RatAI : MonoBehaviour
{
    public enum RatState
    {
        Idle = 0,
        Run = 1,
        Walk = 2,
        Attack = 3,
        HitRecovery = 4,
        Dead = 5
    }

    [Header("Referencias")]
    [Tooltip("Transform del jugador. Si está vacío, se busca automáticamente.")]
    public Transform player;

    [Tooltip("Componente Animator de la rata.")]
    public Animator animator;

    [Tooltip("Script de la barra de vida que detecta la proximidad.")]
    public RatHealthBarUI healthBarUI;

    [Tooltip("Componente de salud de la rata.")]
    public RatHealth ratHealth;

    [Header("Velocidades de Movimiento")]
    public float runSpeed = 8.45f; // Aumentado otro 30% (de 6.5f a 8.45f)
    public float walkSpeed = 10.56f; // El doble de rápido (de 5.28f a 10.56f)
    public float rotationSpeed = 14.0f;

    [Header("Distancias y Tiempos")]
    [Tooltip("Tiempo límite en segundos que la rata persigue al jugador antes de desistir y volver.")]
    public float chaseDuration = 3.0f;

    [Tooltip("Distancia para considerar que ha alcanzado al jugador para atacarlo.")]
    public float attackReachDistance = 3.3f;

    [Tooltip("Distancia mínima al jugador para evitar atravesarlo con el cuerpo.")]
    public float minStoppingDistance = 3.1f;

    [Tooltip("Distancia al punto de origen para considerar que ha regresado.")]
    public float returnReachDistance = 0.3f;

    [Header("Retroceso y Recuperación al ser Golpeada")]
    [Tooltip("Distancia en metros que la rata retrocede al recibir un golpe.")]
    public float knockbackDistance = 1.6f;

    [Tooltip("Tiempo en segundos que tarda en desplazarse hacia atrás por el golpe.")]
    public float knockbackDuration = 0.25f;

    [Tooltip("Tiempo en segundos que la rata espera quieta tras el retroceso antes de volver a golpear.")]
    public float hitRecoveryWaitTime = 2.0f;

    [Header("Referencias de Esqueleto")]
    [Tooltip("Transform de la trompa o cabeza de la rata (ratHead o ratHead_end).")]
    public Transform snoutTransform;

    [Header("Combate y Ataque")]
    [Tooltip("Daño que la rata inflige al jugador por cada golpe.")]
    public float attackDamage = 35.0f;

    [Tooltip("Intervalo en segundos entre cada golpe de ataque.")]
    public float attackInterval = 1.2f;

    [Tooltip("Referencia al componente de salud del jugador.")]
    public PlayerHealth playerHealth;

    [Header("Orientación del Modelo")]
    [Tooltip("El modelo 3D tiene la cabeza apuntando en -Z local. Si está activo, invierte la orientación para que la cabeza vaya de frente.")]
    public bool invertForward = true;

    private Vector3 homePosition;
    private Quaternion homeRotation;
    private RatState currentState = RatState.Idle;
    private float chaseTimer = 0f;
    private float attackTimer = 0f;
    private bool wasHealthBarVisible = false;

    // Variables de retroceso y recuperación
    private float hitRecoveryTimer = 0f;
    private Vector3 knockbackVelocity = Vector3.zero;

    private static readonly int StateHash = Animator.StringToHash("State");

    public RatState CurrentState => currentState;
    public Vector3 HomePosition => homePosition;

    private Vector3 ModelForward => invertForward ? -transform.forward : transform.forward;

    private void Awake()
    {
        homePosition = transform.position;
        homeRotation = transform.rotation;

        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }

        if (healthBarUI == null)
        {
            healthBarUI = Object.FindAnyObjectByType<RatHealthBarUI>();
        }

        if (ratHealth == null)
        {
            ratHealth = GetComponent<RatHealth>();
        }

        FindSnoutIfNeeded();
    }

    private void OnEnable()
    {
        if (ratHealth == null) ratHealth = GetComponent<RatHealth>();
        if (ratHealth != null)
        {
            ratHealth.OnDeath += HandleDeath;
        }
    }

    private void OnDisable()
    {
        if (ratHealth != null)
        {
            ratHealth.OnDeath -= HandleDeath;
        }
    }

    private void HandleDeath()
    {
        Die();
    }

    public void Die()
    {
        SetState(RatState.Dead);
        knockbackVelocity = Vector3.zero;

        // Reproducir la animación de muerte
        if (animator != null)
        {
            animator.Play("Death", 0, 0f);
        }

        // Desactivar el colisionador para que no bloquee ni reciba más golpes
        var col = GetComponent<Collider>();
        if (col != null)
        {
            col.enabled = false;
        }
    }

    private void FindSnoutIfNeeded()
    {
        if (snoutTransform == null)
        {
            foreach (var t in GetComponentsInChildren<Transform>())
            {
                if (t.name == "ratHead_end" || t.name == "ratHead")
                {
                    snoutTransform = t;
                    break;
                }
            }
        }
    }

    private void Start()
    {
        FindPlayerIfNeeded();
        if (ratHealth == null) ratHealth = GetComponent<RatHealth>();
        if (ratHealth != null)
        {
            ratHealth.OnDeath -= HandleDeath;
            ratHealth.OnDeath += HandleDeath;
            if (ratHealth.IsDead)
            {
                Die();
                return;
            }
        }
        SetState(RatState.Idle);
    }

    public void Update()
    {
        // Si la rata está muerta, no procesar IA ni movimiento, se queda tirada en el suelo
        if (currentState == RatState.Dead || (ratHealth != null && ratHealth.IsDead))
        {
            if (currentState != RatState.Dead)
            {
                Die();
            }
            return;
        }

        FindPlayerIfNeeded();

        // 1. Detectar el momento exacto en que la barra de vida se hace visible
        bool isHealthBarVisible = healthBarUI != null && healthBarUI.IsPlayerInRange;

        if (isHealthBarVisible && !wasHealthBarVisible)
        {
            // Acaba de aparecer la barra de vida -> iniciar persecución
            StartChasing();
        }

        wasHealthBarVisible = isHealthBarVisible;

        // 2. Máquina de estados
        switch (currentState)
        {
            case RatState.Idle:
                // Si la barra está visible y la rata ya está en home o terminó una acción previa,
                // aseguramos que esté en Idle
                break;

            case RatState.Run:
                UpdateRunState(Time.deltaTime);
                break;

            case RatState.Attack:
                UpdateAttackState(Time.deltaTime);
                break;

            case RatState.Walk:
                UpdateWalkState(Time.deltaTime);
                break;

            case RatState.HitRecovery:
                UpdateHitRecoveryState(Time.deltaTime);
                break;
        }
    }

    /// <summary>
    /// Llamado cuando el jugador golpea a la rata. Retrocede un poco, espera 2 segundos y vuelve a atacar/perseguir.
    /// </summary>
    public void TakeHit(Vector3 knockbackDirection)
    {
        // Si ya está muerta o se quedó sin vida, no procesar golpe
        if (currentState == RatState.Dead || (ratHealth != null && ratHealth.IsDead))
        {
            return;
        }

        // Cancelar estados previos y pasar a recuperación
        SetState(RatState.HitRecovery);

        // Si la dirección viene vacía, empujar en contra del frente del modelo
        if (knockbackDirection.sqrMagnitude < 0.001f)
        {
            knockbackDirection = -ModelForward;
        }
        knockbackDirection.y = 0f;
        knockbackDirection = knockbackDirection.normalized;

        float kSpeed = knockbackDuration > 0f ? (knockbackDistance / knockbackDuration) : 0f;
        knockbackVelocity = knockbackDirection * kSpeed;
        hitRecoveryTimer = hitRecoveryWaitTime;

        // Reiniciar el temporizador de persecución para asegurar que busque volver a atacar
        chaseTimer = Mathf.Max(chaseTimer, 3.0f);
    }

    private void UpdateHitRecoveryState(float deltaTime)
    {
        // 1. Aplicar retroceso físico suave
        if (knockbackVelocity.sqrMagnitude > 0.01f)
        {
            transform.position += knockbackVelocity * deltaTime;
            knockbackVelocity = Vector3.MoveTowards(knockbackVelocity, Vector3.zero, (knockbackDistance / (knockbackDuration * knockbackDuration)) * deltaTime);
        }

        // Mantener la trompa mirando hacia el jugador mientras se recupera
        if (player != null)
        {
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            RotateTowards(toPlayer, deltaTime);
        }

        // 2. Esperar 2 segundos
        hitRecoveryTimer -= deltaTime;
        if (hitRecoveryTimer <= 0f)
        {
            // Tras los 2 segundos, volver a golpear o perseguir para atacar
            if (player != null)
            {
                Vector3 toPlayer = player.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.magnitude <= attackReachDistance)
                {
                    SetState(RatState.Attack);
                }
                else
                {
                    SetState(RatState.Run);
                }
            }
            else
            {
                SetState(RatState.Walk);
            }
        }
    }

    public void StartChasing()
    {
        chaseTimer = chaseDuration;
        SetState(RatState.Run);
    }

    private void RotateTowards(Vector3 direction, float deltaTime)
    {
        if (direction.sqrMagnitude < 0.0001f) return;

        // Si la cabeza del modelo está en -Z local del GameObject,
        // para que la cabeza mire a direction, transform.forward debe apuntar a -direction.
        Vector3 desiredForward = invertForward ? -direction.normalized : direction.normalized;
        Quaternion targetRot = Quaternion.LookRotation(desiredForward);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * deltaTime);
    }

    private void UpdateRunState(float deltaTime)
    {
        if (player == null)
        {
            SetState(RatState.Walk);
            return;
        }

        chaseTimer -= deltaTime;

        // Medir distancia desde la trompa al jugador (o distancia entre raíces)
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distanceToPlayer = toPlayer.magnitude;

        // Rotar para que la trompa mire hacia el jugador
        RotateTowards(toPlayer, deltaTime);

        // Si la trompa ya alcanza al jugador (distancia <= attackReachDistance), pasar a ataque
        if (distanceToPlayer <= attackReachDistance)
        {
            SetState(RatState.Attack);
            return;
        }

        // ¿Se agotaron los 3 segundos sin alcanzar al jugador?
        if (chaseTimer <= 0f)
        {
            SetState(RatState.Walk);
            return;
        }

        // Desplazarse hacia el jugador sólo si no ha rebasado la distancia de seguridad
        if (distanceToPlayer > minStoppingDistance)
        {
            Vector3 moveStep = ModelForward * (runSpeed * deltaTime);
            transform.position += moveStep;
        }
    }

    private void UpdateAttackState(float deltaTime)
    {
        if (player == null)
        {
            SetState(RatState.Walk);
            return;
        }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distanceToPlayer = toPlayer.magnitude;

        // Mirar con la trompa hacia el jugador durante el ataque
        RotateTowards(toPlayer, deltaTime);

        // Si la rata está demasiado cerca (atravesando al jugador), retroceder ligeramente para mantener la trompa en contacto
        if (distanceToPlayer < minStoppingDistance)
        {
            Vector3 pushBack = -ModelForward * (2.0f * deltaTime);
            transform.position += pushBack;
        }

        // Si el jugador se alejó demasiado de la zona de ataque, volver a perseguir o regresar
        if (distanceToPlayer > attackReachDistance * 1.35f)
        {
            if (chaseTimer > 0f)
            {
                SetState(RatState.Run);
                return;
            }
            else
            {
                SetState(RatState.Walk);
                return;
            }
        }

        // Contador para el siguiente golpe cada attackInterval (1.2 segundos)
        attackTimer -= deltaTime;
        if (attackTimer <= 0f)
        {
            ExecuteAttackHit();
            attackTimer = attackInterval;
        }
    }

    private void ExecuteAttackHit()
    {
        // Reiniciar la animación de ataque
        if (animator != null)
        {
            animator.Play("Attack", 0, 0f);
        }

        // Aplicar daño al jugador
        FindPlayerHealthIfNeeded();
        if (playerHealth != null)
        {
            playerHealth.TakeDamage(attackDamage);
        }
    }

    private void UpdateWalkState(float deltaTime)
    {
        Vector3 toHome = homePosition - transform.position;
        toHome.y = 0f;
        float distanceToHome = toHome.magnitude;

        // ¿Llegó a su lugar original?
        if (distanceToHome <= returnReachDistance)
        {
            transform.position = new Vector3(homePosition.x, transform.position.y, homePosition.z);
            transform.rotation = homeRotation;
            SetState(RatState.Idle);
            return;
        }

        // Rotar para que la cabeza mire hacia su punto de origen
        RotateTowards(toHome, deltaTime);

        // Desplazarse en la dirección donde apunta la cabeza
        Vector3 moveStep = ModelForward * (walkSpeed * deltaTime);
        transform.position += moveStep;
    }

    public void SetState(RatState newState)
    {
        currentState = newState;
        if (newState == RatState.Attack)
        {
            // Ejecutar el primer golpe inmediatamente al alcanzar al jugador
            attackTimer = 0f;
        }

        if (animator != null)
        {
            if (newState == RatState.HitRecovery)
            {
                // Durante la recuperación por impacto reproducir Idle
                animator.SetInteger(StateHash, (int)RatState.Idle);
            }
            else
            {
                animator.SetInteger(StateHash, (int)newState);
            }
        }
    }

    private void FindPlayerIfNeeded()
    {
        if (player == null)
        {
            var p = Object.FindAnyObjectByType<PlayerController>();
            if (p != null)
            {
                player = p.transform;
            }
            else
            {
                var go = GameObject.Find("Jugador");
                if (go != null) player = go.transform;
            }
        }

        FindPlayerHealthIfNeeded();
    }

    private void FindPlayerHealthIfNeeded()
    {
        if (playerHealth == null)
        {
            if (player != null)
            {
                playerHealth = player.GetComponent<PlayerHealth>();
            }
            if (playerHealth == null)
            {
                playerHealth = Object.FindAnyObjectByType<PlayerHealth>();
            }
        }
    }

    public void SetHomePosition(Vector3 pos, Quaternion rot)
    {
        homePosition = pos;
        homeRotation = rot;
    }
}