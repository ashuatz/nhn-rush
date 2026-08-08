// 타워 사거리 표시. 구(Sphere) 메시를 프록시로 그리고, 픽셀마다 씬 뎁스에서
// 월드 위치를 복원해 "타워 중심으로부터의 3D 거리"를 계산한다.
// 그래서 표시 범위가 지형 굴곡/계단/타워 표면을 그대로 타고 흐른다.
//
// 게임 로직(Tower.EffectiveRange)도 Vector3 거리로 판정하므로 보이는 원과 실제 사거리가 일치한다.
//
// 요구 조건: URP Asset의 Depth Texture가 켜져 있어야 한다 (없으면 아무것도 그려지지 않는다).
// 반경은 트랜스폼 스케일에서 읽는다 - 유닛 스피어 지름이 1이므로 localScale = 반경 * 2.
Shader "Rush/FX/Range Sphere"
{
    Properties
    {
        [Header(Fill)]
        [HDR] _FillColor("Fill Color", Color) = (0.18, 0.62, 1.0, 0.16)
        _FillFalloff("Fill Falloff (중심 0 -> 가장자리 1)", Range(0.1, 6.0)) = 1.6
        _FillInner("Fill Inner Cut", Range(0.0, 0.95)) = 0.0

        [Header(Edge Ring)]
        [HDR] _EdgeColor("Edge Color", Color) = (0.45, 0.95, 1.0, 1.0)
        _EdgeWidth("Edge Width (world)", Range(0.01, 2.0)) = 0.18
        _EdgeIntensity("Edge Intensity", Range(0.0, 4.0)) = 1.4

        [Header(Grid)]
        _GridSpacing("Grid Spacing (world)", Range(0.0, 10.0)) = 1.0
        _GridThickness("Grid Thickness", Range(0.0, 0.5)) = 0.04
        _GridStrength("Grid Strength", Range(0.0, 1.0)) = 0.18

        [Header(Pulse)]
        _PulseSpeed("Pulse Speed", Range(0.0, 4.0)) = 0.7
        _PulseWidth("Pulse Width (world)", Range(0.01, 3.0)) = 0.5
        _PulseStrength("Pulse Strength", Range(0.0, 2.0)) = 0.45

        [Header(Dome Silhouette)]
        [HDR] _DomeColor("Dome Color", Color) = (0.35, 0.85, 1.0, 1.0)
        _DomeStrength("Dome Strength", Range(0.0, 2.0)) = 0.12
        _DomePower("Dome Rim Power", Range(1.0, 12.0)) = 4.0

        [Header(Shape)]
        _RadiusScale("Radius Scale", Range(0.1, 2.0)) = 1.0
        _HeightFade("Height Fade (0 = off)", Range(0.0, 20.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RangeSphere"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            // 프리멀티플라이드 알파: 필은 반투명, 링/펄스는 애디티브에 가깝게 섞인다
            Blend One OneMinusSrcAlpha
            ZWrite Off
            // 사거리 표시는 지형에 가려지면 안 된다. 카메라가 구 안에 들어가도 유지되도록 뒷면을 그린다
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/Shaders/Gameplay/GameplayFxCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FillColor;
                float _FillFalloff;
                float _FillInner;

                float4 _EdgeColor;
                float _EdgeWidth;
                float _EdgeIntensity;

                float _GridSpacing;
                float _GridThickness;
                float _GridStrength;

                float _PulseSpeed;
                float _PulseWidth;
                float _PulseStrength;

                float4 _DomeColor;
                float _DomeStrength;
                float _DomePower;

                float _RadiusScale;
                float _HeightFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            // 트랜스폼에서 구의 중심과 월드 반경을 읽는다 (유닛 스피어 반경 0.5 기준).
            void GetSphere(out float3 center, out float radius)
            {
                center = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);

                float3 axisX = float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20);

                radius = 0.5 * length(axisX) * _RadiusScale;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 center;
                float radius;
                GetSphere(center, radius);

                // 프록시 구가 논리 반경보다 작으면 그 바깥 픽셀은 아예 생성되지 않아 경계선이 잘린다.
                // _RadiusScale 확대분과 경계선 두께만큼 메시를 부풀려 커버 범위를 맞춘다
                float baseRadius = radius / max(_RadiusScale, 1e-4);
                float expand = _RadiusScale + (_EdgeWidth * 0.5 + 0.01) / max(baseRadius, 1e-4);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz * expand);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);

                return output;
            }

            // 뎁스 버퍼가 스카이(원경)를 가리키는지 판정한다.
            bool IsSkyDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth <= 1e-7;
                #else
                    return rawDepth >= 1.0 - 1e-7;
                #endif
            }

            // 뎁스 텍스처 값을 클립 공간 깊이로 맞춘다.
            // reversed-Z가 아닌 플랫폼(GLES 등)은 뎁스가 [0,1]이라 [near, 1] 범위로 다시 매핑해야 한다.
            float ToDeviceDepth(float rawDepth)
            {
                #if UNITY_REVERSED_Z
                    return rawDepth;
                #else
                    return lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 1e-5);

                float3 center;
                float radius;
                GetSphere(center, radius);

                float rawDepth = SampleSceneDepth(screenUV);
                float3 surfaceWS = ComputeWorldSpacePosition(screenUV, ToDeviceDepth(rawDepth), UNITY_MATRIX_I_VP);

                float3 delta = surfaceWS - center;
                float distanceToCenter = length(delta);

                // 그래디언트는 픽셀별 분기 밖에서 한 번만 구한다.
                // 하늘 픽셀은 복원 위치가 발산하므로 미분이 폭주하지 않도록 거리를 잘라서 넘긴다
                float clampedDistance = min(distanceToCenter, radius * 4.0);
                float aaWidth = fwidth(clampedDistance);

                // 하늘 / 사거리 밖은 바닥에 칠할 것이 없다. 분기 대신 마스크로 처리한다
                float sceneMask = IsSkyDepth(rawDepth) ? 0.0 : 1.0;
                sceneMask *= 1.0 - step(radius + _EdgeWidth, distanceToCenter);

                // 세로로 지나치게 먼 지오메트리(높은 벽 위 등)는 흐리게 뺀다
                float heightMask = 1.0;

                if (_HeightFade > 0.0)
                    heightMask = saturate(1.0 - abs(delta.y) / _HeightFade);

                float surfaceMask = sceneMask * heightMask;
                float insideMask = 1.0 - RushAaStepW(radius, distanceToCenter, aaWidth);
                float normalized = saturate(distanceToCenter / max(radius, 1e-4));

                float3 color = 0.0;
                float alpha = 0.0;

                // 아래 마스크는 전부 "프리멀티플라이드 알파의 알파" 값이다.
                // 색상 알파를 마스크에 미리 섞어야 RGB와 출력 알파가 어긋나지 않는다

                // 필: 중심에서 가장자리로 갈수록 진해진다
                float fill = pow(normalized, _FillFalloff);

                if (_FillInner > 0.0)
                    fill *= smoothstep(_FillInner, _FillInner + 0.05, normalized);

                fill *= insideMask * surfaceMask * _FillColor.a;

                RushAccumulate(color, alpha, _FillColor.rgb, fill, 1.0);

                // 경계 링: 실제 사거리 경계
                float edge = RushAaBandW(distanceToCenter, radius, _EdgeWidth * 0.5, aaWidth);
                edge *= _EdgeIntensity * surfaceMask * _EdgeColor.a;

                RushAccumulate(color, alpha, _EdgeColor.rgb, edge, 0.6);

                // 동심원 눈금: 거리감을 준다
                if (_GridStrength > 0.0)
                {
                    float grid = RushRepeatLineW(distanceToCenter, _GridSpacing, _GridThickness, aaWidth);
                    grid *= _GridStrength * insideMask * surfaceMask * _EdgeColor.a;

                    RushAccumulate(color, alpha, _EdgeColor.rgb, grid, 0.5);
                }

                // 펄스: 중심에서 바깥으로 퍼져나가는 파문
                if (_PulseStrength > 0.0)
                {
                    float phase = frac(_Time.y * _PulseSpeed);
                    float pulseRadius = phase * radius;
                    float pulse = 1.0 - smoothstep(0.0, _PulseWidth, abs(distanceToCenter - pulseRadius));

                    // 퍼질수록 옅어지게 해서 링이 경계와 겹쳐 튀지 않게 한다
                    pulse *= (1.0 - phase) * _PulseStrength * surfaceMask * _EdgeColor.a;

                    RushAccumulate(color, alpha, _EdgeColor.rgb, pulse, 0.4);
                }

                // 구 표면 실루엣(돔). 바닥 표시와 별개로 항상 은은하게 깔린다
                if (_DomeStrength > 0.0)
                {
                    float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                    float facing = saturate(1.0 - abs(dot(normalize(input.normalWS), viewDir)));
                    float dome = pow(facing, _DomePower) * _DomeStrength * _DomeColor.a;

                    RushAccumulate(color, alpha, _DomeColor.rgb, dome, 0.5);
                }

                return float4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
