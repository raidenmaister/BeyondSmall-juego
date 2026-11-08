// Hormiga con patas animadas en la GPU (misma idea que CreatureVisual.DeformSkitter, pero sin
// deformar la malla en la CPU): permite hordas de muchas hormigas. Cada hormiga desfasa su paso
// segun su posicion. Compatible con SRP Batcher y GPU instancing.
Shader "Hormiguero/Hormiga"
{
    Properties
    {
        _BaseMap("Textura", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Frecuencia("Pasos por segundo", Float) = 3
        _Amplitud("Amplitud del paso (0 = quieta)", Range(0,1.5)) = 1
        _Balanceo("Balanceo de patas", Float) = 0.1
        _Elevacion("Elevacion de patas", Float) = 0.07
        // limites de la malla en espacio objeto (los pone el generador)
        _ZMin("Z minima", Float) = -0.85
        _Largo("Largo", Float) = 1.7
        _YMin("Y minima", Float) = 0
        _Alto("Alto", Float) = 1
        _MedioAncho("Medio ancho", Float) = 0.8
        _Brillo("Brillo especular", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _Frecuencia, _Amplitud, _Balanceo, _Elevacion;
            float _ZMin, _Largo, _YMin, _Alto, _MedioAncho;
            half _Brillo;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        #include "Assets/Exterior/Shaders/ExteriorComun.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float3 AnimarPatas(float3 v)
        {
            float tt = (v.z - _ZMin) / _Largo;
            float lat = v.x / _MedioAncho;
            float h = (v.y - _YMin) / _Alto;
            float desfase = HashExterior(OrigenObjeto().xz) * 10.0;
            float fase = _Time.y * _Frecuencia + desfase;
            float pata = saturate((abs(lat) - 0.3) / 0.4) * saturate((0.6 - h) / 0.4);
            // tripode alternado: tres grupos por lado, el lado derecho en contrafase
            float grupo = min(2.0, floor(tt * 3.0));
            float ph = PI * fmod(grupo + (lat > 0 ? 1.0 : 0.0), 2.0);
            float s = sin(6.2832 * fase + ph);
            v.z += s * _Balanceo * _Largo * pata * _Amplitud;
            v.y += max(0.0, cos(6.2832 * fase + ph)) * _Elevacion * _Largo * pata * _Amplitud;
            // antenas
            float antena = step(0.8, tt) * step(0.35, h) * step(0.05, abs(lat)) * step(abs(lat), 0.5) * (1.0 - pata);
            v.x += sin(_Time.y * 7.0 + lat * 5.0 + desfase) * 0.012 * _Largo * (0.3 + _Amplitud) * antena;
            return v;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half niebla : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = TransformObjectToWorld(AnimarPatas(i.positionOS.xyz));
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.niebla = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = normalize(i.normalWS);
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.niebla;
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);
                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.occlusion = 1;
                s.smoothness = _Brillo;
                s.specular = half3(0.25, 0.25, 0.25) * _Brillo;
                half4 c = UniversalFragmentBlinnPhong(d, s);
                c.rgb = MixFog(c.rgb, d.fogCoord);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 p = TransformObjectToWorld(AnimarPatas(i.positionOS.xyz));
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - p);
                #else
                    float3 ld = _LightDirection;
                #endif
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, ld)));
                return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o); o.positionCS = TransformObjectToHClip(AnimarPatas(i.positionOS.xyz)); return o; }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o); o.positionCS = TransformObjectToHClip(AnimarPatas(i.positionOS.xyz)); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { return EmpaquetarNormalDepthNormals(normalize(i.normalWS)); }
            ENDHLSL
        }
    }
    FallBack Off
}
