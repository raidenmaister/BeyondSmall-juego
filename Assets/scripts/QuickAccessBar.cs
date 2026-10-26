using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Acceso rápido del 1 al 4. Los ítems se arrastran desde el inventario a los 4 apartados de la página
/// "Equipamiento rápido" (slots de tipo Quick); este componente los muestra en el HUD y permite
/// seleccionarlos con las teclas 1-4.
/// </summary>
public class QuickAccessBar : MonoBehaviour
{
    [Serializable]
    public class HudSlot
    {
        public GameObject root;
        public Image highlight;
        public Image icon;
        public TextMeshProUGUI quantityText;
        public TextMeshProUGUI keyLabel;
    }

    public static QuickAccessBar Instance { get; private set; }

    [Header("Slots de la página del inventario (los que se llenan arrastrando)")]
    public InventorySlotUI[] pageSlots = new InventorySlotUI[4];

    [Header("Casillas del HUD")]
    public HudSlot[] hudSlots = new HudSlot[4];

    [Header("Apariencia")]
    public Color selectedColor = new Color(1f, 0.84f, 0f, 1f);

    /// <summary>Índice seleccionado (0-3) o -1.</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>Se dispara al pulsar una tecla 1-4 sobre una casilla con ítem: (índice, id del ítem).</summary>
    public event Action<int, string> OnQuickSlotUsed;

    private static readonly KeyCode[] Keys =
    {
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4
    };
    private static readonly KeyCode[] KeypadKeys =
    {
        KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4
    };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Update()
    {
        // Con el inventario abierto (juego en pausa) no se usan las teclas
        if (ControladorInventario.Instance != null && ControladorInventario.Instance.IsOpen) return;

        for (int i = 0; i < Keys.Length; i++)
        {
            if (Input.GetKeyDown(Keys[i]) || Input.GetKeyDown(KeypadKeys[i]))
            {
                Select(i);
            }
        }
    }

    private void LateUpdate()
    {
        Refresh();
    }

    public string GetItemId(int index)
    {
        var s = SlotAt(index);
        return s != null && !s.IsEmpty ? s.ItemId : null;
    }

    public void Select(int index)
    {
        var s = SlotAt(index);
        if (s == null || s.IsEmpty) return;           // casilla vacía: no hace nada

        if (SelectedIndex == index)
        {
            SelectedIndex = -1;                         // pulsar de nuevo deselecciona
            return;
        }

        SelectedIndex = index;
        OnQuickSlotUsed?.Invoke(index, s.ItemId);
    }

    private InventorySlotUI SlotAt(int i)
    {
        return pageSlots != null && i >= 0 && i < pageSlots.Length ? pageSlots[i] : null;
    }

    private void Refresh()
    {
        if (hudSlots == null) return;

        if (SelectedIndex >= 0)
        {
            var sel = SlotAt(SelectedIndex);
            if (sel == null || sel.IsEmpty) SelectedIndex = -1;
        }

        for (int i = 0; i < hudSlots.Length; i++)
        {
            var h = hudSlots[i];
            var s = SlotAt(i);
            if (h == null || h.root == null) continue;

            bool has = s != null && !s.IsEmpty && s.CurrentIcon != null;

            if (h.icon != null)
            {
                h.icon.enabled = has;
                if (has) h.icon.sprite = s.CurrentIcon;
            }
            if (h.quantityText != null)
            {
                bool showQty = s != null && !s.IsEmpty && s.Quantity > 1;
                h.quantityText.gameObject.SetActive(showQty);
                if (showQty) h.quantityText.text = s.Quantity.ToString();
            }
            if (h.highlight != null)
            {
                h.highlight.enabled = (i == SelectedIndex);
                h.highlight.color = selectedColor;
            }
        }
    }
}
