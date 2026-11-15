using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Texto con vida (Guinxu, dialogos de Paper Mario): las letras aparecen poco a poco,
/// la puntuacion hace pausas (", " corta, ". ? ! ..." larga) para dar ritmo y que se lea mejor,
/// y hay etiquetas propias para transmitir COMO se dice:
///   &lt;temblor&gt;...&lt;/temblor&gt;  letras que tiemblan (miedo, enfado, enfasis)
///   &lt;ola&gt;...&lt;/ola&gt;          letras que bailan (alegria, canto, algo magico)
/// Funciona con tiempo real, asi que tambien va en pausa o en camara lenta.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class TextoVivo : MonoBehaviour
{
    public float letrasPorSegundo = 38f;
    public float pausaComa = 0.18f, pausaPunto = 0.38f;
    public AudioClip sonidoLetra;
    [Range(0f, 1f)] public float volumenLetra = 0.25f;

    TMP_Text texto;
    readonly List<int> temblor = new List<int>(), ola = new List<int>();
    readonly List<float> apariciones = new List<float>();
    float reloj, siguiente;
    int visibles, total;
    AudioSource fuente;

    public bool Terminado => visibles >= total;

    /// <summary>Atajo: pone el texto con efectos en un TMP y lo empieza a escribir.</summary>
    public static TextoVivo Escribir(TMP_Text t, string s, float letrasPorSegundo = 38f)
    {
        var v = t.GetComponent<TextoVivo>();
        if (v == null) v = t.gameObject.AddComponent<TextoVivo>();
        v.letrasPorSegundo = letrasPorSegundo;
        v.Poner(s);
        return v;
    }

    /// <summary>Quita las etiquetas propias (para mostrar el texto sin efectos en otro sitio).</summary>
    public static string Limpio(string s)
    {
        return s.Replace("<temblor>", "").Replace("</temblor>", "").Replace("<ola>", "").Replace("</ola>", "");
    }

    void Awake() { texto = GetComponent<TMP_Text>(); }

    public void Poner(string s)
    {
        if (texto == null) texto = GetComponent<TMP_Text>();
        temblor.Clear(); ola.Clear(); apariciones.Clear();
        // quitar nuestras etiquetas apuntando que letras visibles van con cada efecto
        var sb = new StringBuilder();
        bool enTemblor = false, enOla = false;
        int indiceVisible = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '<')
            {
                int fin = s.IndexOf('>', i);
                if (fin > i)
                {
                    string tag = s.Substring(i + 1, fin - i - 1);
                    if (tag == "temblor") { enTemblor = true; i = fin; continue; }
                    if (tag == "/temblor") { enTemblor = false; i = fin; continue; }
                    if (tag == "ola") { enOla = true; i = fin; continue; }
                    if (tag == "/ola") { enOla = false; i = fin; continue; }
                    sb.Append(s, i, fin - i + 1);   // etiqueta de TMP: se copia tal cual
                    i = fin; continue;
                }
            }
            if (enTemblor) temblor.Add(indiceVisible);
            if (enOla) ola.Add(indiceVisible);
            sb.Append(s[i]);
            indiceVisible++;
        }
        texto.text = sb.ToString();
        texto.maxVisibleCharacters = 0;
        texto.ForceMeshUpdate();
        total = texto.textInfo.characterCount;
        visibles = 0; reloj = 0f; siguiente = 0.05f;
        enabled = true;
    }

    /// <summary>Muestra todo de golpe (el jugador tiene prisa).</summary>
    public void Completar()
    {
        while (apariciones.Count < total) apariciones.Add(reloj - 1f);
        visibles = total;
        texto.maxVisibleCharacters = total;
    }

    void Update()
    {
        if (texto == null) return;
        float dt = Time.unscaledDeltaTime;
        reloj += dt;

        // aparecer letra a letra con pausas en la puntuacion
        while (visibles < total && reloj >= siguiente)
        {
            var info = texto.textInfo.characterInfo[visibles];
            char c = info.character;
            visibles++;
            apariciones.Add(reloj);
            float espera = 1f / Mathf.Max(1f, letrasPorSegundo);
            char sig = visibles < total ? texto.textInfo.characterInfo[visibles].character : ' ';
            if ((c == ',' || c == ';' || c == ':') && sig == ' ') espera += pausaComa;
            else if ((c == '.' || c == '?' || c == '!' || c == '…') && (sig == ' ' || sig == '\n' || visibles >= total)) espera += pausaPunto;
            else if (c == '\n') espera += pausaComa;
            siguiente += espera;
            if (sonidoLetra != null && !char.IsWhiteSpace(c) && visibles % 2 == 0) Sonar();
        }
        texto.maxVisibleCharacters = visibles;
        Animar();
    }

    void Sonar()
    {
        if (fuente == null)
        {
            fuente = gameObject.AddComponent<AudioSource>();
            fuente.playOnAwake = false; fuente.spatialBlend = 0f; fuente.ignoreListenerPause = true;
        }
        fuente.pitch = Random.Range(0.9f, 1.15f);
        fuente.PlayOneShot(sonidoLetra, volumenLetra * Ajustes.VolEfectos);
    }

    void Animar()
    {
        texto.ForceMeshUpdate();
        var ti = texto.textInfo;
        float t = Time.unscaledTime;
        bool cambio = false;
        for (int i = 0; i < ti.characterCount && i < visibles; i++)
        {
            var ci = ti.characterInfo[i];
            if (!ci.isVisible) continue;
            var verts = ti.meshInfo[ci.materialReferenceIndex].vertices;
            int v0 = ci.vertexIndex;
            Vector3 centro = (verts[v0] + verts[v0 + 2]) * 0.5f;
            Vector3 desp = Vector3.zero;
            float escala = 1f;

            // cada letra entra con un pequeño "pop"
            float edad = reloj - (i < apariciones.Count ? apariciones[i] : -1f);
            if (edad < 0.12f) escala = Mathf.Lerp(1.6f, 1f, edad / 0.12f);

            if (temblor.Contains(i))
                desp += new Vector3(Mathf.PerlinNoise(i * 3.1f, t * 18f) - 0.5f, Mathf.PerlinNoise(i * 7.3f, t * 18f) - 0.5f, 0f) * ci.pointSize * 0.14f;
            if (ola.Contains(i))
                desp += new Vector3(0f, Mathf.Sin(t * 7f + i * 0.55f) * ci.pointSize * 0.12f, 0f);

            if (desp == Vector3.zero && Mathf.Approximately(escala, 1f)) continue;
            for (int k = 0; k < 4; k++)
                verts[v0 + k] = centro + (verts[v0 + k] - centro) * escala + desp;
            cambio = true;
        }
        if (cambio)
            for (int m = 0; m < ti.meshInfo.Length; m++)
            {
                ti.meshInfo[m].mesh.vertices = ti.meshInfo[m].vertices;
                texto.UpdateGeometry(ti.meshInfo[m].mesh, m);
            }
    }
}
