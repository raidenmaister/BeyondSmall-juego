using UnityEngine;

/// <summary>
/// El loot que suelta un bicho "nace" con vida (Guinxu, Flatworld): crece desde cero pasandose de tamaño,
/// se estira mientras vuela y se aplasta al tocar el suelo con un poco de polvo.
/// Un destello blanco breve al aparecer ayuda a verlo (claridad visual).
/// </summary>
public class AparecerLoot : MonoBehaviour
{
    Vector3 escala;
    float t;
    bool aterrizado;
    Rigidbody rb;
    float velAnterior;

    void Start()
    {
        escala = transform.localScale;
        rb = GetComponent<Rigidbody>();
        transform.localScale = escala * 0.01f;
        Juice.Chispas(transform.position, new Color(1f, 0.95f, 0.7f), 6, 2f);
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t < 0.28f)
        {
            // crecer con rebote (se pasa de tamaño y vuelve)
            float k = t / 0.28f;
            float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.35f;
            float base_ = Mathf.SmoothStep(0.01f, 1f, Mathf.Min(1f, k * 1.6f));
            transform.localScale = escala * base_ * s;
            return;
        }
        if (!aterrizado)
        {
            transform.localScale = escala;
            float vy = rb != null ? rb.linearVelocity.y : 0f;
            // tocar suelo = la velocidad hacia abajo se corta de golpe
            if (rb == null || (velAnterior < -1f && vy > velAnterior * 0.3f) || t > 2.5f)
            {
                aterrizado = true;
                if (rb != null && velAnterior < -1f)
                {
                    Juice.Aplastar(transform, Mathf.Clamp(-velAnterior * 0.05f, 0.12f, 0.3f));
                    Juice.Polvo(transform.position, 0.4f, 4);
                }
                Destroy(this, 1f);
            }
            velAnterior = vy;
        }
    }

    void OnDestroy()
    {
        if (!aterrizado && t < 0.28f && this != null && transform != null) transform.localScale = escala;
    }
}
