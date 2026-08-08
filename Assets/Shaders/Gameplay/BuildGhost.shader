// 건설 예정 건물 실루엣(고스트). 림 라이트로 형태를 잡고 절차적 노이즈로 내부를 흔든다.
// 텍스처 리소스가 필요 없어 어떤 타워 메시에 그대로 씌워도 동작한다.
//
// 사용법
//  - 배치 가능/불가 전환은 _InvalidBlend 하나만 0 <-> 1 로 바꾼다 (색 두 벌을 코드에서 들고 있을 필요 없음).
//  - 아래에서 위로 차오르는 건설 연출은 _BuildProgress 를 0 -> 1 로 올린다.
//  - _GhostHeight 는 트랜스폼 원점 기준 모델 높이. 와이프 구간을 정하는 데만 쓴다.
Shader "Rush/FX/Build Ghost"
{
    Properties
    {
        [Header(Body)]
        [HDR] _BaseColor("Base Color", Color) = (0.20, 0.65, 1.0, 0.18)
        [HDR] _RimColor("Rim Color", Color) = (0.55, 0.95, 1.0, 1.0)
        _RimPower("Rim Power", Range(0.5, 12.0)) = 3.0
        _RimStrength("Rim Strength", Range(0.0, 4.0)) = 1.5

        [Header(Noise)]
        _NoiseScale("Noise Scale", Range(0.1, 12.0)) = 2.2
        _NoiseSpeed("Noise Speed", Range(0.0, 4.0)) = 0.6
        _NoiseStrength("Noise Strength", Range(0.0, 1.0)) = 0.65
        _NoiseFloor("Noise Floor", Range(0.0, 1.0)) = 0.25

        [Header(Scanline)]
        _ScanSpacing("Scan Spacing (world)", Range(0.0, 4.0)) = 0.35
        _ScanThickness("Scan Thickness", Range(0.0, 0.5)) = 0.12
        _ScanSpeed("Scan Speed", Range(-4.0, 4.0)) = 0.8
        _ScanStrength("Scan Strength", Range(0.0, 2.0)) = 0.4

        [Header(Build Wipe)]
        _BuildProgress("Build Progress", Range(0.0, 1.0)) = 1.0
        _GhostHeight("Ghost Height (world)", Range(0.1, 20.0)) = 2.0
        _WipeEdge("Wipe Edge Width", Range(0.001, 0.5)) = 0.06
        [HDR] _WipeColor("Wipe Line Color", Color) = (0.8, 1.0, 1.0, 1.0)

        [Header(State)]
        [HDR] _InvalidColor("Invalid Color", Color) = (1.0, 0.25, 0.2, 1.0)
        _InvalidBlend("Invalid Blend", Range(0.0, 1.0)) = 0.0

        [Header(Render)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "BuildGhost"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shaders/Gameplay/GameplayFxCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;

                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseStrength;
                float _NoiseFloor;

                float _ScanSpacing;
                float _ScanThickness;
                float _ScanSpeed;
                float _ScanStrength;

                float _BuildProgress;
                float _GhostHeight;
                float _WipeEdge;
                float4 _WipeColor;

                float4 _InvalidColor;
                float _InvalidBlend;

                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float heightNorm : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float originY = UNITY_MATRIX_M._m13;

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.heightNorm = saturate((positionWS.y - originY) / max(_GhostHeight, 1e-3));

                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));

                // 양면 렌더에서도 림이 뒤집히지 않도록 절대값을 쓴다
                float facing = saturate(1.0 - abs(dot(normalWS, viewDir)));
                float rim = pow(facing, _RimPower) * _RimStrength;

                // 노이즈는 위로 흐르게 해서 형체가 잡히는 중인 느낌을 준다
                float3 noiseCoord = input.positionWS * _NoiseScale;
                noiseCoord.y -= _Time.y * _NoiseSpeed;

                float noise = RushFbm(noiseCoord);
                float noiseMask = lerp(1.0, lerp(_NoiseFloor, 1.0, noise), _NoiseStrength);

                // 스캔라인: 월드 높이를 따라 흐르는 가로줄
                float scanCoord = input.positionWS.y - _Time.y * _ScanSpeed;
                float scan = RushRepeatLineW(scanCoord, _ScanSpacing, _ScanThickness, fwidth(scanCoord));

                // 건설 와이프: 경계 아래쪽만 그린다.
                // 소프트 폭만큼 여유를 두고 매핑해야 진행도 1에서 모델 상단이 반투명해지지 않는다
                float wipeThreshold = lerp(-_WipeEdge, 1.0 + _WipeEdge, _BuildProgress);
                float wipe = 1.0 - RushAaStepW(wipeThreshold, input.heightNorm, _WipeEdge);
                float wipeLine = RushAaBandW(input.heightNorm, wipeThreshold, _WipeEdge, _WipeEdge);

                // 완전히 숨겼거나 다 지었으면 경계선은 그리지 않는다
                wipeLine *= step(0.001, _BuildProgress) * step(_BuildProgress, 0.999);

                float3 bodyTint = lerp(_BaseColor.rgb, _InvalidColor.rgb, _InvalidBlend);
                float3 rimTint = lerp(_RimColor.rgb, _InvalidColor.rgb, _InvalidBlend);

                float3 color = 0.0;
                float alpha = 0.0;

                float body = _BaseColor.a * noiseMask * wipe;
                RushAccumulate(color, alpha, bodyTint, body, 1.0);

                float rimMask = rim * _RimColor.a * noiseMask * wipe;
                RushAccumulate(color, alpha, rimTint, rimMask, 0.6);

                float scanMask = scan * _ScanStrength * wipe;
                RushAccumulate(color, alpha, rimTint, scanMask, 0.4);

                float lineMask = wipeLine * _WipeColor.a;
                RushAccumulate(color, alpha, lerp(_WipeColor.rgb, _InvalidColor.rgb, _InvalidBlend), lineMask, 0.5);

                return float4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
