using UnityEngine;

/// <summary>
/// Antorcha del jugador. Si tienes una en el inventario, se enciende y se apaga con su tecla (T por defecto)
/// o eligiendola en la barra rapida. Encendida, el humo ahuyenta a las abejas: no se acercan a menos
/// de Radio, ni siquiera furiosas. Va en la mano derecha del personaje.
/// </summary>
public class Antorcha : MonoBehaviour
{
    public static bool Encendida { get; private set; }
    public static Vector3 Posicion { get; private set; }
    public const float Radio = 12f;
    public const string ItemId = "Antorcha";

    public GameObject modeloMano;     // instancia de la antorcha encendida (se pega a la mano)
    public string huesoMano = "mixamorig:RightHand";
    public float escala = 0.8f;
    public float inclinacion = 20f;
    public AudioClip sonidoEncender, sonidoFuego;

    Transform mano;
    AudioSource fuego, fx;
    int teniaAntes;
    bool avisado;

    void Start()
    {
        foreach (var t in GetComponentsInChildren<Transform>()) if (t.name == huesoMano) { mano = t; break; }
        if (modeloMano != null)
        {
            // va suelta (no hija del hueso): en LateUpdate se coloca en la mano, siempre derecha
            modeloMano.transform.SetParent(transform, true);
            modeloMano.SetActive(false);
        }
        fuego = gameObject.AddComponent<AudioSource>();
        fuego.clip = sonidoFuego; fuego.loop = true; fuego.spatialBlend = 0.6f; fuego.playOnAwake = false;
        fx = gameObject.AddComponent<AudioSource>(); fx.playOnAwake = false;
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed += AlUsarRapido;
    }

    void OnDestroy()
    {
        if (QuickAccessBar.Instance != null) QuickAccessBar.Instance.OnQuickSlotUsed -= AlUsarRapido;
        Encendida = false;
    }

    void AlUsarRapido(int slot, string itemId) { if (itemId == ItemId) Alternar(); }

    int Cuantas => ControladorInventario.Instance != null ? ControladorInventario.Instance.GetTotalCount(ItemId) : 0;

    void Update()
    {
        int n = Cuantas;
        if (n > teniaAntes && !avisado)
        {
            avisado = true;
            Aviso.Mostrar("Tienes una antorcha: pulsa [" + Controles.NombreTecla(Controles.Accion.Antorcha) + "] para encenderla", new Color(1f, 0.7f, 0.3f), 4.5f);
        }
        teniaAntes = n;
        if (Encendida && n <= 0) Poner(false);
        if (Controles.Bajada(Controles.Accion.Antorcha)) Alternar();
        if (Encendida && modeloMano != null) Posicion = modeloMano.transform.position;
        if (fuego != null) fuego.volume = Encendida ? 0.45f * Ajustes.VolEfectos : 0f;
    }

    void LateUpdate()
    {
        if (!Encendida || modeloMano == null) return;
        // la llama siempre hacia arriba, un poco inclinada hacia delante; se sujeta por el tercio inferior
        var rot = Quaternion.LookRotation(transform.forward, Vector3.up) * Quaternion.Euler(inclinacion, 0f, 0f);
        Vector3 agarre = mano != null ? mano.position : transform.position + transform.right * 0.45f + Vector3.up * 1.1f;
        modeloMano.transform.SetPositionAndRotation(agarre - rot * Vector3.up * (0.33f * escala), rot);
        modeloMano.transform.localScale = Vector3.one * escala / Mathf.Max(0.001f, transform.lossyScale.x);
    }

    void Alternar()
    {
        if (!Encendida && Cuantas <= 0) return;
        Poner(!Encendida);
    }

    void Poner(bool on)
    {
        Encendida = on;
        if (modeloMano != null) modeloMano.SetActive(on);
        if (on)
        {
            if (sonidoEncender != null) fx.PlayOneShot(sonidoEncender, Ajustes.VolEfectos);
            if (fuego != null && sonidoFuego != null && !fuego.isPlaying) fuego.Play();
        }
        else if (fuego != null) fuego.Stop();
    }
}
