using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Barra de comida del jugador. Baja poco a poco, mucho mas al correr y al pelear.
/// Se llena comiendo carne (tecla F, clic derecho en el inventario o seleccionandola
/// en el acceso rapido). La carne cocinada llena mas y cura mucha mas vida.
/// Con el estomago lleno se regenera vida; con hambre la stamina se recupera lento
/// y a cero se pierde vida.
/// </summary>
public class PlayerHunger : MonoBehaviour
{
    public static PlayerHunger Instance { get; private set; }

    [Header("Comida")]
    [SerializeField] private float maxFood = 100f;
    [SerializeField] private float currentFood = 100f;

    [Header("Gasto (puntos por segundo)")]
    [Tooltip("Gasto constante, aunque estes quieto")]
    public float gastoBase = 0.05f;
    [Tooltip("Gasto extra mientras corres")]
    public float gastoCorriendo = 0.8f;
    [Tooltip("Gasto extra mientras atacas")]
    public float gastoPeleando = 3f;

    [Header("Efectos")]
    [Tooltip("Vida que se pierde por segundo con la comida en 0")]
    public float danioPorHambre = 2f;
    [Tooltip("Por encima de este porcentaje se regenera vida")]
    [Range(0f, 1f)] public float umbralSaciado = 0.8f;
    public float regeneracionSaciado = 0.6f;
    [Tooltip("Por debajo de este porcentaje la stamina se recupera a la mitad")]
    [Range(0f, 1f)] public float umbralHambre = 0.25f;

    [Header("Alimentos: comida que llena / vida que cura")]
    public Vector2 carneCruda = new Vector2(20f, 10f);
    public Vector2 carneCocinada = new Vector2(40f, 45f);
    public KeyCode teclaComer = KeyCode.F;

    [Header("UI (se busca sola en PlayerHUD_Canvas/HungerBar_Container)")]
    public Image fillImage;
    public TextMeshProUGUI foodText;
    public Color colorLleno = new Color(1f, 0.62f, 0.15f);
    public Color colorHambre = new Color(0.95f, 0.2f, 0.12f);
    public float smoothSpeed = 10f;

    public float MaxFood => maxFood;
    public float CurrentFood => currentFood;
    public float FoodPercent => maxFood > 0f ? Mathf.Clamp01(currentFood / maxFood) : 0f;

    PlayerController controller;
    PlayerHealth health;
    PlayerStamina stamina;
    float regenStaminaOriginal;
    float displayedFill = 1f;
    float acumuladoHambre;

    public static bool EsComida(string itemId)
    {
        string id = ControladorInventario.NormalizeItemName(itemId);
        return id == "Carne" || id == "Carne_Cocinada";
    }

    void Awake()
    {
        Instance = this;
        controller = GetComponent<PlayerController>();
        health = GetComponent<PlayerHealth>();
        stamina = GetComponent<PlayerStamina>();
        if (stamina != null) regenStaminaOriginal = stamina.regenRate;
        currentFood = Mathf.Clamp(currentFood, 0f, maxFood);
        displayedFill = FoodPercent;
    }

    void OnEnable()
    {
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed += AlUsarAccesoRapido;
    }

    void Start()
    {
        // por si QuickAccessBar se inicializo despues que nosotros
        if (QuickAccessBar.Instance != null)
        {
            QuickAccessBar.Instance.OnQuickSlotUsed -= AlUsarAccesoRapido;
            QuickAccessBar.Instance.OnQuickSlotUsed += AlUsarAccesoRapido;
        }
        BuscarUI();
        ActualizarTexto();
    }

    void OnDisable()
    {
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed -= AlUsarAccesoRapido;
    }

    void AlUsarAccesoRapido(int indice, string itemId)
    {
        if (!EsComida(itemId)) return;
        Comer(itemId);
        // la comida no se "equipa": se consume y se deselecciona
        if (QuickAccessBar.Instance != null && QuickAccessBar.Instance.SelectedIndex == indice) QuickAccessBar.Instance.Select(indice);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || (health != null && health.IsDead)) return;

