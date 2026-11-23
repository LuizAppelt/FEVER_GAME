Shader "MapaEstomago/URP/PS1_VirusPlayer"
{
    Properties
    {
        _BaseMap ("Textura Albedo Pixel Art (128x128)", 2D) = "white" {}
        _BaseColor ("Cor Multiplicadora", Color) = (1, 1, 1, 1)
        _EmissionMap ("Textura Emissiva Pixel Art (128x128)", 2D) = "black" {}
        [HDR] _EmissionColor ("Cor Emissiva Bioluminescente", Color) = (0.35, 1.65, 2.10, 1)
        _PulseSpeed ("Velocidade Pulso Bioluminescente", Range(0, 8)) = 2.8
        _VertexSnap ("Resolucao Vertex Snap PS1 (0 = desativado)", Range(0, 320)) = 200
        _Wetness ("Brilho Carapaca Viral", Range(0, 1)) = 0.55
        _AmbientFloor ("Luz Ambiente Minima", Range(0, 1)) = 0.34
        _HitFlash ("Flash de Dano / Ataque (0..1)", Range(0, 1)) = 0
        [HDR] _HitFlashColor ("Cor do Flash", Color) = (0.45, 2.2, 0.35, 1)
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
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                float _PulseSpeed;
                float _VertexSnap;
                half _Wetness;
                half _AmbientFloor;
                half _HitFlash;
                half4 _HitFlashColor;
            CBUFFER_END
        ENDHLSL

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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "PS1_StomachFog.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
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
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                half3 emissiveSample = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb;

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalize(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);

                Light mainLight = GetMainLight(inputData.shadowCoord);
                half mainNdotL = saturate(dot(inputData.normalWS, mainLight.direction));
                half3 totalLight = half3(_AmbientFloor, _AmbientFloor * 0.85, _AmbientFloor * 0.95);
                totalLight += mainLight.color * (mainNdotL * mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                half3 specTotal = half3(0, 0, 0);
                if (_Wetness > 0.01)
                {
                    half3 hVec = normalize(mainLight.direction + inputData.viewDirectionWS);
                    half spec = pow(saturate(dot(inputData.normalWS, hVec)), 24.0) * _Wetness;
                    specTotal += mainLight.color * spec * mainLight.shadowAttenuation;
                }

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

                half pulse = 0.88 + 0.16 * sin(_Time.y * _PulseSpeed);
                half3 emission = emissiveSample * _EmissionColor.rgb * pulse;

                half3 color = albedoSample.rgb * totalLight + specTotal + emission;
                color = lerp(color, _HitFlashColor.rgb, saturate(_HitFlash));
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
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
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
