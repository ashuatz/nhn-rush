#ifndef TERRAINROAD_FORWARDPASS_INCLUDED
#define TERRAINROAD_FORWARDPASS_INCLUDED

// URP SimpleLit 포워드 패스를 그대로 재사용하고 프래그먼트만 교체한다.
// SimpleLitForwardPass와 같은 구조이며, 알베도에 길을 합성하는 한 단계가 더 있다.

#include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitForwardPass.hlsl"
#include "Assets/Shaders/LutHeightFog/LutHeightFogCore.hlsl"

// 길은 잔디와 다른 밀도로 반복시켜야 해서 자체 타일링을 갖는다.
// 이 유니폼이 URP의 UnityPerMaterial CBUFFER 밖에 있어 SRP Batcher가 꺼지는데,
// 지형은 메시 하나라 묶일 게 없어 문제되지 않는다 (TerrainRoad.shader 상단 주석 참고).
TEXTURE2D(_RoadMap);
SAMPLER(sampler_RoadMap);
float4 _RoadMap_ST;

TEXTURE2D(_RoadMask);
SAMPLER(sampler_RoadMask);

/// 마스크 R 채널 구간을 길 텍스처로 덮는다. 조명 전에 알베도만 바꾸므로
/// 그림자/포그/라이팅은 전부 원래 경로를 그대로 탄다.
void ApplyRoad(float2 uv, inout SurfaceData surfaceData)
{
    // input.uv에는 잔디 타일링(_BaseMap_ST)이 이미 곱해져 있다.
    // 마스크와 길은 그 타일링을 따라가면 안 되므로 메시 원본 UV로 되돌린 뒤 각자 다시 계산한다.
    float2 meshUV = UNDO_TRANSFORM_TEX(uv, _BaseMap);

    half mask = SAMPLE_TEXTURE2D(_RoadMask, sampler_RoadMask, meshUV).r;
    half3 road = SAMPLE_TEXTURE2D(_RoadMap, sampler_RoadMap, TRANSFORM_TEX(meshUV, _RoadMap)).rgb;

    // 길에도 _BaseColor를 곱해 지형과 같은 톤 조정을 받게 한다
    surfaceData.albedo = lerp(surfaceData.albedo, road * _BaseColor.rgb, mask);
}

void TerrainRoadFragment(
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

    ApplyRoad(input.uv, surfaceData);

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

    // URP 기본 포그 대신 2D LUT 포그를 적용한다 (SimpleLit과 동일).
    color.rgb = ApplyLutHeightFog(color.rgb, inputData.positionWS);

    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif // TERRAINROAD_FORWARDPASS_INCLUDED
