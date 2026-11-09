using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Opciones del juego (audio, video, camara), guardadas en PlayerPrefs.
/// Se aplican solas en cada escena: volumen general, calidad, resolucion, brillo (post-proceso)
/// y sensibilidad de la camara de Cinemachine. Tambien crea el menu de pausa en las escenas de juego.
/// Los demas scripts multiplican su volumen por VolMusica / VolEfectos / VolAmbiente.
/// </summary>
public static class Ajustes
{
    public static float VolGeneral = 0.9f, VolMusica = 0.7f, VolEfectos = 0.9f, VolAmbiente = 0.8f;
    public static float Sensibilidad = 1f;
    public static bool InvertirY;
    public static float Brillo;                 // -1..1 (exposicion)
    public static int Calidad;
    public static bool PantallaCompleta = true;
    public static int Resolucion = -1;          // indice en Resoluciones()
    public static bool VSync = true;
    public static int LimiteFps = 0;            // 0 = sin limite
    public static bool MostrarFps;
    public static bool Subtitulos = true;
    /// <summary>Minutos entre autoguardados (0 = desactivado).</summary>
    public static int AutoguardadoMin = 10;

    /// <summary>Lo activa el menu al pulsar Jugar: la escena del juego empieza con la intro de "despertar".</summary>
    public static bool IntroPendiente = true;

    public static event System.Action Cambiado;

