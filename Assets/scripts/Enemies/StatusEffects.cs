using UnityEngine;

/// <summary>
/// Efectos de estado del jugador (ralentizacion de telarana, veneno de ciempies...).
/// Se anade solo al jugador la primera vez que se usa.
/// </summary>
public class StatusEffects : MonoBehaviour
{
    static StatusEffects instance;

    float slowMult = 1f, slowUntil;
    float poisonDps, poisonUntil, poisonTick;
    PlayerHealth health;

    public static float SpeedMultiplier => instance != null && Time.time < instance.slowUntil ? instance.slowMult : 1f;

    public static StatusEffects Get()
    {
        if (instance == null)
        {
            var pc = Object.FindAnyObjectByType<PlayerController>();
            if (pc != null)
            {
                instance = pc.gameObject.GetComponent<StatusEffects>();
                if (instance == null) instance = pc.gameObject.AddComponent<StatusEffects>();
            }
        }
        return instance;
    }

    public static void Slow(float multiplier, float seconds)
    {
        var s = Get();
        if (s == null) return;
        s.slowMult = Mathf.Clamp(multiplier, 0.1f, 1f);
        s.slowUntil = Mathf.Max(s.slowUntil, Time.time + seconds);
    }

    public static void Poison(float damagePerSecond, float seconds)
    {
        var s = Get();
        if (s == null) return;
        s.poisonDps = damagePerSecond;
        s.poisonUntil = Mathf.Max(s.poisonUntil, Time.time + seconds);
    }

    void Awake()
    {
        instance = this;
        health = GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (health == null || health.IsDead || Time.time >= poisonUntil) return;
        poisonTick += Time.deltaTime;
        if (poisonTick >= 1f)
        {
            poisonTick -= 1f;
            health.TakeDamage(poisonDps);
        }
    }

    void OnGUI()
    {
        bool slow = Time.time < slowUntil, poison = Time.time < poisonUntil;
        if (!slow && !poison) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        float y = 150f;
        if (poison)
        {
            style.normal.textColor = new Color(0.5f, 1f, 0.3f);
            GUI.Label(new Rect(20, y, 300, 22), "Envenenado (" + (poisonUntil - Time.time).ToString("F1") + "s)", style);
            y += 22;
        }
        if (slow)
        {
            style.normal.textColor = new Color(0.9f, 0.9f, 1f);
            GUI.Label(new Rect(20, y, 300, 22), "Atrapado en telarana (" + (slowUntil - Time.time).ToString("F1") + "s)", style);
        }
    }
}
