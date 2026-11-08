// Paredes del hormiguero: texturas triplanares (no necesita UVs), la luz de los hongos viene
// horneada en el color de vertice (rgb) y el alpha del vertice indica cuanto da al exterior
// (ahi se mezcla con el sol y el cielo). Barato: sin luces en tiempo real.
Shader "Hormiguero/Cueva"
{
    Properties
    {
        _Pared("Pared", 2D) = "white" {}
        [Normal] _NormalPared("Normal pared", 2D) = "bump" {}
        _Piso("Piso", 2D) = "white" {}
        [Normal] _NormalPiso("Normal piso", 2D) = "bump" {}
        _Tamano("Tamaño de la textura (unidades)", Float) = 14
        _ColorPared("Tinte pared", Color) = (1,1,1,1)
        _ColorPiso("Tinte piso", Color) = (1,1,1,1)
        _LuzMinima("Luz minima en la cueva", Color) = (0.02,0.018,0.015,1)
        _FuerzaHorneada("Fuerza de la luz horneada", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _Pared_ST;
            float _Tamano;
            half4 _ColorPared, _ColorPiso, _LuzMinima;
            half _FuerzaHorneada;
        CBUFFER_END
        TEXTURE2D(_Pared); SAMPLER(sampler_Pared);
        TEXTURE2D(_NormalPared);
        TEXTURE2D(_Piso);
        TEXTURE2D(_NormalPiso);
        #include "Assets/Exterior/Shaders/ExteriorComun.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
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
                o.color = i.color;
                o.niebla = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 p = i.positionWS / _Tamano;
                half3 n = normalize(i.normalWS);
                half3 w = pow(abs(n), 4.0); w /= (w.x + w.y + w.z);
                // paredes con la textura excavada; lo que mira hacia arriba es piso de barro
                half3 px = SAMPLE_TEXTURE2D(_Pared, sampler_Pared, p.zy).rgb;
                half3 pz = SAMPLE_TEXTURE2D(_Pared, sampler_Pared, p.xy).rgb;
                half3 py = SAMPLE_TEXTURE2D(_Piso, sampler_Pared, p.xz).rgb;
                half3 pyTecho = SAMPLE_TEXTURE2D(_Pared, sampler_Pared, p.xz * 0.8).rgb;
                half esPiso = saturate(n.y * 3.0 - 1.2);
                half3 arriba = lerp(pyTecho * _ColorPared.rgb, py * _ColorPiso.rgb, esPiso);
                half3 albedo = (px * w.x + pz * w.z) * _ColorPared.rgb + arriba * w.y;

                // detalle de relieve con las normales (solo afecta un poco la luz)
                half3 nx = UnpackNormal(SAMPLE_TEXTURE2D(_NormalPared, sampler_Pared, p.zy));
                half3 nz = UnpackNormal(SAMPLE_TEXTURE2D(_NormalPared, sampler_Pared, p.xy));
                half3 ny = UnpackNormal(SAMPLE_TEXTURE2D(_NormalPiso, sampler_Pared, p.xz));
                half3 nw = normalize(n + (half3(0, nx.y, nx.x) * w.x + half3(ny.x, 0, ny.y) * w.y + half3(nz.x, nz.y, 0) * w.z) * 0.6);

                // luz horneada de los hongos (rgb) + un minimo para que no sea negro absoluto
                half3 horneada = i.color.rgb * _FuerzaHorneada * (0.75 + 0.25 * saturate(dot(nw, n))) + _LuzMinima.rgb;
                half3 c = albedo * horneada;

                // donde la cueva da al exterior (alpha del vertice) se ilumina con el sol y el cielo
                half afuera = i.color.a;
                if (afuera > 0.001)
                {
                    Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                    half3 sol = L.color * saturate(dot(nw, L.direction)) * L.shadowAttenuation + SampleSH(nw);
                    c = lerp(c, albedo * (sol + horneada * 0.3), afuera);
                }
                c = MixFog(c, i.niebla);
                return half4(c, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, n, _LightDirection)));
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
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_instancing
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { return EmpaquetarNormalDepthNormals(normalize(i.normalWS)); }
            ENDHLSL
        }
    }
    FallBack Off
}
