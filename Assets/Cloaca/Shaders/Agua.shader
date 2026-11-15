// Agua de la cloaca (rehecha): transparente con REFRACCION del fondo (textura opaca de la camara), turbia segun la
// profundidad real (textura de profundidad), espuma en las orillas y alrededor de lo que asoma, ondas que corren con la
// corriente, reflejo del entorno con fresnel y destellos de las luces (Forward+).
// Igual que antes: la luz de la cueva viene horneada en el color de vertice (rgb), el alpha del vertice es la velocidad
// de la corriente y UV2.x cuanto da al cielo (rejillas). UV en unidades del mundo con V a favor de la corriente.
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
        _Turbidez("Turbidez (1/unidades)", Float) = 0.9
        _Refraccion("Distorsion de la refraccion", Float) = 0.035
        _Espuma("Ancho de la espuma (unidades)", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ColorHondo, _ColorSucio, _ColorNata, _LuzMinima, _Reflejo;
            float _Escala, _Velocidad, _Turbidez, _Refraccion, _Espuma;
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
            float a = Ruido(p - d) * 0.55 + Ruido(p * 2.3 - d * 1.7 + 13.1) * 0.3 + Ruido(p * 5.1 - d * 2.9 + 7.7) * 0.15;
            // estanques sin corriente: dos trenes de ondas cruzados que se mecen
            float b = Ruido(p * 1.6 + float2(sin(t * 0.4), cos(t * 0.33)) * 1.5) * 0.6 + Ruido(p * 3.7 - float2(t * 0.21, t * 0.17)) * 0.4;
            return lerp(b, a, saturate(flujo * 3.0));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend One Zero
            ZWrite On
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half4 color : COLOR;
                half niebla : TEXCOORD3;
                half cielo : TEXCOORD4;
                float4 pantalla : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                // la superficie sube y baja un poco (oleaje suave)
                ws.y += (sin(ws.x * 0.35 + _Time.y * 1.3) + sin(ws.z * 0.27 - _Time.y * 1.1)) * 0.035;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                o.cielo = i.uv2.x;
                o.color = i.color;
                o.niebla = ComputeFogFactor(o.positionCS.z);
                o.pantalla = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i, bool frente : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float t = _Time.y;
                float flujo = i.color.a * _Velocidad;
                float2 p = i.uv / _Escala;
                // normal de las ondas por diferencias finitas
                float e = 0.04;
                float h = Ondas(p, t, flujo);
                float hx = Ondas(p + float2(e, 0), t, flujo);
                float hy = Ondas(p + float2(0, e), t, flujo);
                half3 n0 = normalize(i.normalWS) * (frente ? 1 : -1);
                half3 tU = normalize(cross(n0, half3(0, 0, 1)) + half3(1e-4, 0, 0));
                half3 tV = normalize(cross(tU, n0));
                half3 n = normalize(n0 + (tU * (h - hx) + tV * (h - hy)) * 9.0);

                // profundidad del agua en este pixel (fondo - superficie)
                float2 suv = i.pantalla.xy / i.pantalla.w;
                float ojo = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float fondo = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float hondo = max(0.0, fondo - ojo);
                // refraccion: el fondo se ve desplazado por las ondas (sin coger cosas que estan delante del agua)
                float2 desv = (n.xz - n0.xz) * _Refraccion * saturate(hondo * 0.5);
                float2 ruv = suv + desv;
                float fondoR = LinearEyeDepth(SampleSceneDepth(ruv), _ZBufferParams);
                if (fondoR < ojo) { ruv = suv; fondoR = fondo; }
                float hondoR = max(0.0, fondoR - ojo);
                half3 fondoColor = SampleSceneColor(ruv);

                // agua turbia: cuanto mas honda, mas color propio (verde-marron) y menos fondo
                float m = Ruido(p * 0.45 - float2(0, t * flujo * 0.5) + 31.0);
                half3 albedo = lerp(_ColorHondo.rgb, _ColorSucio.rgb, smoothstep(0.35, 0.8, m) * 0.6 + h * 0.25);
                half3 horneada = i.color.rgb + _LuzMinima.rgb;
                float transmision = exp(-hondoR * _Turbidez);
                half3 c = lerp(albedo * horneada * 1.3, fondoColor * lerp(half3(0.55, 0.62, 0.4), half3(1, 1, 1), transmision), transmision);

                // espuma: en las orillas, alrededor de lo que asoma y natas que viajan con la corriente
                float orilla = 1.0 - saturate(hondo / max(0.05, _Espuma));
                float rompe = Ruido(p * 6.0 - float2(t * 0.6, t * flujo * 1.2));
                float espuma = saturate(orilla * 1.4 - rompe * 0.6) * 0.85;
                float nata = smoothstep(0.78, 0.92, Ruido(p * 1.3 - float2(0, t * flujo * 0.9) + 5.0)) * (0.4 + 0.6 * saturate(flujo));
                espuma = max(espuma, nata * 0.6);
                c = lerp(c, _ColorNata.rgb * (horneada * 1.6 + 0.08), espuma);

                // reflejo del entorno (sondas / cielo) con fresnel
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                half fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, V)), 5.0);
                half3 entorno = GlossyEnvironmentReflection(reflect(-V, n), i.positionWS, 0.08h, 1.0h, GetNormalizedScreenSpaceUV(i.positionCS));
                half3 refl = lerp(_Reflejo.rgb + horneada * 0.25, entorno, saturate(i.cielo + 0.25));
                c = lerp(c, refl, fres * (1.0 - espuma));

                // destellos de las luces (Forward+)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 spec = 0;
                uint cuenta = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(cuenta)
                    Light L = GetAdditionalLight(lightIndex, i.positionWS);
                    half3 H = normalize(L.direction + V);
                    half s = pow(saturate(dot(n, H)), 160.0) * _Brillo;
                    spec += L.color * L.distanceAttenuation * s;
                LIGHT_LOOP_END
                Light S = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 Hs = normalize(S.direction + V);
                spec += S.color * S.shadowAttenuation * pow(saturate(dot(n, Hs)), 220.0) * _Brillo * 1.2 * saturate(i.cielo + 0.15);
                c += spec * (1.0 - espuma);
                c = MixFog(c, i.niebla);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
