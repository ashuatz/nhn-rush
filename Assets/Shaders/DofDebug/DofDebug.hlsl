#ifndef DOFDEBUG_INCLUDED
#define DOFDEBUG_INCLUDED

// DOF 포커스 디버그 뷰.
// URP DepthOfField 볼륨의 실제 값으로 CoC(착란원)를 계산해 흑백으로 시각화한다.
// CoC 수식은 URP의 BokehDepthOfField.shader / GaussianDepthOfField.shader와 동일하다.
// 디버그 뷰가 실제 블러와 어긋나면 의미가 없으므로 절대 다르게 계산하지 않는다.

// x: Bokeh 포커스 거리(P) / Gaussian 블러 시작 거리
// y: Bokeh maxCoC / Gaussian 블러 최대 거리
// z: Bokeh 모드면 1, Gaussian이면 0
// w: 포커스로 간주할 CoC 임계값
float4 _DofDebugParams;

// x: 뷰 모드 (0 = FocusMask, 1 = Overlay, 2 = BlurAmount)
// y: Overlay 모드에서 블러 영역을 얼마나 어둡게 깔지 (0~1)
float4 _DofDebugView;

#define DOF_FOCUS_DISTANCE      _DofDebugParams.x
#define DOF_MAX_COC             _DofDebugParams.y
#define DOF_GAUSSIAN_START      _DofDebugParams.x
#define DOF_GAUSSIAN_END        _DofDebugParams.y
#define DOF_IS_BOKEH            (_DofDebugParams.z > 0.5)
#define DOF_FOCUS_THRESHOLD     _DofDebugParams.w

#define DOF_VIEW_MODE           _DofDebugView.x
#define DOF_OVERLAY_DARKNESS    _DofDebugView.y

#define DOF_VIEW_FOCUS_MASK     0
#define DOF_VIEW_OVERLAY        1
#define DOF_VIEW_BLUR_AMOUNT    2

// 0 = 완벽한 포커스, 1 = 최대 블러.
// URP는 CoC를 [-1, 1]로 클램프해 쓰므로 절댓값을 saturate 하면 블러 강도가 된다.
float ComputeDofBlurAmount(float eyeDepth)
{
    if (DOF_IS_BOKEH)
    {
        float coc = (1.0 - DOF_FOCUS_DISTANCE / eyeDepth) * DOF_MAX_COC;
        return saturate(abs(coc));
    }

    // Gaussian은 원경만 흐리게 한다. 시작 거리 앞은 전부 포커스다.
    float span = DOF_GAUSSIAN_END - DOF_GAUSSIAN_START;
    float gaussianCoc = (eyeDepth - DOF_GAUSSIAN_START) / max(span, 1e-5);

    return saturate(gaussianCoc);
}

// 임계값 안쪽을 흰색, 밖을 검정으로. 경계만 살짝 부드럽게 해 에일리어싱을 줄인다.
float ComputeFocusMask(float blurAmount)
{
    float threshold = max(DOF_FOCUS_THRESHOLD, 1e-5);

    return 1.0 - smoothstep(threshold * 0.8, threshold, blurAmount);
}

half4 FragDofDebug(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.texcoord;
    half3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

    float rawDepth = SampleSceneDepth(uv);

    // URP DOF와 같은 깊이 변환을 쓴다.
    float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
    float blurAmount = ComputeDofBlurAmount(eyeDepth);

    if (DOF_VIEW_MODE < DOF_VIEW_OVERLAY)
    {
        half mask = ComputeFocusMask(blurAmount);

        return half4(mask, mask, mask, 1.0);
    }

    if (DOF_VIEW_MODE < DOF_VIEW_BLUR_AMOUNT)
    {
        // 무엇이 포커스에 걸렸는지 형체로 확인하는 모드.
        // 블러 영역은 어둡게 깔고 포커스 영역은 원래 밝기를 유지한다.
        half mask = ComputeFocusMask(blurAmount);
        half3 dimmed = sceneColor * (1.0 - DOF_OVERLAY_DARKNESS);

        return half4(lerp(dimmed, sceneColor, mask), 1.0);
    }

    // 블러 강도 그대로. 흰색이 최대 블러.
    return half4(blurAmount, blurAmount, blurAmount, 1.0);
}

#endif // DOFDEBUG_INCLUDED
