using UnityEngine;

public class ControladorInventario : MonoBehaviour
{
    [Header("Referencias de UI")]
    [Tooltip("Arrastra aquí el objeto raíz del inventario (InventoryBackground)")]
    public GameObject inventarioUI;

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
}