using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Red de seguridad contra destellos: activa "Stop NaN" de URP en toda camara de juego con post-proceso.
/// Un pixel NaN/infinito (normal nula, division por cero en un shader...) sale negro y el Bloom
/// lo esparce como una mancha blanca gigante; Stop NaN lo reemplaza por negro antes del post-proceso.
/// Se instala solo al arrancar, sin componentes en la escena.
/// </summary>
static class CamaraSinNaN
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Instalar()
    {
        RenderPipelineManager.beginCameraRendering -= Revisar;
        RenderPipelineManager.beginCameraRendering += Revisar;
    }

    static void Revisar(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.cameraType != CameraType.Game) return;
        if (!cam.TryGetComponent(out UniversalAdditionalCameraData datos)) return;
        if (datos.renderPostProcessing && !datos.stopNaN) datos.stopNaN = true;
    }
}
