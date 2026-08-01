using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Common.Rendering
{
    /// <summary>
    /// DOF 포커스 디버그 뷰를 화면에 그리는 패스.
    /// 후처리 이후에 넣어 최종 화면을 덮는다.
    /// </summary>
    sealed class DofDebugPass : ScriptableRenderPass
    {
        const string ColorCopyName = "_DofDebugColorCopy";
        const string CopyPassName = "DOF Debug Copy Color";
        const string DebugPassName = "DOF Debug View";

        static readonly MaterialPropertyBlock SharedPropertyBlock = new MaterialPropertyBlock();
        static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

        Material material;

        public DofDebugPass()
        {
            profilingSampler = new ProfilingSampler(DebugPassName);

            // 깊이에서 CoC를 계산하고, Overlay 모드는 씬 컬러도 읽는다.
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
        }

        public void Setup(Material debugMaterial)
        {
            material = debugMaterial;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
                return;

            if (!DofDebugState.Enabled)
                return;

            if (!DofDebugMath.TryGetParams(out DofDebugParams dofParams))
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            if (!resourceData.activeColorTexture.IsValid())
                return;

            if (!resourceData.cameraDepthTexture.IsValid())
                return;

            // 같은 타겟을 읽으면서 쓸 수 없으므로 씬 컬러 사본을 만든다.
            TextureDesc copyDesc = renderGraph.GetTextureDesc(resourceData.cameraColor);
            copyDesc.name = ColorCopyName;
            copyDesc.clearBuffer = false;

            TextureHandle colorCopy = renderGraph.CreateTexture(copyDesc);
            renderGraph.AddBlitPass(resourceData.activeColorTexture, colorCopy, Vector2.one, Vector2.zero, passName: CopyPassName);

            using (IRasterRenderGraphBuilder builder =
                   renderGraph.AddRasterRenderPass(DebugPassName, out PassData passData, profilingSampler))
            {
                passData.material = material;
                passData.sourceTexture = colorCopy;
                passData.debugParams = dofParams.ToShaderParams(DofDebugState.FocusThreshold);
                passData.viewParams = new Vector4((float)DofDebugState.View, DofDebugState.OverlayDarkness, 0f, 0f);

                builder.UseTexture(colorCopy, AccessFlags.Read);
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => ExecuteDebugPass(context.cmd, data));
            }

            // 창이 "실제로 그려지는 중"인지 표시하는 데 쓴다.
            DofDebugState.LastRenderedFrame = Time.frameCount;
        }

        static void ExecuteDebugPass(RasterCommandBuffer cmd, PassData data)
        {
            SharedPropertyBlock.Clear();
            SharedPropertyBlock.SetTexture(DofDebugShaderIds.BlitTexture, data.sourceTexture);
            SharedPropertyBlock.SetVector(DofDebugShaderIds.BlitScaleBias, FullScreenScaleBias);
            SharedPropertyBlock.SetVector(DofDebugShaderIds.DofDebugParams, data.debugParams);
            SharedPropertyBlock.SetVector(DofDebugShaderIds.DofDebugView, data.viewParams);

            cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, SharedPropertyBlock);
        }

        class PassData
        {
            internal Material material;
            internal TextureHandle sourceTexture;
            internal Vector4 debugParams;
            internal Vector4 viewParams;
        }
    }
}