        // --- Gasto
        float gasto = gastoBase;
        if (controller != null)
        {
            if (controller.IsRunning) gasto += gastoCorriendo;
            if (controller.IsAttacking) gasto += gastoPeleando;
        }
        currentFood = Mathf.Max(0f, currentFood - gasto * dt);

        // --- Efectos sobre vida y stamina
        if (health != null)
        {
            if (currentFood <= 0f)
            {
                acumuladoHambre += danioPorHambre * dt;
                if (acumuladoHambre >= 1f) { health.TakeDamage(Mathf.Floor(acumuladoHambre)); acumuladoHambre -= Mathf.Floor(acumuladoHambre); }
            }
            else if (FoodPercent >= umbralSaciado && health.CurrentHealth < health.MaxHealth)
            {
                health.Heal(regeneracionSaciado * dt);
            }
        }
        if (stamina != null) stamina.regenRate = FoodPercent < umbralHambre ? regenStaminaOriginal * 0.5f : regenStaminaOriginal;

        // --- Comer con F (primero la cocinada)
        bool inventarioAbierto = ControladorInventario.Instance != null && ControladorInventario.Instance.IsOpen;
        if (!inventarioAbierto && Input.GetKeyDown(teclaComer))
        {
            var inv = ControladorInventario.Instance;
            if (inv != null && inv.GetTotalCount("Carne_Cocinada") > 0) Comer("Carne_Cocinada");
            else if (inv != null && inv.GetTotalCount("Carne") > 0) Comer("Carne");
            else SurvivalUI.Aviso("No tienes comida", new Color(1f, 0.6f, 0.5f));
        }

        // --- Barra
        if (fillImage != null)
        {
            displayedFill = Mathf.MoveTowards(displayedFill, FoodPercent, smoothSpeed * Time.unscaledDeltaTime);
            fillImage.fillAmount = displayedFill;
            Color c = Color.Lerp(colorHambre, colorLleno, Mathf.InverseLerp(0.1f, 0.4f, FoodPercent));
            if (FoodPercent < umbralHambre) c = Color.Lerp(c, Color.white, 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.time * 6f)));
            fillImage.color = c;
        }
        ActualizarTexto();
    }

    /// <summary>Come una unidad del alimento si esta en el inventario.</summary>
    public bool Comer(string itemId)
    {
        if (health != null && health.IsDead) return false;
        string id = ControladorInventario.NormalizeItemName(itemId);
        if (!EsComida(id)) return false;
        var inv = ControladorInventario.Instance;
        if (inv == null || !inv.RemoveItem(id, 1)) return false;

        Vector2 valor = id == "Carne_Cocinada" ? carneCocinada : carneCruda;
        currentFood = Mathf.Min(maxFood, currentFood + valor.x);
        if (health != null) health.Heal(valor.y);
        string nombre = id == "Carne_Cocinada" ? "Carne cocinada" : "Carne cruda";
        SurvivalUI.Aviso($"{nombre}: +{valor.x:0} comida  +{valor.y:0} vida", new Color(0.65f, 1f, 0.55f));
        return true;
    }

    public void SetFood(float valor)
    {
        currentFood = Mathf.Clamp(valor, 0f, maxFood);
        ActualizarTexto();
    }

    void ActualizarTexto()
    {
        if (foodText != null) foodText.text = string.Format("{0}/{1} COMIDA", Mathf.CeilToInt(currentFood), Mathf.CeilToInt(maxFood));
    }

    void BuscarUI()
    {
        if (fillImage != null && foodText != null) return;
        var hud = GameObject.Find("PlayerHUD_Canvas");
        var barra = hud != null ? hud.transform.Find("HungerBar_Container") : null;
        if (barra == null) return;
        if (fillImage == null) { var f = barra.Find("HungerBar_Frame/Track/Fill"); if (f != null) fillImage = f.GetComponent<Image>(); }
        if (foodText == null) { var t = barra.Find("HungerBar_Frame/Food_Text"); if (t != null) foodText = t.GetComponent<TextMeshProUGUI>(); }
    }
}
