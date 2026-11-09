using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Herramienta (se usa desde Herramientas > Final > Tomar fotos, con el juego en Play): arma escenitas
/// comicas con copias quietas de los bichos y les saca fotos para los creditos (Resources/Fotos), y un retrato
/// con fondo transparente de cada tipo de bicho (Resources/Retratos/&lt;Tipo&gt;.png) para las estadisticas.
/// </summary>
public class FotografoFinal : MonoBehaviour
{
    public const string Carpeta = "Assets/Final/Resources/";
    public static bool Trabajando { get; private set; }

    Vector3 estudio;
    readonly List<GameObject> props = new List<GameObject>();
    const int CapaRetrato = 31;

    public static void Tomar()
    {
        if (Trabajando) return;
        new GameObject("FotografoFinal").AddComponent<FotografoFinal>();
    }

    void Start() { StartCoroutine(Trabajo()); }

    // ================================================================== trabajo
    IEnumerator Trabajo()
    {
        Trabajando = true;
        Directory.CreateDirectory(Carpeta + "Fotos"); Directory.CreateDirectory(Carpeta + "Retratos");
        var dn = DayNightCycle.Instance;
        float hora = dn != null ? dn.hora : 12f; bool pausa = dn != null && dn.pausado;
        if (dn != null) { dn.hora = 10.5f; dn.pausado = true; dn.AplicarAhora(); }
        bool niebla = RenderSettings.fog; RenderSettings.fog = false;
        float bias = QualitySettings.lodBias; QualitySettings.lodBias = 4f;
        var jugador = FindAnyObjectByType<PlayerController>();
        var ocultos = new List<Renderer>();
        if (jugador != null) foreach (var r in jugador.GetComponentsInChildren<Renderer>()) if (r.enabled) { r.enabled = false; ocultos.Add(r); }

        // estudio: el escritorio del profesor (plano, despejado y con el salon de fondo)
        var escritorio = GameObject.Find("class/desk");
        if (escritorio != null) { var b = escritorio.GetComponent<Renderer>().bounds; estudio = new Vector3(b.center.x, b.max.y + 0.01f, b.center.z - 1.5f); }
        else { estudio = new Vector3(150f, 0f, -330f); estudio.y = ZonaJuego.Altura(estudio.x, estudio.z); }
        // luces de estudio (calida de frente, fria de relleno)
        var luces = new List<GameObject>();
        foreach (var (pos, col, inten) in new[] { (new Vector3(-6f, 10f, -9f), new Color(1f, 0.9f, 0.75f), 70f), (new Vector3(8f, 6f, 4f), new Color(0.7f, 0.8f, 1f), 30f) })
        {
            var l = new GameObject("LuzEstudio").AddComponent<Light>();
            l.type = LightType.Point; l.range = 30f; l.intensity = inten; l.color = col; l.shadows = LightShadows.Soft;
            l.transform.position = estudio + pos; luces.Add(l.gameObject);
        }

        foreach (var paso in Fotos()) { yield return paso; }
        foreach (var paso in Retratos()) { yield return paso; }

        foreach (var l in luces) Destroy(l);
        foreach (var r in ocultos) if (r != null) r.enabled = true;
        QualitySettings.lodBias = bias; RenderSettings.fog = niebla;
        if (dn != null) { dn.hora = hora; dn.pausado = pausa; dn.AplicarAhora(); }
        Trabajando = false;
#if UNITY_EDITOR
        UnityEditor.AssetDatabase.Refresh();
#endif
        Debug.Log("[FotografoFinal] fotos y retratos listos en " + Carpeta);
        Destroy(gameObject);
    }

