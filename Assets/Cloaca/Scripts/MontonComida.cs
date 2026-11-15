using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Monton de comida robada en la camara del Rey Rata (pan, queso, migas). Con poca vida el rey corre a comer
/// de ellos para curarse; cada comida gasta una porcion. El jugador puede destrozarlos a golpes (sueltan carne).
/// </summary>
[RequireComponent(typeof(RatHealth))]
public class MontonComida : MonoBehaviour
{
    public static readonly List<MontonComida> Todos = new List<MontonComida>();

    public int porciones = 3;
    public Transform visual;
    public GameObject particulas;

    RatHealth vida;
    Vector3 escala0;
    int porciones0;
    float sacudida;

    public bool Disponible => porciones > 0 && vida != null && !vida.IsDead && gameObject.activeInHierarchy;

    void Awake()
    {
        vida = GetComponent<RatHealth>();
        if (visual != null) escala0 = visual.localScale;
        porciones0 = Mathf.Max(1, porciones);
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.isTrigger = true;
    }

    void OnEnable() { Todos.Add(this); vida.OnDeath += Destrozado; vida.OnHealthChanged += Golpe; }
    void OnDisable() { Todos.Remove(this); vida.OnDeath -= Destrozado; vida.OnHealthChanged -= Golpe; }

    void Golpe(float actual, float max) { sacudida = 0.35f; }

    void Update()
    {
        if (visual == null) return;
        if (sacudida > 0f)
        {
            sacudida -= Time.deltaTime;
            visual.localPosition = new Vector3(Mathf.Sin(Time.time * 70f), 0f, Mathf.Cos(Time.time * 61f)) * 0.12f * sacudida;
        }
    }

    /// <summary>El rey se come una porcion. Devuelve false si ya no queda.</summary>
    public bool Comer()
    {
        if (!Disponible) return false;
        porciones--;
        Ajustar();
        if (porciones <= 0)
        {
            if (particulas != null) Instantiate(particulas, transform.position + Vector3.up, Quaternion.identity);
            gameObject.SetActive(false);
        }
        return true;
    }

    void Ajustar()
    {
        if (visual != null) visual.localScale = escala0 * Mathf.Lerp(0.45f, 1f, porciones / (float)porciones0);
    }

    void Destrozado()
    {
        porciones = 0;
        if (particulas != null) Instantiate(particulas, transform.position + Vector3.up, Quaternion.identity);
        var drops = GetComponent<DropTable>();
        if (drops != null) drops.Drop(transform.position);
        Aviso.Mostrar("¡Destrozaste un monton de comida del rey!", new Color(1f, 0.85f, 0.4f), 2.5f);
        gameObject.SetActive(false);
    }
}
