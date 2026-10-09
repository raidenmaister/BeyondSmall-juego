using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public CharacterController controller;
    public Animator animator;
    public Transform cameraTransform;

    [Header("Velocidades")]
    public float walkSpeed = 2f;
    public float runSpeed = 4.5f;
    public float crouchSpeed = 1f;
    public float turnSmoothTime = 0.1f;
    private float turnSmoothVelocity;

    [Header("Agacharse")]
    public float crouchHeight = 1f;
    private float originalHeight;
    private Vector3 originalCenter;
    private bool isCrouching;

    [Header("Salto y Gravedad")]
    public float jumpHeight = 1.5f;
    public float gravity = -25f;
    public float fallMultiplier = 2.5f;
    private Vector3 velocity;
    private bool isGrounded;

    [Header("Combate y Combo")]
    public float chargeThreshold = 0.35f;
    public float comboResetTime = 0.8f; // Tiempo sin cliquear para reiniciar combo a Golpe 1
    public float attackRange = 1.8f; // Rango cuerpo a cuerpo realista (1.8m de alcance)
    public float attackDamage = 20f; // Daño al golpear (5 golpes para derrotar a la rata de 100 PV)
    public float staminaCostPerHit = 30f; // Coste de stamina por golpe
    public PlayerStamina playerStamina;

    private int comboStep = 0; // 0 = Siguiente es Golpe 1, 1 = Siguiente es Golpe 2
    private bool isAttacking = false;
    private bool hasQueuedNextCombo = false;
    private float clickDownTime = 0f;
    private bool hasChargedAttackFired = false;
    private float lastAttackFinishTime = 0f;

    void Start()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponent<Animator>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        if (controller != null)
        {
            originalHeight = controller.height;
            originalCenter = controller.center;
        }

        if (playerStamina == null)
        {
            playerStamina = GetComponent<PlayerStamina>();
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        isGrounded = controller.isGrounded;
        if (animator != null) animator.SetBool("IsGrounded", isGrounded);

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // Reiniciar el combo tras inactividad
        if (comboStep != 0 && !isAttacking && (Time.time - lastAttackFinishTime > comboResetTime))
        {
            ResetCombo();
        }

        CheckActionStatus();
        HandleCombatInput();
        HandleBlockInput();

        // Control de Agachado
        if (Input.GetKey(KeyCode.LeftControl) && isGrounded && !isAttacking)
        {
            isCrouching = true;
            controller.height = crouchHeight;
            controller.center = new Vector3(originalCenter.x, crouchHeight / 2f, originalCenter.z);
        }
        else
        {
            isCrouching = false;
            controller.height = originalHeight;
            controller.center = originalCenter;
        }

        if (animator != null) animator.SetBool("IsCrouching", isCrouching);

        // Movimiento Horizontal
        float horizontal = 0f;
        float vertical = 0f;

        if (!isAttacking)
        {
            horizontal = Input.GetAxisRaw("Horizontal");
            vertical = Input.GetAxisRaw("Vertical");
        }

        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;

        if (direction.magnitude >= 0.1f && !isAttacking)
        {
            bool isRunning = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            float currentSpeed = walkSpeed;
            if (isCrouching) currentSpeed = crouchSpeed;
            else if (isRunning) currentSpeed = runSpeed;

            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            controller.Move(moveDir.normalized * currentSpeed * Time.deltaTime);

            if (animator != null) animator.SetFloat("Speed", isRunning && !isCrouching ? 2f : 1f);
        }
        else
        {
            if (animator != null) animator.SetFloat("Speed", 0f);
        }

        // Salto
        if (Input.GetButtonDown("Jump") && isGrounded && !isCrouching && !isAttacking)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            if (animator != null) animator.SetTrigger("Jump");
        }

        // Gravedad
        if (velocity.y < 0)
        {
            velocity.y += gravity * fallMultiplier * Time.deltaTime;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }

        controller.Move(velocity * Time.deltaTime);
    }

    void CheckActionStatus()
    {
        if (animator == null) return;

        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        bool inActionTag = stateInfo.IsTag("Attack") || stateInfo.IsTag("Block");

        if (inActionTag)
        {
            // Liberar control si la animación ya llegó al 85% o si ya está cambiando a Idle
            if (stateInfo.normalizedTime >= 0.85f || animator.IsInTransition(0))
            {
                if (animator.IsInTransition(0))
                {
                    AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
                    // Si el siguiente estado no es un ataque ni un bloqueo, liberamos el control
                    if (!nextState.IsTag("Attack") && !nextState.IsTag("Block"))
                    {
                        ReleaseAction();
                    }
                }
                else
                {
                    ReleaseAction();
                }
            }
            else
            {
                isAttacking = true;
            }
        }
        else
        {
            ReleaseAction();
        }
    }

    void ReleaseAction()
    {
        if (isAttacking)
        {
            isAttacking = false;
            lastAttackFinishTime = Time.time;

            if (hasQueuedNextCombo)
            {
                hasQueuedNextCombo = false;
                if (playerStamina == null || playerStamina.TryConsumeStamina(staminaCostPerHit))
                {
                    ExecuteNextComboStep();
                }
            }
        }
    }

    void HandleCombatInput()
    {
        if (!isGrounded || isCrouching) return;

        if (Input.GetMouseButtonDown(0))
        {
            clickDownTime = Time.time;
            hasChargedAttackFired = false;

            if (isAttacking)
            {
                if (playerStamina == null || playerStamina.HasEnoughStamina(staminaCostPerHit))
                {
                    hasQueuedNextCombo = true;
                }
            }
            else
            {
                if (playerStamina == null || playerStamina.TryConsumeStamina(staminaCostPerHit))
                {
                    ExecuteNextComboStep();
                }
            }
        }

        // Golpe Cargado (mantener presionado)
        if (Input.GetMouseButton(0) && !hasChargedAttackFired && !isAttacking)
        {
            if (Time.time - clickDownTime >= chargeThreshold)
            {
                if (playerStamina != null && !playerStamina.HasEnoughStamina(staminaCostPerHit))
                {
                    // No hay suficiente stamina para golpear
                    return;
                }

                if (playerStamina != null)
                {
                    playerStamina.ConsumeStamina(staminaCostPerHit);
                }

                ClearAnimatorTriggers();
                animator.SetTrigger("HeavyAttack");
                hasChargedAttackFired = true;
                isAttacking = true;
                PerformAttackHit();
                ResetCombo();
            }
        }

        // Golpe Ligero en cola si se soltó el clic
        if (Input.GetMouseButtonUp(0))
        {
            if (!hasChargedAttackFired && isAttacking && (Time.time - clickDownTime < chargeThreshold))
            {
                hasQueuedNextCombo = true;
            }
        }
    }

    void ExecuteNextComboStep()
    {
        isAttacking = true;
        ClearAnimatorTriggers();

        if (comboStep == 0)
        {
            animator.SetTrigger("Attack1");
            comboStep = 1; // Siguiente clic activará Golpe 2
        }
        else
        {
            animator.SetTrigger("Attack2");
            comboStep = 0; // Siguiente clic regresará a Golpe 1
        }

        PerformAttackHit();
    }

    void PerformAttackHit()
    {
        // 1. Detección física en esfera frontal cerrada frente a los puños del jugador
        Vector3 hitCenter = transform.position + transform.forward * 1.0f + Vector3.up * 0.8f;
        Collider[] hits = Physics.OverlapSphere(hitCenter, attackRange);

        bool hitFound = false;
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            var ratHealth = hit.GetComponentInParent<RatHealth>();
            if (ratHealth != null && !ratHealth.IsDead)
            {
                // Verificar que el impacto esté dentro de un cono frontal de 70 grados
                Vector3 toTarget = hit.bounds.center - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.001f && Vector3.Angle(transform.forward, toTarget.normalized) <= 70f)
                {
                    ApplyHitToRat(ratHealth);
                    hitFound = true;
                    break;
                }
            }
        }

        // 2. Respaldo directo estricto por proximidad (únicamente si la rata está a distancia cuerpo a cuerpo <= 2.2m)
        if (!hitFound)
        {
            var allRats = Object.FindObjectsByType<RatHealth>(FindObjectsInactive.Exclude);
            foreach (var ratHealth in allRats)
            {
                if (ratHealth.IsDead) continue;

                // Medir distancia al punto más cercano de la rata (cabeza/trompa o cuerpo)
                Vector3 targetPoint = ratHealth.transform.position;
                var ratAI = ratHealth.GetComponent<RatAI>();
                if (ratAI != null && ratAI.snoutTransform != null)
                {
                    targetPoint = ratAI.snoutTransform.position;
                }

                Vector3 toPoint = targetPoint - transform.position;
                toPoint.y = 0f;
                float horizontalDistance = toPoint.magnitude;

                // Alcance máximo cuerpo a cuerpo estricto: 2.2 metros
                if (horizontalDistance <= 2.2f)
                {
                    float angle = Vector3.Angle(transform.forward, toPoint.normalized);
                    if (angle <= 70f) // Frente a la vista del jugador
                    {
                        ApplyHitToRat(ratHealth);
                        break;
                    }
                }
            }
        }
    }

    private void ApplyHitToRat(RatHealth ratHealth)
    {
        ratHealth.TakeDamage(attackDamage);

        var ratAI = ratHealth.GetComponent<RatAI>();
        if (ratAI != null)
        {
            Vector3 knockbackDir = (ratHealth.transform.position - transform.position).normalized;
            knockbackDir.y = 0f;
            ratAI.TakeHit(knockbackDir);
        }
    }

    void HandleBlockInput()
    {
        if (Input.GetMouseButtonDown(1) && isGrounded && !isAttacking)
        {
            if (animator != null)
            {
                ClearAnimatorTriggers();
                animator.SetTrigger("BlockTrigger");
                isAttacking = true;
            }
        }
    }

    void ClearAnimatorTriggers()
    {
        if (animator == null) return;
        animator.ResetTrigger("Attack1");
        animator.ResetTrigger("Attack2");
        animator.ResetTrigger("HeavyAttack");
        animator.ResetTrigger("BlockTrigger");
    }

    void ResetCombo()
    {
        comboStep = 0;
        hasQueuedNextCombo = false;
        ClearAnimatorTriggers();
    }
}