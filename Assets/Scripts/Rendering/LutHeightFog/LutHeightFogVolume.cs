using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Common.Rendering
{
    /// <summary>
    /// 2D LUT 기반 거리/높이 포그의 아티스트 파라미터.
    /// 실제 합성은 <see cref="LutHeightFogRendererFeature"/>가 스크린스페이스 패스로 처리한다.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("Common/LUT Height Fog")]
    public sealed class LutHeightFogVolume : VolumeComponent, IPostProcessComponent
    {
        // 셰이더가 TEXTURE2D로 선언하므로 큐브맵/3D/배열을 받으면 바인딩이 깨진다.
        // Tex2D로 못박아 인스펙터에서 애초에 걸러낸다.
        [Tooltip("2D 포그 LUT. U = 카메라 거리, V = 월드 높이, RGB = 포그 색, A = 포그 농도. "
                 + "Common/LUT Height Fog Baker 윈도우로 생성한다.")]
        public TextureParameter fogLut = new TextureParameter(null, TextureDimension.Tex2D);

        [Tooltip("LUT의 U축이 대응하는 카메라 시야 거리 구간.")]
        public FogRangeParameter distanceRange = new FogRangeParameter(new FogRange(0f, 120f));

        [Tooltip("LUT의 V축이 대응하는 월드 Y 높이 구간.")]
        public FogRangeParameter heightRange = new FogRangeParameter(new FogRange(0f, 20f));

        [Tooltip("LUT 알파에 곱해지는 전체 농도 배율.")]
        public ClampedFloatParameter density = new ClampedFloatParameter(1f, 0f, 2f);

        [Tooltip("하늘에 적용되는 농도 배율. 0이면 스카이박스를 건드리지 않는다.")]
        public ClampedFloatParameter skyDensity = new ClampedFloatParameter(1f, 0f, 1f);

        public bool IsActive()
        {
            if (!active)
                return false;

            if (fogLut.value == null)
                return false;

            if (density.value <= 0f)
                return false;

            return true;
        }

        /// <summary>
        /// 셰이더에 넘길 (거리 시작, 거리 역구간, 높이 시작, 높이 역구간).
        /// </summary>
        public Vector4 GetRangeParams()
        {
            FogRange distance = distanceRange.value;
            FogRange height = heightRange.value;

            return new Vector4(
                distance.start,
                distance.InverseSpan(),
                height.start,
                height.InverseSpan());
        }

        /// <summary>
        /// 셰이더에 넘길 (농도 배율, 하늘 농도 배율, LUT 텍셀 폭, LUT 텍셀 높이).
        ///
        /// 텍셀 크기는 셰이더가 LUT 좌표를 텍셀 중심에 맞추는 데 쓴다.
        /// LUT을 (size-1)로 굽기 때문에 이 보정이 없으면 전 구간에 반텍셀 오차가 남는다.
        /// </summary>
        public Vector4 GetBlendParams()
        {
            float texelWidth = 0f;
            float texelHeight = 0f;

            Texture lut = fogLut.value;

            if (lut != null && lut.width > 0 && lut.height > 0)
            {
                texelWidth = 1f / lut.width;
                texelHeight = 1f / lut.height;
            }

            return new Vector4(density.value, skyDensity.value, texelWidth, texelHeight);
        }
    }
}
