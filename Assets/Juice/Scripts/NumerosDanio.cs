using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Numeros de daño que saltan del bicho al pegarle (Alva Majo: "si dispararle le hace daño, deberia estar claro").
/// Entran grandes, rebotan, suben y se desvanecen. El golpe cargado sale en naranja y mas grande.
/// </summary>
public class NumerosDanio : MonoBehaviour
{
    class Numero { public TextMeshProUGUI t; public Vector3 mundo; public float edad; public Vector2 deriva; public float tam; }

    static NumerosDanio instancia;
    readonly List<Numero> activos = new List<Numero>();
    readonly Stack<TextMeshProUGUI> libres = new Stack<TextMeshProUGUI>();
    RectTransform raiz;

    public static void Mostrar(Vector3 mundo, float danio, bool fuerte)
    {
        if (danio <= 0f) return;
        if (instancia == null)
        {
            var go = new GameObject("NumerosDanio");
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 30;
            instancia = go.AddComponent<NumerosDanio>();
            instancia.raiz = (RectTransform)go.transform;
        }
        instancia.Nuevo(mundo, danio, fuerte);
    }

    void Nuevo(Vector3 mundo, float danio, bool fuerte)
    {
        TextMeshProUGUI t = libres.Count > 0 ? libres.Pop() : CrearTexto();
        t.gameObject.SetActive(true);
        t.text = Mathf.RoundToInt(danio).ToString();
        t.color = fuerte ? new Color(1f, 0.62f, 0.2f) : new Color(1f, 0.97f, 0.85f);
        activos.Add(new Numero { t = t, mundo = mundo, edad = 0f, deriva = new Vector2(Random.Range(-40f, 40f), 0f), tam = fuerte ? 52f : 38f });
    }

    TextMeshProUGUI CrearTexto()
    {
        var go = new GameObject("Num", typeof(RectTransform));
        go.transform.SetParent(raiz, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.alignment = TextAlignmentOptions.Center;
        t.fontStyle = FontStyles.Bold;
        t.raycastTarget = false;
        t.outlineWidth = 0.3f; t.outlineColor = new Color32(40, 10, 0, 230);
        ((RectTransform)go.transform).sizeDelta = new Vector2(200f, 80f);
        return t;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        for (int i = activos.Count - 1; i >= 0; i--)
        {
            var n = activos[i];
            n.edad += Time.unscaledDeltaTime;
            const float vida = 0.9f;
            if (n.edad >= vida || cam == null)
            {
                n.t.gameObject.SetActive(false); libres.Push(n.t); activos.RemoveAt(i); continue;
            }
            Vector3 sp = cam.WorldToScreenPoint(n.mundo);
            if (sp.z < 0f) { n.t.enabled = false; continue; }
            n.t.enabled = true;
            float k = n.edad / vida;
            // salto hacia arriba con rebote, luego sube despacio
            float y = 60f * (1f - Mathf.Pow(1f - Mathf.Min(1f, k * 2.2f), 3f)) + 25f * k;
            Vector2 pos = new Vector2(sp.x, sp.y) + new Vector2(n.deriva.x * k, y);
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(raiz, pos, null, out local);
            n.t.rectTransform.anchoredPosition = local;
            float pop = n.edad < 0.1f ? Mathf.Lerp(1.8f, 1f, n.edad / 0.1f) : 1f;
            n.t.fontSize = n.tam * pop;
            var c = n.t.color; c.a = 1f - Mathf.Clamp01((k - 0.6f) / 0.4f); n.t.color = c;
        }
    }
}
