using UnityEngine;

public class WorldItem : MonoBehaviour
{
    [Header("Datos del Item")]
    [Tooltip("Identificador único del item (ej. Clips, Borrador, Ligas, broche)")]
    public string itemId = "Item";

    [Tooltip("Nombre a mostrar en la interfaz")]
    public string displayName = "Item";

    [Tooltip("Icono del item que aparecerá en el inventario")]
    public Sprite itemIcon;

    [Tooltip("Cantidad que otorga al recogerlo")]
    public int quantity = 1;
}
