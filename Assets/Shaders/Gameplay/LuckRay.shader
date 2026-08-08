// 행운 발동 연출(보상 C15). 가산 합성으로 빛나는 황금 기둥과 모여드는 불티를 그린다.
//
// 텍스처 없이 두 가지만 쓴다:
// - 실루엣 중심일수록 진하게 (노멀과 시선의 내적) -> 원통이 빛기둥처럼, 구체가 둥근 불티처럼 보인다
// - 위로 갈수록 옅어지게 (오브젝트 공간 Y) -> 기둥이 하늘로 흩어지는 느낌. 불티는 _HeightFade를 0으로 꺼 둔다
Shader "Rush/FX/Luck Ray"
{
    Properties
    {
        [Header(Color)]
        [HDR] _CoreColor("Core Color", Color) = (1.0, 0.92, 0.55, 1.0)
        [HDR] _EdgeColor("Edge Color", Color) = (1.0, 0.65, 0.15, 1.0)
        _Intensity("Intensity", Range(0.0, 4.0)) = 1.4

        [Header(Shape)]
        _CorePower("Core Power", Range(0.5, 8.0)) = 2.2
        _HeightFade("Height Fade (0 = off)", Range(0.0, 1.0)) = 1.0
        _HeightBase("Height Fade Base", Range(-2.0, 2.0)) = -1.0
        _HeightRange("Height Fade Range", Range(0.1, 4.0)) = 2.0
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
            Name "LuckRay"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            // 가산 합성: 겹칠수록 밝아져서 발광처럼 보인다. 정렬에 신경 쓸 필요가 없다
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _EdgeColor;
                float _Intensity;

                float _CorePower;
                float _HeightFade;
                float _HeightBase;
                float _HeightRange;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color : TEXCOORD2;
                float heightT : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;

                // 파티클 메시는 오브젝트 공간이 곧 메시 로컬이라 Y를 그대로 높이로 쓸 수 있다
                output.heightT = saturate((input.positionOS.y - _HeightBase) / _HeightRange);

                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));

                // 카메라를 정면으로 마주보는 부분이 중심. 가장자리로 갈수록 얇아져 빛기둥처럼 보인다
                float core = pow(saturate(dot(normalWS, viewDir)), _CorePower);

                float3 color = lerp(_EdgeColor.rgb, _CoreColor.rgb, core);
                float alpha = core;

                if (_HeightFade > 0.0)
                    alpha *= lerp(1.0, 1.0 - input.heightT, _HeightFade);

                alpha *= input.color.a;
                color *= input.color.rgb * _Intensity;

                // 가산이라 알파 채널은 쓰이지 않는다. 밝기 자체로 농도를 표현한다
                return float4(color * alpha, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
