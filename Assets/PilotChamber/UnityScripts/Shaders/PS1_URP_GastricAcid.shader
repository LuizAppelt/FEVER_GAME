Shader "MapaEstomago/URP/PS1_GastricAcid"
{
    Properties
    {
        _BaseMap ("Textura Acido", 2D) = "white" {}
        _BaseColor ("Cor Base", Color) = (0.85, 1.0, 0.75, 1)
        _EmissionColor ("Cor Emissiva HDR", Color) = (0.35, 1.4, 0.15, 1)
        _ScrollSpeedA ("Velocidade Camada 1 (XY)", Vector) = (0.045, 0.032, 0, 0)
        _ScrollSpeedB ("Velocidade Camada 2 (XY)", Vector) = (-0.028, 0.052, 0, 0)
        _WaveSpeed ("Velocidade Fervura Vertices", Range(0, 8)) = 2.6
        _WaveAmp ("Amplitude Ondulacao (m)", Range(0, 0.12)) = 0.035
        _VertexSnap ("Resolucao Vertex Snap PS1", Range(0, 320)) = 180
        _PulseSpeed ("Velocidade Brilho Pulsante", Range(0, 8)) = 2.2
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
            #include "PS1_StomachFog.hlsl"

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
                float2 uvA        : TEXCOORD0;
                float2 uvB        : TEXCOORD1;
                float4 vcolor     : COLOR;
                half   fogFactor  : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                float4 _ScrollSpeedA;
                float4 _ScrollSpeedB;
                float _WaveSpeed;
                float _WaveAmp;
                float _VertexSnap;
                float _PulseSpeed;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posOS = input.positionOS.xyz;
                float boil = sin(_Time.y * _WaveSpeed + posOS.x * 1.45 + posOS.z * 1.25)
                           + 0.5 * cos(_Time.y * (_WaveSpeed * 1.35) - posOS.x * 2.1 + posOS.z * 1.8);
                posOS.y += boil * _WaveAmp;

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
                float2 baseUV = TRANSFORM_TEX(input.uv, _BaseMap);
                output.uvA = baseUV + _Time.y * _ScrollSpeedA.xy;
                output.uvB = baseUV * 1.35 + _Time.y * _ScrollSpeedB.xy;
                output.vcolor = input.color;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 texA = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uvA);
                half4 texB = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uvB);

                // Combina duas camadas para simular espuma toxica colidindo e fervendo
                half3 combined = max(texA.rgb, texB.rgb * 0.85) * 0.75 + (texA.rgb * texB.rgb) * 0.55;
                half pulse = 0.88 + 0.18 * sin(_Time.y * _PulseSpeed);
                half3 litColor = combined * _BaseColor.rgb * input.vcolor.rgb * pulse;
                half3 emission = combined * _EmissionColor.rgb * pulse;

                half3 finalCol = litColor + emission;
                finalCol = ApplyPS1StomachFog(finalCol, input.positionWS, input.positionCS);
                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }
    }
}
