Shader "MapaEstomago/URP/PS1_AcidVaporParticle"
{
    Properties
    {
        _BaseMap ("Sprite Vapor / Gota PS1", 2D) = "white" {}
        _BaseColor ("Cor Multiplicadora", Color) = (0.55, 0.95, 0.35, 0.65)
        [HDR] _EmissionColor ("Brilho Toxico HDR", Color) = (0.16, 0.55, 0.08, 1)
        _EdgeSoftness ("Suavidade Borda Radial", Range(0.25, 3.0)) = 1.15
        _DitherFade ("Forca Bayer 4x4 Dither PS1", Range(0, 1)) = 0.85
        _AlphaLevels ("Niveis Quantizacao Alpha PS1 (0 = off)", Range(0, 32)) = 12
        _AlphaCutoff ("Corte Minimo Alpha", Range(0, 0.25)) = 0.02
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Blend Origem", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Blend Destino", Float) = 10
        [Toggle] _AdditiveMode ("Modo Aditivo (Sem Fog Claro)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }
        LOD 100
        Cull Off
        ZWrite Off
        Blend [_SrcBlend] [_DstBlend]

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
                float4 color      : COLOR;
                half   fogFactor  : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                float _EdgeSoftness;
                float _DitherFade;
                float _AlphaLevels;
                float _AlphaCutoff;
                float _AdditiveMode;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogFactor = (half)ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);

                // Radial mask eliminates hard quad borders even when particles stretch or rotate
                float2 centeredUV = input.uv * 2.0 - 1.0;
                float radial = saturate(1.0 - dot(centeredUV, centeredUV));
                half radialMask = (half)pow(max(radial, 0.0001), _EdgeSoftness);

                half alpha = tex.a * (half)input.color.a * _BaseColor.a * radialMask;

                // PS1 ordered Bayer 4x4 dither fade + optional discrete opacity quantization
                half bayer = (half)GetPS1BayerDither4x4(input.positionCS.xy);
                half ditherOffset = bayer * (half)(0.18 * _DitherFade);
                half ditheredAlpha = saturate(alpha + ditherOffset);

                if (ditheredAlpha <= (half)_AlphaCutoff || alpha <= 0.004h)
                {
                    discard;
                }

                if (_AlphaLevels > 1.0)
                {
                    half levels = (half)_AlphaLevels;
                    alpha = floor(ditheredAlpha * levels + 0.5h) / levels;
                }
                else
                {
                    alpha = ditheredAlpha;
                }

                half3 rgb = tex.rgb * _BaseColor.rgb * (half3)input.color.rgb;
                rgb += tex.rgb * (half3)input.color.rgb * _EmissionColor.rgb;

                if (_AdditiveMode > 0.5)
                {
                    rgb = MixFogColor(rgb, half3(0.0h, 0.0h, 0.0h), input.fogFactor);
                }
                else
                {
                    rgb = MixFog(rgb, input.fogFactor);
                    rgb = ApplyPS1StomachFog(rgb, input.positionWS, input.positionCS);
                }

                return half4(rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
