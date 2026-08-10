using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Common.Rendering
{
    /// <summary>
    /// 적 캐릭터용 플래너 섀도우 피처.
    ///
    /// 그림자 맵 대신 캐릭터 메시를 지면 평면에 눌러 다시 그린다. 캐릭터 하나당 드로우 1회,
    /// 텍스처 0장이라 모바일에서 라이트 섀도우를 켜지 않고도 접지감을 낼 수 있다.
    /// 대상은 캐스트 섀도우를 끄고 쓴다 (Rush > 적 캐스트 섀도우 끄기 가 프리팹에 베이크한다).
    ///
    /// 대상 선별은 레이어(기본 Enemy)로 한다. 레이어는 렌더러가 붙은 오브젝트 기준이라
    /// 프리팹 루트만 바꾸면 안 되고 모델 자식까지 같은 레이어여야 한다.
    /// </summary>
    [DisallowMultipleRendererFeature("Planar Shadow")]
    public sealed class PlanarShadowRendererFeature : ScriptableRendererFeature
    {
        const string ShaderPath = "Hidden/Rush/PlanarShadow";

        /// <summary>적 캐릭터 레이어 이름. 셋업 액션도 이 이름을 쓴다.</summary>
        public const string EnemyLayerName = "Enemy";

        [Tooltip("Hidden/Rush/PlanarShadow 셰이더. 비어 있으면 에디터에서 자동으로 채운다.")]
        [SerializeField] Shader shadowShader;

        [Tooltip("그림자를 그릴 레이어. 렌더러가 붙은 오브젝트의 레이어 기준이다.")]
        [SerializeField] LayerMask targetLayers = 1 << 8;

        [Tooltip("그림자 색. 알파가 짙기다.")]
        [SerializeField] Color shadowColor = new Color(0f, 0f, 0.02f, 0.45f);

        [Tooltip("그림자를 눕힐 지면 높이(월드 Y). 유닛이 y=0에 서므로 기본은 0이다.")]
        [SerializeField] float planeHeight;

        [Tooltip("지면에서 띄우는 양. z-파이팅이 보이면 조금 올린다.")]
        [Range(0f, 0.2f)]
        [SerializeField] float planeBias = 0.012f;

        [Tooltip("정점이 평면까지 이동할 수 있는 최대 거리. 태양이 낮을 때 그림자가 늘어지는 것을 막는다.")]
        [Range(0.5f, 40f)]
        [SerializeField] float maxStretch = 8f;

        Material shadowMaterial;
        PlanarShadowPass shadowPass;

        public override void Create()
        {
            if (shadowShader == null)
                shadowShader = Shader.Find(ShaderPath);

            shadowPass = new PlanarShadowPass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;

            if (cameraType == CameraType.Preview)
                return;

            if (cameraType == CameraType.Reflection)
                return;

            if (UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
                return;

            if (!TryGetMaterial())
                return;

            ApplyMaterialParams();

            shadowPass.Setup(shadowMaterial, targetLayers.value);

            renderer.EnqueuePass(shadowPass);
        }

        // 인스펙터에서 만지는 값이 바로 보이도록 매 프레임 넣는다. 상수 버퍼 4개라 부담이 없다.
        void ApplyMaterialParams()
        {
            shadowMaterial.SetColor(PlanarShadowShaderIds.ShadowColor, shadowColor);
            shadowMaterial.SetFloat(PlanarShadowShaderIds.PlaneHeight, planeHeight);
            shadowMaterial.SetFloat(PlanarShadowShaderIds.PlaneBias, planeBias);
            shadowMaterial.SetFloat(PlanarShadowShaderIds.MaxStretch, maxStretch);
        }

        bool TryGetMaterial()
        {
            if (shadowMaterial != null)
                return true;

            if (shadowShader == null)
            {
                Debug.LogWarning($"[{nameof(PlanarShadowRendererFeature)}] 셰이더 '{ShaderPath}'를 찾을 수 없어 플래너 섀도우를 건너뛴다.", this);
                return false;
            }

            if (!shadowShader.isSupported)
            {
                Debug.LogWarning($"[{nameof(PlanarShadowRendererFeature)}] 셰이더 '{ShaderPath}'가 이 플랫폼에서 지원되지 않는다.", this);
                return false;
            }

            shadowMaterial = CoreUtils.CreateEngineMaterial(shadowShader);

            return shadowMaterial != null;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            CoreUtils.Destroy(shadowMaterial);
            shadowMaterial = null;
        }

#if UNITY_EDITOR
        // 셰이더 참조를 에셋에 직렬화해 둬야 빌드에 포함된다.
        void OnValidate()
        {
            if (shadowShader != null)
                return;

            Shader found = Shader.Find(ShaderPath);

            if (found == null)
                return;

            shadowShader = found;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }

    static class PlanarShadowShaderIds
    {
        public static readonly int ShadowColor = Shader.PropertyToID("_ShadowColor");
        public static readonly int PlaneHeight = Shader.PropertyToID("_PlaneHeight");
        public static readonly int PlaneBias = Shader.PropertyToID("_PlaneBias");
        public static readonly int MaxStretch = Shader.PropertyToID("_MaxStretch");
    }
}