    IEnumerable<object> Fotos()
    {
        // 1. la abeja ladrona: se lleva tu mochila y las hormigas la miran
        var abeja = Prop("Abeja", new Vector3(0.3f, 2.4f, 0f), 90f);
        Accesorio(Mochila.Modelo(null, 0.75f), new Vector3(0.45f, 1.45f, 0.05f), 20f, new Vector3(0, 0, 18));
        var h1 = Prop("Hormiga", new Vector3(-2.3f, 0f, 0.4f), 80f, cv => cv.pitch = 28f);
        yield return Foto("01_Ladrona", 0f, 8f);

        // 2. siesta sobre un carbon
        Accesorio(Copia(Buscar("Carbon_0")), new Vector3(0f, 0f, 0f), 0f, Vector3.zero, 2.6f);
        Prop("Oruga", new Vector3(-0.2f, 0.5f, 0.1f), 25f, cv => cv.move = 0f);
        yield return Foto("02_Siesta", 30f, 22f);

        // 3. futbol: hormigas contra la cochinilla hecha bola
        Prop("Cochinilla", new Vector3(0f, 0f, 0f), 0f, cv => { cv.curl = 1f; cv.spin = 40f; });
        Prop("Hormiga", new Vector3(-2.2f, 0f, 0.2f), 90f, cv => cv.move = 1f);
        Prop("Hormiga", new Vector3(2.4f, 0f, 0.6f), -100f, cv => cv.move = 0.6f);
        yield return Foto("03_Futbol", 0f, 18f);

        // 4. la reina no sabe que tiene una mariposa en la cabeza
        var reina = Prop("ReinaHormiga", Vector3.zero, 160f);
        if (reina != null)
        {
            var b = Limites(reina);
            Vector3 cabeza = reina.transform.position + reina.transform.forward * b.extents.z * 0.75f;
            cabeza.y = b.max.y + 0.2f;
            Prop("Mariposa", Quaternion.Euler(0f, 180f, 0f) * (cabeza - estudio), 160f);   // deshace el giro de Colocar
        }
        yield return Foto("04_Corona", 25f, 14f);

        // 5. la araña con una antorcha
        Prop("Arana", Vector3.zero, 0f);
        var antorcha = Antorcha();
        if (antorcha != null) Accesorio(antorcha, new Vector3(0.9f, 0f, 0.5f), 0f, new Vector3(0, 0, 8), 0.42f);
        yield return Foto("05_Antorcha", -25f, 12f);

        // 6. carrera: el ciempies persigue al escarabajo
        Prop("Escarabajo", new Vector3(1.7f, 0f, 0f), 90f, cv => cv.move = 1f);
        Prop("Ciempies", new Vector3(-2.2f, 0f, 0.3f), 90f, cv => cv.move = 1f);
        yield return Foto("06_Carrera", 0f, 14f);

        // 7. banquete de hormigas
        var carne = Resources.Load<GameObject>("Drops/Carne");
        if (carne != null) Accesorio(Copia(carne), new Vector3(0f, 0.15f, 0f), 30f, Vector3.zero, 1.8f);
        var hueso = Resources.Load<GameObject>("Drops/Hueso");
        if (hueso != null) Accesorio(Copia(hueso), new Vector3(1.3f, 0.1f, 1.0f), 70f, Vector3.zero, 1.4f);
        Prop("Hormiga", new Vector3(-2.1f, 0f, 0.1f), 95f);
        Prop("Hormiga", new Vector3(1.9f, 0f, -1.2f), -55f);
        yield return Foto("07_Banquete", 20f, 32f);

        // 8. la lombriz duerme dentro de una liga como si fuera su cama
        Accesorio(Copia(BuscarPrefijo("black_generic_hair_tie")), new Vector3(0f, 0.05f, 0f), 0f, Vector3.zero, 1.6f);
        Prop("Lombriz", new Vector3(0f, 0.08f, 0f), 30f, cv => { cv.pitch = 0f; cv.sink = 0f; cv.move = 0.15f; });
        yield return Foto("08_HulaHoop", 15f, 25f);

        // 9. la reina abeja en su trono (la bateria verde)
        var bat = Buscar("BATERIA_Colmena") ?? Buscar("BATERIA_Hormiguero");
        GameObject trono = bat != null ? Accesorio(Copia(bat), Vector3.zero, 0f, Vector3.zero, 2.6f) : null;
        float alto = trono != null ? Limites(trono).max.y - estudio.y : 3f;
        Prop("ReinaAbeja", new Vector3(0f, alto - 0.3f, 0f), 200f);
        yield return Foto("09_Trono", 20f, 10f);

        // 10. la cochinilla se esconde entre carbones
        var carbon = Buscar("Carbon_0");
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad + 0.4f;
            Accesorio(Copia(carbon), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.3f, i * 50f, Vector3.zero, 1.5f + 0.2f * i);
        }
        Prop("Cochinilla", Vector3.zero, 30f, cv => cv.curl = 1f);
        yield return Foto("10_Escondite", 10f, 35f);
    }

    IEnumerable<object> Retratos()
    {
        foreach (var (clave, _) in Estadisticas.Tipos)
        {
            var g = Prop(clave, Vector3.zero, 150f, cv => cv.move = 0.3f);
            if (g == null) continue;
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = CapaRetrato;
            yield return null; yield return null; yield return null;
            Render(Carpeta + "Retratos/" + clave + ".png", 384, 384, 35f, 18f, true);
            Limpiar();
            yield return null;
        }
    }

    IEnumerator Foto(string nombre, float azimut, float elevacion)
    {
        // dejar que corran unos cuadros de animacion
        for (int i = 0; i < 4; i++) yield return null;
        Render(Carpeta + "Fotos/" + nombre + ".png", 1024, 768, azimut, elevacion, false);
        Limpiar();
        yield return null;
    }

    // ================================================================== props
    GameObject Buscar(string n) { var g = GameObject.Find(n); return g; }

    GameObject BuscarPrefijo(string p)
    {
        foreach (var g in gameObject.scene.GetRootGameObjects()) if (g.name.StartsWith(p)) return g;
        return null;
    }

    /// <summary>Un bicho del tipo pedido, copiado de la escena y quieto (sin IA ni colisiones).</summary>
    GameObject Prop(string tipo, Vector3 local, float rotY, System.Action<CreatureVisual> pose = null)
    {
        GameObject original = null;
        foreach (var rh in FindObjectsByType<RatHealth>(FindObjectsInactive.Include))
            if (Estadisticas.Tipo(rh.gameObject) == tipo && (original == null || rh.gameObject.activeInHierarchy)) original = rh.gameObject;
        if (original == null) { Debug.LogWarning("[FotografoFinal] no hay " + tipo); return null; }
        var c = Copia(original);
        foreach (var t in c.GetComponentsInChildren<Transform>(true)) if (t.name == "WormMound") Destroy(t.gameObject);
        foreach (var cv in c.GetComponentsInChildren<CreatureVisual>(true)) { cv.sink = 0f; cv.curl = 0f; cv.roll = 0f; cv.lunge = 0f; }
        Colocar(c, local, rotY, Vector3.zero, 1f);
        if (pose != null) foreach (var cv in c.GetComponentsInChildren<CreatureVisual>(true)) pose(cv);
        // local.y = altura de la parte de abajo del bicho (algunos modelos estan desplazados de su origen)
        var b = Limites(c);
        float yDeseado = estudio.y + local.y;
        c.transform.position += Vector3.up * (yDeseado - b.min.y);
        return c;
    }

    GameObject Copia(GameObject original)
    {
        if (original == null) return null;
        bool act = original.activeSelf;
        original.SetActive(false);
        var c = Instantiate(original);
        original.SetActive(act);
        foreach (var mb in c.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is CreatureVisual)) mb.enabled = false;
        foreach (var col in c.GetComponentsInChildren<Collider>(true)) col.enabled = false;
        foreach (var ag in c.GetComponentsInChildren<NavMeshAgent>(true)) ag.enabled = false;
        foreach (var rb in c.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        foreach (var ps in c.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop();
        props.Add(c);
        return c;
    }

    GameObject Accesorio(GameObject g, Vector3 local, float rotY, Vector3 inclinar, float escala = 1f)
    {
        if (g == null) return null;
        if (!props.Contains(g)) props.Add(g);
        Colocar(g, local, rotY, inclinar, escala);
        // algunos modelos tienen la malla lejos de su origen: se centra en el punto pedido, apoyada en la mesa
        var b = Limites(g);
        Vector3 deseado = estudio + Quaternion.Euler(0f, 180f, 0f) * local;
        g.transform.position += new Vector3(deseado.x - b.center.x, deseado.y - b.min.y, deseado.z - b.center.z);
        return g;
    }

    void Colocar(GameObject g, Vector3 local, float rotY, Vector3 inclinar, float escala)
    {
        g.transform.SetParent(null, true);
        // toda la escena girada 180 grados: la camara mira hacia el pizarron
        local = Quaternion.Euler(0f, 180f, 0f) * local; rotY += 180f;
        g.transform.SetPositionAndRotation(estudio + local, Quaternion.Euler(inclinar.x, rotY, inclinar.z));
        if (!Mathf.Approximately(escala, 1f)) g.transform.localScale = g.transform.localScale * escala;
        g.SetActive(true);
    }

    GameObject Antorcha()
    {
        var a = FindAnyObjectByType<Antorcha>();
        if (a == null || a.modeloMano == null) return null;
        var c = Instantiate(a.modeloMano);
        c.SetActive(true);
        foreach (var ps in c.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; ps.Play(); }
        props.Add(c);
        return c;
    }

    void Limpiar()
    {
        foreach (var p in props) if (p != null) Destroy(p);
        props.Clear();
    }

    static Bounds Limites(GameObject g)
    {
        var rs = g.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(g.transform.position, Vector3.one * 0.5f); bool hay = false;
        foreach (var r in rs) { if (r is ParticleSystemRenderer) continue; if (!hay) { b = r.bounds; hay = true; } else b.Encapsulate(r.bounds); }
        return b;
    }

    // ================================================================== camara
    void Render(string ruta, int w, int h, float azimut, float elevacion, bool transparente)
    {
        Bounds b = new Bounds(estudio + Vector3.up, Vector3.one); bool hay = false;
        foreach (var p in props) { if (p == null) continue; var lb = Limites(p); if (!hay) { b = lb; hay = true; } else b.Encapsulate(lb); }
        var go = new GameObject("CamaraFoto");
        var cam = go.AddComponent<Camera>();
        cam.fieldOfView = 34f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f; cam.useOcclusionCulling = false;
        cam.aspect = w / (float)h;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = !transparente; data.renderShadows = true;
        if (transparente) { cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0); cam.cullingMask = 1 << CapaRetrato; }
        else cam.clearFlags = CameraClearFlags.Skybox;
        Quaternion rot = Quaternion.Euler(elevacion, azimut, 0f);                 // azimut 0 = desde -Z mirando a +Z (pizarron de fondo)
        float radio = Mathf.Max(b.extents.magnitude, 0.5f);
        float dist = radio / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * (transparente ? 1.0f : 0.92f);
        Vector3 centro = b.center + Vector3.up * (transparente ? 0f : b.extents.y * 0.1f);
        cam.transform.SetPositionAndRotation(centro - rot * Vector3.forward * dist, rot);
        var rt = new RenderTexture(w * 2, h * 2, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();
        var antes = RenderTexture.active; RenderTexture.active = rt;
        var grande = new Texture2D(w * 2, h * 2, TextureFormat.RGBA32, false);
        grande.ReadPixels(new Rect(0, 0, w * 2, h * 2), 0, 0); grande.Apply();
        RenderTexture.active = antes;
        cam.targetTexture = null; Destroy(go); rt.Release(); Destroy(rt);
        // reducir a la mitad (suaviza bordes)
        var px = grande.GetPixels(); var o = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                Color s = px[(2 * y) * w * 2 + 2 * x] + px[(2 * y) * w * 2 + 2 * x + 1] + px[(2 * y + 1) * w * 2 + 2 * x] + px[(2 * y + 1) * w * 2 + 2 * x + 1];
                o[y * w + x] = s * 0.25f;
            }
        Destroy(grande);
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false); t.SetPixels(o); t.Apply();
        File.WriteAllBytes(ruta, t.EncodeToPNG());
        Destroy(t);
    }
}
