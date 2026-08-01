using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Common.Rendering
{
    /// <summary>
    /// 포그가 보간되는 선형 구간. 거리 축과 높이 축에 같은 형태로 쓰인다.
    /// </summary>
    [Serializable]
    public struct FogRange
    {
        /// <summary>LUT 좌표 0에 대응하는 값.</summary>
        public float start;

        /// <summary>LUT 좌표 1에 대응하는 값.</summary>
        public float end;

        /// <summary>구간이 0으로 붕괴했을 때 나눗셈을 막는 최소 폭.</summary>
        const float MinSpan = 1e-4f;

        public FogRange(float start, float end)
        {
            this.start = start;
            this.end = end;
        }

        /// <summary>
        /// 셰이더에서 역보간에 쓰는 1/(end-start).
        /// start &gt; end 로 뒤집어 놓는 것도 유효한 연출이므로 부호는 유지한다.
        /// </summary>
        public float InverseSpan()
        {
            float span = end - start;

            if (span > -MinSpan && span < MinSpan)
            {
                // 구간이 0에 가까우면 경계에서 바로 전환되도록 기울기를 최대로 준다.
                if (span < 0f)
                    return -1f / MinSpan;

                return 1f / MinSpan;
            }

            return 1f / span;
        }
    }

    /// <summary>
    /// <see cref="FogRange"/>를 볼륨에서 보간 가능한 파라미터로 노출한다.
    /// </summary>
    [Serializable]
    public sealed class FogRangeParameter : VolumeParameter<FogRange>
    {
        /// <summary>커스텀 드로어가 SerializedProperty 타입을 검사할 때 쓰는 이름.</summary>
        public const string TypeName = nameof(FogRange);

        public FogRangeParameter(FogRange value, bool overrideState = false)
            : base(value, overrideState)
        {
        }

        public override void Interp(FogRange from, FogRange to, float t)
        {
            value = new FogRange(
                Mathf.Lerp(from.start, to.start, t),
                Mathf.Lerp(from.end, to.end, t));
        }
    }

    /// <summary>
    /// 포그 셰이더 프로퍼티 ID 캐시. 문자열 해싱을 프레임마다 반복하지 않기 위함.
    /// </summary>
    public static class LutHeightFogShaderIds
    {
        public static readonly int BlitTexture = Shader.PropertyToID("_BlitTexture");
        public static readonly int BlitScaleBias = Shader.PropertyToID("_BlitScaleBias");
        public static readonly int FogLut = Shader.PropertyToID("_FogLut");
        public static readonly int FogRangeParams = Shader.PropertyToID("_FogRangeParams");
        public static readonly int FogBlendParams = Shader.PropertyToID("_FogBlendParams");
    }
}
