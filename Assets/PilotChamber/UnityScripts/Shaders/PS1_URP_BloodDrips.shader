Shader "MapaEstomago/URP/PS1_BloodDrips"
{
    Properties
    {
        _BaseMap ("Textura Sangue RGBA", 2D) = "white" {}
        _BaseColor ("Cor do Sangue", Color) = (1, 1, 1, 1)
        _Cutoff ("Corte Alpha PS1", Range(0, 1)) = 0.45
        _DripSpeed ("Velocidade Escorrimento Lento", Range(0, 1)) = 0.040
        _DripSpeedFast ("Velocidade Escorrimento Rapido", Range(0, 1)) = 0.090
        _LaneCount ("Faixas Verticais na Textura", Range(1, 8)) = 4
        _SurgeAmount ("Alternancia de Velocidade (Pulso)", Range(0, 0.5)) = 0.35
        _SurgeFreq ("Frequencia de Alternancia", Range(0.1, 4)) = 1.35
        _WaveSpeed ("Velocidade Onda Lateral", Range(0, 5)) = 1.4
        _WaveAmp ("Amplitude Onda Lateral (UV)", Range(0, 0.08)) = 0.018
        _SwaySpeed ("Velocidade Balanco Sutil", Range(0, 5)) = 1.6
        _SwayAmp ("Amplitude Balanco (m)", Range(0, 0.1)) = 0.0
        _VertexSnap ("Resolucao Vertex Snap PS1", Range(0, 320)) = 180
        _AmbientFloor ("Luz Ambiente Minima", Range(0, 1)) = 0.30
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Float) = 2
        [Toggle] _EnableBG3Cutout ("Corte Visao BG3 (Camera->Player)", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }
        LOD 200
        Cull [_Cull]

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PS1_StomachFog.hlsl"
            #include "PS1_BG3Cutout.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
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
                float4 vcolor     : COLOR;
                half   fogFactor  : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Cutoff;
                float _DripSpeed;
                float _DripSpeedFast;
                float _LaneCount;
                float _SurgeAmount;
                float _SurgeFreq;
                float _WaveSpeed;
                float _WaveAmp;
                float _SwaySpeed;
                float _SwayAmp;
                float _VertexSnap;
                half _AmbientFloor;
                float _EnableBG3Cutout;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posOS = input.positionOS.xyz;
                if (_SwayAmp > 0.0001)
                {
                    float sway = sin(_Time.y * _SwaySpeed + posOS.x * 1.4 + posOS.z * 1.1) * _SwayAmp;
                    posOS.x += sway;
                    posOS.z += sway * 0.7;
                }

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
                output.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));

                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.vcolor = input.color;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                if (_EnableBG3Cutout > 0.5)
                {
                    ApplyBG3CameraCutout(input.positionWS, input.positionCS, input.normalWS);
                }

                float2 baseUV = input.uv;
                float t = _Time.y;

                // Cada faixa vertical (com margem transparente na textura) desce (+V em Unity apos V-flip do glTFast)
                // em uma velocidade alternada propria, sem sobrepor ou duplicar excesso na parede.
                float lanes = max(_LaneCount, 1.0);
                float globalLaneIdx = floor(baseUV.x * lanes);
                float laneU = frac(baseUV.x * lanes);
                float laneFade = sin(laneU * 3.14159265);

                float laneFactor = frac(globalLaneIdx * 0.6180339 + 0.27);
                float laneSpeed = lerp(_DripSpeed, _DripSpeedFast, laneFactor);

                float f = max(_SurgeFreq, 0.05);
                float phase = globalLaneIdx * 1.85;
                float osc = (cos(phase) - cos(t * f + phase)) / f;
                float dist = laneSpeed * (t + _SurgeAmount * osc);

                float2 uvScroll = baseUV;
                uvScroll.y += dist + frac(globalLaneIdx * 0.37);
                uvScroll.x += sin(uvScroll.y * 6.28318 * 1.3 + t * _WaveSpeed + globalLaneIdx * 1.4) * (_WaveAmp * laneFade);

                half4 tex = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uvScroll, 0) * _BaseColor;
                clip(tex.a - _Cutoff);
                half3 bloodCol = tex.rgb;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);

                Light mainLight = GetMainLight(inputData.shadowCoord);
                half ndl = saturate(dot(inputData.normalWS, mainLight.direction));
                half3 totalLight = half3(_AmbientFloor, _AmbientFloor * 0.5, _AmbientFloor * 0.5)
                                 + mainLight.color * (ndl * mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                // Reflexo umido no sangue fresco
                half3 hVec = normalize(mainLight.direction + inputData.viewDirectionWS);
                half spec = pow(saturate(dot(inputData.normalWS, hVec)), 28.0) * 0.45;
                half3 specTotal = mainLight.color * spec;

                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                    half lndl = saturate(dot(inputData.normalWS, light.direction));
                    totalLight += light.color * (lndl * light.distanceAttenuation * light.shadowAttenuation);
                LIGHT_LOOP_END
                #endif

                half3 finalCol = bloodCol * input.vcolor.rgb * totalLight + specTotal;
                finalCol = ApplyPS1StomachFog(finalCol, input.positionWS, input.positionCS);
                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }
    }
}
