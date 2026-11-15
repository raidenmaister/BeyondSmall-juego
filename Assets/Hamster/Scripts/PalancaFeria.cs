using UnityEngine;

/// <summary>
/// Palanca de feria (palito de paleta con pomo rojo sobre una cajita). Se jala con Interactuar (E).
/// Hamsterlandia decide si se puede jalar (la de la funcion pide las otras tres) y que pasa despues.
/// </summary>
public class PalancaFeria : MonoBehaviour
{
    public string nombre = "la atraccion";
    public Transform brazo;
    public Renderer foco;              // focos de la cajita: rojo apagada, verde encendida
    public float radio = 3.2f;
    public float anguloApagada = -40f, anguloEncendida = 40f;
    [System.NonSerialized] public bool activada;

    float k;
    Transform jugador;
    MaterialPropertyBlock mpb;
    static readonly int Emision = Shader.PropertyToID("_EmissionColor");

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        mpb = new MaterialPropertyBlock();
        k = activada ? 1f : 0f;
        Pintar();
    }

    public void Poner(bool on, bool inmediato)
    {
        activada = on;
        if (inmediato) { k = on ? 1f : 0f; Pintar(); }
    }

    void Update()
    {
        float meta = activada ? 1f : 0f;
        if (!Mathf.Approximately(k, meta)) { k = Mathf.MoveTowards(k, meta, Time.deltaTime * 3f); Pintar(); }
        else if (foco != null && !activada) Pintar();   // parpadeo
        if (jugador == null || IntroDespertar.Activa || activada) return;
        var inv = ControladorInventario.Instance;
        if (inv != null && inv.IsOpen) return;
        Vector3 d = jugador.position - transform.position;
        if (Mathf.Abs(d.y) > 3f) return;
        d.y = 0f;
        if (d.magnitude > radio) return;
        var parque = Hamsterlandia.Instancia;
        if (parque == null) return;
        string motivo;
        if (!parque.PuedeJalar(this, out motivo)) { SurvivalUI.Indicacion(this, motivo); return; }
        SurvivalUI.Indicacion(this, "[" + Controles.NombreTecla(Controles.Accion.Interactuar) + "] Jalar la palanca de " + nombre);
        if (Controles.Bajada(Controles.Accion.Interactuar)) parque.Jalar(this);
    }

    void Pintar()
    {
        if (brazo != null) brazo.localRotation = Quaternion.Euler(Mathf.Lerp(anguloApagada, anguloEncendida, Mathf.SmoothStep(0f, 1f, k)), 0f, 0f);
        if (foco != null)
        {
            Color c = activada ? new Color(0.3f, 1f, 0.4f) * 3f : new Color(1f, 0.15f, 0.1f) * (1.2f + 1.2f * Mathf.Max(0f, Mathf.Sin(Time.time * 5f)));
            foco.GetPropertyBlock(mpb); mpb.SetColor(Emision, c); foco.SetPropertyBlock(mpb);
        }
    }
}
