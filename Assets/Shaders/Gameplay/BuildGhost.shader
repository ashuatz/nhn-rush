// 건설 예정 건물 실루엣(고스트). 스텐실로 픽셀당 한 번만 칠해 깔끔한 단색 실루엣을 만든다.
//
// 반투명 메시를 그냥 겹쳐 그리면 내부 면과 윤곽이 계속 누적돼 지저분해진다.
// 여기서는 마스크 패스가 스텐실에 형태를 찍고, 채움 패스가 그 픽셀을 딱 한 번만 칠한 뒤
// 스텐실을 0으로 되돌린다. 그래서 겹침이 아무리 많아도 결과는 평평한 실루엣 하나다.
//
// 사용법
//  - 배치 가능/불가 전환은 _InvalidBlend 하나만 0 <-> 1 로 바꾼다.
//  - 아래에서 위로 차오르는 건설 연출은 _BuildProgress 를 0 -> 1 로 올린다.
//  - _GhostHeight 는 트랜스폼 원점 기준 모델 높이. 와이프 구간을 정하는 데만 쓴다.
//
// 주의: 실루엣이 화면에서 서로 겹칠 정도로 여러 개 동시에 보이면 스텐실이 섞일 수 있다.
// 건설 프리뷰는 한 번에 하나만 켜므로 문제되지 않는다.
Shader "Rush/FX/Build Ghost"
{
    Properties
    {
        [Header(Silhouette)]
        [HDR] _Color("Color", Color) = (0.30, 0.72, 1.0, 0.75)
        _TopBoost("Top Brightness", Range(0.0, 1.0)) = 0.25
        _BottomFade("Bottom Fade", Range(0.0, 1.0)) = 0.15

        [Header(State)]
        [HDR] _InvalidColor("Invalid Color", Color) = (1.0, 0.30, 0.25, 1.0)
        _InvalidBlend("Invalid Blend", Range(0.0, 1.0)) = 0.0

        [Header(Build Wipe)]
        _BuildProgress("Build Progress", Range(0.0, 1.0)) = 1.0
        _GhostHeight("Ghost Height (world)", Range(0.1, 20.0)) = 2.0
        _WipeEdge("Wipe Edge Width", Range(0.001, 0.5)) = 0.05
        [HDR] _WipeColor("Wipe Line Color", Color) = (0.85, 1.0, 1.0, 1.0)

        [Header(Render)]
        // 지형이나 다른 건물에 가려지지 않게 기본은 Always. 가려지길 원하면 LEqual(4).
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 8
        [IntRange] _StencilRef("Stencil Ref", Range(1, 255)) = 42
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+30"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _TopBoost;
            float _BottomFade;

            float4 _InvalidColor;
            float _InvalidBlend;

            float _BuildProgress;
            float _GhostHeight;
            float _WipeEdge;
            float4 _WipeColor;

            float _ZTest;
            float _StencilRef;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float heightNorm : TEXCOORD0;
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;

            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            float originY = UNITY_MATRIX_M._m13;

            output.positionCS = TransformWorldToHClip(positionWS);
            output.heightNorm = saturate((positionWS.y - originY) / max(_GhostHeight, 1e-3));

            return output;
        }

        // 건설 와이프 경계. 소프트 폭만큼 여유를 둬야 진행도 0/1에서 딱 떨어진다.
        float GetWipeThreshold()
        {
            return lerp(-_WipeEdge, 1.0 + _WipeEdge, _BuildProgress);
        }

        // 와이프 위쪽은 아직 안 지어진 부분이므로 잘라낸다.
        // 마스크 패스와 채움 패스가 같은 기준으로 잘라야 실루엣이 어긋나지 않는다.
        void ClipByWipe(float heightNorm)
        {
            clip(GetWipeThreshold() - heightNorm);
        }
        ENDHLSL

        // 1) 마스크: 색은 쓰지 않고 실루엣 모양만 스텐실에 찍는다
        Pass
        {
            Name "GhostMask"
            Tags
            {
                "LightMode" = "SRPDefaultUnlit"
            }

            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            ColorMask 0

            Stencil
            {
                Ref [_StencilRef]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragMask

            float4 FragMask(Varyings input) : SV_Target
            {
                ClipByWipe(input.heightNorm);

                return 0;
            }
            ENDHLSL
        }

        // 2) 채움: 스텐실이 찍힌 픽셀만 한 번 칠하고 스텐실을 되돌린다
        Pass
        {
            Name "GhostFill"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref [_StencilRef]
                Comp Equal
                Pass Zero
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragFill

            float4 FragFill(Varyings input) : SV_Target
            {
                ClipByWipe(input.heightNorm);

                float3 tint = lerp(_Color.rgb, _InvalidColor.rgb, _InvalidBlend);

                // 평평한 단색은 심심하므로 위아래로만 아주 옅게 밝기를 준다
                float gradient = 1.0 + _TopBoost * input.heightNorm - _BottomFade * (1.0 - input.heightNorm);
                tint *= gradient;

                float alpha = _Color.a;

                // 건설 중이면 차오르는 경계에 밝은 선을 얹는다
                float threshold = GetWipeThreshold();
                float edge = 1.0 - smoothstep(0.0, _WipeEdge, abs(input.heightNorm - threshold));
                edge *= step(0.001, _BuildProgress) * step(_BuildProgress, 0.999);

                tint = lerp(tint, lerp(_WipeColor.rgb, _InvalidColor.rgb, _InvalidBlend), edge * _WipeColor.a);
                alpha = lerp(alpha, 1.0, edge * _WipeColor.a);

                return float4(tint, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
