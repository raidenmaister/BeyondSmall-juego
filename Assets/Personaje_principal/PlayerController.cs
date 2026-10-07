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

    void Start()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponent<Animator>();
        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        // Guardar valores originales del CharacterController
        if (controller != null)
        {
            originalHeight = controller.height;
            originalCenter = controller.center;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // 1. Detección de suelo
        isGrounded = controller.isGrounded;
        if (animator != null) animator.SetBool("IsGrounded", isGrounded);

        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        // 2. Control de Agachado (Left Control)
        if (Input.GetKey(KeyCode.LeftControl) && isGrounded)
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

        // 3. Movimiento Horizontal
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 direction = new Vector3(horizontal, 0f, vertical).normalized;

        if (direction.magnitude >= 0.1f)
        {
            bool isRunning = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            
            // Determinar velocidad actual según el estado
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

        // 4. Salto (solo si no está agachado)
        if (Input.GetButtonDown("Jump") && isGrounded && !isCrouching)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            if (animator != null) animator.SetTrigger("Jump");
        }

        // 5. Aplicar Gravedad
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
}