// 캐주얼 연기 퍼프. 구체 메시 파티클을 여러 개 겹쳐 뭉게뭉게한 덩어리를 만든다.
// 텍스처를 쓰지 않고 노멀 방향으로만 명암을 줘서 어느 각도에서 봐도 형태가 읽힌다.
//
// 소프트 파티클(지면과 만나는 경계를 부드럽게)은 씬 뎁스를 쓴다.
// URP Asset의 Depth Texture가 꺼져 있으면 _SoftFade를 0으로 두면 된다.
Shader "Rush/FX/Smoke Puff"
{
    Properties
    {
        [Header(Color)]
        [HDR] _TopColor("Top Color", Color) = (0.95, 0.95, 0.98, 1.0)
        [HDR] _BottomColor("Bottom Color", Color) = (0.45, 0.47, 0.55, 1.0)
        _Opacity("Opacity", Range(0.0, 1.0)) = 0.85

        [Header(Shape)]
        _RimStrength("Rim Strength", Range(0.0, 1.0)) = 0.35
        _RimPower("Rim Power", Range(0.5, 8.0)) = 2.0
        _SoftFade("Soft Particle Fade (0 = off)", Range(0.0, 3.0)) = 0.5
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
            Name "SmokePuff"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor;
                float4 _BottomColor;
                float _Opacity;

                float _RimStrength;
                float _RimPower;
                float _SoftFade;
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
                float4 screenPos : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.screenPos = ComputeScreenPos(output.positionCS);

                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);

                // 위를 향한 면일수록 밝게. 텍스처 없이도 덩어리가 둥글게 보인다
                float upness = saturate(normalWS.y * 0.5 + 0.5);
                float3 color = lerp(_BottomColor.rgb, _TopColor.rgb, upness);

                // 가장자리를 살짝 밝혀 서로 겹친 구체들의 윤곽이 뭉치지 않게 한다
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float rim = pow(saturate(1.0 - abs(dot(normalWS, viewDir))), _RimPower);
                color += _TopColor.rgb * rim * _RimStrength;

                color *= input.color.rgb;

                float alpha = _Opacity * input.color.a;

                // 소프트 파티클: 지면을 뚫고 들어간 부분을 부드럽게 지운다
                if (_SoftFade > 0.0)
                {
                    float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 1e-5);
                    float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                    float particleDepth = input.screenPos.w;

                    alpha *= saturate((sceneDepth - particleDepth) / _SoftFade);
                }

                return float4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
