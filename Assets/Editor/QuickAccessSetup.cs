using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Crea el HUD de acceso rápido (teclas 1-4) y convierte los 4 apartados de "Equipamiento rápido"
/// del inventario en slots donde se pueden soltar ítems arrastrándolos.
/// </summary>
public static class QuickAccessSetup
{
    static readonly Color[] TagColors =
    {
        new Color(0.95f, 0.76f, 0.25f), // 1 amarillo
        new Color(0.50f, 0.68f, 0.82f), // 2 azul
        new Color(0.85f, 0.32f, 0.30f), // 3 rojo
        new Color(0.52f, 0.68f, 0.40f), // 4 verde
    };

    [MenuItem("Tools/Inventario/Crear acceso rapido (HUD 1-4)")]
    public static void Run()
    {
        var log = new StringBuilder();
        try { RunInternal(log); }
        catch (System.Exception e) { log.AppendLine("ERROR: " + e); }
        File.WriteAllText("Temp/quick_setup.txt", log.ToString());
        Debug.Log("[QuickAccessSetup] " + log);
    }

    static void RunInternal(StringBuilder log)
    {
        var canvas = Resources.FindObjectsOfTypeAll<Canvas>().FirstOrDefault(c => c.gameObject.scene.IsValid() && c.name == "PlayerHUD_Canvas");
        if (canvas == null) { log.AppendLine("sin PlayerHUD_Canvas"); return; }
        var bg = canvas.transform.Find("InventoryBackground");
        var grid = bg.Find("LeftPage_Inventory/GridContainer");
        var eqContainer = bg.Find("MiddlePage_Equipment/EquipmentGridContainer") as RectTransform;
        if (eqContainer == null) { log.AppendLine("sin EquipmentGridContainer"); return; }

        var template = grid.GetComponentsInChildren<InventorySlotUI>(true).First();

        // ---- 1) slots del cuaderno (4 apartados) ----
        var oldSlots = eqContainer.Cast<Transform>().Where(t => t.name.StartsWith("Slot") || t.name.StartsWith("Quick")).ToList();
        log.AppendLine("apartados existentes: " + oldSlots.Count);
        var pageSlots = new InventorySlotUI[4];
        for (int i = 0; i < 4; i++)
        {
            var go = Object.Instantiate(template.gameObject, eqContainer);
            go.name = "QuickPageSlot_" + (i + 1);
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            if (i < oldSlots.Count)
            {
                var o = (RectTransform)oldSlots[i];
                rt.anchorMin = o.anchorMin; rt.anchorMax = o.anchorMax; rt.pivot = o.pivot;
                rt.sizeDelta = o.sizeDelta; rt.anchoredPosition = o.anchoredPosition;
            }
            var img = go.GetComponent<Image>();
            img.color = new Color(1, 1, 1, 0); img.raycastTarget = true;
            var s = go.GetComponent<InventorySlotUI>();
            s.SetKind(InventorySlotUI.SlotKind.Quick);
            s.Clear();
            // icono un poco mas grande y a la izquierda de la caja ancha
            var icon = go.transform.Find("ItemIcon") as RectTransform;
            if (icon != null) { icon.sizeDelta = new Vector2(52, 52); icon.anchoredPosition = Vector2.zero; }
            pageSlots[i] = s;
        }
        foreach (var o in oldSlots) Object.DestroyImmediate(o.gameObject);
        for (int i = 0; i < 4; i++) pageSlots[i].transform.SetSiblingIndex(i);

        // ---- 2) HUD ----
        var old = canvas.transform.Find("QuickAccessBar");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = new GameObject("QuickAccessBar", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        var rrt = (RectTransform)root.transform;
        const float slot = 68f, gap = 12f;
        rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0f);
        rrt.pivot = new Vector2(0.5f, 0f);
        rrt.sizeDelta = new Vector2(slot * 4 + gap * 3, slot);
        rrt.anchoredPosition = new Vector2(0, 112f);

        var bar = root.AddComponent<QuickAccessBar>();
        bar.pageSlots = pageSlots;
        bar.hudSlots = new QuickAccessBar.HudSlot[4];

        for (int i = 0; i < 4; i++)
        {
            var h = new QuickAccessBar.HudSlot();
            var go = new GameObject("QuickSlot_" + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(root.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.sizeDelta = new Vector2(slot, slot);
            rt.anchoredPosition = new Vector2(i * (slot + gap), 0);
            var hl = go.GetComponent<Image>();
            hl.color = new Color(1f, 0.84f, 0f, 1f); hl.raycastTarget = false; hl.enabled = false;
            h.root = go; h.highlight = hl;

            var back = NewImage("Fondo", go.transform, new Color(0.08f, 0.08f, 0.08f, 0.85f));
            Stretch(back.rectTransform, 3);

            // marco del color de la etiqueta del cuaderno
            var tag = NewImage("Etiqueta", go.transform, TagColors[i]);
            tag.rectTransform.anchorMin = new Vector2(0, 1); tag.rectTransform.anchorMax = new Vector2(0, 1);
            tag.rectTransform.pivot = new Vector2(0, 1);
            tag.rectTransform.sizeDelta = new Vector2(20, 20);
            tag.rectTransform.anchoredPosition = new Vector2(3, -3);

            var icon = NewImage("ItemIcon", go.transform, Color.white);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            icon.rectTransform.sizeDelta = new Vector2(48, 48);
            icon.preserveAspect = true; icon.enabled = false;
            h.icon = icon;

            var key = NewText("Tecla", go.transform, (i + 1).ToString(), 16, new Color(0.1f, 0.1f, 0.1f));
            key.rectTransform.anchorMin = new Vector2(0, 1); key.rectTransform.anchorMax = new Vector2(0, 1);
            key.rectTransform.pivot = new Vector2(0, 1);
            key.rectTransform.sizeDelta = new Vector2(20, 20);
            key.rectTransform.anchoredPosition = new Vector2(3, -3);
            key.alignment = TextAlignmentOptions.Center;
            h.keyLabel = key;

            var qty = NewText("Cantidad", go.transform, "", 16, Color.white);
            qty.rectTransform.anchorMin = qty.rectTransform.anchorMax = new Vector2(1, 0);
            qty.rectTransform.pivot = new Vector2(1, 0);
            qty.rectTransform.sizeDelta = new Vector2(40, 20);
            qty.rectTransform.anchoredPosition = new Vector2(-5, 3);
            qty.alignment = TextAlignmentOptions.BottomRight;
            qty.gameObject.SetActive(false);
            h.quantityText = qty;

            bar.hudSlots[i] = h;
        }

        // debajo del inventario para que este lo cubra al abrirse
        root.transform.SetAsFirstSibling();

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveScene(canvas.gameObject.scene);
        log.AppendLine("HUD de acceso rapido creado y escena guardada");
    }

    static Image NewImage(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c; img.raycastTarget = false;
        return img;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.fontStyle = FontStyles.Bold; t.color = c; t.raycastTarget = false;
        return t;
    }

    static void Stretch(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
    }

    // ---- ayudas de prueba (solo Play) ----
    [MenuItem("Tools/Inventario/Dar items de prueba 2")]
    public static void GiveItems()
    {
        if (!Application.isPlaying || ControladorInventario.Instance == null) return;
        var c = ControladorInventario.Instance;
        c.AddItem("Clips", null, 2); c.AddItem("Borrador", null, 1); c.AddItem("Ligas", null, 3);
        c.AddItem("Chincheta", null, 2); c.AddItem("Tornillo", null, 4); c.AddItem("Tuerca", null, 1);
    }

    [MenuItem("Tools/Inventario/Abrir o cerrar inventario 2")]
    public static void ToggleInv()
    {
        if (Application.isPlaying && ControladorInventario.Instance != null) ControladorInventario.Instance.ToggleInventario();
    }
}
