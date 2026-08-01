#ifndef SIMPLELIT_FORWARDPASS_INCLUDED
#define SIMPLELIT_FORWARDPASS_INCLUDED

// URP SimpleLit 포워드 패스를 그대로 재사용하고 프래그먼트만 교체한다.
// Varyings / Attributes / LitPassVertexSimple / InitializeInputData /
// InitializeSimpleLitSurfaceData 를 전부 URP 것을 쓰므로 중복 구현이 없다.
//
// 교체하는 부분은 URP 원본의 딱 한 줄이다:
//   color.rgb = MixFog(color.rgb, inputData.fogCoord);   ->   ApplyLutHeightFog(...)
//
// URP가 이 파일을 갱신해도 여기서 다시 구현한 코드가 없으므로 표류 위험이 작다.

#include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitForwardPass.hlsl"
#include "Assets/Shaders/LutHeightFog/LutHeightFogCore.hlsl"

void SimpleLitFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SurfaceData surfaceData;
    InitializeSimpleLitSurfaceData(input.uv, surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

    half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);

    // URP 기본 포그(unity_FogColor 선형/지수) 대신 2D LUT 포그를 적용한다.
    // 볼륨이 글로벌 프로퍼티를 채우지 않은 상태에서는 농도가 0이라 원래 색이 그대로 나온다.
    color.rgb = ApplyLutHeightFog(color.rgb, inputData.positionWS);

    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif // SIMPLELIT_FORWARDPASS_INCLUDED
