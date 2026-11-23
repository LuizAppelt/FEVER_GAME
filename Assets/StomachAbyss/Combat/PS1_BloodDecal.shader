Shader "MapaEstomago/URP/PS1_BloodDecal"
{
    Properties
    {
        _BaseMap ("Blood Decal RGBA", 2D) = "white" {}
        _BaseColor ("Color Tint", Color) = (1, 1, 1, 1)
        _EmissionBoost ("Brightness", Range(0, 3)) = 1.0
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.42
        _TintRemap ("Luma Tint Remap (0=Crimson, 1=Custom Ichor)", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest+5"
            "IgnoreProjector" = "True"
        }
        LOD 100
        Cull Off
        ZWrite Off
        ZTest LEqual
        Offset -2, -2

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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
                half   fogFactor  : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _EmissionBoost;
                half _Cutoff;
                half _TintRemap;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(tex.a * input.color.a - _Cutoff);

                half luma = saturate(max(tex.r, max(tex.g, tex.b)) * 1.18);
                half3 crimsonCol = tex.rgb * _BaseColor.rgb;
                half3 ichorCol = luma * _BaseColor.rgb;
                half3 rgb = lerp(crimsonCol, ichorCol, saturate(_TintRemap)) * input.color.rgb * _EmissionBoost;

                rgb = ApplyPS1StomachFog(rgb, input.positionWS, input.positionCS);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
