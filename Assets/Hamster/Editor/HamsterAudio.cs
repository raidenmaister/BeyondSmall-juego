using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Musica y sonidos de Hamsterlandia, sintetizados por codigo (nada descargado):
///  - vals tetrico en La menor en tres capas que se tocan juntas y en sincronia
///    (caja musical, organillo de feria y un theremin que baja cromatico): cada palanca enciende una;
///  - galope de circo desafinado para la persecucion;
///  - fanfarria "ta-da... wah", chillidos de Bolita, palanca, pasos, rueda que rechina, atorado y campana.
/// Se escriben como WAV de 16 bits en Assets/Hamster/Audio (los usa HamsterBuilder).
/// </summary>
public static class HamsterAudio
{
    public const string Carpeta = "Assets/Hamster/Audio/";
    const int SR = 44100;
    static System.Random rnd;

    public static void Generar()
    {
        Directory.CreateDirectory(Carpeta);
        rnd = new System.Random(2027);
        Escribir("Vals_Caja", ValsCaja());
        Escribir("Vals_Organillo", ValsOrganillo());
        Escribir("Vals_Theremin", ValsTheremin());
        Escribir("Galope", Galope());
        Escribir("Fanfarria", Fanfarria());
        Escribir("Escape", Escape());
        Escribir("Chillido_1", Chillido(0.22f, 2300f, 3600f, 1));
        Escribir("Chillido_2", Chillido(0.34f, 2000f, 3100f, 2));
        Escribir("Chillido_3", Chillido(0.16f, 2800f, 4200f, 3));
        Escribir("Chillido_Enojo", ChillidoEnojo());
        Escribir("Palanca", Palanca());
        Escribir("Paso_Bolita", Paso());
        Escribir("Rueda", Rueda());
        Escribir("Atorado", Atorado());
        Escribir("Campana", Campana());
        Escribir("Ambiente_Feria", Ambiente());
        Escribir("Tobogan", Tobogan());
        AssetDatabase.Refresh();
        foreach (var f in Directory.GetFiles(Carpeta, "*.wav"))
        {
            var ai = AssetImporter.GetAtPath(f.Replace('\\', '/')) as AudioImporter;
            if (ai == null) continue;
            var s = ai.defaultSampleSettings;
            bool largo = f.Contains("Vals") || f.Contains("Galope") || f.Contains("Ambiente") || f.Contains("Rueda");
            s.loadType = largo ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.7f;
            ai.defaultSampleSettings = s;
            ai.SaveAndReimport();
        }
    }

    // ================================================================== utilidades
    static float Hz(float midi) => 440f * Mathf.Pow(2f, (midi - 69f) / 12f);
    static float Rand() => (float)rnd.NextDouble() * 2f - 1f;

