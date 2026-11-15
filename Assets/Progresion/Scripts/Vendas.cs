using UnityEngine;

/// <summary>
/// La tela del salon tiene uso: tela + liga = venda. Con H (o eligiendola en la barra rapida) cura 70 de vida
/// y quita el veneno. Tarda un momento en ponerse (Guinxu: la curacion de Hollow Knight que se "siente").
/// </summary>
public class Vendas : MonoBehaviour
{
    public const string ItemId = "Venda";
    public float cura = 70f, tiempo = 0.9f;

    PlayerHealth vida;
    float progreso = -1f;
    bool avisado;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= AlCargar;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += AlCargar;
        AlCargar(default, default);
    }

    static void AlCargar(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
    {
        var p = Object.FindAnyObjectByType<PlayerController>();
        if (p != null && p.GetComponent<Vendas>() == null) p.gameObject.AddComponent<Vendas>();
    }

    void Start()
    {
        vida = GetComponent<PlayerHealth>();
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed += AlUsarRapido;
    }

    void OnDestroy() { if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed -= AlUsarRapido; }

    void AlUsarRapido(int slot, string itemId)
    {
        if (itemId != ItemId) return;
        Empezar();
        if (QuickAccessBar.Instance != null && QuickAccessBar.Instance.SelectedIndex == slot) QuickAccessBar.Instance.Select(slot);
    }

    static int Cuantas => ControladorInventario.Instance != null ? ControladorInventario.Instance.GetTotalCount(ItemId) : 0;

    void Empezar()
    {
        if (progreso >= 0f || vida == null || vida.IsDead) return;
        if (Cuantas <= 0)
        {
            SurvivalUI.Aviso("No tienes vendas: tela + liga en el inventario", new Color(1f, 0.7f, 0.5f));
            return;
        }
        if (vida.CurrentHealth >= vida.MaxHealth - 0.5f && !StatusEffects.Envenenado)
        {
            SurvivalUI.Aviso("No estas herido", new Color(0.85f, 0.9f, 0.85f));
            return;
        }
        progreso = 0f;
    }

    void Update()
    {
        if (!avisado && Cuantas > 0)
        {
            avisado = true;
            SurvivalUI.Aviso("Tienes una venda: pulsa [" + Controles.NombreTecla(Controles.Accion.Curar) + "] cuando estes herido", new Color(0.9f, 1f, 0.9f));
        }
        if (Controles.Bajada(Controles.Accion.Curar)) Empezar();
        if (progreso < 0f) return;

        // vendandose: un poco de temblor y la vida sube al terminar
        progreso += Time.deltaTime / tiempo;
        SurvivalUI.Indicacion(this, "Vendandote... " + Mathf.RoundToInt(progreso * 100f) + "%");
        if (Time.frameCount % 6 == 0) Juice.Sacudir(0.04f);
        if (vida == null || vida.IsDead) { progreso = -1f; return; }
        if (progreso >= 1f)
        {
            progreso = -1f;
            if (ControladorInventario.Instance == null || !ControladorInventario.Instance.RemoveItem(ItemId, 1)) return;
            vida.Heal(cura);
            StatusEffects.Limpiar();
            Juice.Destello(new Color(0.4f, 1f, 0.5f, 0.35f), 0.6f);
            Juice.Chispas(transform.position + Vector3.up * 1.1f, new Color(0.5f, 1f, 0.6f), 16, 2f);
            SonidosJuice.Tocar(SonidosJuice.Recoger, transform.position, 0.8f);
            SurvivalUI.Aviso("+" + Mathf.RoundToInt(cura) + " vida", new Color(0.5f, 1f, 0.6f));
            Logros.Sumar("vendas");
        }
    }
}
