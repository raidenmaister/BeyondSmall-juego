using System.Collections.Generic;
using UnityEngine;

public class ControladorInventario : MonoBehaviour
{
    public static ControladorInventario Instance { get; private set; }

    [Header("Referencias de UI")]
    [Tooltip("Arrastra aquí el objeto raíz del inventario (InventoryBackground)")]
    public GameObject inventarioUI;

    [Header("Slots del Inventario")]
    [Tooltip("Contenedor de los slots (GridContainer)")]
    public Transform gridContainer;
    public List<InventorySlotUI> slots = new List<InventorySlotUI>();

    [Header("Base de Datos de Iconos")]
    [Tooltip("Iconos cargados desde la carpeta Assets/iconos")]
    public List<Sprite> iconDatabase = new List<Sprite>();

    [Header("Crafteo de armas")]
    [Tooltip("Slots donde se arrastran los dos ítems para crear un arma (2 entradas).")]
    public List<InventorySlotUI> craftInputSlots = new List<InventorySlotUI>();
    [Tooltip("Slot de resultado (solo visual por ahora).")]
    public InventorySlotUI craftResultSlot;

    public bool IsOpen => inventarioUI != null && inventarioUI.activeSelf;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        InitializeSlotsIfNeeded();
    }

    void Start()
    {
        // Asegura que el inventario comience cerrado al iniciar la partida
        if (inventarioUI != null)
        {
            inventarioUI.SetActive(false);
        }
    }

    void Update()
    {
        // Al presionar la tecla 'I'
        if (Input.GetKeyDown(KeyCode.I))
        {
            ToggleInventario();
        }
    }

    public void ToggleInventario()
    {
        if (inventarioUI == null) return;

        // Cambia el estado (si está abierto se cierra, si está cerrado se abre)
        bool estadoActual = inventarioUI.activeSelf;
        bool nuevoEstado = !estadoActual;

        inventarioUI.SetActive(nuevoEstado);

        // Control del cursor del ratón
        Cursor.visible = nuevoEstado;
        Cursor.lockState = nuevoEstado ? CursorLockMode.None : CursorLockMode.Locked;

        // Pausa o reanuda el juego
        Time.timeScale = nuevoEstado ? 0f : 1f;
    }

    public void InitializeSlotsIfNeeded()
    {
        if (slots == null || slots.Count == 0)
        {
            slots = new List<InventorySlotUI>();
            if (gridContainer == null && inventarioUI != null)
            {
                var gc = inventarioUI.transform.Find("LeftPage_Inventory/GridContainer");
                if (gc != null) gridContainer = gc;
            }

            if (gridContainer != null)
            {
                var slotComps = gridContainer.GetComponentsInChildren<InventorySlotUI>(true);
                slots.AddRange(slotComps);
            }
        }
    }

    /// <summary>
    /// Añade un item al inventario. Si ya existe uno del mismo tipo, incrementa su cantidad.
    /// Si no existe, lo coloca en el primer slot libre disponible.
    /// </summary>
    public bool AddItem(string itemId, Sprite icon = null, int amount = 1)
    {
        if (string.IsNullOrEmpty(itemId)) return false;

        InitializeSlotsIfNeeded();

        string normalizedId = NormalizeItemName(itemId);
        if (icon == null)
        {
            icon = GetIconByName(normalizedId);
        }

        // 1. Si ya había uno de su tipo, incrementar en uno (o amount) la cantidad
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot != null && !slot.IsEmpty && string.Equals(slot.ItemId, normalizedId, System.StringComparison.OrdinalIgnoreCase))
            {
                slot.AddQuantity(amount);
                Debug.Log($"[ControladorInventario] Incrementado item '{normalizedId}' a cantidad {slot.Quantity} en slot {i}.");
                return true;
            }
        }

        // 2. Si es nuevo, buscar el primer slot disponible
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot != null && slot.IsEmpty)
            {
                slot.SetItem(normalizedId, icon, amount);
                Debug.Log($"[ControladorInventario] Item '{normalizedId}' colocado en slot {i}.");
                return true;
            }
        }

        Debug.LogWarning("[ControladorInventario] No hay slots disponibles en el inventario.");
        return false;
    }

    public int GetItemCount(string itemId)
    {
        string normalizedId = NormalizeItemName(itemId);
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            if (slot != null && !slot.IsEmpty && string.Equals(slot.ItemId, normalizedId, System.StringComparison.OrdinalIgnoreCase))
            {
                return slot.Quantity;
            }
        }
        return 0;
    }

    public Sprite GetIconByName(string nameOrId)
    {
        if (string.IsNullOrEmpty(nameOrId)) return null;

        string normalized = NormalizeItemName(nameOrId);

        if (iconDatabase != null)
        {
            // Búsqueda exacta primero
            for (int i = 0; i < iconDatabase.Count; i++)
            {
                var s = iconDatabase[i];
                if (s != null && string.Equals(s.name, normalized, System.StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }

            // Búsqueda parcial si no encontró exacta
            for (int i = 0; i < iconDatabase.Count; i++)
            {
                var s = iconDatabase[i];
                if (s != null)
                {
                    if (s.name.IndexOf(normalized, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        normalized.IndexOf(s.name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return s;
                    }
                }
            }
        }

        return null;
    }

    public static string NormalizeItemName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return "Item";
        string lower = rawName.ToLower();

        if (lower.Contains("carne") || lower.Contains("meat")) return "Carne";
        if (lower.Contains("chincheta") && lower.Contains("doblada")) return "Chincheta_Doblada";
        if (lower.Contains("tornillo")) return "Tornillo";
        if (lower.Contains("tuerca")) return "Tuerca";
        if (lower == "tela" || lower.StartsWith("tela_")) return "Tela";
        // Las chinchetas del suelo se llaman "plaggy_cc0-pin-569_N": son chinchetas, NO el broche (pinza).
        if (lower.Contains("chincheta") || lower.Contains("plaggy") || lower.Contains("pushpin") ||
            lower.Contains("thumbtack") || lower.Contains("pin-569")) return "Chincheta";
        if (lower.Contains("broche") || lower.Contains("binder")) return "broche";
        if (lower.Contains("arma_clip")) return "Arma_Clip";
        if (lower.Contains("clip")) return "Clips";
        if (lower.Contains("eraser") || lower.Contains("borrador")) return "Borrador";
        if (lower.Contains("hair_tie") || lower.Contains("liga")) return "Ligas";
        if (lower.Contains("alambre")) return "Alambre";
        if (lower.Contains("cinta_plastico")) return "Cinta_Plastico";
        if (lower.Contains("cinta_tela") || lower.Contains("cinta")) return "Cinta_Tela";
        if (lower.Contains("lapiz")) return "Lapiz";
        if (lower.Contains("mazo")) return "Mazo";
        if (lower.Contains("regla")) return "Regla";
        if (lower.Contains("sacapuntas")) return "Sacapuntas";
        if (lower.Contains("lanza")) return "Lanza";
        if (lower.Contains("hacha")) return "Hacha";
        if (lower.Contains("garra")) return "Garra";

        int lastUnderscore = rawName.LastIndexOf('_');
        if (lastUnderscore > 0)
        {
            return rawName.Substring(0, lastUnderscore);
        }

        return rawName;
    }
}
