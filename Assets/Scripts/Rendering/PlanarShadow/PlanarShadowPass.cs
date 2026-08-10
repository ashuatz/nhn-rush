using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Common.Rendering
{
    /// <summary>
    /// 지정한 레이어의 오브젝트를 플래너 섀도우 머티리얼로 한 번 더 그리는 패스.
    ///
    /// 불투명 이후에 넣는다. 지면이 이미 깊이에 있어야 그림자가 지형에 가려질 수 있고,
    /// 스카이박스/투명 앞이라 이후 스크린스페이스 포그도 그림자에 함께 적용된다.
    ///
    /// 스텐실을 읽고 쓰므로 깊이 어태치먼트를 ReadWrite로 잡는다.
    /// </summary>
    sealed class PlanarShadowPass : ScriptableRenderPass
    {
        const string PassName = "Planar Shadow";

        // 오버라이드 머티리얼을 쓰더라도 대상 선별은 원본 머티리얼의 패스 태그로 한다.
        static readonly List<ShaderTagId> ShaderTagIds = new List<ShaderTagId>
        {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
        };

        Material shadowMaterial;
        int targetLayerMask;

        public PlanarShadowPass()
        {
            profilingSampler = new ProfilingSampler(PassName);
            renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public void Setup(Material material, int layerMask)
        {
            shadowMaterial = material;
            targetLayerMask = layerMask;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (shadowMaterial == null)
                return;

            if (targetLayerMask == 0)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            if (!resourceData.activeColorTexture.IsValid())
                return;

            if (!resourceData.activeDepthTexture.IsValid())
                return;

            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass(PassName, out PassData passData, profilingSampler))
            {
                passData.rendererList = CreateShadowRendererList(renderGraph, renderingData, cameraData, lightData);

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => context.cmd.DrawRendererList(data.rendererList));
            }
        }

        RendererListHandle CreateShadowRendererList(RenderGraph renderGraph, UniversalRenderingData renderingData,
            UniversalCameraData cameraData, UniversalLightData lightData)
        {
            DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(
                ShaderTagIds, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);

            drawingSettings.overrideMaterial = shadowMaterial;
            drawingSettings.overrideMaterialPassIndex = 0;

            // 단색 그림자라 라이트맵/프로브 등 오브젝트별 라이팅 데이터가 필요 없다.
            drawingSettings.perObjectData = PerObjectData.None;

            var filteringSettings = new FilteringSettings(RenderQueueRange.opaque, targetLayerMask);

            var listParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);

            return renderGraph.CreateRendererList(listParams);
        }

        class PassData
        {
            internal RendererListHandle rendererList;
        }
    }
}
