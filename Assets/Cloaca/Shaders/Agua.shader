// Agua sucia de la cloaca: opaca, verde-marron, con ondas que corren con la corriente, natas de mugre
// y destellos de las luces cercanas (Forward+). La luz de la cueva viene horneada en el color de vertice (rgb);
// el alpha del vertice es la velocidad de la corriente y UV2.x cuanto da al cielo (rejillas). Las UV son coordenadas en unidades del mundo, con V a favor
// de la corriente (asi sirve igual para canales, estanques y cascadas).
Shader "Cloaca/Agua"
{
    Properties
    {
        _ColorHondo("Color hondo", Color) = (0.07,0.09,0.05,1)
        _ColorSucio("Color de la mugre", Color) = (0.32,0.34,0.16,1)
        _ColorNata("Nata / espuma", Color) = (0.55,0.6,0.35,1)
        _Escala("Tamano de las ondas (unidades)", Float) = 6
        _Velocidad("Velocidad de la corriente", Float) = 2.2
        _Brillo("Brillo especular", Float) = 2.5
        _LuzMinima("Luz minima", Color) = (0.035,0.04,0.03,1)
        _Reflejo("Color del reflejo del techo", Color) = (0.05,0.06,0.05,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ColorHondo, _ColorSucio, _ColorNata, _LuzMinima, _Reflejo;
            float _Escala, _Velocidad;
            half _Brillo;
        CBUFFER_END
        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv : TEXCOORD0;
            float2 uv2 : TEXCOORD1;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
        float Ruido(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), u.x), u.y);
        }
        float Ondas(float2 p, float t, float flujo)
        {
            float2 d = float2(0, t * flujo);
            float a = Ruido(p - d) * 0.6 + Ruido(p * 2.3 - d * 1.7 + 13.1) * 0.3 + Ruido(p * 5.1 - d * 2.9 + 7.7) * 0.1;
            // estanques sin corriente: se mecen un poco
            float b = Ruido(p * 1.6 + float2(sin(t * 0.4), cos(t * 0.33)) * 1.5);
            return lerp(b, a, saturate(flujo * 3.0));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
                half niebla : TEXCOORD3;
                half cielo : TEXCOORD4;
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
                o.uv = i.uv;
                o.cielo = i.uv2.x;
                o.color = i.color;
                o.niebla = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y;
                float flujo = i.color.a * _Velocidad;
                float2 p = i.uv / _Escala;
                // normal de las ondas por diferencias finitas
                float e = 0.05;
                float h = Ondas(p, t, flujo);
                float hx = Ondas(p + float2(e, 0), t, flujo);
                float hy = Ondas(p + float2(0, e), t, flujo);
                half3 n0 = normalize(i.normalWS);
                // tangentes aproximadas: U ~ X del mundo, V ~ Z (las cascadas usan su normal)
                half3 tU = normalize(cross(n0, half3(0, 0, 1)) + half3(1e-4, 0, 0));
                half3 tV = normalize(cross(tU, n0));
                half3 n = normalize(n0 + (tU * (h - hx) + tV * (h - hy)) * 6.0);

                // mugre: manchas que viajan con la corriente
                float m = Ruido(p * 0.45 - float2(0, t * flujo * 0.5) + 31.0);
                half3 albedo = lerp(_ColorHondo.rgb, _ColorSucio.rgb, smoothstep(0.35, 0.8, m) * 0.6 + h * 0.25);
                float nata = smoothstep(0.78, 0.92, Ruido(p * 1.3 - float2(0, t * flujo * 0.9) + 5.0)) * (0.4 + 0.6 * saturate(flujo));
                albedo = lerp(albedo, _ColorNata.rgb, nata * 0.7);

                half3 horneada = i.color.rgb + _LuzMinima.rgb;
                half3 c = albedo * horneada;

                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                half fres = pow(1.0 - saturate(dot(n, V)), 4.0);
                c += _Reflejo.rgb * fres + horneada * fres * 0.25;

                // destellos de las luces (Forward+)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 spec = 0;
                uint cuenta = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(cuenta)
                    Light L = GetAdditionalLight(lightIndex, i.positionWS);
                    half3 H = normalize(L.direction + V);
                    half s = pow(saturate(dot(n, H)), 90.0) * _Brillo;
                    half difu = saturate(dot(n, L.direction)) * 0.25;
                    spec += L.color * L.distanceAttenuation * (s + difu * albedo);
                LIGHT_LOOP_END
                // el sol solo entra por las rejillas (sombra)
                Light S = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 Hs = normalize(S.direction + V);
                spec += S.color * S.shadowAttenuation * (pow(saturate(dot(n, Hs)), 120.0) * _Brillo * 0.6 + saturate(dot(n, S.direction)) * albedo * 0.35) * i.cielo;
                c += spec;
                c = MixFog(c, i.niebla);
                return half4(c, 1);
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
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
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
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