    static void Escribir(string nombre, float[] s)
    {
        // normaliza a -1 dBFS
        float max = 0.0001f;
        foreach (var v in s) max = Mathf.Max(max, Mathf.Abs(v));
        float k = 0.89f / max;
        using (var fs = new FileStream(Carpeta + nombre + ".wav", FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            int bytes = s.Length * 2;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            w.Write(System.Text.Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (var v in s) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * k * 32767f), -32768, 32767));
        }
    }

    /// <summary>Suma una nota en un buffer circular (asi los loops empalman sin corte).</summary>
    static void Nota(float[] buf, float t0, float dur, float midi, float vol, System.Func<float, float, float, float> timbre, bool circular = true)
    {
        float f = Hz(midi);
        int i0 = Mathf.RoundToInt(t0 * SR), n = Mathf.RoundToInt(dur * SR);
        for (int i = 0; i < n; i++)
        {
            int j = i0 + i;
            if (circular) j %= buf.Length; else if (j >= buf.Length) break;
            float t = i / (float)SR;
            buf[j] += vol * timbre(t, f, dur);
        }
    }

    // caja musical: puntas metalicas (fundamental + parcial inarmonico), decaen rapido, con "wow" que desafina
    static float Caja(float t, float f, float dur)
    {
        float wow = 1f + 0.006f * Mathf.Sin(t * 2.6f);
        float a = Mathf.Exp(-t * 3.2f) * Mathf.Clamp01(t * 400f);
        float p = 2f * Mathf.PI * f * wow * t;
        return a * (Mathf.Sin(p) + 0.35f * Mathf.Sin(p * 3.01f) * Mathf.Exp(-t * 9f) + 0.18f * Mathf.Sin(p * 5.43f) * Mathf.Exp(-t * 16f));
    }

    // organillo/caliope: pulso con vibrato y un poco de aire
    static float Organo(float t, float f, float dur)
    {
        float vib = 1f + 0.012f * Mathf.Sin(t * 2f * Mathf.PI * 6.2f);
        float fase = Mathf.Repeat(f * vib * t, 1f);
        float pulso = fase < 0.32f ? 1f : -0.45f;
        float sen = Mathf.Sin(2f * Mathf.PI * f * vib * t);
        float env = Mathf.Clamp01(t * 60f) * Mathf.Clamp01((dur - t) * 25f);
        return env * (0.55f * pulso + 0.6f * sen) * 0.6f;
    }

    // tuba: triangulo grave con ataque blando
    static float Tuba(float t, float f, float dur)
    {
        float fase = Mathf.Repeat(f * t, 1f);
        float tri = 4f * Mathf.Abs(fase - 0.5f) - 1f;
        float env = Mathf.Clamp01(t * 30f) * Mathf.Exp(-t * 2.5f) * Mathf.Clamp01((dur - t) * 20f);
        return env * (tri + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 2f * t));
    }

    // theremin: seno con vibrato ancho
    static float Theremin(float t, float f, float dur)
    {
        float vib = 1f + 0.022f * Mathf.Sin(t * 2f * Mathf.PI * 5.1f);
        float env = Mathf.Clamp01(t * 3f) * Mathf.Clamp01((dur - t) * 3f);
        return env * Mathf.Sin(2f * Mathf.PI * f * vib * t);
    }

    static float Ruido(float t, float f, float dur) => Rand() * Mathf.Exp(-t * f);

    // ================================================================== vals (3/4, 100 bpm, 16 compases)
    const float Negra = 0.6f, Compas = Negra * 3f;
    static readonly int[] Raices = { 57, 57, 50, 52, 57, 53, 52, 57, 53, 48, 50, 52, 57, 50, 52, 57 };   // La menor
    static readonly int[][] Acordes =
    {
        new[] { 0, 3, 7 }, new[] { 0, 3, 7 }, new[] { 0, 3, 7 }, new[] { 0, 4, 7 }, new[] { 0, 3, 7 }, new[] { 0, 4, 7 }, new[] { 0, 4, 10 }, new[] { 0, 3, 7 },
        new[] { 0, 4, 7 }, new[] { 0, 4, 7 }, new[] { 0, 3, 7 }, new[] { 0, 4, 7 }, new[] { 0, 3, 7 }, new[] { 0, 3, 6 }, new[] { 0, 4, 7 }, new[] { 0, 3, 7 },
    };
    // melodia: (nota midi, duracion en negras); 0 = silencio
    static readonly float[,] Melodia =
    {
        { 76, 1 }, { 81, 1 }, { 84, 1 },   { 83, 1 }, { 81, 1 }, { 76, 1 },   { 77, 1.5f }, { 76, 0.5f }, { 74, 1 },   { 80, 2 }, { 76, 1 },
        { 81, 1 }, { 84, 1 }, { 88, 1 },   { 87, 1 }, { 86, 1 }, { 84, 1 },   { 83, 1 }, { 80, 1 }, { 86, 1 },     { 84, 2 }, { 0, 1 },
        { 81, 1 }, { 84, 1 }, { 89, 1 },   { 88, 1 }, { 86, 0.5f }, { 84, 0.5f }, { 79, 1 },   { 77, 1 }, { 81, 1 }, { 86, 1 },   { 84, 1 }, { 83, 1 }, { 80, 1 },
        { 81, 1 }, { 76, 1 }, { 72, 1 },   { 74, 1 }, { 77, 1 }, { 80, 1 },   { 83, 1.5f }, { 81, 0.5f }, { 80, 1 },   { 81, 3 },
    };

    static float[] Buffer(float segundos) => new float[Mathf.RoundToInt(segundos * SR)];

    static float[] ValsCaja()
    {
        var b = Buffer(Compas * 16f);
        float t = 0f;
        for (int i = 0; i < Melodia.GetLength(0); i++)
        {
            float n = Melodia[i, 0], d = Melodia[i, 1] * Negra;
            if (n > 0f) Nota(b, t, 2.2f, n + 12f, 0.5f, Caja);
            t += d;
        }
        // arpegio suave del acorde en las corcheas de en medio
        for (int c = 0; c < 16; c++)
            for (int k = 0; k < 3; k++)
            {
                int nota = Raices[c] + 12 + Acordes[c][k];
                Nota(b, c * Compas + Negra * (k + 0.5f), 1.6f, nota + 12, 0.16f, Caja);
            }
        return b;
    }

    static float[] ValsOrganillo()
    {
        var b = Buffer(Compas * 16f);
        for (int c = 0; c < 16; c++)
        {
            float t0 = c * Compas;
            Nota(b, t0, Negra * 0.9f, Raices[c] - 12, 0.6f, Tuba);
            Nota(b, t0, Negra * 0.5f, Raices[c], 0.25f, Organo);
            for (int g = 1; g <= 2; g++)
                foreach (int iv in Acordes[c])
                    Nota(b, t0 + g * Negra, Negra * 0.45f, Raices[c] + 12 + iv, 0.17f, Organo);
        }
        // la melodia en el organo, una octava abajo y un poco tarde (suena "de feria vieja")
        float t = 0.012f;
        for (int i = 0; i < Melodia.GetLength(0); i++)
        {
            float n = Melodia[i, 0], d = Melodia[i, 1] * Negra;
            if (n > 0f) Nota(b, t, d * 0.92f, n, 0.22f, Organo);
            t += d;
        }
        return b;
    }

    static float[] ValsTheremin()
    {
        var b = Buffer(Compas * 16f);
        // linea que baja cromatica, dos compases por nota, y un pulso grave de "corazon"
        int[] linea = { 69, 68, 67, 66, 65, 64, 63, 64 };
        for (int i = 0; i < 8; i++) Nota(b, i * Compas * 2f, Compas * 2f + 0.4f, linea[i], 0.42f, Theremin);
        for (int c = 0; c < 16; c++)
        {
            Nota(b, c * Compas, 0.25f, 33f, 0.7f, (t, f, d) => Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 14f));
            Nota(b, c * Compas + 0.22f, 0.25f, 33f, 0.45f, (t, f, d) => Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 16f));
        }
        return b;
    }

    // ================================================================== galope de persecucion (2/4, 176 bpm, 32 compases)
    static float[] Galope()
    {
        float negra = 60f / 176f, compas = negra * 2f;
        var b = Buffer(compas * 32f);
        int[] raiz = { 50, 50, 57, 57, 50, 55, 57, 50, 58, 58, 53, 53, 55, 50, 57, 50 };
        bool[] menor = { true, true, false, false, true, true, false, true, false, false, false, false, true, true, false, true };
        for (int c = 0; c < 32; c++)
        {
            int k = c % 16; int r = raiz[k]; int ter = menor[k] ? 3 : 4;
            float t0 = c * compas;
            // oom-pah: raiz y quinta en la tuba, acorde en el organo
            Nota(b, t0, negra * 0.6f, r - 12, 0.55f, Tuba);
            Nota(b, t0 + negra, negra * 0.6f, r - 5, 0.5f, Tuba);
            foreach (int iv in new[] { 0, ter, 7 })
            {
                Nota(b, t0 + negra * 0.5f, negra * 0.3f, r + 12 + iv, 0.12f, Organo);
                Nota(b, t0 + negra * 1.5f, negra * 0.3f, r + 12 + iv, 0.12f, Organo);
            }
            // melodia en corcheas: patron cromatico que cambia cada compas
            int[] pat = (c % 4) switch
            {
                0 => new[] { 24, 23, 24, 12 + ter + 12 },
                1 => new[] { 19, 18, 19, 24 },
                2 => new[] { 12 + ter + 12, 19, 12 + ter + 12, 24 },
                _ => new[] { 26, 24, 23, 19 },
            };
            if (c % 16 == 15) pat = new[] { 24, 19, 12 + ter + 12, 12 };
            for (int e = 0; e < 4; e++)
                Nota(b, t0 + e * negra * 0.5f, negra * 0.45f, r + pat[e] + 0.15f * Mathf.Sin(c), 0.3f, Organo);
            // bateria: bombo en 1, caja en 2, hi-hat en corcheas
            Nota(b, t0, 0.2f, 36f, 0.9f, (t, f, d) => Mathf.Sin(2f * Mathf.PI * (60f + 90f * Mathf.Exp(-t * 30f)) * t) * Mathf.Exp(-t * 18f));
            Nota(b, t0 + negra, 0.18f, 22f, 0.35f, Ruido);
            for (int e = 0; e < 4; e++) Nota(b, t0 + e * negra * 0.5f, 0.05f, 60f, 0.08f, Ruido);
            if (c % 8 == 0) Nota(b, t0, 1.2f, 4f, 0.25f, Ruido);   // platillo
        }
        return b;
    }

    // ================================================================== efectos
    static float[] Fanfarria()
    {
        var b = Buffer(3.2f);
        // "ta-daaa": re mayor brillante... y luego "wah-wah" que se desinfla
        Nota(b, 0f, 0.18f, 74, 0.4f, Organo, false); Nota(b, 0f, 0.18f, 78, 0.3f, Organo, false); Nota(b, 0f, 0.18f, 81, 0.3f, Organo, false);
        foreach (int n in new[] { 74, 78, 81, 86 }) Nota(b, 0.25f, 1.1f, n, 0.3f, Organo, false);
        Nota(b, 0.25f, 1.1f, 50, 0.5f, Tuba, false);
        for (int i = 0; i < 2; i++)
        {
            float t0 = 1.5f + i * 0.6f; float n0 = 71f - i * 1f;
            int a = Mathf.RoundToInt(t0 * SR), len = Mathf.RoundToInt(0.55f * SR);
            for (int j = 0; j < len; j++)
            {
                float t = j / (float)SR; float f = Hz(n0 - t * (i == 1 ? 4f : 1f));
                float wah = 0.5f + 0.5f * Mathf.Sin(t * 9f);
                float v = Mathf.Sin(2f * Mathf.PI * f * t) + wah * 0.6f * Mathf.Sin(4f * Mathf.PI * f * t) + wah * 0.3f * Mathf.Sin(6f * Mathf.PI * f * t);
                if (a + j < b.Length) b[a + j] += 0.35f * v * Mathf.Clamp01(t * 30f) * Mathf.Clamp01((0.55f - t) * 8f);
            }
        }
        return b;
    }

    static float[] Escape()
    {
        var b = Buffer(2.6f);
        int[] arp = { 74, 78, 81, 86, 90 };
        for (int i = 0; i < arp.Length; i++) Nota(b, i * 0.1f, 1.8f - i * 0.1f, arp[i], 0.35f, Caja, false);
        Nota(b, 0.5f, 2f, 98, 0.3f, Campanita, false);
        Nota(b, 0f, 1.2f, 50, 0.4f, Tuba, false);
        return b;
    }

    static float Campanita(float t, float f, float dur)
    {
        float p = 2f * Mathf.PI * f * t;
        return Mathf.Exp(-t * 2.2f) * (Mathf.Sin(p) + 0.5f * Mathf.Sin(p * 2.76f) + 0.25f * Mathf.Sin(p * 5.4f)) * Mathf.Clamp01(t * 800f);
    }

    static float[] Campana() { var b = Buffer(2.5f); Nota(b, 0f, 2.5f, 91, 0.6f, Campanita, false); Nota(b, 0f, 2.5f, 79, 0.3f, Campanita, false); return b; }

    static float[] Chillido(float dur, float f0, float f1, int variante)
    {
        var b = Buffer(dur + 0.05f);
        float fase = 0f;
        for (int i = 0; i < Mathf.RoundToInt(dur * SR); i++)
        {
            float t = i / (float)SR, u = t / dur;
            float f = variante == 2 ? Mathf.Lerp(f0, f1, Mathf.Sin(u * Mathf.PI)) + 300f * Mathf.Sin(u * 40f)
                                    : Mathf.Lerp(f0, f1, Mathf.Sin(u * Mathf.PI * (variante == 3 ? 2f : 1f)) * 0.5f + u * 0.5f);
            fase += f / SR;
            float am = 0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * 38f);
            float env = Mathf.Clamp01(t * 200f) * Mathf.Clamp01((dur - t) * 30f);
            b[i] = env * am * (Mathf.Sin(2f * Mathf.PI * fase) + 0.25f * Mathf.Sin(4f * Mathf.PI * fase)) + 0.05f * Rand() * env;
        }
        return b;
    }

    static float[] ChillidoEnojo()
    {
        var b = Buffer(0.9f);
        float fase = 0f;
        for (int i = 0; i < b.Length - 2000; i++)
        {
            float t = i / (float)SR;
            float f = 1800f + 900f * Mathf.Abs(Mathf.Sin(t * 22f)) + 400f * t;
            fase += f / SR;
            float env = Mathf.Clamp01(t * 100f) * Mathf.Clamp01((0.85f - t) * 10f);
            float aspero = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * fase)) * 0.3f + Mathf.Sin(2f * Mathf.PI * fase);
            b[i] = env * aspero * (0.7f + 0.3f * Mathf.Sin(t * 2f * Mathf.PI * 55f));
        }
        return b;
    }

    static float[] Palanca()
    {
        var b = Buffer(0.8f);
        // trinquete (clics) y golpe metalico al final
        for (int k = 0; k < 6; k++) Nota(b, k * 0.045f, 0.03f, 90f, 0.35f, Ruido, false);
        Nota(b, 0.3f, 0.45f, 40f, 0.9f, (t, f, d) => Mathf.Sin(2f * Mathf.PI * (f + 40f * Mathf.Exp(-t * 20f)) * t) * Mathf.Exp(-t * 12f), false);
        Nota(b, 0.3f, 0.4f, 79f, 0.25f, Campanita, false);
        Nota(b, 0.3f, 0.06f, 30f, 0.5f, Ruido, false);
        return b;
    }

    static float[] Paso()
    {
        var b = Buffer(0.25f);
        Nota(b, 0f, 0.25f, 31f, 1f, (t, f, d) => Mathf.Sin(2f * Mathf.PI * (f + 60f * Mathf.Exp(-t * 40f)) * t) * Mathf.Exp(-t * 22f), false);
        Nota(b, 0f, 0.03f, 70f, 0.25f, Ruido, false);
        return b;
    }

    static float[] Rueda()
    {
        // vuelta de rueda: rechinido que sube y baja, empalma en loop
        var b = Buffer(1.2f);
        float fase = 0f;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)SR, u = t / 1.2f;
            float env = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 2f);
            float f = 620f + 260f * Mathf.Sin(u * Mathf.PI * 2f) + 30f * Rand();
            fase += f / SR;
            float saw = 2f * Mathf.Repeat(fase, 1f) - 1f;
            b[i] = env * saw * (0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI * 31f)) * 0.6f;
            // traqueteo de la rueda
            if (Mathf.Repeat(t, 0.15f) < 0.004f) b[i] += 0.5f * Rand();
        }
        return b;
    }

    static float[] Atorado()
    {
        // "nnnnggghhh..." chillido estirado que se esfuerza, y un "plop" al final
        var b = Buffer(2.2f);
        float fase = 0f;
        for (int i = 0; i < Mathf.RoundToInt(1.8f * SR); i++)
        {
            float t = i / (float)SR;
            float f = 1500f + 700f * t + 120f * Mathf.Sin(t * 30f);
            fase += f / SR;
            float env = Mathf.Clamp01(t * 20f) * Mathf.Clamp01((1.8f - t) * 6f);
            b[i] = env * (Mathf.Sin(2f * Mathf.PI * fase) * (0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * 13f)));
        }
        Nota(b, 1.85f, 0.3f, 60f, 1f, (t, f, d) => Mathf.Sin(2f * Mathf.PI * (f * (1f + 2f * Mathf.Exp(-t * 30f))) * t) * Mathf.Exp(-t * 18f), false);
        return b;
    }

    static float[] Ambiente()
    {
        // zumbido grave que respira y crujidos de plastico viejo (loop de 12 s)
        var b = Buffer(12f);
        float lp = 0f;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)SR;
            lp += (Rand() - lp) * 0.02f;
            float resp = 0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / 6f);
            b[i] = 0.5f * lp * resp + 0.12f * Mathf.Sin(2f * Mathf.PI * 55f * t) * (0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI / 12f));
        }
        for (int k = 0; k < 7; k++)
        {
            float t0 = (float)rnd.NextDouble() * 11f;
            for (int c = 0; c < 5 + rnd.Next(6); c++) Nota(b, t0 + c * 0.03f + (float)rnd.NextDouble() * 0.02f, 0.02f, 80f, 0.25f, Ruido);
        }
        return b;
    }

    static float[] Tobogan()
    {
        var b = Buffer(1.6f);
        float lp = 0f;
        for (int i = 0; i < b.Length; i++)
        {
            float t = i / (float)SR;
            lp += (Rand() - lp) * 0.08f;
            float env = Mathf.Clamp01(t * 8f) * Mathf.Clamp01((1.6f - t) * 3f);
            b[i] = env * lp;
        }
        // "wiii" de silbato de embolo que baja
        Nota(b, 0f, 1.4f, 0f, 0.35f, (t, f, d) => Mathf.Sin(2f * Mathf.PI * (1600f - 700f * t) * t) * Mathf.Clamp01(t * 20f) * Mathf.Clamp01((1.4f - t) * 4f), false);
        return b;
    }
}
