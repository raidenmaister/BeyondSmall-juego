using UnityEngine;

/// <summary>
/// Larva de abeja en su celda de la guarderia: se retuerce un poco. Si el jugador la mata, toda la colmena se enfurece.
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class LarvaAbeja : MonoBehaviour
{
    public AudioClip sonidoAplastar;

    RatHealth vida;
    Quaternion rot0; Vector3 esc0; float fase;
    bool muerta;

    void Awake()
    {
        vida = GetComponent<RatHealth>();
        rot0 = transform.localRotation; esc0 = transform.localScale; fase = Random.value * 10f;
    }

    void OnEnable() { vida.OnDeath += Morir; }
    void OnDisable() { vida.OnDeath -= Morir; }

    void Update()
    {
        if (muerta) return;
        float t = Time.time * 1.7f + fase;
        transform.localRotation = rot0 * Quaternion.Euler(0f, Mathf.Sin(t) * 10f, Mathf.Sin(t * 0.7f) * 6f);
        float k = 1f + 0.06f * Mathf.Sin(t * 2.1f);
        transform.localScale = new Vector3(esc0.x / Mathf.Sqrt(k), esc0.y / Mathf.Sqrt(k), esc0.z * k);
    }

    void Morir()
    {
        if (muerta) return;
        muerta = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        if (sonidoAplastar != null) AudioSource.PlayClipAtPoint(sonidoAplastar, transform.position, Ajustes.VolEfectos);
        if (Colmena.Instancia != null) Colmena.Instancia.LarvaMuerta();
        StartCoroutine(Aplastar());
    }

    System.Collections.IEnumerator Aplastar()
    {
        Vector3 e = transform.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
        {
            transform.localScale = new Vector3(e.x * (1f + t * 0.5f), e.y * (1f - t * 0.8f), e.z * (1f + t * 0.3f));
            yield return null;
        }
        yield return new WaitForSeconds(2f);
        Destroy(gameObject);
    }
}
