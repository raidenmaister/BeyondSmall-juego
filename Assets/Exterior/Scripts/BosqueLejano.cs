using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Bosque estatico detras de la cerca: cientos de arboles impostor (un quad que mira a la camara)
/// repartidos en un anillo. Se dibujan con GPU instancing en una llamada por tipo de arbol.
/// </summary>
[ExecuteAlways]
public class BosqueLejano : MonoBehaviour
{
    [System.Serializable]
    public class TipoArbol
    {
        public Material material;
        [Tooltip("Ancho / alto de la imagen")]
        public float proporcion = 1f;
        public Vector2 altura = new Vector2(200f, 350f);
        [Range(0f, 1f)] public float frecuencia = 1f;
    }

    public TipoArbol[] tipos;
    public Mesh quad;
    public int semilla = 77;
    public int cantidad = 900;
    public float radioMin = 440f;
    public float radioMax = 1500f;
    [Tooltip("Mas arboles cerca de la cerca que a lo lejos")]
    [Range(0.2f, 3f)] public float concentracion = 1.6f;
    [Tooltip("Claros en el bosque por donde se ven las montañas (0 = bosque cerrado)")]
    [Range(0f, 0.9f)] public float claros = 0.45f;

    /// <summary>Lo activa el hormiguero: bajo tierra no se dibuja el bosque lejano.</summary>
    public static bool ocultoGlobal;

    readonly List<Matrix4x4[]> porTipo = new List<Matrix4x4[]>();
    RenderParams[] rp;

    void OnEnable()
    {
        Generar();
        RenderPipelineManager.beginCameraRendering += Dibujar;
    }

    void OnDisable() { RenderPipelineManager.beginCameraRendering -= Dibujar; }
    void OnValidate() { if (isActiveAndEnabled) Generar(); }

    public void Generar()
    {
        porTipo.Clear();
        if (tipos == null || tipos.Length == 0) return;
        var rnd = new System.Random(semilla);
        var listas = new List<Matrix4x4>[tipos.Length];
        for (int i = 0; i < tipos.Length; i++) listas[i] = new List<Matrix4x4>();
        float suma = 0f; foreach (var t in tipos) suma += Mathf.Max(0.001f, t.frecuencia);
        Vector3 c = ZonaJuego.centro;

        for (int i = 0, intentos = 0; i < cantidad && intentos < cantidad * 8; intentos++)
        {
            float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
            float u = Mathf.Pow((float)rnd.NextDouble(), concentracion);
            float r = Mathf.Lerp(radioMin, radioMax, u);
            float x = c.x + Mathf.Cos(a) * r, z = c.z + Mathf.Sin(a) * r;
            // claros: zonas (segun el angulo) con pocos arboles
            float m = Mathf.PerlinNoise(Mathf.Cos(a) * 2.2f + 9.3f, Mathf.Sin(a) * 2.2f + 4.1f);
            if ((float)rnd.NextDouble() < Mathf.InverseLerp(0.55f, 0.3f, m) * claros * 2f) continue;
            i++;
            float elige = (float)rnd.NextDouble() * suma; int ti = 0;
            for (; ti < tipos.Length - 1; ti++) { elige -= Mathf.Max(0.001f, tipos[ti].frecuencia); if (elige <= 0f) break; }
            var t = tipos[ti];
            float h = Mathf.Lerp(t.altura.x, t.altura.y, (float)rnd.NextDouble());
            // se hunden un poco para que el tronco no flote sobre las lomas
            float y = ZonaJuego.Altura(x, z) - h * 0.02f;
            listas[ti].Add(Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.identity, new Vector3(h * t.proporcion, h, 1f)));
        }
        rp = new RenderParams[tipos.Length];
        for (int i = 0; i < tipos.Length; i++)
        {
            porTipo.Add(listas[i].ToArray());
            if (tipos[i].material == null) continue;
            rp[i] = new RenderParams(tipos[i].material)
            {
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                worldBounds = new Bounds(c, Vector3.one * radioMax * 2.2f),
                layer = gameObject.layer,
            };
        }
    }

    void Dibujar(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        if (quad == null || rp == null) return;
        if (ocultoGlobal && cam.cameraType == CameraType.Game) return;
        for (int i = 0; i < porTipo.Count; i++)
        {
            if (tipos[i].material == null || porTipo[i].Length == 0) continue;
            var p = rp[i]; p.camera = cam;
            var m = porTipo[i];
            for (int k = 0; k < m.Length; k += 1023)
                Graphics.RenderMeshInstanced(p, quad, 0, m, Mathf.Min(1023, m.Length - k), k);
        }
    }
}
