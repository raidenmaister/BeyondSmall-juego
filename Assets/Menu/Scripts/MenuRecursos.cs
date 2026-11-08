using TMPro;
using UnityEngine;

/// <summary>Recursos del menu (fuentes, logo, fondos, sonidos). Vive en Resources para que el menu de pausa lo encuentre.</summary>
[CreateAssetMenu(menuName = "Beyond Small/Recursos del menu")]
public class MenuRecursos : ScriptableObject
{
    public TMP_FontAsset fuenteMenu;      // Barlow Condensed: opciones del menu
    public TMP_FontAsset fuenteTexto;     // Barlow: textos y valores
    public Sprite logo;
    public Sprite[] fondos;
    [Tooltip("Lugar de cada fondo (se muestra abajo a la derecha)")]
    public string[] nombresFondos;
    public AudioClip musica;
    public AudioClip sonidoMover, sonidoAceptar, sonidoAtras, sonidoAjuste;
    public Color acento = new Color(0.27f, 0.86f, 0.78f);
    public string escenaJuego = "Class_rom_exemple";
    public string escenaMenu = "MenuPrincipal";
}
