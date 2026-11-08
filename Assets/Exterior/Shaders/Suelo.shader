// Suelo del exterior: mezcla dos texturas (tierra y hojarasca) con un ruido de baja frecuencia
// y una segunda escala de la textura principal para que no se note la repeticion.
// Proyeccion en el plano XZ del mundo, recibe sombras.
Shader "Exterior/Suelo"
{
    Properties
    {
        _CapaA("Tierra", 2D) = "white" {}
        [Normal] _NormalA("Normal tierra", 2D) = "bump" {}
        _CapaB("Hojarasca", 2D) = "white" {}
        [Normal] _NormalB("Normal hojarasca", 2D) = "bump" {}
        _ColorA("Tinte tierra", Color) = (1,1,1,1)
        _ColorB("Tinte hojarasca", Color) = (1,1,1,1)
        _Tamano("Tamaño de la textura (unidades)", Float) = 36
        _TamanoMezcla("Tamaño de las manchas (unidades)", Float) = 160
        _FuerzaNormal("Fuerza normal", Range(0,2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _CapaA_ST;
            half4 _ColorA;
            half4 _ColorB;
            float _Tamano;
            float _TamanoMezcla;
            half _FuerzaNormal;
        CBUFFER_END
        TEXTURE2D(_CapaA); SAMPLER(sampler_CapaA);
        TEXTURE2D(_NormalA);
        TEXTURE2D(_CapaB);
        TEXTURE2D(_NormalB);
        #include "ExteriorComun.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        float RuidoValor(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = HashExterior(i), b = HashExterior(i + float2(1, 0)), c = HashExterior(i + float2(0, 1)), d = HashExterior(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
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
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half niebla : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.niebla = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float2 w = i.positionWS.xz;
                float2 uv = w / _Tamano;
                float2 uv2 = w / (_Tamano * 3.7) + 0.37;

                // manchas de hojarasca con ruido de dos octavas
                float m = RuidoValor(w / _TamanoMezcla) * 0.65 + RuidoValor(w / (_TamanoMezcla * 0.3) + 7.1) * 0.35;
                m = smoothstep(0.38, 0.66, m);

                half3 a = SAMPLE_TEXTURE2D(_CapaA, sampler_CapaA, uv).rgb;
                half3 a2 = SAMPLE_TEXTURE2D(_CapaA, sampler_CapaA, uv2).rgb;
                a = lerp(a, a2, 0.35) * _ColorA.rgb;
                half3 b = SAMPLE_TEXTURE2D(_CapaB, sampler_CapaA, uv * 0.9).rgb * _ColorB.rgb;
                half3 albedo = lerp(a, b, m);

                half3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalA, sampler_CapaA, uv));
                half3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalB, sampler_CapaA, uv * 0.9));
                half3 nt = normalize(lerp(nA, nB, m));
                nt.xy *= _FuerzaNormal;

                // base tangente para la proyeccion XZ
                half3 n = normalize(i.normalWS);
                half3 t = normalize(half3(1, 0, 0) - n * n.x);
                half3 bt = cross(n, t);
                half3 nw = normalize(t * nt.x + bt * -nt.y + n * nt.z);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = nw;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.niebla;
                d.bakedGI = SampleSH(nw);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo;
                s.alpha = 1;
                s.occlusion = 1;
                s.smoothness = 0.1;
                half4 c = UniversalFragmentBlinnPhong(d, s);
                c.rgb = MixFog(c.rgb, d.fogCoord);
                return half4(c.rgb, 1);
            }
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
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
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
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { return EmpaquetarNormalDepthNormals(normalize(i.normalWS)); }
            ENDHLSL
        }
    }
    FallBack Off
}
