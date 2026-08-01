#ifndef LUTHEIGHTFOG_INCLUDED
#define LUTHEIGHTFOG_INCLUDED

// 스크린스페이스 경로. 깊이 버퍼에서 월드 좌표를 복원해 포그를 합성한다.
// 스톡 URP 셰이더를 그대로 쓰는 오브젝트에 포그를 넣기 위한 경로다.
// 수식 자체는 LutHeightFogCore.hlsl이 소유한다.

#include "Assets/Shaders/LutHeightFog/LutHeightFogCore.hlsl"

// 하늘(스카이박스)은 깊이가 원거리 평면에 붙어 있어 월드 좌표 복원이 무의미하다.
bool IsSkyDepth(float rawDepth)
{
#if UNITY_REVERSED_Z
    return rawDepth <= 0.0;
#else
    return rawDepth >= 1.0;
#endif
}

half4 FragLutHeightFog(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 uv = input.texcoord;
    half3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

    float rawDepth = SampleSceneDepth(uv);

    // 하늘은 LUT의 최원거리/최상단 지점 색만 별도 배율로 섞는다.
    // 스카이박스 셰이더를 건드리지 않고 포그와 하늘을 이어 붙이기 위한 경로.
    if (IsSkyDepth(rawDepth))
    {
        half4 skyFog = SampleFogLut(1.0, 1.0);
        half skyDensity = saturate(skyFog.a * FOG_DENSITY_SCALE * FOG_SKY_DENSITY_SCALE);

        return half4(lerp(sceneColor, skyFog.rgb, skyDensity), 1.0);
    }

    float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

    return half4(ApplyLutHeightFog(sceneColor, positionWS), 1.0);
}

#endif // LUTHEIGHTFOG_INCLUDED
