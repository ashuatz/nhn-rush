// 적 캐릭터용 플래너 섀도우. 그림자 맵을 쓰지 않고 캐릭터 메시를 한 번 더 그린다.
// 정점 단계에서 월드 좌표를 메인 라이트 방향으로 지면 평면에 눌러 붙이므로
// 스키닝된 캐릭터도 그대로 따라온다 (GPU 스키닝 결과가 POSITION으로 들어온다).
//
// 투영된 메시는 자기 자신과 심하게 겹치기 때문에 그냥 알파 블렌드하면 겹친 곳만
// 몇 배로 진해진다. 스텐실 상위 비트를 마킹해 픽셀당 한 번만 그리는 것으로 막는다.
// Ref/마스크로 쓰는 0x80은 URP가 유저용으로 비워 둔 상위 4비트 영역이다.
//
// PlanarShadowRendererFeature가 오버라이드 머티리얼로 쓴다. 직접 머티리얼을 만들 필요는 없다.
Shader "Hidden/Rush/PlanarShadow"
{
    Properties
    {
        _ShadowColor("Shadow Color", Color) = (0.0, 0.0, 0.02, 0.45)
        _PlaneHeight("Plane Height (world Y)", Float) = 0.0
        _PlaneBias("Plane Bias", Range(0.0, 0.2)) = 0.012
        _MaxStretch("Max Stretch", Range(0.5, 40.0)) = 8.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PlanarShadow"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            // 픽셀당 1회. 이미 마킹된 곳은 통과시키지 않아 겹침 누적을 막는다.
            Stencil
            {
                Ref 128
                ReadMask 128
                WriteMask 128
                Comp NotEqual
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            // 지면과 같은 높이에 눕는 폴리곤이므로 살짝 앞으로 당겨 z-파이팅을 피한다
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                float _PlaneHeight;
                float _PlaneBias;
                float _MaxStretch;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // 표면에서 광원을 향하는 방향. 태양이 지평선에 가까우면 그림자가 발산하므로
            // 그 경우는 기본 각도로 되돌린다.
            float3 ShadowLightDirection()
            {
                float3 lightDir = normalize(_MainLightPosition.xyz);

                if (lightDir.y < 0.15)
                    return normalize(float3(0.25, 1.0, 0.15));

                return lightDir;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                float planeY = _PlaneHeight + _PlaneBias;
                float3 lightDir = ShadowLightDirection();

                // 평면까지 광원 반대 방향으로 이동. 평면 아래 정점은 제자리에 둔다.
                float travel = (positionWS.y - planeY) / lightDir.y;
                travel = clamp(travel, 0.0, _MaxStretch);

                float3 shadowWS = positionWS - lightDir * travel;
                shadowWS.y = planeY;

                output.positionCS = TransformWorldToHClip(shadowWS);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                return _ShadowColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
