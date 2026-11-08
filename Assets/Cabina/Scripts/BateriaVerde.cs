using UnityEngine;

/// <summary>Bateria verde recogible: gira despacio y su luz late, para verla en cuevas oscuras.</summary>
public class BateriaVerde : MonoBehaviour
{
    public Transform visual;
    public Light luz;
    float i0, fase;

    void Start()
    {
        if (luz != null) i0 = luz.intensity;
        fase = Random.value * 10f;
    }

    void Update()
    {
        if (visual != null) visual.Rotate(0f, 35f * Time.deltaTime, 0f, Space.World);
        if (luz != null) luz.intensity = i0 * (0.75f + 0.25f * Mathf.Sin(Time.time * 2.4f + fase));
    }
}
