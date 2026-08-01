using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Common.Rendering
{
    /// <summary>
    /// DOF 포커스 디버그 뷰 피처.
    ///
    /// 렌더러 에셋에 한 번 추가해두고, 켜고 끄는 것은
    /// `Common > DOF Focus Debug` 창이 <see cref="DofDebugState"/>를 통해 한다.
    /// 디버그를 켤 때마다 렌더러 에셋을 더럽히지 않기 위한 구성이다.
    /// </summary>
    [DisallowMultipleRendererFeature("DOF Focus Debug")]
    public sealed class DofDebugRendererFeature : ScriptableRendererFeature
    {
        const string ShaderPath = "Hidden/Common/DofDebug";

        [Tooltip("Hidden/Common/DofDebug 셰이더. 비어 있으면 에디터에서 자동으로 채운다.")]
        [SerializeField] Shader debugShader;

        Material debugMaterial;
        DofDebugPass debugPass;

        public override void Create()
        {
            if (debugShader == null)
                debugShader = Shader.Find(ShaderPath);

            debugPass = new DofDebugPass();

            // 후처리 결과 위에 덮어써야 최종 화면 기준으로 판단할 수 있다.
            debugPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 창에서 껐으면 아무것도 하지 않는다. 셰이더 로드도 시도하지 않는다.
            if (!DofDebugState.Enabled)
                return;

            CameraType cameraType = renderingData.cameraData.cameraType;

            if (cameraType == CameraType.Preview)
                return;

            if (cameraType == CameraType.Reflection)
                return;

            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
                return;

            if (!TryGetMaterial())
                return;

            debugPass.Setup(debugMaterial);

            renderer.EnqueuePass(debugPass);
        }

        bool TryGetMaterial()
        {
            if (debugMaterial != null)
                return true;

            if (debugShader == null)
            {
                Debug.LogWarning($"[{nameof(DofDebugRendererFeature)}] 셰이더 '{ShaderPath}'를 찾을 수 없어 디버그 뷰를 건너뛴다.", this);
                return false;
            }

            if (!debugShader.isSupported)
            {
                Debug.LogWarning($"[{nameof(DofDebugRendererFeature)}] 셰이더 '{ShaderPath}'가 이 플랫폼에서 지원되지 않는다.", this);
                return false;
            }

            debugMaterial = CoreUtils.CreateEngineMaterial(debugShader);

            return debugMaterial != null;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CoreUtils.Destroy(debugMaterial);
            debugMaterial = null;
        }

#if UNITY_EDITOR
        // 셰이더 참조를 에셋에 직렬화해 둬야 빌드에 포함된다.
        void OnValidate()
        {
            if (debugShader != null)
                return;

            Shader found = Shader.Find(ShaderPath);

            if (found == null)
                return;

            debugShader = found;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
