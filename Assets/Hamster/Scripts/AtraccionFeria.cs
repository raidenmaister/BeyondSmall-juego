using UnityEngine;

/// <summary>
/// Atraccion de Hamsterlandia (carrusel de tapitas, noria, caja musical, carpa, rueda de Bolita, foquitos).
/// Encendida: gira, sus figuras suben y bajan o se quedan derechas, y sus foquitos se prenden en cadena.
/// Apagada: quieta y con los focos muertos (de vez en cuando uno parpadea, para dar miedito).
/// </summary>
public class AtraccionFeria : MonoBehaviour
{
    public enum Tipo { Carrusel, Noria, CajaMusical, Carpa, Rueda, Focos }

    public Tipo tipo;
    public Transform giro;                 // lo que da vueltas
    public Vector3 eje = Vector3.up;       // eje local de giro
    public float velocidad = 30f;          // grados/s encendida
    public Transform[] figuras;            // caballitos (suben y bajan) o canastillas (se quedan derechas)
    public Transform bailarina;            // caja musical: gira sobre si misma
    public Renderer[] focos;               // se encienden en cadena
    public Color[] coloresFocos = { new Color(1f, 0.85f, 0.4f) };
    public Light[] luces;
    [System.NonSerialized] public bool encendida;
    [System.NonSerialized] public float extra = 1f;   // la funcion lo sube (todo mas rapido y brillante)
    [System.NonSerialized] public float velocidadManual = -1f;   // la rueda: la mueve Bolita

    float k, angulo;
    float[] intensidad;
    Vector3[] posFiguras;
    MaterialPropertyBlock mpb;
    static readonly int Emision = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        mpb = new MaterialPropertyBlock();
        if (luces != null)
        {
            intensidad = new float[luces.Length];
            for (int i = 0; i < luces.Length; i++) if (luces[i] != null) { intensidad[i] = luces[i].intensity; luces[i].enabled = false; }
        }
        if (figuras != null) { posFiguras = new Vector3[figuras.Length]; for (int i = 0; i < figuras.Length; i++) if (figuras[i] != null) posFiguras[i] = figuras[i].localPosition; }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        k = Mathf.MoveTowards(k, encendida ? 1f : 0f, dt * 0.8f);
        float vel = velocidadManual >= 0f ? velocidadManual : velocidad * Mathf.SmoothStep(0f, 1f, k) * extra;
        angulo += vel * dt;
        if (giro != null && Mathf.Abs(vel) > 0.0001f) giro.localRotation = Quaternion.AngleAxis(angulo, eje);

        if (figuras != null)
            for (int i = 0; i < figuras.Length; i++)
            {
                var f = figuras[i]; if (f == null) continue;
                if (tipo == Tipo.Noria) f.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 1.7f + i) * 6f * k);   // derechas y meciendose
                else f.localPosition = posFiguras[i] + Vector3.up * Mathf.Sin(angulo * Mathf.Deg2Rad * 3f + i * 1.3f) * 0.6f * k;
            }
        if (bailarina != null && k > 0f) bailarina.Rotate(0f, 120f * k * dt * extra, 0f, Space.Self);

        if (focos != null && focos.Length > 0)
        {
            for (int i = 0; i < focos.Length; i++)
            {
                var r = focos[i]; if (r == null) continue;
                Color c = coloresFocos[i % coloresFocos.Length];
                float on;
                if (k > 0.01f)
                {
                    // se van prendiendo en cadena y luego persiguen la luz alrededor
                    float llegada = Mathf.Clamp01(k * 1.6f - i / (float)focos.Length * 0.6f);
                    float carrera = 0.65f + 0.35f * Mathf.Sin(Time.time * 6f * extra - i * 0.9f);
                    on = llegada * carrera * (2.4f + 1.6f * (extra - 1f));
                }
                else on = Random.value < 0.002f ? 1.5f : 0.04f;
                r.GetPropertyBlock(mpb); mpb.SetColor(Emision, c * on); r.SetPropertyBlock(mpb);
            }
        }
        if (luces != null)
            for (int i = 0; i < luces.Length; i++)
            {
                var l = luces[i]; if (l == null) continue;
                l.enabled = k > 0.01f;
                l.intensity = intensidad[i] * k * (0.85f + 0.15f * Mathf.Sin(Time.time * 7f + i)) * Mathf.Min(extra, 1.6f);
            }
    }
}
