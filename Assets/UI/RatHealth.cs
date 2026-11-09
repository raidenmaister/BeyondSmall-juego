using UnityEngine;

public class RatHealth : MonoBehaviour
{
    [Header("Configuración")]
    [SerializeField] private string enemyName = "Rata";
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth = 100f;

    public string EnemyName => enemyName;
    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthPercent => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
    public bool IsDead => currentHealth <= 0f;

    public System.Action<float, float> OnHealthChanged;
    public System.Action OnDeath;
    /// <summary>Cualquier bicho que muere (estadisticas de la partida).</summary>
    public static System.Action<RatHealth> AlguienMurio;
    /// <summary>Permite a la IA reducir o anular el daño (armadura, enterrada...).</summary>
    public System.Func<float, float> DamageFilter;

    private void Awake()
    {
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        if (DamageFilter != null) amount = DamageFilter(amount);
        if (amount <= 0f) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (IsDead)
        {
            OnDeath?.Invoke();
            AlguienMurio?.Invoke(this);
        }
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;

        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>Escala la vida sin avisar a nadie (dificultad); no despierta jefes ni dispara eventos.</summary>
    public void EscalarVida(float k)
    {
        if (k <= 0f || Mathf.Approximately(k, 1f)) return;
        maxHealth *= k; currentHealth *= k;
    }

    /// <summary>Restaura la vida guardada sin disparar eventos.</summary>
    public void FijarVida(float actual)
    {
        currentHealth = Mathf.Clamp(actual, 0.01f, maxHealth);
    }

    public void SetHealth(float current, float max)
    {
        maxHealth = Mathf.Max(1f, max);
        currentHealth = Mathf.Clamp(current, 0f, maxHealth);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }
}
