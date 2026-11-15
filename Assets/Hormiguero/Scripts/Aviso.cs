using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Letrero grande y temporal en el centro de la pantalla (p. ej. "¡La colonia esta furiosa!").
/// Se escribe con TextoVivo (admite &lt;temblor&gt; y &lt;ola&gt;). Si ya hay uno en pantalla, el nuevo espera su
/// turno en una cola para que dos letreros nunca se pisen (claridad visual).
/// </summary>
public class Aviso : MonoBehaviour
{
    TextMeshProUGUI texto;
    float t, duracion;

    static Aviso actual;
    static readonly Queue<(string m, Color c, float s)> cola = new Queue<(string, Color, float)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reiniciar() { actual = null; cola.Clear(); }

    public static void Mostrar(string mensaje, Color color, float segundos = 3.5f)
    {
        if (actual != null)
        {
            // el mismo mensaje dos veces seguidas no se repite
            foreach (var x in cola) if (x.m == mensaje) return;
            if (actual.texto != null && actual.mensaje == mensaje) return;
            if (cola.Count < 6) cola.Enqueue((mensaje, color, segundos));
            // el que esta en pantalla se da prisa
            actual.duracion = Mathf.Min(actual.duracion, Mathf.Max(actual.t + 0.9f, 1.6f));
            return;
        }
        Crear(mensaje, color, segundos);
    }

    string mensaje;

    static void Crear(string mensaje, Color color, float segundos)
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
        a.texto = tx; a.duracion = segundos + 0.4f; a.mensaje = mensaje;
        actual = a;
        // las letras entran una a una con pausas en la puntuacion (admite <temblor> y <ola>)
        TextoVivo.Escribir(tx, mensaje, 45f);
    }

    void Update()
    {
        t += Time.unscaledDeltaTime;
        float alpha = Mathf.Clamp01(t / 0.3f) * Mathf.Clamp01((duracion - t) / 0.8f);
        var c = texto.color; c.a = alpha; texto.color = c;
        texto.transform.localScale = Vector3.one * (1f + 0.15f * Mathf.Exp(-t * 6f));
        if (t >= duracion) Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (actual != this) return;
        actual = null;
        if (cola.Count > 0) { var x = cola.Dequeue(); Crear(x.m, x.c, x.s); }
    }
}
