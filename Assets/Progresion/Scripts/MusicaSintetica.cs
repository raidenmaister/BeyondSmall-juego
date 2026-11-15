using UnityEngine;

/// <summary>
/// Musica del final hecha por codigo (como el vals de Hamsterlandia):
///   Tension()  la cuenta atras: cuerdas graves en menor, latido y un violin que chirria de vez en cuando.
///   Batalla()  la hecatombe: ostinato de violines estridentes a 160 bpm en re menor, tremolo, timbales,
///              metales que golpean y glissandos que chillan. Bucle de 12 s.
///   Victoria() la llegada: fanfarria en re mayor con metales, campanas y redoble.
/// </summary>
public static class MusicaSintetica
{
    const int Hz = 32000;
    static AudioClip tension, batalla, victoria;

    static float Nota(int semitonosDesdeLa4) => 440f * Mathf.Pow(2f, semitonosDesdeLa4 / 12f);

    // ---- osciladores (fase acumulada para no tener chasquidos)
    static float Sierra(float fase) => 2f * (fase - Mathf.Floor(fase + 0.5f));
    static float Seno(float fase) => Mathf.Sin(fase * 2f * Mathf.PI);
    static uint semilla = 1234567;
    static float Ruido() { semilla ^= semilla << 13; semilla ^= semilla >> 17; semilla ^= semilla << 5; return (semilla / (float)uint.MaxValue) * 2f - 1f; }

    /// <summary>"Violin": sierras desafinadas con vibrato, filtrada a lo bruto (media movil) y con arco (ataque).</summary>
    class Cuerda
    {
        float f1, f2, f3, filtro;
        public float Muestra(float frec, float t, float vibrato, float brillo)
        {
            float v = 1f + 0.006f * Mathf.Sin(t * 2f * Mathf.PI * 5.5f) * vibrato;
            f1 += frec * v / Hz; f2 += frec * v * 1.004f / Hz; f3 += frec * v * 0.996f / Hz;
            f1 -= Mathf.Floor(f1); f2 -= Mathf.Floor(f2); f3 -= Mathf.Floor(f3);
            float s = (Sierra(f1) + Sierra(f2) * 0.8f + Sierra(f3) * 0.8f) / 2.6f;
            filtro += (s - filtro) * brillo;           // brillo 0..1: cuanto mas, mas estridente
            return filtro;
        }
    }

