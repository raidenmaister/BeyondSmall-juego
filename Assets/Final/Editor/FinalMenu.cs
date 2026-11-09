using UnityEditor;
using UnityEngine;

/// <summary>Herramientas del final: fotos de los bichos y atajos para probar la cinematica y los creditos.</summary>
public static class FinalMenu
{
    [MenuItem("Herramientas/Final/Tomar fotos de los bichos (en Play)")]
    static void Fotos()
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[Final] Dale Play a la escena del juego primero."); return; }
        FotografoFinal.Tomar();
    }

    [MenuItem("Herramientas/Final/Probar el final (en Play)")]
    static void Probar()
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[Final] Dale Play a la escena del juego primero."); return; }
        var maq = MaquinaCrecimiento.Instancia;
        var pc = Object.FindAnyObjectByType<PlayerController>();
        if (maq == null || pc == null) return;
        maq.Restaurar(MaquinaCrecimiento.Necesarias);
        var cc = pc.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pc.transform.position = maq.transform.TransformPoint(new Vector3(0f, 1.05f, -2f));
        if (cc != null) cc.enabled = true;
        var f = Object.FindAnyObjectByType<FinalJuego>();
        if (f != null) f.Empezar();
    }

    [MenuItem("Herramientas/Final/Probar solo los creditos (en Play)")]
    static void Creditos()
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[Final] Dale Play a la escena del juego primero."); return; }
        var src = new GameObject("MusicaFinal").AddComponent<AudioSource>();
        src.clip = Resources.Load<AudioClip>("Final_Musica"); src.volume = 0.8f * Ajustes.VolMusica; src.time = 18f; src.Play();
        var c = new GameObject("CreditosFinal").AddComponent<CreditosFinal>();
        c.musica = src;
        src.transform.SetParent(c.transform, true);
    }
}

/// <summary>Las fotos y retratos del final se importan tal cual (sin reescalar a potencia de 2, con transparencia).</summary>
public class ImportarFotosFinal : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Final/Resources/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
    }

    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith("Assets/Final/Resources/")) return;
        var ai = (AudioImporter)assetImporter;
        var s = ai.defaultSampleSettings;
        s.loadType = assetPath.Contains("Musica") ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
        s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.7f;
        ai.defaultSampleSettings = s;
    }
}
