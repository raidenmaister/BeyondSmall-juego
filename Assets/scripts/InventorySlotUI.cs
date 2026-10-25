using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class InventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler, IPointerClickHandler
{
    public enum SlotKind { Inventory, CraftInput, CraftResult }

    [Header("Referencias UI")]
    [SerializeField] private Image itemIcon;
    [SerializeField] private GameObject quantityBadge;
    [SerializeField] private TextMeshProUGUI quantityText;

    [Header("Configuración")]
    [Tooltip("Si es true, muestra '1' incluso cuando solo hay una unidad. Si es false, solo muestra el número cuando hay 2 o más.")]
    [SerializeField] private bool showQuantityForSingleItem = false;

    [Tooltip("Inventory = slot normal. CraftInput = slot donde se suelta un ítem para crear un arma. CraftResult = resultado (solo visual).")]
    [SerializeField] private SlotKind kind = SlotKind.Inventory;

    private string currentItemId;
    private int currentQuantity = 0;
    private InventorySlotUI originSlot;   // de qué slot del inventario salió el ítem (para devolverlo)

    // Estado global del arrastre
    private static InventorySlotUI dragSource;
    private static GameObject dragGhost;
    private static bool dropHandled;

    public bool IsEmpty => currentQuantity <= 0 || string.IsNullOrEmpty(currentItemId);
    public string ItemId => currentItemId;
    public int Quantity => currentQuantity;
    public Sprite CurrentIcon => itemIcon != null ? itemIcon.sprite : null;
    public SlotKind Kind => kind;

    public void Setup(Image icon, GameObject badge, TextMeshProUGUI text)
    {
        itemIcon = icon;
        quantityBadge = badge;
        quantityText = text;
        UpdateVisuals();
    }

    public void SetKind(SlotKind newKind) { kind = newKind; }

    public void SetItem(string itemId, Sprite icon, int quantity = 1)
    {
        currentItemId = itemId;
        currentQuantity = Mathf.Max(0, quantity);

        if (itemIcon != null)
        {
            itemIcon.sprite = icon;
        }

        UpdateVisuals();
    }

    public void AddQuantity(int amount)
    {
        if (amount <= 0) return;
        currentQuantity += amount;
        UpdateVisuals();
    }

    public void SetQuantity(int amount)
    {
        currentQuantity = Mathf.Max(0, amount);
        if (currentQuantity == 0)
        {
            Clear();
            return;
        }
        UpdateVisuals();
    }

    public void Clear()
    {
        currentItemId = null;
        currentQuantity = 0;
        originSlot = null;
        if (itemIcon != null)
        {
            itemIcon.sprite = null;
        }
        UpdateVisuals();
    }

    public void UpdateVisuals()
    {
        if (IsEmpty)
        {
            if (itemIcon != null)
            {
                itemIcon.gameObject.SetActive(false);
            }
            if (quantityBadge != null)
            {
                quantityBadge.SetActive(false);
            }
            if (quantityText != null)
            {
                quantityText.gameObject.SetActive(false);
                quantityText.text = "";
            }
        }
        else
        {
            if (itemIcon != null)
            {
                itemIcon.gameObject.SetActive(true);
                itemIcon.enabled = (itemIcon.sprite != null);
            }

            bool showBadge = currentQuantity > 1 || showQuantityForSingleItem;

            if (quantityBadge != null)
            {
                quantityBadge.SetActive(showBadge);
            }

            if (quantityText != null)
            {
                quantityText.gameObject.SetActive(showBadge);
                quantityText.text = showBadge ? currentQuantity.ToString() : "";
            }
        }
    }

    // ------------------------------------------------------------------
    //  Arrastrar y soltar
    // ------------------------------------------------------------------

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (kind == SlotKind.CraftResult || IsEmpty || itemIcon == null) return;

        dragSource = this;
        dropHandled = false;

        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvas = canvas.rootCanvas;

        dragGhost = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        dragGhost.transform.SetParent(canvas.transform, false);
        dragGhost.transform.SetAsLastSibling();

        var cg = dragGhost.GetComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        var img = dragGhost.GetComponent<Image>();
        img.sprite = itemIcon.sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;

        // Mismo tamaño en pantalla que el icono original
        var rt = (RectTransform)dragGhost.transform;
        float ratio = itemIcon.transform.lossyScale.x / Mathf.Max(0.0001f, canvas.transform.lossyScale.x);
        rt.sizeDelta = itemIcon.rectTransform.rect.size * ratio;
        rt.position = eventData.position;

        var c = itemIcon.color; c.a = 0.35f; itemIcon.color = c;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragGhost != null && dragSource == this)
        {
            dragGhost.transform.position = eventData.position;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (dragSource != this) return;

        if (dragGhost != null) Destroy(dragGhost);
        dragGhost = null;

        if (itemIcon != null)
        {
            var c = itemIcon.color; c.a = 1f; itemIcon.color = c;
        }

        // Un ítem sacado de un slot de crafteo y soltado fuera de cualquier slot vuelve a la mochila
        if (!dropHandled && kind == SlotKind.CraftInput && !IsEmpty)
        {
            ReturnToInventory();
        }

        dragSource = null;
    }

    public void OnDrop(PointerEventData eventData)
    {
        var src = dragSource;
        if (src == null || src == this || src.IsEmpty) return;

        dropHandled = true;

        switch (kind)
        {
            case SlotKind.CraftResult:
                break; // el resultado no acepta ítems

            case SlotKind.CraftInput:
                DropOntoCraftInput(src);
                break;

            default:
                DropOntoInventory(src);
                break;
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Clic derecho sobre un slot de crafteo: devolver el ítem a la mochila
        if (eventData.button == PointerEventData.InputButton.Right && kind == SlotKind.CraftInput && !IsEmpty)
        {
            ReturnToInventory();
        }
    }

    private void DropOntoCraftInput(InventorySlotUI src)
    {
        if (src.kind == SlotKind.CraftInput)
        {
            // Intercambiar entre los dos slots de crafteo
            SwapWith(src);
            return;
        }

        // Viene del inventario: se mueve UNA unidad al slot de crafteo
        string id = src.currentItemId;
        Sprite icon = src.CurrentIcon;

        if (!IsEmpty)
        {
            // Si ya hay algo aquí, primero se devuelve a la mochila
            if (!ReturnToInventory()) return;
        }

        SetItem(id, icon, 1);
        originSlot = src;
        src.SetQuantity(src.currentQuantity - 1);
    }

    private void DropOntoInventory(InventorySlotUI src)
    {
        if (src.kind == SlotKind.CraftInput)
        {
            // Devolver del crafteo al slot soltado
            string id = src.currentItemId;
            Sprite icon = src.CurrentIcon;

            if (IsEmpty) SetItem(id, icon, 1);
            else if (string.Equals(currentItemId, id, System.StringComparison.OrdinalIgnoreCase)) AddQuantity(1);
            else
            {
                if (ControladorInventario.Instance == null || !ControladorInventario.Instance.AddItem(id, icon, 1)) return;
            }
            src.Clear();
            return;
        }

        // Inventario -> inventario
        if (IsEmpty)
        {
            SetItem(src.currentItemId, src.CurrentIcon, src.currentQuantity);
            src.Clear();
        }
        else if (string.Equals(currentItemId, src.currentItemId, System.StringComparison.OrdinalIgnoreCase))
        {
            AddQuantity(src.currentQuantity);
            src.Clear();
        }
        else
        {
            SwapWith(src);
        }
    }

    private void SwapWith(InventorySlotUI other)
    {
        string idA = currentItemId; int qA = currentQuantity; Sprite iA = CurrentIcon; var oA = originSlot;
        string idB = other.currentItemId; int qB = other.currentQuantity; Sprite iB = other.CurrentIcon; var oB = other.originSlot;

        if (string.IsNullOrEmpty(idB)) Clear(); else SetItem(idB, iB, qB);
        if (string.IsNullOrEmpty(idA)) other.Clear(); else other.SetItem(idA, iA, qA);

        originSlot = oB;
        other.originSlot = oA;
    }

    /// <summary>Devuelve el ítem del slot de crafteo a la mochila. Devuelve false si no hay espacio.</summary>
    private bool ReturnToInventory()
    {
        if (IsEmpty) return true;

        string id = currentItemId;
        Sprite icon = CurrentIcon;

        // 1) A su slot de origen, si sigue libre o tiene el mismo ítem
        if (originSlot != null && originSlot.kind == SlotKind.Inventory)
        {
            if (originSlot.IsEmpty)
            {
                originSlot.SetItem(id, icon, 1);
                Clear();
                return true;
            }
            if (string.Equals(originSlot.currentItemId, id, System.StringComparison.OrdinalIgnoreCase))
            {
                originSlot.AddQuantity(1);
                Clear();
                return true;
            }
        }

        // 2) A cualquier slot disponible
        if (ControladorInventario.Instance != null && ControladorInventario.Instance.AddItem(id, icon, 1))
        {
            Clear();
            return true;
        }

        return false;
    }
}
