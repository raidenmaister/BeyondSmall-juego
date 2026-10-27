// Abdomen bioluminiscente de la luciernaga. Emite luz verde-amarilla cuya fuerza
// (_Glow) pulsa por instancia; el centro de cada segmento brilla mas que los
// bordes (fresnel invertido) para dar sensacion de tejido translucido.
Shader "Custom/FireflyLantern"
{
    Properties
    {
        _BaseColor ("Color apagado", Color) = (0.55, 0.6, 0.35, 1)
        [HDR] _GlowColor ("Color de luz", Color) = (0.55, 1.6, 0.2, 1)
        _Glow ("Brillo", Float) = 1
        _GlowBoost ("Intensidad maxima", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _GlowColor;
                float _Glow, _GlowBoost;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndv = saturate(dot(n, v));
                Light main = GetMainLight();
                half3 diffuse = _BaseColor.rgb * (main.color * saturate(dot(n, main.direction)) * 0.6 + SampleSH(n));
                float core = lerp(0.45, 1.0, pow(ndv, 0.7));
                half3 emission = _GlowColor.rgb * _Glow * _GlowBoost * core;
                half3 col = diffuse + emission;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
