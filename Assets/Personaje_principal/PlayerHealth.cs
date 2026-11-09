using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerHealth : MonoBehaviour
{
    [Header("Configuración de Vida")]
    [SerializeField] private float maxHealth = 200f;
    [SerializeField] private float currentHealth = 200f;

    [Header("Referencias UI (Opcional, se buscan automáticamente)")]
    public Image fillImage;
    public TextMeshProUGUI hpText;

    [Header("Animación de Barra")]
    public float smoothSpeed = 10f;

    private float displayedFill = 1f;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthPercent => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
    public bool IsDead => currentHealth <= 0f;

    public System.Action<float, float> OnHealthChanged;
    public System.Action OnDeath;

    private void Awake()
    {
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        displayedFill = HealthPercent;
    }

    private void Start()
    {
        FindUIReferencesIfNeeded();
        UpdateUI(immediate: true);
    }

    private void Update()
    {
        if (fillImage != null)
        {
            float targetFill = HealthPercent;
            displayedFill = Mathf.MoveTowards(displayedFill, targetFill, smoothSpeed * Time.deltaTime);
            fillImage.fillAmount = displayedFill;
        }
    }

    /// <summary>Daño de animales (mordidas, picaduras, veneno, proyectiles). En pacifico no hace nada.</summary>
    public void TakeDamage(float amount) { TakeDamage(amount, true); }

    /// <summary>bloqueable = false para el veneno (ya esta adentro, el escudo no sirve).</summary>
    public void TakeDamage(float amount, bool bloqueable)
    {
        if (!Partida.Agresivos) return;
        if (bloqueable && Escudo.Instancia != null) amount = Escudo.Instancia.Filtrar(amount);
        DanioEntorno(amount);
    }

    /// <summary>Daño que no viene de animales (hambre): cuenta en todas las dificultades.</summary>
    public void DanioEntorno(float amount)
    {
        if (IsDead || amount <= 0f || FinalJuego.Activo) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        UpdateUI(immediate: false);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (IsDead)
        {
            OnDeath?.Invoke();
        }
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        UpdateUI(immediate: false);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    public void SetHealth(float current, float max)
    {
        maxHealth = Mathf.Max(1f, max);
        currentHealth = Mathf.Clamp(current, 0f, maxHealth);
        UpdateUI(immediate: true);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void UpdateUI(bool immediate)
    {
        FindUIReferencesIfNeeded();

        if (hpText != null)
        {
            hpText.text = string.Format("{0}/{1} PV", Mathf.CeilToInt(currentHealth), Mathf.CeilToInt(maxHealth));
        }

        if (fillImage != null)
        {
            float targetFill = HealthPercent;
            if (immediate)
            {
                displayedFill = targetFill;
                fillImage.fillAmount = targetFill;
            }
        }
    }

    private void FindUIReferencesIfNeeded()
    {
        if (fillImage == null || hpText == null)
        {
            var hud = GameObject.Find("PlayerHUD_Canvas");
            if (hud != null)
            {
                var pBar = hud.transform.Find("HealthBar_Container");
                if (pBar != null)
                {
                    if (fillImage == null)
                    {
                        var fill = pBar.Find("HealthBar_Frame/Track/Fill");
                        if (fill != null) fillImage = fill.GetComponent<Image>();
                    }
                    if (hpText == null)
                    {
                        var textT = pBar.Find("HealthBar_Frame/HP_Text");
                        if (textT != null) hpText = textT.GetComponent<TextMeshProUGUI>();
                    }
                }
            }
        }
    }
}
