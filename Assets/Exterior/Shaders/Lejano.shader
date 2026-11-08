// Paisaje lejano (estatico y barato): montañas con color por vertice, suelo lejano y
// arboles impostor (un quad que siempre mira a la camara, girando solo en Y).
// Iluminacion simple (sol + ambiente), sin sombras, niebla con densidad escalable
// para que las montañas sigan viendose a varios kilometros.
Shader "Exterior/Lejano"
{
    Properties
    {
        _BaseMap("Textura", 2D) = "white" {}
        _BaseColor("Color", Color) = (1,1,1,1)
        _Cutoff("Recorte alpha", Range(0,1)) = 0.5
        _EscalaNiebla("Escala de la niebla", Range(0,1)) = 1
        _Ambiente("Fuerza del ambiente", Range(0,2)) = 1
        [Toggle(_BILLBOARD)] _Billboard("Billboard (mira a la camara)", Float) = 0
        [Toggle(_ALPHATEST_ON)] _AlphaClip("Recorte alpha", Float) = 0
        [Toggle(_COLOR_VERTICE)] _ColorVertice("Usar color de vertice", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
            half _EscalaNiebla;
            half _Ambiente;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        #include "ExteriorComun.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // posicion en el mundo; los billboards se orientan hacia la camara
        float3 PosicionMundo(Attributes i, out float3 normalWS)
        {
        #if defined(_BILLBOARD)
            float3 o = OrigenObjeto();
            float sx = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
            float sy = length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21));
            float3 alCam = _WorldSpaceCameraPos - o; alCam.y = 0;
            alCam = normalize(alCam + float3(1e-4, 0, 0));
            float3 derecha = float3(alCam.z, 0, -alCam.x);
            normalWS = normalize(alCam + float3(0, 0.6, 0));
            return o + derecha * i.positionOS.x * sx + float3(0, 1, 0) * i.positionOS.y * sy;
        #else
            normalWS = TransformObjectToWorldNormal(i.normalOS);
            return TransformObjectToWorld(i.positionOS.xyz);
        #endif
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _BILLBOARD
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _COLOR_VERTICE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 n;
                o.positionWS = PosicionMundo(i, n);
                o.normalWS = n;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.color = i.color;
                #if defined(_BILLBOARD)
                    // un poco de variacion de tono por arbol
                    half h = HashExterior(OrigenObjeto().xz);
                    o.color = half4(lerp(half3(0.82, 0.86, 0.78), half3(1.08, 1.05, 0.95), h), 1);
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                #if defined(_ALPHATEST_ON)
                    clip(t.a - _Cutoff);
                #endif
                half3 albedo = t.rgb;
                #if defined(_COLOR_VERTICE) || defined(_BILLBOARD)
                    albedo *= i.color.rgb;
                #endif
                half3 n = normalize(i.normalWS);
                Light L = GetMainLight();
                half difusa = saturate(dot(n, L.direction) * 0.6 + 0.4);
                half3 luz = L.color * difusa + SampleSH(n) * _Ambiente;
                half3 c = albedo * luz;
                c = NieblaEscalada(c, distance(i.positionWS, _WorldSpaceCameraPos), _EscalaNiebla);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _BILLBOARD
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 n; o.positionCS = TransformWorldToHClip(PosicionMundo(i, n));
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }
            half frag(Varyings i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return i.positionCS.z;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