    // ------------------------------------------------------------------ tension
    public static AudioClip Tension()
    {
        if (tension != null) return tension;
        float dur = 16f; int n = (int)(dur * Hz);
        var d = new float[n];
        var bajo = new Cuerda(); var medio = new Cuerda(); var agudo = new Cuerda();
        int[] acordes = { -19, -20, -17, -21 };     // re, do#, mi, do (graves): inquietante
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Hz;
            int c = (int)(t / 4f) % 4;
            float b = bajo.Muestra(Nota(acordes[c]), t, 0.5f, 0.05f) * 0.35f;
            float m = medio.Muestra(Nota(acordes[c] + 15), t, 1f, 0.06f) * 0.16f * (0.6f + 0.4f * Mathf.Sin(t * 0.8f));
            // latido cada segundo (dos golpes)
            float tl = t % 1f;
            float lat = (Seno(tl * 55f) * Mathf.Exp(-tl * 18f) + (tl > 0.22f ? Seno((tl - 0.22f) * 50f) * Mathf.Exp(-(tl - 0.22f) * 20f) * 0.7f : 0f)) * 0.5f;
            // violin que chirria (glissando hacia arriba) en el compas 3 de cada 4
            float tg = t % 8f;
            float chirrido = 0f;
            if (tg > 5.5f && tg < 7.5f)
            {
                float k = (tg - 5.5f) / 2f;
                chirrido = agudo.Muestra(Nota(10 + (int)(k * 14f)) * (1f + k * 0.08f), t, 2f, 0.35f) * 0.09f * Mathf.Sin(k * Mathf.PI);
            }
            d[i] = Mathf.Clamp((b + m + lat + chirrido) * Envolvente(t, dur), -1f, 1f);
        }
        return tension = Crear("Musica_Tension", d);
    }

    // ------------------------------------------------------------------ batalla
    public static AudioClip Batalla()
    {
        if (batalla != null) return batalla;
        const float bpm = 160f; float negra = 60f / bpm, semi = negra / 4f;
        float dur = negra * 32f;                       // 8 compases
        int n = (int)(dur * Hz);
        var d = new float[n];
        var v1 = new Cuerda(); var v2 = new Cuerda(); var cello = new Cuerda(); var grito = new Cuerda(); var metal = new Cuerda();
        // re menor: re fa la / sib re fa / do mi sol / la do# mi (dominante)
        int[] raiz = { -7, -11, -9, -12 };
        int[][] arpegio = { new[] { 0, 3, 7, 12, 7, 3, 0, 3 }, new[] { 0, 4, 7, 12, 7, 4, 0, 4 }, new[] { 0, 4, 7, 12, 7, 4, 0, 4 }, new[] { 0, 4, 7, 10, 7, 4, 0, 4 } };
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Hz;
            int compas = (int)(t / (negra * 4f)) % 8;
            int ac = (compas / 2) % 4;
            int paso = (int)(t / semi);
            float ts = t - paso * semi;
            // violines: semicorcheas de arpegio con arco cortado (spiccato) y tremolo
            int nota = raiz[ac] + 12 + arpegio[ac][paso % 8];
            float arco = Mathf.Clamp01(ts / 0.008f) * Mathf.Exp(-ts * 9f);
            float s = v1.Muestra(Nota(nota), t, 0.4f, 0.45f) * arco * 0.3f;
            s += v2.Muestra(Nota(nota + 12), t, 0.4f, 0.55f) * arco * 0.14f;
            // chelos: negras graves y pesadas
            float tn = t % negra;
            s += cello.Muestra(Nota(raiz[ac] - 12), t, 0.2f, 0.12f) * Mathf.Exp(-tn * 3f) * 0.4f;
            // timbal en el 1 y el 3, redoble en el ultimo compas
            float tt = t % (negra * 2f);
            float timbal = Seno(tt * (70f - tt * 30f)) * Mathf.Exp(-tt * 7f) * 0.55f;
            if (compas == 7) timbal += Ruido() * 0.12f * (0.5f + 0.5f * Seno(t * 22f));
            s += timbal;
            // metales: golpe en el primer tiempo de cada dos compases
            float tc = t % (negra * 8f);
            if (tc < negra * 1.5f) s += metal.Muestra(Nota(raiz[ac] + 7), t, 0.1f, 0.3f) * Mathf.Exp(-tc * 2.2f) * 0.22f;
            // violin estridente: glissando que chilla en los compases 4 y 8
            if (compas == 3 || compas == 7)
            {
                float k = (t % (negra * 4f)) / (negra * 4f);
                s += grito.Muestra(Nota(17) * (1f + k * 0.5f), t, 3f, 0.8f) * 0.12f * Mathf.Sin(k * Mathf.PI);
            }
            d[i] = Mathf.Clamp(s * 0.9f, -1f, 1f);
        }
        // empalme del bucle
        for (int i = 0; i < 400; i++) { float k = i / 400f; d[i] *= k; d[n - 1 - i] *= k; }
        return batalla = Crear("Musica_Hecatombe", d);
    }

    // ------------------------------------------------------------------ victoria
    public static AudioClip Victoria()
    {
        if (victoria != null) return victoria;
        float dur = 11f; int n = (int)(dur * Hz);
        var d = new float[n];
        var m1 = new Cuerda(); var m2 = new Cuerda(); var m3 = new Cuerda(); var bajo = new Cuerda();
        // fanfarria en re mayor: re-re-re-la | re (largo) | fa#-sol-la | re (agudo, largo)
        (float t0, float dur, int nota)[] mel =
        {
            (0f, 0.18f, -7), (0.2f, 0.18f, -7), (0.4f, 0.18f, -7), (0.6f, 0.6f, 0),
            (1.3f, 0.9f, 5), (2.3f, 0.3f, 2), (2.65f, 0.3f, 4), (3f, 0.3f, 5), (3.4f, 2.5f, 17),
        };
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / Hz;
            float s = 0f;
            foreach (var x in mel)
            {
                if (t < x.t0 || t > x.t0 + x.dur + 0.4f) continue;
                float tt = t - x.t0;
                float env = Mathf.Clamp01(tt / 0.02f) * (tt < x.dur ? 1f : Mathf.Exp(-(tt - x.dur) * 10f));
                s += m1.Muestra(Nota(x.nota), t, 0.6f, 0.35f) * env * 0.22f;
                s += m2.Muestra(Nota(x.nota - 5), t, 0.6f, 0.3f) * env * 0.12f;
            }
            // acorde final largo (re mayor) con campanas
            if (t > 3.4f)
            {
                float tt = t - 3.4f;
                s += m3.Muestra(Nota(5), t, 1f, 0.2f) * Mathf.Exp(-tt * 0.35f) * 0.1f;
                s += bajo.Muestra(Nota(-19), t, 0.2f, 0.1f) * Mathf.Exp(-tt * 0.4f) * 0.3f;
                for (int c = 0; c < 6; c++)
                {
                    float tc = tt - c * 0.35f;
                    if (tc < 0f) continue;
                    int[] camp = { 17, 21, 24, 29, 24, 21 };
                    s += Seno(Nota(camp[c]) * tc) * Mathf.Exp(-tc * 3f) * 0.08f;
                }
            }
            // redoble de timbal al principio y golpe en la nota larga
            if (t < 0.6f) s += Ruido() * 0.1f * (0.5f + 0.5f * Seno(t * 25f));
            float tg = t - 3.4f;
            if (tg > 0f) s += Seno(tg * (60f - tg * 20f)) * Mathf.Exp(-tg * 4f) * 0.5f;
            d[i] = Mathf.Clamp(s * Mathf.Clamp01((dur - t) / 1.5f), -1f, 1f);
        }
        return victoria = Crear("Musica_Victoria", d);
    }

    static float Envolvente(float t, float dur) => Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((dur - t) / 0.05f);

    static AudioClip Crear(string nombre, float[] d)
    {
        var c = AudioClip.Create(nombre, d.Length, 1, Hz, false);
        c.SetData(d, 0);
        return c;
    }
}
