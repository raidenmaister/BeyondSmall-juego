// Llama para particulas (URP). Cada particula es una lengua de fuego: forma de gota
// deformada por ruido que sube con el tiempo; el color sale de una rampa
// blanco-amarillo -> naranja -> rojo segun la "temperatura" (vertex color alpha =
// vida restante de la particula). Mezcla aditiva para que el bloom la haga brillar.
Shader "Custom/FireFlame"
{
    Properties
    {
        _Intensity ("Intensidad", Float) = 3
        [HDR] _ColorCore ("Nucleo", Color) = (1.6, 1.35, 0.8, 1)
        [HDR] _ColorMid ("Medio", Color) = (1.5, 0.55, 0.1, 1)
        [HDR] _ColorEdge ("Borde", Color) = (0.7, 0.08, 0.02, 1)
        _NoiseScale ("Escala ruido", Float) = 3
        _NoiseSpeed ("Velocidad ruido", Float) = 2.2
        _Distortion ("Distorsion", Range(0, 0.5)) = 0.22
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorCore, _ColorMid, _ColorEdge;
                float _Intensity, _NoiseScale, _NoiseSpeed, _Distortion;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float seed : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv.xy;
                // semilla por particula (vertex stream StableRandom.x empaquetado en TEXCOORD0.z)
                o.seed = v.uv.z;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), u.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p) { return noise(p) * 0.6 + noise(p * 2.1 + 3.7) * 0.3 + noise(p * 4.3 + 7.1) * 0.1; }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y * _NoiseSpeed;
                // el ruido sube: las llamas "lamen" hacia arriba
                float2 nuv = float2(uv.x * _NoiseScale + i.seed * 10, uv.y * _NoiseScale * 0.8 - t);
                float n = fbm(nuv);
                uv.x += (n - 0.5) * _Distortion * (0.3 + uv.y);
                // forma de gota: ancha abajo, puntiaguda arriba
                float x = (uv.x - 0.5) * 2;
                float y = uv.y;
                float ancho = lerp(0.85, 0.05, pow(saturate(y), 0.9));
                float forma = saturate(1 - abs(x) / max(ancho, 0.001));
                forma *= smoothstep(0.0, 0.12, y) * smoothstep(1.0, 0.55, y);
                forma = pow(forma, 0.8);
                float llama = saturate(forma * (0.55 + n * 0.9) - 0.15);

                float vida = i.color.a;               // 1 recien nacida, 0 al morir
                float temp = saturate(llama * 1.4 * (0.4 + vida * 0.8));
                half3 col = lerp(_ColorEdge.rgb, _ColorMid.rgb, smoothstep(0.05, 0.45, temp));
                col = lerp(col, _ColorCore.rgb, smoothstep(0.5, 0.95, temp));
                col *= llama * _Intensity * i.color.rgb * saturate(vida * 2.5);
                col = MixFogColor(col, half3(0, 0, 0), i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
