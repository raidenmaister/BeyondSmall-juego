using UnityEngine;

/// <summary>
/// Ambiente de dia (pajaros y viento entre los arboles). Se apaga suavemente al
/// anochecer para dejar paso a los grillos del ciclo dia/noche.
/// </summary>
public class AmbientSound : MonoBehaviour
{
    public AudioClip ambienteDia;
    [Range(0f, 1f)] public float volumenDia = 0.45f;
    [Range(0f, 1f)] public float volumenNoche = 0.05f;

    AudioSource fuente;

    void Awake()
    {
        fuente = gameObject.AddComponent<AudioSource>();
        fuente.clip = ambienteDia;
        fuente.loop = true;
        fuente.spatialBlend = 0f;
        fuente.playOnAwake = false;
        fuente.volume = volumenDia;
    }

    void Start()
    {
        if (ambienteDia == null) return;
        fuente.time = Random.Range(0f, ambienteDia.length * 0.8f);
        fuente.Play();
    }

    void Update()
    {
        float noche = DayNightCycle.Instance != null ? DayNightCycle.Instance.Noche : 0f;
        float objetivo = Mathf.Lerp(volumenDia, volumenNoche, noche) * (1f - Mathf.Clamp01(DayNightCycle.Encierro)) * Ajustes.VolAmbiente;
        fuente.volume = Mathf.MoveTowards(fuente.volume, objetivo, 0.3f * Time.deltaTime);
    }
}
