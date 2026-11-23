Shader "MapaEstomago/URP/PS1_StomachGasWisp"
{
    Properties
    {
        _GasColor ("Cor do Vapor Gastrico", Color) = (0.26, 0.48, 0.18, 0.22)
        _VertexSnap ("Resolucao Vertex Snap PS1", Range(0, 320)) = 0
        _DriftSpeed ("Velocidade Deriva", Range(0, 3)) = 0.55
        _WaveAmp ("Amplitude Ondulacao Low-Poly (m)", Range(0, 0.6)) = 0.16
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

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
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 vcolor     : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _GasColor;
                float _VertexSnap;
                float _DriftSpeed;
                float _WaveAmp;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posOS = input.positionOS.xyz;
                float t = _Time.y * _DriftSpeed;
                posOS.y += sin(t * 1.6 + posOS.x * 0.75 + posOS.z * 0.65) * _WaveAmp;

                VertexPositionInputs posInputs = GetVertexPositionInputs(posOS);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.uv = input.uv;
                output.vcolor = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                if (_StomachFogPlayerPos.w < 0.5 || _StomachFogParams.x <= 0.001)
                    discard;

                float3 focusPos   = _StomachFogPlayerPos.xyz;
                float clearRadius = _StomachFogRadii.x;
                float midRadius   = _StomachFogRadii.y;
                float maxRadius   = _StomachFogRadii.z;

                // Borda suave na malha low-poly
                float2 centeredUV = input.uv * 2.0 - 1.0;
                float radialMask = saturate(1.0 - dot(centeredUV, centeredUV));
                radialMask = radialMask * radialMask;

                // Pixelizacao PS1 no espaco do mundo para blocos de vapor low-poly
                float3 quantizedWS = floor(input.positionWS * 3.0) / 3.0;
                float gasNoise = SampleSilentHillGasNoise(quantizedWS, 0.20, float2(0.42, 0.28));

                // Zero absoluto de neblina ao redor do jogador; nuvens aparecem no medio alcance
                float distToPlayer = length(input.positionWS.xz - focusPos.xz);
                float playerClearMask = smoothstep(clearRadius * 1.15, midRadius * 1.05, distToPlayer);
                float farFade = 1.0 - smoothstep(maxRadius * 0.85, maxRadius * 1.35, distToPlayer);

                // Concentra mais nos bolsoes de gas (onde gasNoise > 0.4) para nao lavar a cena uniformemente
                float cloudPocket = smoothstep(0.32, 0.78, gasNoise);
                float alpha = radialMask * cloudPocket * _GasColor.a * input.vcolor.a * playerClearMask * farFade;

                // Dither PS1 Bayer 4x4 suave no alpha
                float dither = GetPS1BayerDither4x4(input.positionCS.xy);
                alpha = saturate(alpha + dither * 0.04 * radialMask);
                if (alpha < 0.01) discard;

                half3 col = ApplyPS1StomachFog(_GasColor.rgb * input.vcolor.rgb, input.positionWS, input.positionCS);
                return half4(col, (half)alpha);
            }
            ENDHLSL
        }
    }
}
