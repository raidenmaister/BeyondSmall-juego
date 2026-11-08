// Cera de la colmena: rejilla de hexagonos procedural (triplanar, sin UVs). Cada celda es al azar
// miel (ambar brillante), opercular (tapa de cera clara) o vacia (oscura y honda). La luz viene
// horneada en el color de vertice como en la cueva del hormiguero; el alpha del vertice indica
// cuanto llega del sol (bocas del arbol).
Shader "Colmena/Panal"
{
    Properties
    {
        _Tamano("Tamaño de celda", Float) = 1.15
        _Cera("Color de la cera", Color) = (0.93, 0.72, 0.32, 1)
        _Miel("Color de la miel", Color) = (1.0, 0.55, 0.06, 1)
        _Tapa("Color de la tapa", Color) = (0.98, 0.86, 0.55, 1)
        _Hueco("Color del fondo vacio", Color) = (0.25, 0.13, 0.04, 1)
        _ProbMiel("Proporcion de miel", Range(0,1)) = 0.45
        _ProbTapa("Proporcion de tapas", Range(0,1)) = 0.25
        _BrilloMiel("Brillo propio de la miel", Range(0,2)) = 0.45
        _LuzMinima("Luz minima", Color) = (0.05,0.035,0.02,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _Tamano;
            half4 _Cera, _Miel, _Tapa, _Hueco, _LuzMinima;
            half _ProbMiel, _ProbTapa, _BrilloMiel;
        CBUFFER_END
        #include "Assets/Exterior/Shaders/ExteriorComun.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // celda hexagonal: devuelve (vector al centro de la celda, id de la celda)
        float4 Hex(float2 uv)
        {
            const float2 r = float2(1.0, 1.7320508);
            const float2 h = r * 0.5;
            float2 a2 = uv - r * floor(uv / r) - h;
            float2 b2 = (uv - h) - r * floor((uv - h) / r) - h;
            float2 gv = dot(a2, a2) < dot(b2, b2) ? a2 : b2;
            return float4(gv, uv - gv);
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
                half3 n = normalize(i.normalWS);
                half3 an = abs(n);
                // plano dominante: hexagonos nitidos (sin mezclar proyecciones)
                float2 uv = an.y > an.x && an.y > an.z ? i.positionWS.xz : (an.x > an.z ? i.positionWS.zy : i.positionWS.xy);
                float4 hx = Hex(uv / _Tamano);
                float2 gv = hx.xy;
                // distancia al borde de la celda (0 en el borde, ~0.5 en el centro)
                float d = max(dot(abs(gv), normalize(float2(1.0, 1.7320508))), abs(gv.x));
                float borde = 0.5 - d;
                float azar = HashExterior(hx.zw + floor(i.positionWS.y * 0.05));
                float azar2 = HashExterior(hx.zw * 1.7 + 3.1);

                half3 view = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 albedo; half emision = 0; half brillo = 0;
                half rim = 1.0 - smoothstep(0.03, 0.085, borde);               // pared de cera
                if (azar < _ProbMiel)
                {   // miel: ambar, mas clara en el centro, con reflejo
                    half prof = smoothstep(0.0, 0.45, borde);
                    albedo = _Miel.rgb * lerp(0.55, 1.15, prof) * lerp(0.85, 1.1, azar2);
                    emision = _BrilloMiel * prof;
                    brillo = pow(saturate(dot(reflect(-view, n), normalize(half3(0.3, 1, 0.2)))), 24) * 0.8 * prof;
                }
                else if (azar < _ProbMiel + _ProbTapa)
                {   // celda tapada con cera (abombada)
                    half domo = smoothstep(0.0, 0.5, borde);
                    albedo = _Tapa.rgb * lerp(0.75, 1.05, domo) * lerp(0.9, 1.05, azar2);
                }
                else
                {   // vacia: el fondo se ve oscuro, las paredes interiores se aclaran hacia el borde
                    half prof = smoothstep(0.02, 0.4, borde);
                    albedo = lerp(_Cera.rgb * 0.7, _Hueco.rgb, prof);
                }
                albedo = lerp(albedo, _Cera.rgb * 1.08, rim);

                // luz horneada + algo de sol por las bocas del arbol
                half3 luz = i.color.rgb + _LuzMinima.rgb;
                half afuera = i.color.a;
                if (afuera > 0.001)
                {
                    Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                    luz += (L.color * saturate(dot(n, L.direction)) * L.shadowAttenuation + SampleSH(n)) * afuera;
                }
                half3 c = albedo * luz + _Miel.rgb * emision * (0.25 + luz * 0.5) + brillo * luz;
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
                float3 nn = TransformObjectToWorldNormal(i.normalOS);
                o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(p, nn, _LightDirection)));
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