    static bool cargado;
    static Volume volumenBrillo;
    static ColorAdjustments ajusteBrillo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Iniciar()
    {
        Cargar();
        Aplicar();
        SceneManager.sceneLoaded += (s, m) => AlCargarEscena();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void PrimeraEscena() { AlCargarEscena(); }

    static void AlCargarEscena()
    {
        volumenBrillo = null;
        AplicarEscena();
        // menu de pausa en las escenas con jugador
        if (Object.FindAnyObjectByType<PlayerController>() != null && Object.FindAnyObjectByType<MenuUI>() == null)
        {
            var r = Resources.Load<MenuRecursos>("MenuRecursos");
            if (r != null)
            {
                // inactivo mientras se configura: Awake de MenuUI necesita el modo ya puesto
                var go = new GameObject("MenuPausa");
                go.SetActive(false);
                var m = go.AddComponent<MenuUI>();
                m.recursos = r; m.modo = MenuUI.Modo.Pausa;
                go.SetActive(true);
            }
        }
    }

    public static void Cargar()
    {
        if (cargado) return;
        cargado = true;
        VolGeneral = PlayerPrefs.GetFloat("aj_volGeneral", VolGeneral);
        VolMusica = PlayerPrefs.GetFloat("aj_volMusica", VolMusica);
        VolEfectos = PlayerPrefs.GetFloat("aj_volEfectos", VolEfectos);
        VolAmbiente = PlayerPrefs.GetFloat("aj_volAmbiente", VolAmbiente);
        Sensibilidad = PlayerPrefs.GetFloat("aj_sens", Sensibilidad);
        InvertirY = PlayerPrefs.GetInt("aj_invY", 0) == 1;
        Brillo = PlayerPrefs.GetFloat("aj_brillo", 0f);
        Calidad = PlayerPrefs.GetInt("aj_calidad", QualitySettings.GetQualityLevel());
        PantallaCompleta = PlayerPrefs.GetInt("aj_full", Screen.fullScreen ? 1 : 0) == 1;
        Resolucion = PlayerPrefs.GetInt("aj_res", -1);
        VSync = PlayerPrefs.GetInt("aj_vsync", 1) == 1;
        LimiteFps = PlayerPrefs.GetInt("aj_fps", 0);
        MostrarFps = PlayerPrefs.GetInt("aj_verFps", 0) == 1;
        Subtitulos = PlayerPrefs.GetInt("aj_subs", 1) == 1;
        AutoguardadoMin = PlayerPrefs.GetInt("aj_autoguardado", 10);
    }

    public static void Guardar()
    {
        PlayerPrefs.SetFloat("aj_volGeneral", VolGeneral);
        PlayerPrefs.SetFloat("aj_volMusica", VolMusica);
        PlayerPrefs.SetFloat("aj_volEfectos", VolEfectos);
        PlayerPrefs.SetFloat("aj_volAmbiente", VolAmbiente);
        PlayerPrefs.SetFloat("aj_sens", Sensibilidad);
        PlayerPrefs.SetInt("aj_invY", InvertirY ? 1 : 0);
        PlayerPrefs.SetFloat("aj_brillo", Brillo);
        PlayerPrefs.SetInt("aj_calidad", Calidad);
        PlayerPrefs.SetInt("aj_full", PantallaCompleta ? 1 : 0);
        PlayerPrefs.SetInt("aj_res", Resolucion);
        PlayerPrefs.SetInt("aj_vsync", VSync ? 1 : 0);
        PlayerPrefs.SetInt("aj_fps", LimiteFps);
        PlayerPrefs.SetInt("aj_verFps", MostrarFps ? 1 : 0);
        PlayerPrefs.SetInt("aj_subs", Subtitulos ? 1 : 0);
        PlayerPrefs.SetInt("aj_autoguardado", AutoguardadoMin);
        PlayerPrefs.Save();
    }

    public static void Restablecer()
    {
        VolGeneral = 0.9f; VolMusica = 0.7f; VolEfectos = 0.9f; VolAmbiente = 0.8f;
        Sensibilidad = 1f; InvertirY = false; Brillo = 0f;
        VSync = true; LimiteFps = 0; MostrarFps = false; Subtitulos = true; AutoguardadoMin = 10;
        Aplicar(); Guardar();
    }

    // ------------------------------------------------------------------ resoluciones
    public static List<Resolution> Resoluciones()
    {
        var res = new List<Resolution>();
        foreach (var r in Screen.resolutions)
            if (!res.Exists(x => x.width == r.width && x.height == r.height)) res.Add(r);
        if (res.Count == 0) res.Add(Screen.currentResolution);
        return res;
    }

    public static int ResolucionActual()
    {
        var l = Resoluciones();
        if (Resolucion >= 0 && Resolucion < l.Count) return Resolucion;
        int mejor = l.Count - 1;
        for (int i = 0; i < l.Count; i++) if (l[i].width == Screen.width && l[i].height == Screen.height) mejor = i;
        return mejor;
    }

    // ------------------------------------------------------------------ aplicar
    /// <summary>Aplica todo y avisa a los oyentes (llamar despues de cambiar cualquier valor).</summary>
    public static void Aplicar()
    {
        AudioListener.volume = Mathf.Clamp01(VolGeneral);
        if (Calidad >= 0 && Calidad < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != Calidad)
            QualitySettings.SetQualityLevel(Calidad, true);
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        Application.targetFrameRate = LimiteFps > 0 ? LimiteFps : -1;
#if !UNITY_EDITOR
        var l = Resoluciones();
        var r = l[ResolucionActual()];
        if (Screen.width != r.width || Screen.height != r.height || Screen.fullScreen != PantallaCompleta)
            Screen.SetResolution(r.width, r.height, PantallaCompleta ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
#endif
        AplicarEscena();
        Cambiado?.Invoke();
    }

    static void AplicarEscena()
    {
        if (!Application.isPlaying) return;
        // brillo: un Volume global con ajuste de exposicion
        if (volumenBrillo == null)
        {
            var go = new GameObject("AjusteBrillo");
            volumenBrillo = go.AddComponent<Volume>();
            volumenBrillo.isGlobal = true; volumenBrillo.priority = 50;
            var perfil = ScriptableObject.CreateInstance<VolumeProfile>();
            ajusteBrillo = perfil.Add<ColorAdjustments>(true);
            volumenBrillo.sharedProfile = perfil;
        }
        ajusteBrillo.postExposure.Override(Brillo * 1.2f);
        volumenBrillo.weight = Mathf.Abs(Brillo) > 0.001f ? 1f : 0f;

        // camara: sensibilidad e inversion del eje Y (CinemachineInputAxisController por reflexion)
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>())
        {
            if (mb.GetType().Name != "CinemachineInputAxisController") continue;
            var lista = mb.GetType().GetProperty("Controllers")?.GetValue(mb) as System.Collections.IList;
            if (lista == null) continue;
            for (int i = 0; i < lista.Count && i < 2; i++)
            {
                var ctrl = lista[i];
                var fInput = ctrl.GetType().GetField("Input");
                if (fInput == null) continue;
                object input = fInput.GetValue(ctrl);
                var fGain = input.GetType().GetField("Gain");
                if (fGain == null) continue;
                float baseGain = i == 0 ? 1f : -1f;            // valores originales de la escena
                float g = baseGain * Sensibilidad * (i == 1 && InvertirY ? -1f : 1f);
                fGain.SetValue(input, g);
                fInput.SetValue(ctrl, input);
            }
        }
    }
}
