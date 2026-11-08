using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Arma el menu principal: fuentes TMP, recursos (Resources/MenuRecursos), la escena MenuPrincipal,
/// la intro en la escena del juego y el orden de escenas en Build Settings.
/// Menu: Herramientas > Menu > Construir. Los fondos son las imagenes de Assets/Menu/Fondos.
/// </summary>
public static class MenuBuilder
{
    const string R = "Assets/Menu/";
    const string EscenaMenu = R + "MenuPrincipal.unity";
    const string EscenaJuego = "Assets/ClassRoom/Class_rom_exemple.unity";

    // nombre de lugar para cada fondo (por nombre de archivo)
    static readonly Dictionary<string, string> Lugares = new Dictionary<string, string>
    {
        { "fondo_salon", "El salon abandonado" }, { "fondo_salon2", "Entre los pupitres" }, { "fondo_fogata", "La fogata" },
        { "fondo_puerta", "La salida" }, { "fondo_campo", "El jardin" }, { "fondo_atardecer", "Atardecer" },
        { "fondo_noche", "Luciernagas" }, { "fondo_bichos", "Vecinos peligrosos" }, { "fondo_hormiguero", "El hormiguero" },
        { "fondo_tunel", "Los tuneles" }, { "fondo_camara", "La colonia" }, { "fondo_reina", "La reina" },
        { "fondo_cerca", "El limite" }, { "fondo_escarabajo", "Escarabajo" }, { "fondo_abeja", "Abeja" },
    };

    [MenuItem("Herramientas/Menu/Construir")]
    public static void Construir()
    {
        var recursos = CrearRecursos();
        CrearEscenaMenu(recursos);
        PrepararEscenaJuego();
        var escenas = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(EscenaMenu, true), new EditorBuildSettingsScene(EscenaJuego, true) };
        EditorBuildSettings.scenes = escenas.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[Menu] Construido. Fondos: " + recursos.fondos.Length);
    }

    static TMP_FontAsset Fuente(string ttf, string nombre)
    {
        string path = R + "Fuentes/" + nombre + ".asset";
        var existente = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existente != null) return existente;
        var font = AssetDatabase.LoadAssetAtPath<Font>(R + "Fuentes/" + ttf);
        var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        fa.name = nombre;
        AssetDatabase.CreateAsset(fa, path);
        fa.material.name = nombre + " Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);
        foreach (var t in fa.atlasTextures) { t.name = nombre + " Atlas"; AssetDatabase.AddObjectToAsset(t, fa); }
        // caracteres que el menu usa siempre (evita generarlos en pleno juego)
        fa.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?¿¡-—–_()[]/'\"%+x·…‹›ÁÉÍÓÚÑáéíóúñü", out _);
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        return fa;
    }

    static MenuRecursos CrearRecursos()
    {
        Directory.CreateDirectory(R + "Resources");
        // importacion: logo y fondos como sprites, audio de UI sin latencia
        Sprite(R + "Sprites/Logo.png", 2048, false);
        var fondos = new List<Sprite>(); var nombres = new List<string>();
        foreach (var f in Directory.GetFiles(R + "Fondos", "*.png").OrderBy(x => x))
        {
            string p = f.Replace('\\', '/');
            var s = Sprite(p, 2048, true);
            if (s == null) continue;
            fondos.Add(s);
            string n = Path.GetFileNameWithoutExtension(p);
            nombres.Add(Lugares.TryGetValue(n, out var l) ? l : n);
        }
        foreach (var a in Directory.GetFiles(R + "Audio", "*.mp3").Concat(Directory.GetFiles(R + "Audio", "*.ogg")))
        {
            var ai = (AudioImporter)AssetImporter.GetAtPath(a.Replace('\\', '/'));
            var st = ai.defaultSampleSettings;
            bool largo = a.Contains("Musica");
            st.loadType = largo ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            ai.defaultSampleSettings = st; ai.SaveAndReimport();
        }

        string path = R + "Resources/MenuRecursos.asset";
        var r = AssetDatabase.LoadAssetAtPath<MenuRecursos>(path);
        if (r == null) { r = ScriptableObject.CreateInstance<MenuRecursos>(); AssetDatabase.CreateAsset(r, path); }
        r.fuenteMenu = Fuente("BarlowCondensed-Medium.ttf", "BarlowCondensed SDF");
        r.fuenteTexto = Fuente("Barlow-Medium.ttf", "Barlow SDF");
        r.logo = AssetDatabase.LoadAssetAtPath<Sprite>(R + "Sprites/Logo.png");
        r.fondos = fondos.ToArray();
        r.nombresFondos = nombres.ToArray();
        AudioClip A(string n) { return AssetDatabase.LoadAssetAtPath<AudioClip>(R + "Audio/" + n); }
        r.musica = A("Musica_Menu.ogg");
        r.sonidoMover = A("UI_Mover.mp3"); r.sonidoAceptar = A("UI_Aceptar.mp3"); r.sonidoAtras = A("UI_Atras.mp3"); r.sonidoAjuste = A("UI_Ajuste.mp3");
        r.escenaJuego = Path.GetFileNameWithoutExtension(EscenaJuego);
        r.escenaMenu = Path.GetFileNameWithoutExtension(EscenaMenu);
        EditorUtility.SetDirty(r);
        AssetDatabase.SaveAssets();
        return r;
    }

    static Sprite Sprite(string path, int max, bool comprimir)
    {
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti == null) return null;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.maxTextureSize = max;
        ti.textureCompression = comprimir ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void CrearEscenaMenu(MenuRecursos r)
    {
        var activa = EditorSceneManager.GetActiveScene();
        if (activa.isDirty) EditorSceneManager.SaveScene(activa);
        var escena = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera").AddComponent<Camera>();
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        cam.gameObject.AddComponent<AudioListener>();
        var menu = new GameObject("Menu").AddComponent<MenuUI>();
        menu.recursos = r; menu.modo = MenuUI.Modo.Principal;
        RenderSettings.fog = false;
        EditorSceneManager.SaveScene(escena, EscenaMenu);
    }

    static void PrepararEscenaJuego()
    {
        var escena = EditorSceneManager.OpenScene(EscenaJuego, OpenSceneMode.Single);
        var intro = Object.FindAnyObjectByType<IntroDespertar>();
        if (intro == null) intro = new GameObject("IntroDespertar").AddComponent<IntroDespertar>();
        AudioClip A(string n) { return AssetDatabase.LoadAssetAtPath<AudioClip>(R + "Audio/Intro_" + n + ".mp3"); }
        intro.encendido = A("Encendido"); intro.zumbido = A("Zumbido"); intro.carga = A("Carga"); intro.encoger = A("Encoger");
        intro.apagado = A("Apagado"); intro.latido = A("Latido"); intro.respiro = A("Respiro");
        intro.fuente = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(R + "Fuentes/Barlow SDF.asset");
        EditorUtility.SetDirty(intro);
        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
    }
}
