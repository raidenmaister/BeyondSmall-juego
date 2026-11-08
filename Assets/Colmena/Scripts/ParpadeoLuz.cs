using UnityEngine;

/// <summary>Hace parpadear una luz como una llama.</summary>
[RequireComponent(typeof(Light))]
public class ParpadeoLuz : MonoBehaviour
{
    public float variacion = 0.25f;
    public float velocidad = 9f;
    Light luz; float base0, semilla;

    void Awake() { luz = GetComponent<Light>(); base0 = luz.intensity; semilla = Random.value * 50f; }

    void Update()
    {
        float n = Mathf.PerlinNoise(Time.time * velocidad, semilla) - 0.5f;
        luz.intensity = base0 * (1f + n * 2f * variacion);
    }
}
