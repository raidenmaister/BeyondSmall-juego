using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dificil: un bicho que se quedo con parte de tus cosas al morir. Lleva tu mochila a la espalda,
/// sale en el mapa y en la brujula, y si lo matas suelta la mochila con lo que llevaba.
/// (Las hormigas y las abejas no cargan nada: lo llevan directo a su nido, ver InventarioMuerte.)
/// </summary>
public class Ladron : MonoBehaviour
{
    public static readonly List<Ladron> Todos = new List<Ladron>();

    public List<SlotGuardado> items = new List<SlotGuardado>();
    RatHealth vida;
    GameObject modelo;

    public string Nombre => vida != null ? vida.EnemyName : name;

    public static Ladron Asignar(RatHealth rh, List<SlotGuardado> items)
    {
        var l = rh.GetComponent<Ladron>();
        if (l == null) l = rh.gameObject.AddComponent<Ladron>();
        l.items.AddRange(items);
        return l;
    }

    void Awake()
    {
        vida = GetComponent<RatHealth>();
    }

    void OnEnable()
    {
        Todos.Add(this);
        if (vida == null) vida = GetComponent<RatHealth>();
        if (vida != null) vida.OnDeath += AlMorir;
    }

    void OnDisable()
    {
        Todos.Remove(this);
        if (vida != null) vida.OnDeath -= AlMorir;
    }

    void Start()
    {
        // la mochila robada a la espalda (pequeña)
        var rs = GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(transform.position, Vector3.one);
        bool hay = false;
        foreach (var r in rs) { if (r is ParticleSystemRenderer) continue; if (!hay) { b = r.bounds; hay = true; } else b.Encapsulate(r.bounds); }
        modelo = Mochila.Modelo(transform, 0.55f);
        modelo.transform.localScale = Vector3.one * 0.55f / Mathf.Max(0.001f, transform.lossyScale.x);
        modelo.transform.position = new Vector3(transform.position.x, b.max.y - 0.1f, transform.position.z);
    }

    void AlMorir()
    {
        Soltar();
    }

    /// <summary>Deja la mochila en el suelo (al morir o si el bicho se va).</summary>
    public void Soltar()
    {
        if (items.Count == 0) return;
        Vector3 p = transform.position;
        if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
        Mochila.Crear(p, Mochila.Tipo.Botin, "Lo que robo " + Nombre, new List<SlotGuardado>(items));
        items.Clear();
        SurvivalUI.Aviso("¡" + Nombre + " solto tus cosas!", new Color(0.95f, 0.85f, 0.55f));
        if (modelo != null) Destroy(modelo);
        Destroy(this);
    }
}
