Shader "MapaEstomago/URP/PS1_TriplanarFlesh"
{
    Properties
    {
        _BaseMap ("Textura Base", 2D) = "white" {}
        _BaseColor ("Cor Base", Color) = (1, 1, 1, 1)
        _TextureScale ("Escala da Textura (Metros por repeticao)", Float) = 2.0
        _TriplanarSharpness ("Nitidez de Transicao das Faces", Range(1, 16)) = 5.0
        [Toggle] _UseWorldSpace ("Mapeamento no Espaco do Mundo (Alinhamento Perfeito)", Float) = 1
        _VertexSnap ("Resolucao Vertex Snap PS1 (0 = desativado)", Range(0, 320)) = 180
        _BreathSpeed ("Velocidade Respiracao Peristaltica", Range(0, 6)) = 1.2
        _BreathAmp ("Amplitude Respiracao (m)", Range(0, 0.15)) = 0.012
        _OrganicBulge ("Ondulacao Organica de Carne (Bulge)", Range(0, 0.35)) = 0.08
        _BulgeFrequency ("Frequencia das Ondulacoes", Range(0.1, 3.0)) = 0.85
        _SplitAmount ("Controle de Deformacao Organica", Range(0, 1)) = 0.25
        _Wetness ("Brilho Umido da Carne", Range(0, 1)) = 0.35
        _AmbientFloor ("Luz Ambiente Minima", Range(0, 1)) = 0.24
        [HDR] _EmissionColor ("Cor Emissao (Opcional)", Color) = (0, 0, 0, 1)
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
                float _TextureScale;
                float _TriplanarSharpness;
                float _UseWorldSpace;
                float _VertexSnap;
                float _BreathSpeed;
                float _BreathAmp;
                float _OrganicBulge;
                float _BulgeFrequency;
                float _SplitAmount;
                half _Wetness;
                half _AmbientFloor;
                half4 _EmissionColor;
                float _EnableBG3Cutout;
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
                float3 positionOS : TEXCOORD0;
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

                if (_OrganicBulge > 0.001)
                {
                    float3 wp = posInputs.positionWS * _BulgeFrequency;
                    float n1 = sin(wp.x * 1.4 + wp.z * 1.1) * cos(wp.y * 1.6 + wp.x * 0.7);
                    float n2 = sin(wp.y * 2.0 + wp.z * 1.5) * 0.5;
                    float bulgeDisp = (n1 + n2) * _OrganicBulge;
                    posOS += input.normalOS * bulgeDisp;
                    posInputs = GetVertexPositionInputs(posOS);
                }

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
                output.positionOS = input.positionOS.xyz;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
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

                // Coordenadas Triplanares com ajuste automatico de escala
                float3 coords = (_UseWorldSpace > 0.5) ? input.positionWS : input.positionOS;
                float scaleInv = 1.0 / max(0.001, _TextureScale);
                float2 uvX = coords.zy * scaleInv;
                float2 uvY = coords.xz * scaleInv;
                float2 uvZ = coords.xy * scaleInv;

                // Pesos normais para transicao suave e sem esticamento em curvas
                half3 blendWeights = pow(abs(input.normalWS), max(1.0, _TriplanarSharpness));
                blendWeights /= (blendWeights.x + blendWeights.y + blendWeights.z + 0.0001);

                half4 colX = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX);
                half4 colY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY);
                half4 colZ = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ);

                half4 tex = (colX * blendWeights.x + colY * blendWeights.y + colZ * blendWeights.z) * _BaseColor;
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

        // Shadow Caster Pass para que blocos e paredes do ProBuilder projetem sombras PS1 corretas
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 _LightDirection;

            ShadowVaryings ShadowPassVertex(ShadowAttributes input)
            {
                ShadowVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, normalWS, _LightDirection));
                return output;
            }

            half4 ShadowPassFragment(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
