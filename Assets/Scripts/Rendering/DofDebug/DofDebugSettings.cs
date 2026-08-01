using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Common.Rendering
{
    /// <summary>디버그 뷰 표시 방식.</summary>
    public enum DofDebugView
    {
        /// <summary>포커스 영역만 흰색, 나머지는 검정. 어디가 또렷하게 보이는지 판단용.</summary>
        FocusMask = 0,

        /// <summary>씬 위에 겹쳐 본다. 블러 영역을 어둡게 깔아 무엇이 포커스인지 형체로 확인한다.</summary>
        Overlay = 1,

        /// <summary>블러 강도 계조. 흰색이 최대 블러. 포커스 전이 구간을 볼 때 쓴다.</summary>
        BlurAmount = 2,
    }

    /// <summary>
    /// DOF 디버그 뷰의 세션 한정 상태.
    ///
    /// 렌더러 에셋이나 볼륨을 더럽히지 않도록 static으로만 들고 있다.
    /// 도메인 리로드에서 자동으로 초기화되고, 창을 닫으면 창이 꺼준다.
    /// 화면 전체를 흑백으로 덮는 뷰가 켜진 채로 남으면 혼란스럽기 때문이다.
    /// </summary>
    public static class DofDebugState
    {
        /// <summary>디버그 뷰를 그릴지.</summary>
        public static bool Enabled;

        public static DofDebugView View = DofDebugView.FocusMask;

        /// <summary>이 값 이하의 CoC를 "포커스"로 간주한다. 마스크의 두께를 결정한다.</summary>
        public static float FocusThreshold = 0.15f;

        /// <summary>Overlay 모드에서 블러 영역을 얼마나 어둡게 깔지.</summary>
        public static float OverlayDarkness = 0.8f;

        public const float MinFocusThreshold = 0.01f;
        public const float MaxFocusThreshold = 1f;

        /// <summary>
        /// 디버그 패스가 마지막으로 그려진 프레임. 패스가 직접 기록한다.
        /// 피처를 렌더러 에셋에 추가하지 않으면 토글해도 아무 일이 없으므로,
        /// 창에서 "정말 그려지고 있는지"를 확인하는 데 쓴다.
        /// </summary>
        public static int LastRenderedFrame = -1;

        /// <summary>창을 닫거나 디버그를 끝낼 때 호출한다.</summary>
        public static void Disable()
        {
            Enabled = false;
            LastRenderedFrame = -1;
        }

        /// <summary>디버그 패스가 최근 프레임에 실제로 그려졌는지.</summary>
        public static bool IsRenderingLive()
        {
            if (!Enabled)
                return false;

            if (LastRenderedFrame < 0)
                return false;

            // 에디터는 카메라가 매 프레임 갱신되지 않을 수 있어 여유를 둔다.
            return Time.frameCount - LastRenderedFrame <= 4;
        }
    }

    /// <summary>
    /// URP DepthOfField 볼륨에서 뽑아낸, CoC 계산에 필요한 값들.
    /// </summary>
    public struct DofDebugParams
    {
        public DepthOfFieldMode mode;

        /// <summary>Bokeh 포커스 거리 (P).</summary>
        public float focusDistance;

        /// <summary>Bokeh 최대 착란원. URP와 같은 식으로 계산한다.</summary>
        public float maxCoC;

        public float gaussianStart;
        public float gaussianEnd;

        public bool IsBokeh()
        {
            return mode == DepthOfFieldMode.Bokeh;
        }

        /// <summary>
        /// maxCoC가 0 이하면 CoC 부호가 뒤집혀 블러가 비정상으로 나온다.
        /// focusDistance가 focalLength/1000 보다 작을 때 발생한다.
        /// </summary>
        public bool HasDegenerateBokehCoC()
        {
            if (!IsBokeh())
                return false;

            return maxCoC <= 0f;
        }

        public Vector4 ToShaderParams(float focusThreshold)
        {
            if (IsBokeh())
                return new Vector4(focusDistance, maxCoC, 1f, focusThreshold);

            return new Vector4(gaussianStart, gaussianEnd, 0f, focusThreshold);
        }
    }

    /// <summary>
    /// URP DOF와 동일한 CoC 수식. 셰이더와 에디터 창이 같은 값을 쓰도록 한 곳에 모았다.
    /// 디버그 뷰가 실제 블러와 어긋나면 쓸모가 없으므로 URP 구현을 그대로 따른다.
    /// </summary>
    public static class DofDebugMath
    {
        /// <summary>
        /// 현재 볼륨 스택에서 DOF 설정을 읽는다.
        /// </summary>
        /// <returns>DOF가 켜져 있고 디버그할 값이 있으면 true.</returns>
        public static bool TryGetParams(out DofDebugParams result)
        {
            result = default;

            VolumeStack stack = VolumeManager.instance.stack;

            if (stack == null)
                return false;

            DepthOfField depthOfField = stack.GetComponent<DepthOfField>();

            if (depthOfField == null)
                return false;

            if (depthOfField.mode.value == DepthOfFieldMode.Off)
                return false;

            result = BuildParams(depthOfField);

            return true;
        }

        /// <summary>
        /// URP PostProcessPass의 Bokeh 파라미터 계산을 그대로 재현한다.
        /// (F = 초점거리 m 환산, A = 조리개 지름, maxCoC = (A * F) / (P - F))
        /// </summary>
        public static DofDebugParams BuildParams(DepthOfField depthOfField)
        {
            DofDebugParams result = default;
            result.mode = depthOfField.mode.value;

            result.gaussianStart = depthOfField.gaussianStart.value;
            result.gaussianEnd = Mathf.Max(result.gaussianStart, depthOfField.gaussianEnd.value);

            float focalLength = depthOfField.focalLength.value;
            float aperture = depthOfField.aperture.value;

            float focalLengthInMeters = focalLength / 1000f;
            float apertureDiameter = focalLength / aperture;

            result.focusDistance = depthOfField.focusDistance.value;
            result.maxCoC = (apertureDiameter * focalLengthInMeters)
                            / (result.focusDistance - focalLengthInMeters);

            return result;
        }

        /// <summary>
        /// 주어진 임계값에서 포커스로 보이는 카메라 거리 구간.
        /// </summary>
        /// <param name="nearLimit">포커스가 시작되는 거리.</param>
        /// <param name="farLimit">포커스가 끝나는 거리. 무한하면 <see cref="float.PositiveInfinity"/>.</param>
        /// <returns>구간을 계산할 수 있으면 true.</returns>
        public static bool TryComputeFocusRange(DofDebugParams dofParams, float focusThreshold,
            out float nearLimit, out float farLimit)
        {
            nearLimit = 0f;
            farLimit = float.PositiveInfinity;

            if (!dofParams.IsBokeh())
            {
                // Gaussian은 시작 거리 앞이 전부 포커스고 원경만 흐려진다.
                float span = dofParams.gaussianEnd - dofParams.gaussianStart;
                farLimit = dofParams.gaussianStart + focusThreshold * span;

                return true;
            }

            if (dofParams.HasDegenerateBokehCoC())
                return false;

            // |1 - P/d| * maxCoC = threshold 를 d에 대해 풀면 아래가 된다.
            float ratio = focusThreshold / dofParams.maxCoC;

            nearLimit = dofParams.focusDistance / (1f + ratio);

            if (ratio >= 1f)
            {
                farLimit = float.PositiveInfinity;
                return true;
            }

            farLimit = dofParams.focusDistance / (1f - ratio);

            return true;
        }
    }

    /// <summary>DOF 디버그 셰이더 프로퍼티 ID 캐시.</summary>
    public static class DofDebugShaderIds
    {
        public static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
        public static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        public static readonly int DofDebugParams = Shader.PropertyToID("_DofDebugParams");
        public static readonly int DofDebugView = Shader.PropertyToID("_DofDebugView");
    }
}
