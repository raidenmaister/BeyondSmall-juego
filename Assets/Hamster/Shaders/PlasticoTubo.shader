// Plastico de tubo de hamster: transparente y tintado, pero mas opaco de canto que de frente (fresnel), asi se lee la
// forma redonda del tubo como en los de verdad. Reflejo del entorno, brillo del sol y de las luces, y un poco de
// "lechoso" en el color. Se ven las dos caras (la de dentro tambien, al ir por el tubo).
Shader "Hamster/PlasticoTubo"
{
    Properties
    {
        _BaseColor("Color", Color) = (0.3,0.8,1,1)
        _Claro("Opacidad de frente", Range(0,1)) = 0.14
        _Canto("Opacidad de canto", Range(0,1)) = 0.85
        _Brillo("Brillo", Float) = 1.6
        _Lechoso("Lechoso", Range(0,1)) = 0.18
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Claro, _Canto, _Brillo, _Lechoso;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half niebla : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.niebla = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool frente : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS) * (frente ? 1 : -1);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                half nv = saturate(dot(n, V));
                half fres = pow(1.0 - nv, 2.2);
                half alfa = lerp(_Claro, _Canto, fres);

                Light S = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half luz = saturate(dot(n, S.direction)) * 0.6 + 0.4;
                half3 ambiente = SampleSH(n);
                half3 col = _BaseColor.rgb * (S.color * luz * S.shadowAttenuation * 0.7 + ambiente);
                col = lerp(col, half3(1, 1, 1) * (ambiente + 0.2), _Lechoso * (1 - fres));

                // reflejo del entorno en los cantos
                half3 entorno = GlossyEnvironmentReflection(reflect(-V, n), i.positionWS, 0.05h, 1.0h, GetNormalizedScreenSpaceUV(i.positionCS));
                col += entorno * fres * 0.6;

                // brillos (sol y luces de la feria)
                half3 H = normalize(S.direction + V);
                half3 spec = S.color * S.shadowAttenuation * pow(saturate(dot(n, H)), 140.0) * _Brillo;
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint cuenta = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(cuenta)
                    Light L = GetAdditionalLight(lightIndex, i.positionWS);
                    half3 HL = normalize(L.direction + V);
                    spec += L.color * L.distanceAttenuation * pow(saturate(dot(n, HL)), 90.0) * _Brillo * 0.5;
                LIGHT_LOOP_END
                col += spec;
                alfa = saturate(alfa + dot(spec, half3(0.3, 0.4, 0.3)));
                col = MixFog(col, i.niebla);
                return half4(col, alfa);
            }
            ENDHLSL
        }
    }
}
