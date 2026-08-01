using UnityEngine;

namespace Common.Rendering.EditorTools
{
    /// <summary>
    /// 포그 LUT을 만든 그라디언트 한 쌍. 베이크한 텍스처의 importer userData에 JSON으로 실려
    /// 나중에 다시 편집할 수 있게 한다.
    ///
    /// Gradient는 일반 JsonUtility로 직렬화되지 않으므로 ScriptableObject에 담아
    /// EditorJsonUtility를 통해 다룬다.
    /// </summary>
    sealed class FogLutGradientData : ScriptableObject
    {
        /// <summary>LUT의 U축(카메라 거리)에 대응하는 그라디언트.</summary>
        public Gradient distanceGradient = new Gradient();

        /// <summary>LUT의 V축(월드 높이)에 대응하는 그라디언트.</summary>
        public Gradient heightGradient = new Gradient();

        /// <summary>
        /// 가까울수록 투명하고 멀수록 흐려지는 무채색 기본값.
        /// 높이 축은 전부 흰색/불투명이라 곱해도 거리 그라디언트를 그대로 통과시킨다.
        /// </summary>
        public void ResetToDefault()
        {
            distanceGradient = new Gradient
            {
                mode = GradientMode.Blend,
                colorKeys = new[]
                {
                    new GradientColorKey(new Color(0.62f, 0.68f, 0.75f), 0f),
                    new GradientColorKey(new Color(0.78f, 0.83f, 0.88f), 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 1f),
                },
            };

            heightGradient = new Gradient
            {
                mode = GradientMode.Blend,
                colorKeys = new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f),
                },
                alphaKeys = new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                },
            };
        }
    }
}
