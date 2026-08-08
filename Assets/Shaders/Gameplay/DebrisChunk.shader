// 사망 파편(카오스 프랙처 약식)용 파티클 셰이더.
// 큐브 메시 파티클 하나하나가 캐릭터 알베도의 서로 다른 지점을 뽑아 단색으로 칠해진다.
// 그래서 텍스처를 쪼갠 것처럼 보이면서도 메시를 실제로 분할할 필요가 없다.
//
// 요구 조건: 파티클 렌더러의 Custom Vertex Streams 에 Center(TEXCOORD1)가 있어야 한다.
// 한 조각의 모든 정점은 같은 Center를 받으므로 조각마다 색이 하나로 고정된다.
// 셋업은 RushSetupActions가 처리한다.
//
// 텍스처를 비워 두면 흰색으로 샘플되어 파티클 색(= 몬스터 머티리얼 색)만 남는다.
Shader "Rush/FX/Debris Chunk"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo (비워두면 색만 사용)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)

        [Header(Sampling)]
        _UvSpread("UV Spread", Range(0.0, 1.0)) = 0.85
        _UvCenter("UV Center", Vector) = (0.5, 0.5, 0, 0)
        _ColorJitter("Color Jitter", Range(0.0, 1.0)) = 0.25

        [Header(Shading)]
        _ShadeStrength("Shade Strength", Range(0.0, 1.0)) = 0.45
        _AmbientBoost("Ambient Boost", Range(0.0, 2.0)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "DebrisChunk"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shaders/Gameplay/GameplayFxCommon.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;

                float _UvSpread;
                float4 _UvCenter;
                float _ColorJitter;

                float _ShadeStrength;
                float _AmbientBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                // 파티클 중심 (Custom Vertex Stream: Center)
                float3 center : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 color : TEXCOORD1;
                float2 sampleUV : TEXCOORD2;
                float jitter : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;

                // 조각 중심을 씨앗으로 알베도의 한 지점을 고른다.
                // 정점마다가 아니라 조각마다 정해지므로 한 조각은 단색이 된다
                float h1 = RushHash13(input.center * 13.7);
                float h2 = RushHash13(input.center * 7.3 + 4.2);

                float2 pick = (float2(h1, h2) - 0.5) * _UvSpread + _UvCenter.xy;
                output.sampleUV = saturate(pick) * _BaseMap_ST.xy + _BaseMap_ST.zw;

                output.jitter = RushHash13(input.center * 3.1 + 11.0);

                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.sampleUV);
                float3 color = albedo.rgb * _BaseColor.rgb * input.color.rgb;

                // 조각마다 밝기를 조금씩 흔들어 단조로움을 없앤다
                float jitter = lerp(1.0, 0.6 + input.jitter * 0.8, _ColorJitter);
                color *= jitter;

                // 하프 램버트: 캐주얼하게 그림자 쪽도 완전히 죽지 않는다
                float3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                float ndl = dot(normalWS, mainLight.direction) * 0.5 + 0.5;
                float shade = lerp(1.0 - _ShadeStrength, 1.0, ndl);

                float3 ambient = SampleSH(normalWS) * _AmbientBoost;
                color = color * (mainLight.color * shade + ambient);

                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
