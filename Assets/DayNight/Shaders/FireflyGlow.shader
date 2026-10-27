// Halo aditivo de la luciernaga: un quad que siempre mira a la camara (billboard
// en el vertex shader) con caida radial suave. _Intensity se anima por instancia
// con un MaterialPropertyBlock desde Firefly.cs.
Shader "Custom/FireflyGlow"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.5, 1.6, 0.25, 1)
        _Intensity ("Intensidad", Float) = 1
        _Size ("Tamano (m)", Float) = 1.2
        _Core ("Nucleo", Range(0, 4)) = 1.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent+10" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
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
                half4 _Color;
                float _Intensity, _Size, _Core;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 camRight = UNITY_MATRIX_V[0].xyz;
                float3 camUp = UNITY_MATRIX_V[1].xyz;
                float3 world = center + (camRight * v.positionOS.x + camUp * v.positionOS.y) * _Size;
                o.positionCS = TransformWorldToHClip(world);
                o.uv = v.uv * 2 - 1;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                float glow = exp(-r2 * 5.0) * 0.6 + exp(-r2 * 40.0) * _Core;
                glow *= saturate(1 - r2);
                half3 col = _Color.rgb * glow * _Intensity;
                // con mezcla aditiva la niebla debe llevar el color a negro, no al color de niebla
                col = MixFogColor(col, half3(0, 0, 0), i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
