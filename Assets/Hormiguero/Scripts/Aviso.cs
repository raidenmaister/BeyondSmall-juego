using TMPro;
using UnityEngine;

/// <summary>Letrero grande y temporal en el centro de la pantalla (p. ej. "¡La colonia esta furiosa!").</summary>
public class Aviso : MonoBehaviour
{
    TextMeshProUGUI texto;
    float t, duracion;

    public static void Mostrar(string mensaje, Color color, float segundos = 3.5f)
    {
        var go = new GameObject("Aviso");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var txtGo = new GameObject("Texto");
        txtGo.transform.SetParent(go.transform, false);
        var tx = txtGo.AddComponent<TextMeshProUGUI>();
        tx.text = mensaje;
        tx.fontSize = 64;
        tx.fontStyle = FontStyles.Bold;
        tx.alignment = TextAlignmentOptions.Center;
        tx.color = color;
        tx.outlineWidth = 0.25f;
        tx.outlineColor = new Color32(0, 0, 0, 220);
        var rt = tx.rectTransform;
        rt.anchorMin = new Vector2(0f, 0.6f); rt.anchorMax = new Vector2(1f, 0.8f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var a = go.AddComponent<Aviso>();
        a.texto = tx; a.duracion = segundos;
    }

    void Update()
    {
        t += Time.deltaTime;
        float alpha = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((duracion - t) / 0.8f);
        var c = texto.color; c.a = alpha; texto.color = c;
        texto.transform.localScale = Vector3.one * (1f + 0.15f * Mathf.Exp(-t * 6f));
        if (t >= duracion) Destroy(gameObject);
    }
}

