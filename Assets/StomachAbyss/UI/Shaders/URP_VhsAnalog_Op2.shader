Shader "StomachAbyss/URP_VhsAnalog_Op2"
{
    Properties
    {
        _Intensity ("Base Intensity", Range(0, 1)) = 1.0
        _ChromaShift ("RGB Chroma Shift", Range(0, 0.015)) = 0.0028
        _ChromaBleed ("Horizontal Bleed", Range(0, 1)) = 0.28
        _GrainAmount ("Tape Grain", Range(0, 0.20)) = 0.045
        _ScanlineStrength ("Scanline Strength", Range(0, 0.50)) = 0.22
        _TrackingStrength ("Tracking Band Strength", Range(0, 0.03)) = 0.0065
        _VignetteStrength ("Vignette Strength", Range(0, 1)) = 0.44
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "VHS_Analog_Op2_Pass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float _StomachVhsIntensity;
            float _ChromaShift;
            float _ChromaBleed;
            float _GrainAmount;
            float _ScanlineStrength;
            float _TrackingStrength;
            float _VignetteStrength;

            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float intensity = saturate(_Intensity * _StomachVhsIntensity);

                if (intensity <= 0.001)
                {
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                }

                float t = _Time.y;

                // 1. Faixa sutil de tracking VHS descendo lentamente pela tela
                float bandCenter = frac(-t * 0.085);
                float bandDist = abs(uv.y - bandCenter);
                float bandMask = smoothstep(0.038, 0.0, bandDist);
                float wobble = sin(uv.y * 190.0 + t * 28.0) * _TrackingStrength * bandMask * intensity;

                float2 sampleUv = float2(saturate(uv.x + wobble), uv.y);

                // 2. Sangramento horizontal de croma VHS (3 taps) + separacao RGB
                float shift = _ChromaShift * intensity;
                float bleedStep = shift * 1.45;

                half3 cCenter = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUv).rgb;
                half3 cLeft   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, float2(saturate(sampleUv.x - bleedStep), sampleUv.y)).rgb;
                half3 cRight  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, float2(saturate(sampleUv.x + bleedStep), sampleUv.y)).rgb;
                half3 blurred = (cLeft + cCenter + cRight) * (1.0 / 3.0);

                half rSplit = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, float2(saturate(sampleUv.x + shift), sampleUv.y)).r;
                half bSplit = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, float2(saturate(sampleUv.x - shift), sampleUv.y)).b;

                half3 col = lerp(cCenter, blurred, _ChromaBleed * intensity);
                col.r = lerp(col.r, rSplit, 0.78 * intensity);
                col.b = lerp(col.b, bSplit, 0.78 * intensity);

                // 3. Tom de fita analogica gasta (leve dessaturacao + sombras quentes/roxas)
                half luma = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(col, luma.xxx, 0.14 * intensity);
                col = col * lerp(1.0, 0.96, intensity) + half3(0.024, 0.014, 0.026) * intensity;

                // 4. Granulacao analogica + leve neve de estatica na faixa de tracking
                float2 grainSeed = floor(uv * _ScreenParams.xy * 0.5) + floor(t * 24.0) * 17.3;
                float noise = (hash12(grainSeed) - 0.5) * 2.0;
                col += noise * _GrainAmount * intensity;
                col += saturate(noise) * 0.10 * bandMask * intensity;

                // 5. Scanlines CRT/VHS sutis
                float scan = sin(uv.y * _ScreenParams.y * 1.05) * 0.5 + 0.5;
                col *= 1.0 - (_ScanlineStrength * intensity * (1.0 - scan) * 0.65);

                // 6. Vinheta analogica suave nas bordas
                float2 centered = uv * 2.0 - 1.0;
                float r2 = dot(centered, centered);
                float vig = saturate(1.0 - (_VignetteStrength * intensity) * pow(r2, 0.85));
                col *= lerp(1.0, vig, intensity);

                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
}
