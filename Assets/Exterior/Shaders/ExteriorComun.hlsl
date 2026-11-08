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

// Tramado 4x4: las plantas pegadas a la camara se desvanecen para no tapar al jugador.
float Bayer4(float2 pixel)
{
    uint2 q = uint2(pixel) % 4;
    const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
    return (m[q.y * 4 + q.x] + 0.5) / 16.0;
}

void DesvanecerCercaCamara(float3 posWS, float4 posCS, float inicio, float fin)
{
    float d = distance(posWS, _WorldSpaceCameraPos);
    clip(saturate((d - inicio) / max(fin - inicio, 0.01)) - Bayer4(posCS.xy));
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
