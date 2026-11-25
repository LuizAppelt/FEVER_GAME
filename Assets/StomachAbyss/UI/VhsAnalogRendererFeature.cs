using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace StomachAbyss.UI
{
    /// <summary>
    /// URP Render Graph Renderer Feature para o filtro de Fita VHS Gasta (Opcao 2).
    /// - Aplica apenas na Camera de Jogo (Scene View fica limpa para edicao).
    /// - Controlado em tempo real pela intensidade configurada em GameSettingsManager.VhsIntensity.
    /// </summary>
    public class VhsAnalogRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private Material vhsMaterial;
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;

        private VhsAnalogPass _pass;

        public Material VhsMaterial
        {
            get => vhsMaterial;
            set => vhsMaterial = value;
        }

        public override void Create()
        {
            _pass = new VhsAnalogPass(vhsMaterial)
            {
                renderPassEvent = injectionPoint
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (vhsMaterial == null || _pass == null) return;
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            if (renderingData.cameraData.camera != null && renderingData.cameraData.camera.targetTexture != null) return;

            float intensity = Application.isPlaying ? GameSettingsManager.VhsIntensity : 0.20f;
            if (intensity <= 0.001f) return;

            Shader.SetGlobalFloat(VhsAnalogPass.PropGlobalIntensity, intensity);
            _pass.SetMaterial(vhsMaterial);
            _pass.renderPassEvent = injectionPoint;
            renderer.EnqueuePass(_pass);
        }

        private sealed class VhsAnalogPass : ScriptableRenderPass
        {
            public static readonly int PropGlobalIntensity = Shader.PropertyToID("_StomachVhsIntensity");
            private Material _material;

            public VhsAnalogPass(Material material)
            {
                _material = material;
                requiresIntermediateTexture = true;
            }

            public void SetMaterial(Material material)
            {
                _material = material;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle source = resourceData.activeColorTexture;
                if (!source.IsValid()) return;

                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "_VhsAnalogColorTemp";
                desc.clearBuffer = false;
                desc.msaaSamples = MSAASamples.None;

                TextureHandle destination = renderGraph.CreateTexture(desc);

                RenderGraphUtils.BlitMaterialParameters blitParams =
                    new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0);

                renderGraph.AddBlitPass(blitParams, "VHS_Analog_Op2_Pass");
                resourceData.cameraColor = destination;
            }
        }
    }
}
