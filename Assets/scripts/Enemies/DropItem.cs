using System.Collections;
using UnityEngine;

/// <summary>
/// Objeto soltado por un enemigo: cae, se asienta y queda flotando un poco sobre el suelo
/// (por encima de los parches de musgo) girando suavemente para que se vea y se pueda recoger.
/// </summary>
public class DropItem : MonoBehaviour
{
    public float hoverHeight = 0.45f;
    public float spinSpeed = 35f;

    Rigidbody rb;
    bool settled;
    float baseY, t;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        t = Random.value * 6f;
        StartCoroutine(Settle());
    }

    IEnumerator Settle()
    {
        yield return new WaitForSeconds(0.9f);
        if (rb != null) rb.isKinematic = true;

        // buscar suelo bajo el objeto (ignorando sus propios colliders)
        float ground = 0f;
        var hits = Physics.RaycastAll(transform.position + Vector3.up * 2f, Vector3.down, 12f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MinValue;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.point.y > best && h.point.y < transform.position.y + 1.5f) best = h.point.y;
        }
        if (best > float.MinValue) ground = best;

        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            transform.position += Vector3.up * (ground + hoverHeight - b.min.y);
        }
        baseY = transform.position.y;
        settled = true;
    }

    void Update()
    {
        if (!settled) return;
        t += Time.deltaTime;
        var p = transform.position;
        p.y = baseY + Mathf.Sin(t * 2.2f) * 0.07f;
        transform.position = p;
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
    }
}
