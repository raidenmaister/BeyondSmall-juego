// Skybox procedural para el ciclo dia/noche (URP).
// Gradiente de cielo, disco solar con halo y brillo de atardecer en el horizonte,
// luna con fases/crateres, estrellas que titilan y nubes fbm en movimiento.
// Los valores los controla DayNightCycle.cs cada frame.
Shader "Custom/DayNightSky"
{
    Properties
    {
        _SkyTop ("Cielo arriba", Color) = (0.24, 0.5, 0.9, 1)
        _SkyHorizon ("Cielo horizonte", Color) = (0.68, 0.82, 0.95, 1)
        _GroundColor ("Suelo", Color) = (0.25, 0.27, 0.25, 1)
        _SunDir ("Direccion sol", Vector) = (0, 0.5, 0.8, 0)
        _SunColor ("Color sol", Color) = (1, 0.95, 0.85, 1)
        _SunSize ("Tamano sol", Range(0.005, 0.1)) = 0.035
        _SunsetGlow ("Brillo atardecer", Range(0, 2)) = 0
        _MoonDir ("Direccion luna", Vector) = (0, -0.5, -0.8, 0)
        _MoonColor ("Color luna", Color) = (0.85, 0.9, 1, 1)
        _MoonSize ("Tamano luna", Range(0.01, 0.1)) = 0.045
        _MoonPhase ("Fase luna", Range(-1, 1)) = 0.35
        _StarIntensity ("Estrellas", Range(0, 3)) = 0
        _StarRotation ("Rotacion estrellas", Float) = 0
        _CloudCover ("Nubosidad", Range(0, 1)) = 0.45
        _CloudLit ("Nubes iluminadas", Color) = (1, 1, 1, 1)
        _CloudShade ("Nubes sombra", Color) = (0.55, 0.6, 0.7, 1)
        _CloudSpeed ("Velocidad nubes", Float) = 0.004
        _Exposure ("Exposicion", Range(0, 4)) = 1
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop, _SkyHorizon, _GroundColor, _SunColor, _MoonColor, _CloudLit, _CloudShade;
                float4 _SunDir, _MoonDir;
                float _SunSize, _SunsetGlow, _MoonSize, _MoonPhase, _StarIntensity, _StarRotation;
                float _CloudCover, _CloudSpeed, _Exposure;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dir = v.positionOS.xyz;
                return o;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }
            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float noise2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3 - 2 * f);
                return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), u.x),
                            lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p)
            {
                float s = 0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++) { s += a * noise2(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            // Campo de estrellas en celdas 3D sobre la esfera celeste
            float stars(float3 d, float scale, float density)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float h = hash13(cell);
                if (h < 1 - density) return 0;
                float3 center = cell + 0.5 + (float3(hash13(cell + 7.1), hash13(cell + 3.7), hash13(cell + 1.3)) - 0.5) * 0.6;
                float dist = length(p - center);
                float twinkle = 0.6 + 0.4 * sin(_Time.y * (2 + h * 6) + h * 80);
                return smoothstep(0.32, 0.0, dist) * twinkle * (0.4 + 0.6 * frac(h * 13.7));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 sunDir = normalize(_SunDir.xyz);
                float3 moonDir = normalize(_MoonDir.xyz);

                // Gradiente base
                float up = pow(saturate(h), 0.45);
                float3 col = lerp(_SkyHorizon.rgb, _SkyTop.rgb, up);
                col = lerp(col, _GroundColor.rgb, smoothstep(0.0, -0.08, h));

                // Dispersion alrededor del sol y brillo de atardecer en el horizonte
                float sd = dot(d, sunDir);
                float horizonBand = pow(1 - abs(h), 7);
                float towardSun = pow(saturate(dot(normalize(float3(d.x, 0, d.z) + 1e-4), normalize(float3(sunDir.x, 0, sunDir.z) + 1e-4)) * 0.5 + 0.5), 3);
                col += _SunColor.rgb * horizonBand * towardSun * _SunsetGlow * 0.9;
                float sunAbove = smoothstep(-0.15, 0.05, sunDir.y);
                col += _SunColor.rgb * (pow(saturate(sd), 8) * 0.25 + pow(saturate(sd), 64) * 0.6) * sunAbove;

                // Estrellas (rotan con la hora)
                float c = cos(_StarRotation), s = sin(_StarRotation);
                float3 sdir = float3(d.x, d.y * c - d.z * s, d.y * s + d.z * c);
                float st = stars(sdir, 220, 0.035) + stars(sdir, 420, 0.03) * 0.6;
                // franja tenue tipo via lactea
                float milky = pow(saturate(1 - abs(dot(sdir, normalize(float3(0.3, 0.2, 1))))), 6) * fbm(sdir.xz * 6 + sdir.y * 3);
                float starMask = smoothstep(0.0, 0.25, h) * _StarIntensity;
                col += (st * float3(0.9, 0.95, 1) + milky * float3(0.25, 0.25, 0.4)) * starMask;

                // Luna: disco con fase y crateres
                float md = dot(d, moonDir);
                float moonR = _MoonSize;
                float ang = acos(clamp(md, -1, 1));
                float3 moonCol = 0;
                if (ang < moonR * 1.2)
                {
                    float3 right = normalize(cross(moonDir, float3(0, 1, 0)));
                    float3 upv = cross(right, moonDir);
                    float2 uv = float2(dot(d, right), dot(d, upv)) / moonR;
                    float r2 = dot(uv, uv);
                    float disc = smoothstep(1.0, 0.92, sqrt(r2));
                    float z = sqrt(saturate(1 - r2));
                    float3 n = float3(uv, z);
                    float lit = smoothstep(-0.1, 0.25, dot(n, normalize(float3(_MoonPhase, 0.1, 1 - abs(_MoonPhase)))));
                    float craters = 0.75 + 0.25 * fbm(uv * 3.5 + 4.0);
                    moonCol = _MoonColor.rgb * disc * (0.04 + lit * craters) * 2.2;
                    col = lerp(col, col * 0.15, disc * starMask * 0.3);
                }
                col += moonCol;
                col += _MoonColor.rgb * pow(saturate(md), 400) * 0.35 + _MoonColor.rgb * pow(saturate(md), 30) * 0.06;

                // Disco solar (HDR para el bloom)
                float sunDisc = smoothstep(cos(_SunSize), cos(_SunSize * 0.85), sd);
                col += _SunColor.rgb * sunDisc * 18 * sunAbove;

                // Nubes proyectadas sobre un plano
                if (h > 0.0)
                {
                    float2 cuv = d.xz / (h + 0.12) * 0.9;
                    float2 wind = float2(1, 0.35) * _Time.y * _CloudSpeed * 10;
                    float n1 = fbm(cuv + wind);
                    float n2 = fbm(cuv * 2.1 - wind * 1.6 + 5.3);
                    float dens = smoothstep(1 - _CloudCover, 1 - _CloudCover + 0.35, n1 * 0.75 + n2 * 0.35);
                    dens *= smoothstep(0.0, 0.18, h);
                    // las nubes cerca del sol se iluminan por detras
                    float silver = pow(saturate(sd), 6) * sunAbove;
                    float3 cloudCol = lerp(_CloudShade.rgb, _CloudLit.rgb, saturate(n2 * 1.3)) + _SunColor.rgb * silver * 0.8;
                    col = lerp(col, cloudCol, dens * 0.92);
                }

                col *= _Exposure;
                // dither para evitar bandas en los degradados oscuros
                col += (hash12(i.positionCS.xy) - 0.5) / 255.0;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
