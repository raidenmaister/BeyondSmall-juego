using TMPro;
using UnityEngine;

/// <summary>
/// Textos de supervivencia en el HUD: una indicacion fija ("[C] Cocinar carne")
/// y avisos breves que suben y se desvanecen ("+45 comida").
/// Se crean solos dentro de PlayerHUD_Canvas la primera vez que se usan.
/// </summary>
public class SurvivalUI : MonoBehaviour
{
    static SurvivalUI instance;

    TextMeshProUGUI indicacion;
    TextMeshProUGUI aviso;
    float tiempoAviso;
    Object duenioIndicacion;
    bool indicacionPedida;

    static SurvivalUI Get()
    {
        if (instance != null) return instance;
        var hud = GameObject.Find("PlayerHUD_Canvas");
        if (hud == null) return null;
        instance = hud.GetComponent<SurvivalUI>();
        if (instance == null) instance = hud.AddComponent<SurvivalUI>();
        return instance;
    }

    void Awake()
    {
        indicacion = CrearTexto("Survival_Indicacion", new Vector2(0f, 230f), 24f);
        aviso = CrearTexto("Survival_Aviso", new Vector2(0f, 280f), 26f);
        indicacion.gameObject.SetActive(false);
        aviso.gameObject.SetActive(false);
    }

    TextMeshProUGUI CrearTexto(string nombre, Vector2 pos, float tam)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(900f, 40f);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.alignment = TextAlignmentOptions.Center;
        t.fontSize = tam;
        t.fontStyle = FontStyles.Bold;
        t.raycastTarget = false;
        t.outlineWidth = 0.25f;
        t.outlineColor = new Color32(0, 0, 0, 220);
        return t;
    }

    /// <summary>Muestra la indicacion este frame (hay que llamarla cada frame mientras aplique).</summary>
    public static void Indicacion(Object duenio, string texto)
    {
        var ui = Get();
        if (ui == null) return;
        ui.duenioIndicacion = duenio;
        ui.indicacionPedida = true;
        ui.indicacion.text = texto;
        ui.indicacion.gameObject.SetActive(true);
    }

    public static void Aviso(string texto, Color color)
    {
        var ui = Get();
        if (ui == null) return;
        ui.aviso.text = texto;
        ui.aviso.color = color;
        ui.tiempoAviso = 2.2f;
        ui.aviso.gameObject.SetActive(true);
    }

    void LateUpdate()
    {
        if (!indicacionPedida && indicacion.gameObject.activeSelf) indicacion.gameObject.SetActive(false);
        indicacionPedida = false;

        if (tiempoAviso > 0f)
        {
            tiempoAviso -= Time.unscaledDeltaTime;
            var rt = (RectTransform)aviso.transform;
            rt.anchoredPosition = new Vector2(0f, 280f + (2.2f - tiempoAviso) * 18f);
            var c = aviso.color; c.a = Mathf.Clamp01(tiempoAviso / 0.6f); aviso.color = c;
            if (tiempoAviso <= 0f) aviso.gameObject.SetActive(false);
        }
    }
}
