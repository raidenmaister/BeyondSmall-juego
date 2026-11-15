#ifndef EXTERIOR_COMUN_INCLUDED
#define EXTERIOR_COMUN_INCLUDED

// Utilidades compartidas por los shaders del exterior.

float3 OrigenObjeto()
{
    return float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
}

float HashExterior(float2 p)
{
    return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
}

// Balanceo por viento: mas fuerte cuanto mas alto esta el vertice sobre la base de la planta.
float3 AplicarViento(float3 posWS, float fuerza, float velocidad)
{
    float3 o = OrigenObjeto();
    float alto = max(0.0, posWS.y - o.y);
    float fase = _Time.y * velocidad + o.x * 0.05 + o.z * 0.043;
    float2 rafaga = float2(sin(fase) + 0.45 * sin(fase * 2.3 + 1.7), 0.5 * cos(fase * 0.7));
    float aleteo = sin(_Time.y * velocidad * 4.0 + posWS.x * 0.9 + posWS.z * 0.7) * 0.25;
    posWS.xz += (rafaga + aleteo) * alto * fuerza;
    return posWS;
}

// Niebla con densidad escalada (para que las montañas lejanas se sigan viendo).
half3 NieblaEscalada(half3 color, float distancia, float escala)
{
#if defined(FOG_EXP2)
    float f = unity_FogParams.x * distancia * escala;
    float k = saturate(exp2(-f * f));
    return lerp(unity_FogColor.rgb, color, k);
#elif defined(FOG_EXP)
    float k = saturate(exp2(-unity_FogParams.y * distancia * escala));
    return lerp(unity_FogColor.rgb, color, k);
#elif defined(FOG_LINEAR)
    float k = saturate(distancia * escala * unity_FogParams.z + unity_FogParams.w);
    return lerp(unity_FogColor.rgb, color, k);
#else
    return color;
#endif
}

// Las plantas pegadas a la camara se desvanecen para no tapar al jugador.
// Antes era un tramado Bayer 4x4: se veia una rejilla de puntos gruesa ("texturas empalmadas"). Ahora es ruido de
// gradiente entrelazado (Jimenez 2014): grano fino sin patron que se repita, y la franja de fundido es corta.
float RuidoIGN(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// Pecho del jugador (xyz) y w = 1 si hay jugador. Lo pone FollajeJugador.cs cada cuadro.
float4 _BS_Jugador;

void DesvanecerCercaCamara(float3 posWS, float4 posCS, float inicio, float fin)
{
    float3 cam = _WorldSpaceCameraPos;
    float d = distance(posWS, cam);
    float k;
    if (_BS_Jugador.w > 0.5)
    {
        // solo desaparece lo que TAPA al jugador: un tunel entre la camara y su pecho (mas ancho cerca de el).
        // El resto de plantas cercanas queda solido y nitido (antes todo lo cercano salia tramado).
        float3 ab = _BS_Jugador.xyz - cam;
        float t = saturate(dot(posWS - cam, ab) / max(dot(ab, ab), 1e-4));
        float r = distance(posWS, cam + ab * t);
        float radio = lerp(0.45, 1.0, t);
        float tubo = smoothstep(radio, radio + 0.2, r);
        tubo = max(tubo, step(0.96, t));                        // lo que esta detras del jugador no estorba
        k = min(tubo, smoothstep(inicio * 0.55, inicio * 0.75, d));   // y una hoja pegada a la lente tampoco se dibuja
    }
    else k = smoothstep(inicio, max(fin, inicio + 0.01), d);   // sin jugador (menu, fotos): como antes
    if (k >= 0.999) return;                    // lo normal: nada que recortar
    clip(k - RuidoIGN(floor(posCS.xy)) * 0.98 - 0.01);
}

half4 EmpaquetarNormalDepthNormals(float3 normalWS)
{
#if defined(_GBUFFER_NORMALS_OCT)
    float2 oct = PackNormalOctQuadEncode(normalWS);
    float2 remap = saturate(oct * 0.5 + 0.5);
    return half4(PackFloat2To888(remap), 0.0);
#else
    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
#endif
}

#endif
