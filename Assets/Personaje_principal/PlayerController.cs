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
    [Tooltip("Coyote time (Alva Majo): segundos en los que aun se puede saltar despues de salirse del borde.")]
    public float coyoteTime = 0.15f;
    [Tooltip("Input buffer: si pulsas saltar un poco antes de tocar el suelo, el salto se guarda y sale al aterrizar.")]
    public float jumpBuffer = 0.18f;
    [Tooltip("Al soltar saltar mientras subes, la velocidad se multiplica por esto: saltos cortos y largos.")]
    [Range(0.2f, 1f)] public float corteSaltoCorto = 0.45f;
    [Tooltip("Cerca del punto mas alto la gravedad baja un poco: el salto flota un instante y es mas facil de controlar.")]
    [Range(0.3f, 1f)] public float gravedadCumbre = 0.6f;
    private Vector3 velocity;
    private float ultimoSuelo = -99f;       // ultimo instante tocando suelo (coyote time)
    private float saltoPedido = -99f;       // ultimo instante en que se pulso saltar (input buffer)
    private bool saltando;                  // subiendo por un salto (salto variable; no gastar el coyote dos veces)
    private bool estabaEnSuelo = true;
    private float velocidadCaida;           // para saber lo fuerte que aterriza
    private float ataquePedido = -99f;      // input buffer del ataque (pulsado justo antes de poder pegar)
    [Tooltip("Velocidad de caida con el planeador (mejora Alas + Hueso).")]
    public float velocidadPlaneo = 2.2f;
    public bool Planeando { get; private set; }
    /// <summary>Aterriza (velocidad de caida). Lo usan el squash and stretch y el polvo.</summary>
    public System.Action<float> AlAterrizar;
    /// <summary>Despega con un salto.</summary>
    public System.Action AlSaltar;
    private Vector3 lastSafePosition;
    private bool isGrounded;

    [Header("Combate y Combo")]
    public float chargeThreshold = 0.35f;
    public float comboResetTime = 0.8f; // Tiempo sin cliquear para reiniciar combo a Golpe 1
    public float attackRange = 1.8f; // Rango cuerpo a cuerpo realista (1.8m de alcance)
    public float attackDamage = 20f; // Daño al golpear (5 golpes para derrotar a la rata de 100 PV)
    [Tooltip("El golpe cargado pega mas fuerte: mantener el clic tiene recompensa.")]
    public float heavyDamageMultiplier = 2f;
    public float staminaCostPerHit = 30f; // Coste de stamina por golpe
    public PlayerStamina playerStamina;

    [Header("Inventario")]
    private bool isInventoryOpen = false; // Estado actual del inventario

    private int comboStep = 0; // 0 = Siguiente es Golpe 1, 1 = Siguiente es Golpe 2
    private bool isAttacking = false;
    private bool hasQueuedNextCombo = false;
    private float clickDownTime = 0f;
    private bool hasChargedAttackFired = false;
    private float lastAttackFinishTime = 0f;

    // Para la barra de comida: correr y pelear gastan energia
    public bool IsRunning { get; private set; }
    public bool IsAttacking => isAttacking;
    public bool IsGrounded => isGrounded;
    public bool InventarioAbierto => isInventoryOpen;
    public Vector3 VelocidadVertical => velocity;
    // Para el sonido de los golpes: true si el puñetazo conecto con un enemigo
    public System.Action<bool> OnPunch;

    void Start()
    {
        lastSafePosition = transform.position;
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
        // Gestionar la apertura y cierre del inventario con la tecla I
        HandleInventoryInput();

        // Si el inventario está abierto, podemos bloquear el movimiento del jugador (opcional pero recomendado)
        if (isInventoryOpen)
        {
            IsRunning = false;
            if (animator != null) animator.SetFloat("Speed", 0f);
            return; 
        }

        isGrounded = controller.isGrounded;
        if (animator != null) animator.SetBool("IsGrounded", isGrounded);

        if (isGrounded)
        {
            if (!estabaEnSuelo && velocidadCaida > 1f) AlAterrizar?.Invoke(velocidadCaida);
            ultimoSuelo = Time.time;
            velocidadCaida = 0f;
            if (velocity.y < 0) { velocity.y = -2f; saltando = false; }
        }
        else velocidadCaida = Mathf.Max(velocidadCaida, -velocity.y);
        estabaEnSuelo = isGrounded;

        // Reiniciar el combo tras inactividad
        if (comboStep != 0 && !isAttacking && (Time.time - lastAttackFinishTime > comboResetTime))
        {
            ResetCombo();
        }

        CheckActionStatus();
        HandleCombatInput();
        HandleBlockInput();

        // Control de Agachado
        if (Controles.Pulsada(Controles.Accion.Agacharse) && isGrounded && !isAttacking)
        {
            isCrouching = true;
            controller.height = crouchHeight;
            controller.center = new Vector3(originalCenter.x, crouchHeight / 2f, originalCenter.z);
        }
        else if (!isCrouching || HayHuecoParaLevantarse())
        {
            // no se levanta si hay algo justo encima (antes atravesaba el techo)
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
            horizontal = Controles.Eje(Controles.Accion.Izquierda, Controles.Accion.Derecha, KeyCode.LeftArrow, KeyCode.RightArrow);
            vertical = Controles.Eje(Controles.Accion.Atras, Controles.Accion.Adelante, KeyCode.DownArrow, KeyCode.UpArrow);
        }

        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;

        if (direction.magnitude >= 0.1f && !isAttacking)
        {
            bool isRunning = Controles.Pulsada(Controles.Accion.Correr);
            IsRunning = isRunning && !isCrouching;

            float currentSpeed = walkSpeed;
            if (isCrouching) currentSpeed = crouchSpeed;
            else if (isRunning) currentSpeed = runSpeed;
            if (Planeando) currentSpeed = Mathf.Max(currentSpeed, runSpeed * 1.15f);   // al planear se avanza
            currentSpeed *= StatusEffects.SpeedMultiplier;

            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;
            float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            Vector3 moveDir = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
            controller.Move(moveDir.normalized * currentSpeed * Time.deltaTime);

            if (animator != null) animator.SetFloat("Speed", isRunning && !isCrouching ? 2f : 1f);
        }
        else
        {
            IsRunning = false;
            if (animator != null) animator.SetFloat("Speed", 0f);
        }

        // Salto (Alva Majo: coyote time + input buffer + altura variable)
        if (Controles.Bajada(Controles.Accion.Saltar)) saltoPedido = Time.time;
        bool sueloReciente = isGrounded || (!saltando && Time.time - ultimoSuelo <= coyoteTime);
        if (Time.time - saltoPedido <= jumpBuffer && sueloReciente && !isCrouching && !isAttacking)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * Mejoras.MultSalto * -2f * gravity);
            saltoPedido = -99f;
            saltando = true;
            ultimoSuelo = -99f;
            if (animator != null) animator.SetTrigger("Jump");
            AlSaltar?.Invoke();
        }
        // soltar el boton mientras sube = salto corto
        if (saltando && velocity.y > 0f && Controles.Soltada(Controles.Accion.Saltar))
            velocity.y *= corteSaltoCorto;

        // Planeador (mejora): mantener saltar mientras cae = caida lenta
        Planeando = Mejoras.TienePlaneador && !isGrounded && velocity.y < 0f && Controles.Pulsada(Controles.Accion.Saltar)
                    && Time.time - ultimoSuelo > 0.12f;

        // Gravedad (mas fuerte al caer; mas suave justo en la cumbre del salto)
        if (Planeando)
        {
            velocity.y = Mathf.MoveTowards(velocity.y, -velocidadPlaneo, -gravity * 2f * Time.deltaTime);
        }
        else if (velocity.y < 0)
        {
            velocity.y += gravity * fallMultiplier * Time.deltaTime;
        }
        else
        {
            float k = (saltando && velocity.y < 2.2f && Controles.Pulsada(Controles.Accion.Saltar)) ? gravedadCumbre : 1f;
            velocity.y += gravity * k * Time.deltaTime;
        }

        var flags = controller.Move(velocity * Time.deltaTime);
        if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f && !CorregirEsquina())
            velocity.y = 0f;        // cabezazo de verdad: empieza a caer

        // Red de seguridad: si algo lo saca del mapa, volver al último punto seguro
        if (isGrounded) lastSafePosition = transform.position;
        else if (transform.position.y < lastSafePosition.y - 30f
                 && !Physics.Raycast(transform.position, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore))
        {
            // solo si de verdad no hay suelo debajo (planear o caer de un mueble alto es legal)
            controller.enabled = false;
            transform.position = lastSafePosition + Vector3.up * 0.5f;
            controller.enabled = true;
            velocity = Vector3.zero;
        }
    }

    void HandleInventoryInput()
    {
        if (Controles.Bajada(Controles.Accion.Inventario))
        {
            isInventoryOpen = !isInventoryOpen; // Alterna el estado (true/false)

            if (animator != null)
            {
                animator.SetBool("IsOpen", isInventoryOpen);
            }

            // Manejo del cursor para poder interactuar con los elementos del inventario
            if (isInventoryOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
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
                if (playerStamina == null || playerStamina.TryConsumeStamina(CosteGolpe))
                {
                    ExecuteNextComboStep();
                }
            }
        }
    }

    void HandleCombatInput()
    {
        if (!isGrounded || isCrouching || Escudo.Bloqueando)
        {
            // en el aire o con el escudo arriba no se golpea, pero el clic se guarda un momento
            if (Controles.Bajada(Controles.Accion.Atacar)) ataquePedido = Time.time;
            return;
        }

        // input buffer: un clic justo antes de poder pegar (al aterrizar, al bajar el escudo) no se pierde
        bool clic = Controles.Bajada(Controles.Accion.Atacar);
        if (!clic && !isAttacking && Time.time - ataquePedido <= 0.2f) clic = true;
        if (clic)
        {
            ataquePedido = -99f;
            clickDownTime = Time.time;
            hasChargedAttackFired = false;

            if (isAttacking)
            {
                if (playerStamina == null || playerStamina.HasEnoughStamina(CosteGolpe))
                {
                    hasQueuedNextCombo = true;
                }
            }
            else
            {
                if (playerStamina == null || playerStamina.TryConsumeStamina(CosteGolpe))
                {
                    ExecuteNextComboStep();
                }
                else
                {
                    // Alva Majo: si pulsar no hace nada, que se note POR QUE (la barra se agita y suena)
                    playerStamina.AvisarSinStamina();
                }
            }
        }

        // Golpe Cargado (mantener presionado)
        if (Controles.Pulsada(Controles.Accion.Atacar) && !hasChargedAttackFired && !isAttacking)
        {
            if (Time.time - clickDownTime >= chargeThreshold)
            {
                if (playerStamina != null && !playerStamina.HasEnoughStamina(CosteGolpe))
                {
                    // No hay suficiente stamina para golpear
                    hasChargedAttackFired = true;
                    playerStamina.AvisarSinStamina();
                    return;
                }

                if (playerStamina != null)
                {
                    playerStamina.ConsumeStamina(CosteGolpe);
                }

                ClearAnimatorTriggers();
                animator.SetTrigger("HeavyAttack");
                hasChargedAttackFired = true;
                isAttacking = true;
                PerformAttackHit(true);
                ResetCombo();
            }
        }

        // Golpe Ligero en cola si se soltó el clic
        if (Controles.Soltada(Controles.Accion.Atacar))
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

    // arma en la mano (Armas.cs) o puños
    float CosteGolpe => Armas.Equipada != null ? Armas.Equipada.stamina : staminaCostPerHit;

    void PerformAttackHit(bool cargado = false)
    {
        golpeCargado = cargado;
        var arma = Armas.Equipada;
        float alcance = attackRange + (arma != null ? arma.alcance : 0f);
        float cono = arma != null ? arma.cono * 0.5f + 20f : 70f;
        bool area = arma != null && arma.area;
        // 1. Detección física en esfera frontal (las armas largas llegan mas lejos)
        Vector3 hitCenter = transform.position + transform.forward * (1.0f + (arma != null ? arma.alcance * 0.5f : 0f)) + Vector3.up * 0.8f;
        Collider[] hits = Physics.OverlapSphere(hitCenter, alcance);

        bool hitFound = false;
        var golpeados = new System.Collections.Generic.HashSet<RatHealth>();
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            var ratHealth = hit.GetComponentInParent<RatHealth>();
            if (ratHealth != null && !ratHealth.IsDead && !golpeados.Contains(ratHealth))
            {
                // Verificar que el impacto esté dentro del cono frontal del arma
                Vector3 toTarget = hit.bounds.center - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.001f && Vector3.Angle(transform.forward, toTarget.normalized) <= cono)
                {
                    golpeados.Add(ratHealth);
                    ApplyHitToRat(ratHealth);
                    hitFound = true;
                    if (!area) break;           // el mazo golpea a todos los de delante
                }
            }
        }

        // 2. Respaldo directo estricto por proximidad
        if (!hitFound)
        {
            float maxDist = 2.2f + (arma != null ? arma.alcance : 0f);
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

                if (horizontalDistance <= maxDist)
                {
                    float angle = Vector3.Angle(transform.forward, toPoint.normalized);
                    if (angle <= cono) // Frente a la vista del jugador
                    {
                        ApplyHitToRat(ratHealth);
                        hitFound = true;
                        break;
                    }
                }
            }
        }

        OnPunch?.Invoke(hitFound);
    }

    bool golpeCargado;
    RatHealthBarUI barraEnemigo;

    private void ApplyHitToRat(RatHealth ratHealth)
    {
        var arma = Armas.Equipada;
        float baseDanio = arma != null ? arma.danio : attackDamage;
        float danio = baseDanio * (golpeCargado ? heavyDamageMultiplier : 1f) * Mejoras.MultDanio;
        float hecho;
        if (arma != null && arma.rompeCorazas && ratHealth.DamageFilter != null)
        {
            // las tenazas muerden a traves del caparazon: se salta la armadura de la IA
            var filtro = ratHealth.DamageFilter;
            ratHealth.DamageFilter = null;
            hecho = ratHealth.TakeDamage(danio);
            ratHealth.DamageFilter = filtro;
            if (hecho > 0f) Juice.Chispas(ratHealth.transform.position + Vector3.up * 0.6f, new Color(0.7f, 0.85f, 1f), 10, 3f);
        }
        else hecho = ratHealth.TakeDamage(danio);
        if (arma != null && arma.veneno > 0f && hecho > 0f && !ratHealth.IsDead) VenenoBicho.Aplicar(ratHealth, arma.veneno, 5f);
        if (barraEnemigo == null) barraEnemigo = Object.FindAnyObjectByType<RatHealthBarUI>();
        if (barraEnemigo != null) barraEnemigo.FocusOn(ratHealth);
        SensacionGolpe.AlGolpear(this, ratHealth, hecho, golpeCargado);

        // empuje del arma (martillo y mazo mandan lejos): se escala el retroceso de la IA un momento
        float empuje = arma != null ? arma.empuje : 1f;
        Vector3 knockbackDir = ratHealth.transform.position - transform.position;
        knockbackDir.y = 0f; knockbackDir.Normalize();

        var ratAI = ratHealth.GetComponent<RatAI>();
        if (ratAI != null)
        {
            float d0 = ratAI.knockbackDistance; ratAI.knockbackDistance = d0 * empuje;
            ratAI.TakeHit(knockbackDir);
            ratAI.knockbackDistance = d0;
        }

        var creatureAI = ratHealth.GetComponent<CreatureAI>();
        if (creatureAI != null)
        {
            float d0 = creatureAI.knockbackDistance; creatureAI.knockbackDistance = d0 * empuje;
            creatureAI.TakeHit(knockbackDir);
            creatureAI.knockbackDistance = d0;
        }

        var spiderAI = ratHealth.GetComponent<SpiderAI>();
        if (spiderAI != null)
        {
            float d0 = spiderAI.knockbackDistance; spiderAI.knockbackDistance = d0 * empuje;
            spiderAI.TakeHit(knockbackDir);
            spiderAI.knockbackDistance = d0;
        }
    }

    void HandleBlockInput()
    {
        if (Escudo.TieneEscudo) return;      // con escudo el bloqueo lo hace Escudo (brazo izquierdo)
        if (Controles.Bajada(Controles.Accion.Bloquear) && isGrounded && !isAttacking)
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

    // ------------------------------------------------------------------ ayudas de control
    bool HayHuecoParaLevantarse()
    {
        float r = controller.radius * 0.9f;
        Vector3 pie = transform.position + Vector3.up * (crouchHeight - r);
        float sube = originalHeight - crouchHeight;
        RaycastHit h;
        return !Physics.SphereCast(pie, r, Vector3.up, out h, sube + 0.02f, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Correccion de esquinas (Alva Majo / Super Mario Bros): si al saltar la cabeza roza el borde
    /// de algo, el personaje se recoloca un poco hacia el lado libre en vez de darse el cabezazo.
    /// </summary>
    bool CorregirEsquina()
    {
        float r = controller.radius;
        Vector3 cabeza = transform.position + controller.center + Vector3.up * (controller.height * 0.5f - r);
        Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 der = Vector3.Cross(Vector3.up, fwd);
        Vector3[] dirs = { der, -der, fwd, -fwd, (der + fwd).normalized, (fwd - der).normalized, (der - fwd).normalized, (-fwd - der).normalized };
        RaycastHit h;
        for (float paso = 0.15f; paso <= r * 1.1f + 0.01f; paso += 0.15f)
            foreach (var d in dirs)
            {
                Vector3 p = cabeza + d * paso;
                // hueco libre encima de la nueva posicion y camino libre hasta ella
                if (Physics.CheckSphere(p + Vector3.up * 0.12f, r * 0.95f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.SphereCast(cabeza, r * 0.9f, d, out h, paso, ~0, QueryTriggerInteraction.Ignore)) continue;
                controller.Move(d * paso);
                return true;
            }
        return false;
    }

    void ResetCombo()
    {
        comboStep = 0;
        hasQueuedNextCombo = false;
        ClearAnimatorTriggers();
    }
}