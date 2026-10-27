using UnityEngine;

/// <summary>
/// Fogata: luz que parpadea, sonido de fuego y estacion de cocina.
/// Acercate y pulsa C para cocinar la carne cruda del inventario: se pone un trozo
/// en un palo sobre el fuego, se dora poco a poco y al terminar se obtiene
/// "Carne_Cocinada". Sigue cocinando mientras tengas carne y no te alejes
/// (pulsa C otra vez para parar).
/// </summary>
public class Campfire : MonoBehaviour
{
    [Header("Cocina")]
    public float radioInteraccion = 4.5f;
    public KeyCode teclaCocinar = KeyCode.C;
    [Tooltip("Segundos para cocinar un trozo de carne")]
    public float tiempoCoccion = 3.5f;
    public Sprite iconoCarneCocinada;

    [Header("Fuego")]
    public Light luz;
    public float intensidadLuz = 9f;
    public AudioSource sonido;
    [Tooltip("Carne en el palo que se ve mientras cocinas")]
    public Renderer carneAsando;
    public Color colorCrudo = new Color(0.85f, 0.25f, 0.22f);
    public Color colorCocinado = new Color(0.42f, 0.2f, 0.08f);

    Transform jugador;
    bool cocinando;
    float progreso;
    Vector3 posLuz;
    float semilla;
    MaterialPropertyBlock mpb;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public bool Cocinando => cocinando;

    void Start()
    {
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        if (luz != null) posLuz = luz.transform.localPosition;
        semilla = Random.value * 50f;
        mpb = new MaterialPropertyBlock();
        if (carneAsando != null) carneAsando.gameObject.SetActive(false);
    }

    void Update()
    {
        Parpadeo();
        if (jugador == null) return;

        Vector3 d = jugador.position - transform.position; d.y = 0f;
        bool cerca = d.magnitude <= radioInteraccion;
        var inv = ControladorInventario.Instance;
        bool inventarioAbierto = inv != null && inv.IsOpen;
        int crudas = inv != null ? inv.GetTotalCount("Carne") : 0;

        if (cocinando && (!cerca || crudas == 0)) Detener();

        if (cerca && !inventarioAbierto)
        {
            if (cocinando)
                SurvivalUI.Indicacion(this, $"Cocinando carne... {Mathf.FloorToInt(progreso / tiempoCoccion * 100f)}%   (quedan {crudas})   [{teclaCocinar}] Parar");
            else if (crudas > 0)
                SurvivalUI.Indicacion(this, $"[{teclaCocinar}] Cocinar carne ({crudas})");
            else
                SurvivalUI.Indicacion(this, "Fogata: consigue carne cazando bichos para cocinarla");

            if (Input.GetKeyDown(teclaCocinar))
            {
                if (cocinando) Detener();
                else if (crudas > 0) Empezar();
            }
        }

        if (cocinando)
        {
            progreso += Time.deltaTime;
            PintarCarne(progreso / tiempoCoccion);
            if (progreso >= tiempoCoccion)
            {
                if (inv != null && inv.RemoveItem("Carne", 1))
                {
                    inv.AddItem("Carne_Cocinada", iconoCarneCocinada, 1);
                    SurvivalUI.Aviso("¡Carne cocinada!", new Color(1f, 0.8f, 0.4f));
                }
                progreso = 0f;
                PintarCarne(0f);
            }
        }
    }

    public void Empezar()
    {
        cocinando = true;
        progreso = 0f;
        if (carneAsando != null) carneAsando.gameObject.SetActive(true);
        PintarCarne(0f);
    }

    public void Detener()
    {
        cocinando = false;
        progreso = 0f;
        if (carneAsando != null) carneAsando.gameObject.SetActive(false);
    }

    void PintarCarne(float t)
    {
        if (carneAsando == null) return;
        carneAsando.GetPropertyBlock(mpb);
        mpb.SetColor(BaseColor, Color.Lerp(colorCrudo, colorCocinado, Mathf.SmoothStep(0f, 1f, t)));
        carneAsando.SetPropertyBlock(mpb);
        // gira lentamente sobre el fuego
        carneAsando.transform.parent.Rotate(Vector3.up, 40f * Time.deltaTime, Space.Self);   // el palo es el eje Y local
    }

    void Parpadeo()
    {
        if (luz == null) return;
        float t = Time.time + semilla;
        float f = 0.75f + 0.3f * Mathf.PerlinNoise(t * 5f, 0.3f) + 0.12f * Mathf.PerlinNoise(t * 17f, 2.1f);
        luz.intensity = intensidadLuz * f;
        luz.transform.localPosition = posLuz + new Vector3(Mathf.PerlinNoise(t * 3f, 4f) - 0.5f, Mathf.PerlinNoise(t * 4f, 6f) - 0.5f, Mathf.PerlinNoise(t * 3.5f, 8f) - 0.5f) * 0.25f;
    }
}
