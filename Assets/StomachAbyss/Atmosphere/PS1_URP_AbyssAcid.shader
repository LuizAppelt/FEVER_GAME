Shader "MapaEstomago/URP/PS1_AbyssAcid"
{
    Properties
    {
        _BaseMap ("Textura Acido", 2D) = "white" {}
        _BaseColor ("Cor Fluido Base", Color) = (0.58, 0.92, 0.34, 1)
        _DeepColor ("Cor Fossa Profunda (#0a3006)", Color) = (0.012, 0.068, 0.008, 1)
        [HDR] _EmissionColor ("Cor Veios Toxicos HDR", Color) = (0.42, 1.85, 0.16, 1)
        _ScrollSpeedA ("Velocidade Camada 1 (XY)", Vector) = (0.036, 0.025, 0, 0)
        _ScrollSpeedB ("Velocidade Camada 2 (XY)", Vector) = (-0.024, 0.044, 0, 0)
        _WarpStrength ("Intensidade Distorcao Vortex", Range(0, 0.45)) = 0.14
        _WarpFreq ("Frequencia Distorcao Vortex", Range(0.2, 6)) = 1.65
        _WaveSpeed ("Velocidade Ondulacao", Range(0, 8)) = 1.45
        _WaveAmp ("Amplitude Ondulacao (m)", Range(0, 1.2)) = 0.48
        _VertexSnap ("Resolucao Vertex Snap PS1 (0 = off)", Range(0, 320)) = 200
        _PulseSpeed ("Velocidade Fervura / Pulso", Range(0, 8)) = 2.1
        _FlowMod ("Modulador Temporal Fluxo", Float) = 0.0
        _ColorQuantSteps ("Niveis Quantizacao Cor PS1 (0 = off)", Range(0, 64)) = 24
        _DitherStrength ("Forca Bayer 4x4 Dither PS1", Range(0, 2)) = 0.85
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../../PilotChamber/UnityScripts/Shaders/PS1_StomachFog.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half   waveCrest  : TEXCOORD3;
                half   fogFactor  : TEXCOORD4;
                float4 vcolor     : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _DeepColor;
                half4 _EmissionColor;
                float4 _ScrollSpeedA;
                float4 _ScrollSpeedB;
                float _WarpStrength;
                float _WarpFreq;
                float _WaveSpeed;
                float _WaveAmp;
                float _VertexSnap;
                float _PulseSpeed;
                float _FlowMod;
                float _ColorQuantSteps;
                float _DitherStrength;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posOS = input.positionOS.xyz;
                float t = _Time.y;

                // Multi-scale rolling swells tuned for ~3-7.5m vertex spacing without spatial aliasing
                float ph1 = posOS.x * 0.12 + posOS.z * 0.09;
                float ph2 = -posOS.x * 0.08 + posOS.z * 0.11;
                float ph3 = (posOS.x + posOS.z) * 0.21;
                float ph4 = (posOS.x - posOS.z) * 0.17;

                float sp1 = _WaveSpeed;
                float sp2 = _WaveSpeed * 0.83;
                float sp3 = _WaveSpeed * 1.42;
                float sp4 = _WaveSpeed * 1.75;

                float w1 = sin(t * sp1 + ph1);
                float w2 = cos(t * sp2 + ph2) * 0.60;
                float w3 = sin(t * sp3 + ph3) * 0.32;
                float w4 = cos(t * sp4 + ph4) * 0.20;

                float waveNorm = (w1 + w2 + w3 + w4) * 0.48;
                posOS.y += waveNorm * _WaveAmp;

                // Approximate analytical wave slope normal for boiling surface light glint
                float dHdx = (
                      0.12 * cos(t * sp1 + ph1)
                    + 0.08 * sin(t * sp2 + ph2) * 0.60
                    + 0.21 * cos(t * sp3 + ph3) * 0.32
                    - 0.17 * sin(t * sp4 + ph4) * 0.20
                ) * 0.48 * _WaveAmp;

                float dHdz = (
                      0.09 * cos(t * sp1 + ph1)
                    - 0.11 * sin(t * sp2 + ph2) * 0.60
                    + 0.21 * cos(t * sp3 + ph3) * 0.32
                    + 0.17 * sin(t * sp4 + ph4) * 0.20
                ) * 0.48 * _WaveAmp;

                float3 waveNormalOS = normalize(float3(-dHdx, 1.0, -dHdz));

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                float4 clipPos = posInputs.positionCS;

                if (_VertexSnap > 1.0)
                {
                    float4 snapped = clipPos;
                    snapped.xyz = clipPos.xyz / clipPos.w;
                    snapped.x = floor(_VertexSnap * snapped.x) / _VertexSnap;
                    snapped.y = floor((_VertexSnap * 0.75) * snapped.y) / (_VertexSnap * 0.75);
                    snapped.xyz *= clipPos.w;
                    clipPos = snapped;
                }

                output.positionCS = clipPos;
                output.positionWS = posInputs.positionWS;
                output.normalWS = (half3)normalize(TransformObjectToWorldNormal(waveNormalOS));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.waveCrest = (half)waveNorm;
                output.fogFactor = (half)ComputeFogFactor(posInputs.positionCS.z);
                output.vcolor = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float t = _Time.y;
                float flowT = t + _FlowMod;
                float2 baseUV = input.uv;
                float2 worldXZ = input.positionWS.xz * 0.032;

                // Domain-warped swirling vortex distortion
                float swirlA = sin(baseUV.x * _WarpFreq + worldXZ.x + flowT * 0.55)
                             * cos(baseUV.y * _WarpFreq - worldXZ.y + flowT * 0.47);
                float swirlB = cos((baseUV.x - baseUV.y) * (_WarpFreq * 1.45) - flowT * 0.68 + sin(worldXZ.x + worldXZ.y));
                float2 warp = float2(swirlA + swirlB * 0.5, swirlB - swirlA * 0.5) * _WarpStrength;

                float2 uvA = baseUV + warp + flowT * _ScrollSpeedA.xy;
                float2 uvB = baseUV * 1.38 - warp * 0.82 + flowT * _ScrollSpeedB.xy;

                half4 texA = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvA);
                half4 texB = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvB);

                // Dual-layer interference creates caustic foam veins + boiling pockets
                half ridge = 1.0h - saturate(abs(texA.g - texB.g) * 2.6h);
                half foamProduct = saturate(texA.g * texB.g * 1.65h);
                half layerBlend = max(texA.g, texB.g * 0.90h);
                half fluidPattern = saturate(layerBlend * 0.62h + foamProduct * 0.48h);

                // Crest vs trough modulation from real vertex displacement + surface slope
                half crestFactor = saturate(input.waveCrest * 0.55h + 0.50h);
                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 normWS = normalize(input.normalWS);
                half fresnelBase = max(saturate(1.0h - abs(dot(normWS, viewDirWS))), 0.0001h);
                half slopeHighlight = pow(fresnelBase, 2.2h) * 0.35h;

                half troughToCrest = saturate(fluidPattern * (0.58h + 0.62h * crestFactor) + slopeHighlight * 0.4h);

                // Deep bilious troughs (#0a3006) -> saturated acid body
                half3 baseSample = max(texA.rgb, texB.rgb * 0.85h) * _BaseColor.rgb * (half3)input.vcolor.rgb;
                half3 bodyColor = lerp(_DeepColor.rgb, baseSample, troughToCrest);

                // PS1 ordered Bayer 4x4 dither + color quantization on the liquid gradient
                half bayer = (half)GetPS1BayerDither4x4(input.positionCS.xy) * (half)_DitherStrength;
                if (_ColorQuantSteps > 1.0)
                {
                    half steps = (half)_ColorQuantSteps;
                    bodyColor = floor(saturate(bodyColor + bayer / steps) * steps + 0.5h) / steps;
                }

                // Luminous HDR caustic veins and crest foam
                half pulse = 0.86h + 0.18h * (half)sin(t * _PulseSpeed + input.positionWS.x * 0.08 + input.positionWS.z * 0.06);
                half veinMask = saturate(pow(max(ridge, 0.0001h), 2.2h) * 0.72h + pow(max(foamProduct, 0.0001h), 1.7h) * 0.65h);
                veinMask *= (0.55h + 0.65h * crestFactor);
                half3 emission = _EmissionColor.rgb * veinMask * pulse;

                half3 finalCol = bodyColor + emission;
                finalCol = MixFog(finalCol, input.fogFactor);
                finalCol = ApplyPS1StomachFog(finalCol, input.positionWS, input.positionCS);
                return half4(finalCol, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
