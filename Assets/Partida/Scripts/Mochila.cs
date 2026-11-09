using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bolsa con objetos del jugador en el mundo: la mochila que queda donde moriste (normal) o el botin
/// que los bichos se llevaron a su nido (dificil). Nunca desaparece. Se recoge con Interactuar (E):
/// devuelve todo lo que quepa al inventario. Sale en el mapa y en la brujula.
/// </summary>
public class Mochila : MonoBehaviour
{
    public enum Tipo { Mochila, BotinHormigas, BotinAbejas, Botin }

    public static readonly List<Mochila> Todas = new List<Mochila>();

    public Tipo tipo;
    public string titulo = "Tu mochila";
    public List<SlotGuardado> items = new List<SlotGuardado>();

    const float RadioRecoger = 4.5f;
    Transform jugador;
    Light luz;
    float fase;

    public int Cantidad { get { int n = 0; foreach (var s in items) n += s.cantidad; return n; } }

    // ================================================================== creacion
    public static Mochila Crear(Vector3 pos, Tipo tipo, string titulo, List<SlotGuardado> items)
    {
        var go = new GameObject("Mochila_" + tipo);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
        var m = go.AddComponent<Mochila>();
        m.tipo = tipo; m.titulo = titulo; m.items = items ?? new List<SlotGuardado>();
        Modelo(go.transform, 1f);
        var l = new GameObject("Luz").AddComponent<Light>();
        l.transform.SetParent(go.transform, false); l.transform.localPosition = new Vector3(0f, 2.2f, 0f);
        l.type = LightType.Point; l.range = 9f; l.intensity = 7f; l.shadows = LightShadows.None;
        l.color = tipo == Tipo.Mochila ? new Color(1f, 0.78f, 0.45f) : new Color(1f, 0.5f, 0.35f);
        m.luz = l;
        var sp = go.AddComponent<ItemSparkle>(); sp.color = new Color(1f, 0.85f, 0.5f, 1f);
        return m;
    }

    /// <summary>Mochila de tela hecha con piezas simples (unas 1.4 unidades de alto; el jugador mide 2).</summary>
    public static GameObject Modelo(Transform padre, float escala)
    {
        var raiz = new GameObject("ModeloMochila");
        raiz.transform.SetParent(padre, false);
        raiz.transform.localScale = Vector3.one * escala;
        var tela = Mat("MochilaTela", new Color(0.42f, 0.3f, 0.17f));
        var oscura = Mat("MochilaOscura", new Color(0.24f, 0.16f, 0.09f));
        var hebilla = Mat("MochilaHebilla", new Color(0.75f, 0.7f, 0.55f));
        Pieza(raiz.transform, PrimitiveType.Capsule, new Vector3(0f, 0.62f, 0f), new Vector3(0.95f, 0.62f, 0.62f), Vector3.zero, tela);
        Pieza(raiz.transform, PrimitiveType.Cube, new Vector3(0f, 0.42f, 0f), new Vector3(0.92f, 0.62f, 0.58f), Vector3.zero, tela);
        Pieza(raiz.transform, PrimitiveType.Cube, new Vector3(0f, 0.95f, 0.18f), new Vector3(0.9f, 0.08f, 0.52f), new Vector3(-12f, 0f, 0f), oscura);      // solapa
        Pieza(raiz.transform, PrimitiveType.Cube, new Vector3(0f, 0.36f, 0.32f), new Vector3(0.6f, 0.38f, 0.12f), Vector3.zero, oscura);                   // bolsillo
        Pieza(raiz.transform, PrimitiveType.Cube, new Vector3(0f, 0.62f, 0.42f), new Vector3(0.14f, 0.1f, 0.04f), Vector3.zero, hebilla);
        foreach (float x in new[] { -0.26f, 0.26f })
            Pieza(raiz.transform, PrimitiveType.Cube, new Vector3(x, 0.6f, -0.32f), new Vector3(0.1f, 0.85f, 0.05f), new Vector3(8f, 0f, 0f), oscura);       // correas
        return raiz;
    }

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Material Mat(string n, Color c)
    {
        Material m;
        if (mats.TryGetValue(n, out m) && m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = n };
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.15f);
        mats[n] = m;
        return m;
    }

    static void Pieza(Transform padre, PrimitiveType t, Vector3 pos, Vector3 esc, Vector3 rot, Material m)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(padre, false);
        g.transform.localPosition = pos; g.transform.localScale = esc; g.transform.localEulerAngles = rot;
        g.GetComponent<Renderer>().sharedMaterial = m;
    }

    // ================================================================== ciclo
    void OnEnable() { Todas.Add(this); }
    void OnDisable() { Todas.Remove(this); }

    void Start()
    {
        var pc = FindAnyObjectByType<PlayerController>();
        if (pc != null) jugador = pc.transform;
        fase = Random.value * 10f;
    }

    void Update()
    {
        if (luz != null) luz.intensity = 7f * (0.8f + 0.2f * Mathf.Sin(Time.time * 2f + fase));
        if (jugador == null || GestorPartida.Muerto || IntroDespertar.Activa) return;
        Vector3 d = jugador.position - transform.position;
        if (Mathf.Abs(d.y) > 6f) return;
        d.y = 0f;
        if (d.magnitude > RadioRecoger) return;
        var inv = ControladorInventario.Instance;
        if (inv != null && inv.IsOpen) return;
        SurvivalUI.Indicacion(this, "[" + Controles.NombreTecla(Controles.Accion.Interactuar) + "] Recoger: " + titulo + "  (" + Cantidad + " objetos)");
        if (Controles.Bajada(Controles.Accion.Interactuar)) Recoger();
    }

    public void Recoger()
    {
        var inv = ControladorInventario.Instance;
        if (inv == null) return;
        var quedan = new List<SlotGuardado>();
        foreach (var s in items)
        {
            var icono = GestorPartida.Instancia != null ? GestorPartida.Instancia.BuscarIcono(s.icono, s.id) : inv.GetIconByName(s.id);
            if (!inv.AddItem(s.id, icono, s.cantidad)) quedan.Add(s);
        }
        items = quedan;
        if (items.Count == 0)
        {
            SurvivalUI.Aviso("Recuperaste tus cosas", new Color(0.95f, 0.85f, 0.55f));
            Estadisticas.Actual.mochilasRecuperadas++;
            Destroy(gameObject);
        }
        else SurvivalUI.Aviso("No te cabe todo: quedan " + Cantidad + " objetos", new Color(1f, 0.6f, 0.4f));
    }
}
