using UnityEngine;

/// <summary>Las larvas de la guarderia se retuercen un poco (un solo componente anima a todas sus hijas).</summary>
public class LarvasAnimadas : MonoBehaviour
{
    public float velocidad = 1.6f;
    public float angulo = 12f;

    Transform[] larvas;
    Quaternion[] baseRot;
    Vector3[] baseEsc;
    float[] fase;

    void Start()
    {
        int n = transform.childCount;
        larvas = new Transform[n]; baseRot = new Quaternion[n]; baseEsc = new Vector3[n]; fase = new float[n];
        for (int i = 0; i < n; i++)
        {
            larvas[i] = transform.GetChild(i);
            baseRot[i] = larvas[i].localRotation;
            baseEsc[i] = larvas[i].localScale;
            fase[i] = Random.value * 10f;
        }
    }

    void Update()
    {
        float t = Time.time * velocidad;
        for (int i = 0; i < larvas.Length; i++)
        {
            if (larvas[i] == null) continue;
            float s = Mathf.Sin(t + fase[i]);
            larvas[i].localRotation = baseRot[i] * Quaternion.Euler(0f, s * angulo, Mathf.Sin(t * 0.7f + fase[i]) * angulo * 0.5f);
            // respiran: se estiran y encogen
            float k = 1f + 0.05f * Mathf.Sin(t * 1.9f + fase[i]);
            larvas[i].localScale = new Vector3(baseEsc[i].x / Mathf.Sqrt(k), baseEsc[i].y / Mathf.Sqrt(k), baseEsc[i].z * k);
        }
    }
}
