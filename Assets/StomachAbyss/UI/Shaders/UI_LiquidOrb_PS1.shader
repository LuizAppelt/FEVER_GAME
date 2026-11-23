Shader "StomachAbyss/UI_LiquidOrb_PS1"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        [Header(Liquid Levels)]
        _FillAmount ("Fill Amount", Range(0, 1)) = 0.85
        _GhostFill ("Ghost Trail Fill", Range(0, 1)) = 0.85

        [Header(Liquid Colors)]
        _LiquidDark ("Liquid Deep Color", Color) = (0.28, 0.02, 0.05, 1)
        _LiquidMid ("Liquid Mid Color", Color) = (0.76, 0.07, 0.12, 1)
        _LiquidBright ("Liquid Bright Color", Color) = (0.98, 0.24, 0.24, 1)
        _FoamColor ("Meniscus Foam Color", Color) = (1.0, 0.58, 0.54, 1)
        _GhostColor ("Drain Ghost Color", Color) = (0.95, 0.78, 0.22, 1)
        _EmptyGlassColor ("Empty Glass Color", Color) = (0.06, 0.05, 0.06, 0.94)

        [Header(Wave and Bubble Animation)]
        _WaveAmp ("Wave Amplitude", Range(0, 0.08)) = 0.032
        _WaveFreq ("Wave Frequency", Range(1, 16)) = 6.5
        _WaveSpeed ("Wave Speed", Range(0, 10)) = 2.6
        _BubbleStrength ("Bubble Strength", Range(0, 1)) = 0.55
        _PulseIntensity ("Low Pulse Intensity", Range(0, 1)) = 0.0

        [Header(PS1 Retro Look)]
        _PixelRes ("Pixel Resolution", Range(24, 128)) = 48
        _DitherStrength ("PS1 Dither Strength", Range(0, 0.25)) = 0.09

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

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
            Name "UI_LiquidOrb_PS1"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;

            float _FillAmount;
            float _GhostFill;

            fixed4 _LiquidDark;
            fixed4 _LiquidMid;
            fixed4 _LiquidBright;
            fixed4 _FoamColor;
            fixed4 _GhostColor;
            fixed4 _EmptyGlassColor;

            float _WaveAmp;
            float _WaveFreq;
            float _WaveSpeed;
            float _BubbleStrength;
            float _PulseIntensity;

            float _PixelRes;
            float _DitherStrength;

            float bayer4x4(float2 p)
            {
                int x = (int)fmod(abs(p.x), 4.0);
                int y = (int)fmod(abs(p.y), 4.0);
                int idx = y * 4 + x;
                // 4x4 ordered dither matrix normalized to [-0.5, 0.5]
                float m[16] = {
                     0.0,  8.0,  2.0, 10.0,
                    12.0,  4.0, 14.0,  6.0,
                     3.0, 11.0,  1.0,  9.0,
                    15.0,  7.0, 13.0,  5.0
                };
                return (m[idx] / 16.0) - 0.5;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Quantize UV for crisp PS1 pixel-art look
                float2 pCoord = floor(i.texcoord * _PixelRes);
                float2 uv = (pCoord + 0.5) / _PixelRes;

                // Centered coordinates [-1, 1]
                float2 centered = (uv - 0.5) * 2.0;
                float dist = length(centered);

                // Hard pixel-art circular clip
                if (dist > 0.98)
                    discard;

                float t = _Time.y;
                float nz = sqrt(max(0.0, 1.0 - dist * dist));
                float sphereLight = saturate(-0.42 * centered.x + 0.52 * centered.y + 0.72 * nz);

                // Dual animated waves (attenuated near 0% and 100% fill so empty/full look clean)
                float edgeAtten = sin(saturate(_FillAmount) * 3.14159265);
                float amp = _WaveAmp * (0.25 + 0.75 * edgeAtten);

                float waveFront = sin(uv.x * _WaveFreq + t * _WaveSpeed) * amp
                                + cos(uv.x * (_WaveFreq * 1.7) - t * (_WaveSpeed * 1.35)) * (amp * 0.45);
                float waveBack  = sin(uv.x * (_WaveFreq * 1.15) - t * (_WaveSpeed * 0.85) + 1.8) * (amp * 0.85);

                // Map fill [0..1] slightly inside the sphere so 0% is truly empty and 100% fills the orb
                float baseFillY = lerp(0.02, 0.98, saturate(_FillAmount));
                float ghostFillY = lerp(0.02, 0.98, saturate(_GhostFill));

                float frontY = baseFillY + waveFront;
                float backY = baseFillY + waveBack + 0.018 * edgeAtten;

                float dither = bayer4x4(pCoord) * _DitherStrength;

                // Base empty glass interior
                float3 col = _EmptyGlassColor.rgb + sphereLight * 0.06;
                float alpha = _EmptyGlassColor.a;

                // Ghost drain layer (amber/gold trail behind draining HP/Stamina)
                if (uv.y <= ghostFillY && uv.y > frontY && _GhostFill > _FillAmount + 0.005)
                {
                    col = _GhostColor.rgb * (0.65 + 0.35 * sphereLight);
                    alpha = 1.0;
                }
                // Back liquid wave crest (darker liquid behind front meniscus)
                else if (uv.y <= backY && uv.y > frontY)
                {
                    col = lerp(_LiquidDark.rgb, _LiquidMid.rgb, 0.55);
                    alpha = 1.0;
                }

                // Main front liquid body
                if (uv.y <= frontY)
                {
                    // Vertical depth + spherical highlight + subtle internal swirl
                    float depth = saturate((frontY - uv.y) * 1.35);
                    float swirl = sin(centered.x * 3.6 + centered.y * 2.8 + t * 1.4) * 0.08
                                + cos(centered.y * 4.5 - t * 1.9) * 0.06;

                    float shade = saturate(sphereLight * 0.82 - depth * 0.32 + swirl + dither + 0.16);

                    if (shade < 0.5)
                        col = lerp(_LiquidDark.rgb, _LiquidMid.rgb, shade * 2.0);
                    else
                        col = lerp(_LiquidMid.rgb, _LiquidBright.rgb, (shade - 0.5) * 2.0);

                    // Rising animated bubbles inside the liquid
                    if (_BubbleStrength > 0.01)
                    {
                        float2 bUv = uv * float2(5.5, 5.5);
                        bUv.y -= t * 0.85;
                        float2 cell = floor(bUv);
                        float2 f = frac(bUv) - 0.5;
                        float rnd = hash21(cell);
                        if (rnd > 0.62)
                        {
                            float2 offset = float2(sin(t * 2.0 + rnd * 12.0) * 0.18, (rnd - 0.62) * 0.4);
                            float bDist = length(f - offset);
                            float bRad = lerp(0.08, 0.19, frac(rnd * 17.3));
                            if (bDist < bRad)
                            {
                                col = lerp(col, _FoamColor.rgb, _BubbleStrength * 0.65);
                            }
                        }
                    }

                    // Bright meniscus foam highlight right along the animated liquid surface
                    float surfaceDist = frontY - uv.y;
                    if (surfaceDist < 0.045 && _FillAmount > 0.02 && _FillAmount < 0.98)
                    {
                        col = lerp(_FoamColor.rgb, col, surfaceDist / 0.045);
                    }

                    // Low-health pulse glow
                    if (_PulseIntensity > 0.01)
                    {
                        float pulse = (sin(t * 7.5) * 0.5 + 0.5) * _PulseIntensity;
                        col += _LiquidBright.rgb * pulse * 0.45;
                    }

                    alpha = 1.0;
                }

                // Crisp PS1 glass specular highlight (top-left)
                float specDist = length((centered - float2(-0.36, 0.38)) * float2(1.0, 1.35));
                if (specDist < 0.21)
                {
                    col = lerp(col, float3(1.0, 0.96, 0.94), 0.72);
                }
                else if (length(centered - float2(-0.52, 0.16)) < 0.07)
                {
                    col = lerp(col, float3(1.0, 0.96, 0.94), 0.45);
                }

                // Spherical inner rim shadow so the orb sits recessed inside the pixel bezel
                float rimShadow = smoothstep(0.74, 0.98, dist);
                col *= (1.0 - rimShadow * 0.62);

                return fixed4(col, alpha) * i.color;
            }
            ENDCG
        }
    }
}
