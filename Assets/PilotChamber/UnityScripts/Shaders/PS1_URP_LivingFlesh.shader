Shader "MapaEstomago/URP/PS1_LivingFlesh"
{
    Properties
    {
        _BaseMap ("Textura Base", 2D) = "white" {}
        _BaseColor ("Cor Base", Color) = (1, 1, 1, 1)
        _VertexSnap ("Resolucao Vertex Snap PS1 (0 = desativado)", Range(0, 320)) = 180
        _BreathSpeed ("Velocidade Respiracao Peristaltica", Range(0, 6)) = 1.4
        _BreathAmp ("Amplitude Respiracao (m)", Range(0, 0.15)) = 0.02
        _SplitAmount ("Controle de Frestas PS1 (0=Zero, 1=Total)", Range(0, 1)) = 0.28
        _Wetness ("Brilho Umido da Carne", Range(0, 1)) = 0.35
        _AmbientFloor ("Luz Ambiente Minima", Range(0, 1)) = 0.24
        [HDR] _EmissionColor ("Cor Emissao (Opcional)", Color) = (0, 0, 0, 1)
        [Toggle] _UseVertexColor ("Usar Vertex Colors do GLB", Float) = 1
        [Toggle] _EnableBG3Cutout ("Corte Visao BG3 (Camera->Player)", Float) = 0
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

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _VertexSnap;
                float _BreathSpeed;
                float _BreathAmp;
                float _SplitAmount;
                half _Wetness;
                half _AmbientFloor;
                half4 _EmissionColor;
                float _UseVertexColor;
                float _EnableBG3Cutout;
            CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

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

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posOS = input.positionOS.xyz;
                if (_BreathAmp > 0.0001 && _BreathSpeed > 0.001)
                {
                    float phase = posOS.x * 0.65 + posOS.z * 0.55 + posOS.y * 0.85;
                    float wave = sin(_Time.y * _BreathSpeed + phase);
                    float3 smoothDir = normalize(float3(posOS.x, posOS.y * 0.35 + 0.25, posOS.z) + float3(0.0, 0.001, 0.0));
                    float3 dispDir = normalize(lerp(smoothDir, input.normalOS, saturate(_SplitAmount)));
                    posOS += dispDir * (wave * _BreathAmp);
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
                output.vcolor = (_UseVertexColor > 0.5) ? input.color : float4(1, 1, 1, 1);
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

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 albedo = tex.rgb * input.vcolor.rgb;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);

                // Luz ambiente + direcional principal
                Light mainLight = GetMainLight(inputData.shadowCoord);
                half mainNdotL = saturate(dot(inputData.normalWS, mainLight.direction));
                half3 totalLight = half3(_AmbientFloor, _AmbientFloor * 0.55, _AmbientFloor * 0.62);
                totalLight += mainLight.color * (mainNdotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                half3 specTotal = half3(0, 0, 0);
                if (_Wetness > 0.01)
                {
                    half3 hVec = normalize(mainLight.direction + inputData.viewDirectionWS);
                    half spec = pow(saturate(dot(inputData.normalWS, hVec)), 24.0) * _Wetness;
                    specTotal += mainLight.color * spec * mainLight.shadowAttenuation;
                }

                // Point lights (ex: brilho verde do acido e luzes vermelhas) compativel com Forward+
                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData.positionWS);
                    half ndl = saturate(dot(inputData.normalWS, light.direction));
                    totalLight += light.color * (ndl * light.distanceAttenuation * light.shadowAttenuation);
                    if (_Wetness > 0.01)
                    {
                        half3 hVec = normalize(light.direction + inputData.viewDirectionWS);
                        half spec = pow(saturate(dot(inputData.normalWS, hVec)), 20.0) * _Wetness * 0.7;
                        specTotal += light.color * spec * light.distanceAttenuation;
                    }
                LIGHT_LOOP_END
                #endif

                half3 color = albedo * totalLight + specTotal + albedo * _EmissionColor.rgb;
                color = ApplyPS1StomachFog(color, input.positionWS, input.positionCS);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 posOS = input.positionOS.xyz;
                if (_BreathAmp > 0.0001 && _BreathSpeed > 0.001)
                {
                    float phase = posOS.x * 0.65 + posOS.z * 0.55 + posOS.y * 0.85;
                    float wave = sin(_Time.y * _BreathSpeed + phase);
                    float3 smoothDir = normalize(float3(posOS.x, posOS.y * 0.35 + 0.25, posOS.z) + float3(0.0, 0.001, 0.0));
                    float3 dispDir = normalize(lerp(smoothDir, input.normalOS, saturate(_SplitAmount)));
                    posOS += dispDir * (wave * _BreathAmp);
                }
                float3 posWS = TransformObjectToWorld(posOS);
                float3 normWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirWS = normalize(_LightPosition - posWS);
                #else
                    float3 lightDirWS = _LightDirection;
                #endif
                float4 posCS = TransformWorldToHClip(ApplyShadowBias(posWS, normWS, lightDirWS));
                output.positionCS = ApplyShadowClamping(posCS);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
