Shader "TransparentMirror/Mirror"
{
    Properties
    {
        _ReflectionTexLeft("Reflection Left", 2D) = "black" {}
        _ReflectionTexRight("Reflection Right", 2D) = "black" {}
        _Tint("Tint", Color) = (0.5, 0.5, 0.9, 1)
        _ReflectionFactor("Reflection Factor", Range(0, 1)) = 0.87
        _AlphaCutoff("Alpha Cutoff", Range(0, 0.25)) = 0.02
        _TransparentMode("Transparent Mode", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "TransparentMirror"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_ReflectionTexLeft);
            SAMPLER(sampler_ReflectionTexLeft);
            TEXTURE2D(_ReflectionTexRight);
            SAMPLER(sampler_ReflectionTexRight);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _ReflectionFactor;
                float _AlphaCutoff;
                float _TransparentMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.screenPos = vertexInput.positionNDC;
                return output;
            }

            half4 SampleReflection(float2 uv)
            {
                #if defined(UNITY_SINGLE_PASS_STEREO)
                    if (unity_StereoEyeIndex == 0)
                    {
                        return SAMPLE_TEXTURE2D(_ReflectionTexLeft, sampler_ReflectionTexLeft, uv);
                    }

                    return SAMPLE_TEXTURE2D(_ReflectionTexRight, sampler_ReflectionTexRight, uv);
                #else
                    return SAMPLE_TEXTURE2D(_ReflectionTexLeft, sampler_ReflectionTexLeft, uv);
                #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                screenUV.x = 1.0 - screenUV.x;
                screenUV = UnityStereoTransformScreenSpaceTex(screenUV);
                half4 reflection = SampleReflection(screenUV);
                half4 tinted = reflection * _Tint;
                half4 color = lerp(tinted, reflection, _ReflectionFactor);

                if (_TransparentMode > 0.5)
                {
                    half contentAlpha = reflection.a;
                    if (contentAlpha < _AlphaCutoff)
                    {
                        discard;
                    }

                    color.a = saturate(contentAlpha);
                }
                else
                {
                    color.a = 1;
                }

                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
