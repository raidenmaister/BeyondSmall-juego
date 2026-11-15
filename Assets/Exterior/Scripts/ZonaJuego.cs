using UnityEngine;

/// <summary>
/// Limites del mundo jugable: el salon (interior) y el exterior, que llega hasta la cerca circular.
/// Tambien define la altura del suelo exterior, que comparten el generador del terreno y las IA
/// de los bichos para caminar sobre las colinas.
/// </summary>
public static class ZonaJuego
{
    // interior caminable del salon para los bichos (no atraviesan las paredes)
    public static readonly Vector2 salonMin = new Vector2(-69f, -92f), salonMax = new Vector2(69f, 80f);
    // huella exterior del edificio, paredes incluidas
    public static readonly Vector2 edificioMin = new Vector2(-80.5f, -98.5f), edificioMax = new Vector2(81.5f, 87f);
    public static readonly Vector3 centro = new Vector3(0f, 0f, -6f);
    public const float radioCerca = 420f;
    public const float alturaPiso = -0.06f;

    // hormiguero: entrada en el suelo de afuera; el suelo exterior tiene un hueco de este radio
    public static readonly Vector2 hormigueroEntrada = new Vector2(205f, -150f);
    public const float hormigueroRadio = 40f;

    public static float DistanciaHormiguero(float x, float z)
    {
        float dx = x - hormigueroEntrada.x, dz = z - hormigueroEntrada.y;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // arbol hueco con la colmena: el terreno se aplana alrededor y el suelo exterior tiene un hueco
    public static readonly Vector2 arbolCentro = new Vector2(-215f, -140f);
    public const float arbolRadio = 46f;

    public static float DistanciaArbol(float x, float z)
    {
        float dx = x - arbolCentro.x, dz = z - arbolCentro.y;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // cloaca: alcantarilla en el jardin; al oeste de la rejilla esta el derrumbe por donde se baja.
    // El suelo exterior tiene huecos rectangulares (locales a la alcantarilla): ahi el suelo lo pone la malla de la cloaca.
    // [0] tapa + derrumbe, [1] rejilla cuadrada sobre la camara del Rey Rata.
    // Las esquinas caen en la rejilla de 4 unidades del suelo exterior (x multiplo de 4, z = 2 mod 4).
    public static readonly Vector2 alcantarilla = new Vector2(60f, 210f);
    public static readonly Rect[] cloacaHuecos = { Rect.MinMaxRect(-104f, -20f, 16f, 20f), Rect.MinMaxRect(212f, -4f, 220f, 4f) };

    /// <summary>Distancia (en planta) al hueco de la cloaca mas cercano: 0 dentro.</summary>
    public static float DistanciaCloaca(float x, float z)
    {
        float lx = x - alcantarilla.x, lz = z - alcantarilla.y, best = float.MaxValue;
        foreach (var r in cloacaHuecos)
        {
            float dx = Mathf.Max(r.xMin - lx, 0f, lx - r.xMax);
            float dz = Mathf.Max(r.yMin - lz, 0f, lz - r.yMax);
            best = Mathf.Min(best, dx * dx + dz * dz);
        }
        return Mathf.Sqrt(best);
    }

    public static bool EnHuecoCloaca(float x, float z) { return DistanciaCloaca(x, z) <= 0f; }

    public static bool EnEdificio(Vector3 p, float margen = 0f)
    {
        return p.x > edificioMin.x - margen && p.x < edificioMax.x + margen && p.z > edificioMin.y - margen && p.z < edificioMax.y + margen;
    }

    public static float DistanciaEdificio(float x, float z)
    {
        float dx = Mathf.Max(edificioMin.x - x, 0f, x - edificioMax.x);
        float dz = Mathf.Max(edificioMin.y - z, 0f, z - edificioMax.y);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    public static float DistanciaCentro(float x, float z)
    {
        float dx = x - centro.x, dz = z - centro.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Altura del suelo exterior: colinas suaves, plano junto al edificio para que la puerta quede al ras.</summary>
    public static float Altura(float x, float z)
    {
        // junto al arbol el suelo queda plano (las raices y la entrada al ras)
        float da = DistanciaArbol(x, z);
        if (da < 80f)
        {
            float plano = AlturaBase(arbolCentro.x, arbolCentro.y);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(50f, 80f, da));
            return Mathf.Lerp(plano, AlturaBase(x, z), t);
        }
        return AlturaBase(x, z);
    }

    static float AlturaBase(float x, float z)
    {
        float d = DistanciaEdificio(x, z);
        if (d <= 0f) return alturaPiso;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 80f, d));
        if (k <= 0f) return alturaPiso;
        float h = (Mathf.PerlinNoise(x * 0.0075f + 31.7f, z * 0.0075f + 12.3f) - 0.5f) * 16f
                + (Mathf.PerlinNoise(x * 0.028f + 5.1f, z * 0.028f + 77.9f) - 0.5f) * 3.5f;
        return alturaPiso + h * k;
    }

    /// <summary>Mantiene una posicion dentro de su zona: el salon, o el exterior entre el edificio y la cerca.</summary>
    public static Vector3 Limitar(Vector3 p, bool enSalon, float margen = 3f)
    {
        if (enSalon)
        {
            p.x = Mathf.Clamp(p.x, salonMin.x, salonMax.x);
            p.z = Mathf.Clamp(p.z, salonMin.y, salonMax.y);
            return p;
        }
        float dx = p.x - centro.x, dz = p.z - centro.z;
        float max = radioCerca - margen;
        float d2 = dx * dx + dz * dz;
        if (d2 > max * max)
        {
            float k = max / Mathf.Sqrt(d2);
            p.x = centro.x + dx * k; p.z = centro.z + dz * k;
        }
        // ni al tronco del arbol
        float da = DistanciaArbol(p.x, p.z), ra = arbolRadio + margen;
        if (da < ra && da > 0.001f)
        {
            float k = ra / da;
            p.x = arbolCentro.x + (p.x - arbolCentro.x) * k;
            p.z = arbolCentro.y + (p.z - arbolCentro.y) * k;
        }
        // los bichos de afuera no se meten al monticulo del hormiguero
        float dh = DistanciaHormiguero(p.x, p.z), rh = hormigueroRadio * 0.6f + margen;
        if (dh < rh && dh > 0.001f)
        {
            float k = rh / dh;
            p.x = hormigueroEntrada.x + (p.x - hormigueroEntrada.x) * k;
            p.z = hormigueroEntrada.y + (p.z - hormigueroEntrada.y) * k;
        }
        // ni al derrumbe ni a la rejilla de la cloaca (las ratas de la cloaca no usan esto)
        foreach (var h in cloacaHuecos)
        {
            float lx = p.x - alcantarilla.x, lz = p.z - alcantarilla.y, m2 = margen + 3f;
            float l = lx - (h.xMin - m2), r = (h.xMax + m2) - lx;
            float b = lz - (h.yMin - m2), t = (h.yMax + m2) - lz;
            if (l > 0f && r > 0f && b > 0f && t > 0f)
            {
                float m = Mathf.Min(Mathf.Min(l, r), Mathf.Min(b, t));
                if (m == l) p.x -= l; else if (m == r) p.x += r; else if (m == b) p.z -= b; else p.z += t;
            }
        }
        if (EnEdificio(p, margen))
        {   // empujar hacia fuera por el lado mas cercano
            float l = p.x - (edificioMin.x - margen), r = (edificioMax.x + margen) - p.x;
            float b = p.z - (edificioMin.y - margen), t = (edificioMax.y + margen) - p.z;
            float m = Mathf.Min(Mathf.Min(l, r), Mathf.Min(b, t));
            if (m == l) p.x = edificioMin.x - margen;
            else if (m == r) p.x = edificioMax.x + margen;
            else if (m == b) p.z = edificioMin.y - margen;
            else p.z = edificioMax.y + margen;
        }
        return p;
    }

    /// <summary>True si la posicion toca el limite de su zona (pared del salon, pared exterior o cerca).</summary>
    public static bool EnBorde(Vector3 p, bool enSalon)
    {
        if (enSalon)
            return p.x <= salonMin.x + 0.1f || p.x >= salonMax.x - 0.1f || p.z <= salonMin.y + 0.1f || p.z >= salonMax.y - 0.1f;
        return DistanciaCentro(p.x, p.z) >= radioCerca - 3.1f || EnEdificio(p, 3.1f);
    }

    /// <summary>Altura del suelo para un bicho: el piso del salon o las colinas de afuera.</summary>
    public static float Suelo(Vector3 p, bool enSalon)
    {
        return enSalon ? alturaPiso : Altura(p.x, p.z);
    }
}
