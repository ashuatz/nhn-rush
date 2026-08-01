using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Common.Rendering
{
    /// <summary>
    /// 깊이 버퍼에서 월드 좌표를 복원해 2D LUT 포그를 씬 컬러에 합성하는 스크린스페이스 패스.
    ///
    /// 원본(포워드 셰이더 내부 합성)과 달리 셰이더를 수정하지 않는다.
    /// 대신 씬 컬러를 한 번 복사한 뒤 LUT 머티리얼로 되돌려 그린다.
    /// </summary>
    sealed class LutHeightFogPass : ScriptableRenderPass
    {
        const string ColorCopyName = "_LutHeightFogColorCopy";
        const string CopyPassName = "LUT Height Fog Copy Color";
        const string FogPassName = "LUT Height Fog";

        static readonly MaterialPropertyBlock SharedPropertyBlock = new MaterialPropertyBlock();
        static readonly Vector4 FullScreenScaleBias = new Vector4(1f, 1f, 0f, 0f);

        Material material;

        public LutHeightFogPass()
        {
            profilingSampler = new ProfilingSampler(FogPassName);

            // 깊이 텍스처가 필요하고, 씬 컬러를 읽으므로 백버퍼 직행을 막는다.
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
        }

        public void Setup(Material fogMaterial)
        {
            material = fogMaterial;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
                return;

            LutHeightFogVolume fogVolume = VolumeManager.instance.stack.GetComponent<LutHeightFogVolume>();

            if (fogVolume == null)
                return;

            if (!fogVolume.IsActive())
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
                   renderGraph.AddRasterRenderPass(FogPassName, out PassData passData, profilingSampler))
            {
                passData.material = material;
                passData.sourceTexture = colorCopy;
                passData.fogLut = fogVolume.fogLut.value;
                passData.rangeParams = fogVolume.GetRangeParams();
                passData.blendParams = fogVolume.GetBlendParams();

                builder.UseTexture(colorCopy, AccessFlags.Read);
                builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => ExecuteFogPass(context.cmd, data));
            }
        }

        static void ExecuteFogPass(RasterCommandBuffer cmd, PassData data)
        {
            SharedPropertyBlock.Clear();
            SharedPropertyBlock.SetTexture(LutHeightFogShaderIds.BlitTexture, data.sourceTexture);
            SharedPropertyBlock.SetVector(LutHeightFogShaderIds.BlitScaleBias, FullScreenScaleBias);
            SharedPropertyBlock.SetTexture(LutHeightFogShaderIds.FogLut, data.fogLut);
            SharedPropertyBlock.SetVector(LutHeightFogShaderIds.FogRangeParams, data.rangeParams);
            SharedPropertyBlock.SetVector(LutHeightFogShaderIds.FogBlendParams, data.blendParams);

            cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, SharedPropertyBlock);
        }

        class PassData
        {
            internal Material material;
            internal TextureHandle sourceTexture;
            internal Texture fogLut;
            internal Vector4 rangeParams;
            internal Vector4 blendParams;
        }
    }
}
