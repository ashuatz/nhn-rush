// 타워 슬롯 선택 표시. 바닥에 눕힌 Quad(로컬 XY 평면, UV 0~1)에 그린다.
// 안티에일리어싱된 링 + 회전하는 코너 브래킷 + 안쪽 소프트 글로우로 구성한다.
//
// 스케일이 곧 지름이다 - Quad 기본 크기가 1x1 이므로 localScale = 지름.
Shader "Rush/FX/Selection Ring"
{
    Properties
    {
        [Header(Ring)]
        [HDR] _RingColor("Ring Color", Color) = (1.0, 0.82, 0.32, 1.0)
        _RingRadius("Ring Radius", Range(0.1, 1.0)) = 0.78
        _RingThickness("Ring Thickness", Range(0.001, 0.3)) = 0.045

        [Header(Brackets)]
        [HDR] _BracketColor("Bracket Color", Color) = (1.0, 0.95, 0.6, 1.0)
        _BracketRadius("Bracket Radius", Range(0.1, 1.0)) = 0.93
        _BracketThickness("Bracket Thickness", Range(0.001, 0.3)) = 0.06
        _BracketArc("Bracket Arc (0~1)", Range(0.0, 0.5)) = 0.16
        _BracketCount("Bracket Count", Range(1.0, 12.0)) = 4.0
        _SpinSpeed("Spin Speed (rev/s)", Range(-2.0, 2.0)) = 0.18

        [Header(Fill)]
        [HDR] _FillColor("Fill Color", Color) = (1.0, 0.78, 0.25, 0.12)
        _FillFalloff("Fill Falloff", Range(0.1, 6.0)) = 2.2

        [Header(Pulse)]
        _PulseSpeed("Pulse Speed", Range(0.0, 6.0)) = 2.0
        _PulseAmount("Pulse Amount", Range(0.0, 1.0)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            // 사거리 표시(Transparent+50)보다 뒤에 그려야 링이 범위 필에 덮이지 않는다
            "Queue" = "Transparent+60"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SelectionRing"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            // 바닥면과 같은 높이에 놓여도 z-파이팅이 나지 않게 살짝 앞으로 당긴다
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Gameplay/GameplayFxCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _RingColor;
                float _RingRadius;
                float _RingThickness;

                float4 _BracketColor;
                float _BracketRadius;
                float _BracketThickness;
                float _BracketArc;
                float _BracketCount;
                float _SpinSpeed;

                float4 _FillColor;
                float _FillFalloff;

                float _PulseSpeed;
                float _PulseAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;

                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // UV 중심 기준 -1~1 좌표. 반지름 1이 Quad 가장자리다
                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);
                float radiusWidth = fwidth(radius);

                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseAmount;

                float3 color = 0.0;
                float alpha = 0.0;

                // 안쪽 글로우: 중심이 옅고 링에 가까울수록 진하다
                float fill = pow(saturate(radius / max(_RingRadius, 1e-4)), _FillFalloff);
                fill *= 1.0 - RushAaStepW(_RingRadius, radius, radiusWidth);
                fill *= _FillColor.a * pulse;

                RushAccumulate(color, alpha, _FillColor.rgb, fill, 1.0);

                // 본 링
                float ring = RushAaBandW(radius, _RingRadius, _RingThickness * 0.5, radiusWidth);
                ring *= _RingColor.a * pulse;

                RushAccumulate(color, alpha, _RingColor.rgb, ring, 0.7);

                // 코너 브래킷: 링 바깥을 도는 짧은 호 몇 개
                float angle = atan2(p.y, p.x);
                float spin = _Time.y * _SpinSpeed * RUSH_TAU;
                float segment = frac((angle + spin) / RUSH_TAU * _BracketCount);

                // 세그먼트 중앙에서 얼마나 떨어졌는지 (0 = 중앙, 1 = 경계)
                float segmentDistance = abs(segment - 0.5) * 2.0;

                // 각도 방향 경계는 화면 미분이 -pi/pi 이음매에서 튀므로 고정 폭으로 부드럽게 만든다
                float arcMask = 1.0 - smoothstep(_BracketArc, _BracketArc + 0.06, segmentDistance);
                float bracketBand = RushAaBandW(radius, _BracketRadius, _BracketThickness * 0.5, radiusWidth);
                float bracket = arcMask * bracketBand * _BracketColor.a;

                RushAccumulate(color, alpha, _BracketColor.rgb, bracket, 0.7);

                return float4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
