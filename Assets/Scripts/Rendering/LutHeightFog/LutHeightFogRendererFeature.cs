using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Common.Rendering
{
    /// <summary>
    /// 2D LUT 거리/높이 포그를 URP 렌더러에 끼워 넣는 피처.
    /// 파라미터는 <see cref="LutHeightFogVolume"/>이 들고 있다.
    /// </summary>
    [DisallowMultipleRendererFeature("LUT Height Fog")]
    public sealed class LutHeightFogRendererFeature : ScriptableRendererFeature
    {
        /// <summary>포그를 어디서 합성할지.</summary>
        public enum FogApplyMode
        {
            /// <summary>
            /// 깊이 기반 풀스크린 합성. 스톡 URP 셰이더(Lit 등)를 그대로 쓰는 오브젝트에 적용된다.
            /// 하늘까지 처리할 수 있고 Deferred에서도 동작한다. 컬러 사본 1장을 쓴다.
            /// </summary>
            ScreenSpacePass,

            /// <summary>
            /// `Common/Simple Lit` 머티리얼이 포워드 패스에서 직접 적용한다.
            /// 글로벌 프로퍼티만 세팅하고 풀스크린 패스는 돌지 않는다.
            /// 컬러 사본이 없어 더 싸지만, 그 셰이더를 쓰는 오브젝트만 포그에 잠긴다
            /// (스카이박스와 스톡 셰이더 오브젝트는 제외).
            /// </summary>
            ForwardMaterial,
        }

        const string ShaderPath = "Hidden/Common/LutHeightFog";

        [Tooltip("포그 합성 방식. ScreenSpacePass = 스톡 셰이더 포함 전부 적용. "
                 + "ForwardMaterial = Common/Simple Lit 머티리얼만 적용.")]
        [SerializeField] FogApplyMode applyMode = FogApplyMode.ScreenSpacePass;

        [Tooltip("ScreenSpacePass 합성 시점. 반투명도 포그에 잠기게 하려면 그 이후를 쓴다.")]
        [SerializeField] InjectionPoint injectionPoint = InjectionPoint.BeforeRenderingPostProcessing;

        [Tooltip("Hidden/Common/LutHeightFog 셰이더. 비어 있으면 에디터에서 자동으로 채운다.")]
        [SerializeField] Shader fogShader;

        /// <summary>ScreenSpacePass 합성 시점.</summary>
        public enum InjectionPoint
        {
            BeforeRenderingTransparents = RenderPassEvent.BeforeRenderingTransparents,
            BeforeRenderingPostProcessing = RenderPassEvent.BeforeRenderingPostProcessing,
            AfterRenderingPostProcessing = RenderPassEvent.AfterRenderingPostProcessing,
        }

        Material fogMaterial;
        LutHeightFogPass fogPass;

        public override void Create()
        {
            if (fogShader == null)
                fogShader = Shader.Find(ShaderPath);

            fogPass = new LutHeightFogPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;

            if (cameraType == CameraType.Preview)
                return;

            if (cameraType == CameraType.Reflection)
                return;

            // 깊이 전용 타겟 카메라는 컬러 리소스가 없어 사본 생성이 성립하지 않는다.
            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
                return;

            LutHeightFogVolume fogVolume = VolumeManager.instance.stack?.GetComponent<LutHeightFogVolume>();

            // 포워드 머티리얼이 직접 적용하는 모드. 글로벌만 채우고 패스는 돌리지 않는다.
            if (applyMode == FogApplyMode.ForwardMaterial)
            {
                PushGlobalFogParams(fogVolume);
                return;
            }

            // 스크린스페이스 모드에서는 포워드 셰이더가 같은 포그를 또 얹지 않도록 농도를 눌러둔다.
            DisableGlobalFogParams();

            if (!TryGetMaterial())
                return;

            fogPass.renderPassEvent = (RenderPassEvent)injectionPoint;
            fogPass.Setup(fogMaterial);

            renderer.EnqueuePass(fogPass);
        }

        /// <summary>
        /// `Common/Simple Lit` 이 읽는 글로벌 프로퍼티를 채운다.
        /// 임의의 머티리얼이 대상이라 MaterialPropertyBlock으로는 전달할 수 없다.
        /// </summary>
        static void PushGlobalFogParams(LutHeightFogVolume fogVolume)
        {
            if (fogVolume == null || !fogVolume.IsActive())
            {
                DisableGlobalFogParams();
                return;
            }

            Shader.SetGlobalTexture(LutHeightFogShaderIds.FogLut, fogVolume.fogLut.value);
            Shader.SetGlobalVector(LutHeightFogShaderIds.FogRangeParams, fogVolume.GetRangeParams());
            Shader.SetGlobalVector(LutHeightFogShaderIds.FogBlendParams, fogVolume.GetBlendParams());
        }

        /// <summary>
        /// 농도를 0으로 만들어 포워드 셰이더의 포그를 무력화한다.
        /// LUT 텍스처는 그대로 둬도 lerp 가중치가 0이라 결과에 영향이 없다.
        /// </summary>
        static void DisableGlobalFogParams()
        {
            Shader.SetGlobalVector(LutHeightFogShaderIds.FogBlendParams, Vector4.zero);
        }

        bool TryGetMaterial()
        {
            if (fogMaterial != null)
                return true;

            if (fogShader == null)
            {
                Debug.LogWarning($"[{nameof(LutHeightFogRendererFeature)}] 셰이더 '{ShaderPath}'를 찾을 수 없어 포그를 건너뛴다.", this);
                return false;
            }

            if (!fogShader.isSupported)
            {
                Debug.LogWarning($"[{nameof(LutHeightFogRendererFeature)}] 셰이더 '{ShaderPath}'가 이 플랫폼에서 지원되지 않는다.", this);
                return false;
            }

            fogMaterial = CoreUtils.CreateEngineMaterial(fogShader);

            return fogMaterial != null;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CoreUtils.Destroy(fogMaterial);
            fogMaterial = null;
        }

#if UNITY_EDITOR
        // 셰이더 참조를 에셋에 직렬화해 둬야 빌드에 포함된다.
        // Shader.Find는 빌드에서 신뢰할 수 없으므로 여기서 한 번 고정한다.
        void OnValidate()
        {
            if (fogShader != null)
                return;

            Shader found = Shader.Find(ShaderPath);

            if (found == null)
                return;

            fogShader = found;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
