// Haz de luz que entra por una rejilla: cono aditivo con polvo flotando. La UV.y va de 0 (arriba) a 1 (abajo);
// se desvanece hacia abajo y en los bordes (segun el angulo de vista). _Intensidad la mueve Cloaca segun el sol.
Shader "Cloaca/Haz"
{
    Properties
    {
        _Color("Color", Color) = (1,0.92,0.75,1)
        _Intensidad("Intensidad", Float) = 0.35
        _Polvo("Polvo", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Haz"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensidad, _Polvo;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float Ruido(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x), lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x), lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                half borde = saturate(abs(dot(normalize(i.normalWS), V)));
                borde = borde * borde;
                half alto = saturate(1.0 - i.uv.y) * saturate(i.uv.y * 8.0);
                float polvo = Ruido(i.positionWS * 0.8 + float3(0, -_Time.y * 0.35, _Time.y * 0.1));
                polvo = lerp(1.0, 0.4 + polvo * 1.4, _Polvo);
                // la camara dentro del haz: se apaga para no velar la pantalla
                float cerca = saturate(distance(i.positionWS, GetCameraPositionWS()) / 6.0);
                half k = _Intensidad * borde * alto * polvo * cerca;
                return half4(_Color.rgb * k, 1);
            }
            ENDHLSL
        }
    }
}
