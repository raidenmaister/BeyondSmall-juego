using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerStamina : MonoBehaviour
{
    [Header("Configuración de Stamina")]
    [SerializeField] private float maxStamina = 100f;
    [SerializeField] private float currentStamina = 100f;
    [Tooltip("Puntos de stamina regenerados por segundo.")]
    public float regenRate = 15f;
    [Tooltip("Tiempo de espera en segundos tras consumir stamina antes de empezar a regenerar.")]
    public float regenDelay = 0.5f;

    [Header("Referencias UI")]
    public UnityEngine.UI.Image fillImage;
    public TextMeshProUGUI staminaText;

    [Header("Animación de Barra")]
    public float smoothSpeed = 12f;

    private float displayedFill = 1f;
    private float lastConsumeTime = -99f;

    public float MaxStamina => maxStamina;
    public float CurrentStamina => currentStamina;
    public float StaminaPercent => maxStamina > 0f ? Mathf.Clamp01(currentStamina / maxStamina) : 0f;

    public System.Action<float, float> OnStaminaChanged;

    private void Awake()
    {
        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
        displayedFill = StaminaPercent;
    }

    private void Start()
    {
        FindUIReferencesIfNeeded();
        if (fillImage != null && !colorLeido) { colorBase = fillImage.color; colorLeido = true; }
        UpdateUI(immediate: true);
    }

    private void Update()
    {
        // Regeneración gradual y lenta de stamina tras el tiempo de retardo
        if (Time.time - lastConsumeTime >= regenDelay && currentStamina < maxStamina)
        {
            currentStamina = Mathf.Min(maxStamina, currentStamina + regenRate * Pociones.MultEnergia * Time.deltaTime);
            UpdateUI(immediate: false);
            OnStaminaChanged?.Invoke(currentStamina, maxStamina);
        }

        // Actualización fluida y visible de la barra de llenado
        if (fillImage != null)
        {
            float targetFill = StaminaPercent;
            displayedFill = Mathf.MoveTowards(displayedFill, targetFill, smoothSpeed * Time.deltaTime);
            fillImage.fillAmount = displayedFill;
        }
    }

    float agitar;
    RectTransform barra; Vector2 barraPos;
    float ultimoAviso = -9f;

    /// <summary>
    /// Alva Majo: si pulsas atacar y no pasa nada, el juego tiene que decirte por que.
    /// La barra de stamina se agita y se pone roja, y suena un resoplido.
    /// </summary>
    public void AvisarSinStamina()
    {
        if (Time.time - ultimoAviso < 0.25f) return;
        ultimoAviso = Time.time;
        agitar = 0.45f;
        SonidosJuice.Tocar(SonidosJuice.SinAliento, transform.position + Vector3.up, 0.7f);
    }

    void LateUpdate()
    {
        if (agitar <= 0f) return;
        if (barra == null && fillImage != null)
        {
            var t = fillImage.transform;
            while (t.parent != null && !t.name.EndsWith("_Container")) t = t.parent;
            barra = t as RectTransform;
            if (barra != null) barraPos = barra.anchoredPosition;
        }
        agitar -= Time.unscaledDeltaTime;
        if (barra != null)
            barra.anchoredPosition = barraPos + (agitar > 0f ? new Vector2(Mathf.Sin(Time.unscaledTime * 70f) * 9f * agitar / 0.45f, 0f) : Vector2.zero);
        if (fillImage != null)
            fillImage.color = Color.Lerp(colorBase, new Color(1f, 0.25f, 0.2f), agitar > 0f ? Mathf.PingPong(Time.unscaledTime * 8f, 1f) : 0f);
    }

    Color colorBase = Color.white; bool colorLeido;

    public bool HasEnoughStamina(float amount)
    {
        return currentStamina >= amount;
    }

    public bool TryConsumeStamina(float amount)
    {
        if (currentStamina < amount)
        {
            return false;
        }

        currentStamina = Mathf.Max(0f, currentStamina - amount);
        lastConsumeTime = Time.time;
        UpdateUI(immediate: false);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
        return true;
    }

    public void ConsumeStamina(float amount)
    {
        currentStamina = Mathf.Max(0f, currentStamina - amount);
        lastConsumeTime = Time.time;
        UpdateUI(immediate: false);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public void SetStamina(float current, float max)
    {
        maxStamina = Mathf.Max(1f, max);
        currentStamina = Mathf.Clamp(current, 0f, maxStamina);
        UpdateUI(immediate: true);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    private void UpdateUI(bool immediate)
    {
        FindUIReferencesIfNeeded();

        if (staminaText != null)
        {
            staminaText.text = string.Format("{0}/{1} ST", Mathf.CeilToInt(currentStamina), Mathf.CeilToInt(maxStamina));
        }

        if (fillImage != null && immediate)
        {
            displayedFill = StaminaPercent;
            fillImage.fillAmount = displayedFill;
        }
    }

    private void FindUIReferencesIfNeeded()
    {
        if (fillImage == null || staminaText == null)
        {
            var hud = GameObject.Find("PlayerHUD_Canvas");
            if (hud != null)
            {
                var sBar = hud.transform.Find("StaminaBar_Container");
                if (sBar != null)
                {
                    if (fillImage == null)
                    {
                        var fill = sBar.Find("StaminaBar_Frame/Track/Fill");
                        if (fill != null) fillImage = fill.GetComponent<UnityEngine.UI.Image>();
                    }
                    if (staminaText == null)
                    {
                        var textT = sBar.Find("StaminaBar_Frame/ST_Text");
                        if (textT != null) staminaText = textT.GetComponent<TextMeshProUGUI>();
                    }
                }
            }
        }
    }
}
