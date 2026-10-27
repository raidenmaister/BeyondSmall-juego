// Particula suave (humo / chispas). Textura * color de la particula * tinte HDR.
// Sale premultiplicada: con _DstBlend = OneMinusSrcAlpha es mezcla normal (humo)
// y con _DstBlend = One es aditiva (chispas).
Shader "Custom/ParticleSoft"
{
    Properties
    {
        _MainTex ("Textura", 2D) = "white" {}
        [HDR] _Tint ("Tinte", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destino", Float) = 10
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Plane" }
        Blend One [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                float _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color * _Tint;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                c.rgb *= c.a;
                // aditivo: la niebla apaga hacia negro; normal: hacia el color de la niebla
                c.rgb = MixFogColor(c.rgb, _DstBlend > 5 ? unity_FogColor.rgb * c.a : half3(0, 0, 0), i.fog);
                return c;
            }
            ENDHLSL
        }
    }
}
