using UnityEngine;

/// <summary>
/// Trucos para probar el juego (los crea GestorPartida en la escena del juego).
///   Ctrl + Shift + B : te da las baterias verdes que faltan para encender la cabina
///                      (las 2 que aun no estan en el mundo; nunca pasa de 4 entre puestas y en el inventario).
/// </summary>
public class Trucos : MonoBehaviour
{
    void Update()
    {
        if (Controles.Bloqueado || GestorPartida.Muerto || FinalJuego.Activo) return;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (ctrl && shift && Input.GetKeyDown(KeyCode.B)) DarBaterias();
    }

    /// <summary>Da hasta 2 baterias, sin pasar de las 4 que necesita la cabina. Devuelve cuantas dio.</summary>
    public static int DarBaterias()
    {
        var inv = ControladorInventario.Instance;
        if (inv == null) return 0;
        string id = MaquinaCrecimiento.ItemBateria;
        int puestas = MaquinaCrecimiento.Instancia != null ? MaquinaCrecimiento.Instancia.Colocadas : 0;
        int llevo = inv.GetTotalCount(id);
        int dar = Mathf.Clamp(MaquinaCrecimiento.Necesarias - puestas - llevo, 0, 2);
        if (dar <= 0)
        {
            SurvivalUI.Aviso("Truco: ya tienes todas las baterias", new Color(1f, 0.8f, 0.4f));
            return 0;
        }
        inv.AddItem(id, inv.GetIconByName(id), dar);
        SurvivalUI.Aviso("Truco: +" + dar + (dar == 1 ? " bateria verde" : " baterias verdes"), new Color(0.4f, 1f, 0.55f));
        Debug.Log("[Trucos] baterias dadas: " + dar + " (puestas " + puestas + ", llevabas " + llevo + ")");
        return dar;
    }
}
