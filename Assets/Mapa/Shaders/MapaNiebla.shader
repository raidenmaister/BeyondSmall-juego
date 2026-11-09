// Mapa del cuaderno: muestra el dibujo (_MainTex) solo donde el jugador ya exploro (_Niebla.r)
// y papel en blanco (_Papel) en lo demas; el limite se ve irregular, como un dibujo a medio hacer.
Shader "UI/MapaNiebla"
{
    Properties
    {
        [PerRendererData] _MainTex ("Dibujo", 2D) = "white" {}
        _Papel ("Papel", 2D) = "white" {}
        _Niebla ("Explorado", 2D) = "black" {}
        _Ruido ("Irregularidad del borde", Range(0, 1)) = 0.6
        _Color ("Tinte", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float4 world : TEXCOORD1; };

            sampler2D _MainTex, _Papel, _Niebla;
            float4 _ClipRect;
            float _Ruido;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.world = v.vertex;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float ruido(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 dibujo = tex2D(_MainTex, i.uv);
                fixed4 papel = tex2D(_Papel, i.uv);
                float n = tex2D(_Niebla, i.uv).r;
                // borde irregular: ruido en dos escalas
                float r = ruido(i.uv * 90) * 0.65 + ruido(i.uv * 330) * 0.35;
                float v = n + (r - 0.5) * _Ruido;
                float m = smoothstep(0.45, 0.5, v);
                fixed4 c = lerp(papel, dibujo, m);
                // trazo de lapiz justo en el limite de lo explorado
                float linea = 1 - saturate(abs(v - 0.475) * 28);
                c.rgb = lerp(c.rgb, float3(0.3, 0.27, 0.24), linea * 0.45);
                c *= i.color;
                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(i.world.xy, _ClipRect);
                #endif
                return c;
            }
            ENDCG
        }
    }
}
