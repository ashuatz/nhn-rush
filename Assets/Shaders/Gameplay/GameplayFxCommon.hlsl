#ifndef RUSH_GAMEPLAY_FX_COMMON_INCLUDED
#define RUSH_GAMEPLAY_FX_COMMON_INCLUDED

// 게임플레이 연출 셰이더(사거리 표시 / 건설 실루엣 / 선택 링) 공용 헬퍼.
// 텍스처 리소스 없이 절차적으로 노이즈와 라인을 만들기 위한 최소 도구만 둔다.

#define RUSH_TAU 6.28318530718

// ---------------------------------------------------------------------------
// 노이즈
// ---------------------------------------------------------------------------

// 3D 좌표 -> 0~1 해시. 텍스처 없이 값 노이즈를 만들기 위한 씨앗.
float RushHash13(float3 p)
{
    p = frac(p * 0.1031);
    p += dot(p, p.yzx + 33.33);

    return frac((p.x + p.y) * p.z);
}

// 3D 값 노이즈. 격자 8점을 스무스스텝 보간한다.
float RushValueNoise(float3 p)
{
    float3 cell = floor(p);
    float3 f = frac(p);

    // 격자 경계에서 미분이 튀지 않도록 에르미트 보간을 쓴다
    f = f * f * (3.0 - 2.0 * f);

    float n000 = RushHash13(cell + float3(0.0, 0.0, 0.0));
    float n100 = RushHash13(cell + float3(1.0, 0.0, 0.0));
    float n010 = RushHash13(cell + float3(0.0, 1.0, 0.0));
    float n110 = RushHash13(cell + float3(1.0, 1.0, 0.0));
    float n001 = RushHash13(cell + float3(0.0, 0.0, 1.0));
    float n101 = RushHash13(cell + float3(1.0, 0.0, 1.0));
    float n011 = RushHash13(cell + float3(0.0, 1.0, 1.0));
    float n111 = RushHash13(cell + float3(1.0, 1.0, 1.0));

    float x00 = lerp(n000, n100, f.x);
    float x10 = lerp(n010, n110, f.x);
    float x01 = lerp(n001, n101, f.x);
    float x11 = lerp(n011, n111, f.x);

    float y0 = lerp(x00, x10, f.y);
    float y1 = lerp(x01, x11, f.y);

    return lerp(y0, y1, f.z);
}

// 3옥타브 fBm. 해커톤 규모에서 옥타브 수를 더 늘릴 이유가 없어 고정 전개한다.
float RushFbm(float3 p)
{
    float sum = RushValueNoise(p) * 0.5;
    sum += RushValueNoise(p * 2.03) * 0.3;
    sum += RushValueNoise(p * 4.11) * 0.2;

    return sum;
}

// ---------------------------------------------------------------------------
// 라인 / 밴드
// ---------------------------------------------------------------------------

// 아래 *W 계열은 소프트 폭을 인자로 받는다.
// 픽셀마다 갈리는 분기 뒤에서 fwidth를 호출하면 그래디언트가 정의되지 않으므로,
// 미분은 분기 밖에서 한 번만 구해 이 함수들에 넘긴다.

// value가 edge를 넘으면 1이 되는 소프트 스텝.
float RushAaStepW(float edge, float value, float width)
{
    float w = max(width, 1e-5);

    return smoothstep(edge - w, edge + w, value);
}

// value가 center에서 halfWidth 안쪽이면 1이 되는 밴드(선).
float RushAaBandW(float value, float center, float halfWidth, float width)
{
    float w = max(width, 1e-5);
    float distance = abs(value - center);

    return 1.0 - smoothstep(halfWidth, halfWidth + w, distance);
}

// spacing 간격으로 반복되는 눈금선. thickness는 간격 대비 비율(0~0.5).
// width는 value와 같은 단위(월드 등)로 준다.
float RushRepeatLineW(float value, float spacing, float thickness, float width)
{
    if (spacing <= 1e-4)
        return 0.0;

    float phase = frac(value / spacing);
    float folded = abs(phase - 0.5) * 2.0;

    // 반복 좌표 기준으로 폭을 환산해야 원경에서 선이 뭉치지 않는다
    float w = max(width / spacing * 2.0, 1e-5);

    return smoothstep(1.0 - thickness - w, 1.0 - thickness + w, folded);
}

// 화면 미분을 직접 구하는 편의 버전. 분기 밖(uv 기반 셰이더)에서만 쓴다.
float RushAaStep(float edge, float value)
{
    return RushAaStepW(edge, value, fwidth(value));
}

float RushAaBand(float value, float center, float halfWidth)
{
    return RushAaBandW(value, center, halfWidth, fwidth(value));
}

// ---------------------------------------------------------------------------
// 색 합성
// ---------------------------------------------------------------------------

// 프리멀티플라이드 알파(Blend One OneMinusSrcAlpha)로 누적한다.
// alphaWeight를 1보다 작게 주면 같은 밝기라도 더 애디티브하게 보인다.
void RushAccumulate(inout float3 color, inout float alpha, float3 tint, float mask, float alphaWeight)
{
    color += tint * mask;
    alpha += mask * alphaWeight;
}

#endif // RUSH_GAMEPLAY_FX_COMMON_INCLUDED
