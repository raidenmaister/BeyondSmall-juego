using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RatHealthBarUI : MonoBehaviour
{
    [Header("Objetivos")]
    [Tooltip("Transform del jugador. Si está vacío, se busca automáticamente.")]
    public Transform player;

    [Tooltip("Transform de la rata. Si está vacío, se busca automáticamente.")]
    public Transform ratTarget;

    [Tooltip("Componente de salud de la rata.")]
    public RatHealth ratHealth;

    [Header("Configuración de Proximidad")]
    [Tooltip("Distancia en metros a la que la barra de vida se hace visible.")]
    [Range(2f, 40f)]
    public float detectionDistance = 18.0f;

    [Tooltip("Margen adicional de distancia antes de ocultar la barra para evitar parpadeos.")]
    public float hideBuffer = 1.0f;

    [Header("Referencias de UI")]
    [Tooltip("Contenedor opcional de elementos visuales (NO debe ser el GameObject que tiene este script).")]
    public GameObject healthBarRoot;

    [Tooltip("CanvasGroup para controlar visibilidad y desvanecido.")]
    public CanvasGroup canvasGroup;

    [Tooltip("Imagen roja de relleno de la barra de vida.")]
    public UnityEngine.UI.Image fillImage;

    [Tooltip("Texto con el nombre de la rata.")]
    public TextMeshProUGUI nameText;

    [Tooltip("Texto opcional con valores numéricos de vida (ej. '100 / 100').")]
    public TextMeshProUGUI healthText;

    [Header("Animación")]
    public float fadeSpeed = 8.0f;
    public float healthBarSmoothSpeed = 10.0f;

    private bool isPlayerInRange = false;
    private float targetAlpha = 0f;
    private float displayedFill = 1f;

    public bool IsPlayerInRange => isPlayerInRange;
    public float TargetAlpha => targetAlpha;
    public float CurrentAlpha => canvasGroup != null ? canvasGroup.alpha : 0f;

    private void Awake()
    {
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
        }

        // Iniciar completamente oculto mediante CanvasGroup
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        // Si healthBarRoot apunta a este mismo GameObject, anularlo para evitar desactivar el script
        if (healthBarRoot == gameObject)
        {
            healthBarRoot = null;
        }

        if (healthBarRoot != null && healthBarRoot != gameObject)
        {
            healthBarRoot.SetActive(false);
        }
    }

    private void Start()
    {
        FindReferencesIfNeeded();
        UpdateName();
        UpdateVisuals(immediate: true);
    }

    private void OnEnable()
    {
        FindReferencesIfNeeded();
        if (ratHealth != null)
        {
            ratHealth.OnHealthChanged += HandleHealthChanged;
        }
    }

    private void OnDisable()
    {
        if (ratHealth != null)
        {
            ratHealth.OnHealthChanged -= HandleHealthChanged;
        }
    }

    public void Update()
    {
        EvaluateProximity(Time.deltaTime);
    }

    public void EvaluateProximity(float deltaTime)
    {
        FindReferencesIfNeeded();

        if (player == null || ratTarget == null)
        {
            HideImmediate();
            return;
        }

        // Si la rata está muerta, ocultar la barra
        if (ratHealth != null && ratHealth.IsDead)
        {
            targetAlpha = 0f;
            isPlayerInRange = false;
        }
        else
        {
            float dist = Vector3.Distance(player.position, ratTarget.position);

            if (!isPlayerInRange)
            {
                if (dist <= detectionDistance)
                {
                    isPlayerInRange = true;
                    targetAlpha = 1f;
                    if (healthBarRoot != null && healthBarRoot != gameObject && !healthBarRoot.activeSelf)
                    {
                        healthBarRoot.SetActive(true);
                    }
                    UpdateVisuals(immediate: false);
                }
            }
            else
            {
                if (dist > (detectionDistance + hideBuffer))
                {
                    isPlayerInRange = false;
                    targetAlpha = 0f;
                }
            }
        }

        // Transición de opacidad (Fade In / Fade Out)
        if (canvasGroup != null)
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * deltaTime);
            bool isVisible = canvasGroup.alpha > 0.001f;
            canvasGroup.blocksRaycasts = isVisible;
            canvasGroup.interactable = isVisible;

            if (healthBarRoot != null && healthBarRoot != gameObject)
            {
                if (canvasGroup.alpha <= 0.001f && targetAlpha == 0f)
                {
                    if (healthBarRoot.activeSelf)
                    {
                        healthBarRoot.SetActive(false);
                    }
                }
                else if (targetAlpha > 0f && !healthBarRoot.activeSelf)
                {
                    healthBarRoot.SetActive(true);
                }
            }
        }

        // Actualizar barra de vida mientras sea visible
        if (isPlayerInRange || (canvasGroup != null && canvasGroup.alpha > 0.01f))
        {
            UpdateVisuals(immediate: false);
        }
    }

    public void UpdateVisuals(bool immediate)
    {
        float targetFill = 1f;
        if (ratHealth != null)
        {
            targetFill = ratHealth.HealthPercent;

            if (healthText != null)
            {
                healthText.text = string.Format("{0} / {1}", Mathf.CeilToInt(ratHealth.CurrentHealth), Mathf.CeilToInt(ratHealth.MaxHealth));
            }
        }

        if (fillImage != null)
        {
            if (immediate)
            {
                displayedFill = targetFill;
            }
            else
            {
                displayedFill = Mathf.MoveTowards(displayedFill, targetFill, healthBarSmoothSpeed * Time.deltaTime);
            }
            fillImage.fillAmount = displayedFill;
        }
    }

    private void UpdateName()
    {
        if (nameText != null)
        {
            nameText.text = ratHealth != null ? ratHealth.EnemyName : "Rata";
        }
    }

    private void HandleHealthChanged(float cur, float max)
    {
        UpdateVisuals(immediate: false);
    }

    public void FindReferencesIfNeeded()
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

        if (ratTarget == null)
        {
            if (ratHealth != null)
            {
                ratTarget = ratHealth.transform;
            }
            else
            {
                var rh = Object.FindAnyObjectByType<RatHealth>();
                if (rh != null)
                {
                    ratHealth = rh;
                    ratTarget = rh.transform;
                }
                else
                {
                    var rGo = GameObject.Find("Rata_muerte");
                    if (rGo != null)
                    {
                        ratTarget = rGo.transform;
                        ratHealth = rGo.GetComponent<RatHealth>();
                    }
                }
            }
            UpdateName();
        }
    }

    public void HideImmediate()
    {
        isPlayerInRange = false;
        targetAlpha = 0f;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
        if (healthBarRoot != null && healthBarRoot != gameObject)
        {
            healthBarRoot.SetActive(false);
        }
    }
}
