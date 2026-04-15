Shader "Portals/DisabledPortal"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.01, 0.015, 0.025, 1)
        _GlowColor ("Glow Color", Color) = (0.05, 0.25, 0.35, 1)
        _GlowStrength ("Glow Strength", Range(0, 2)) = 0.45
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
            Name "DisabledPortal"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _GlowColor;
                half _GlowStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centeredUv = input.uv - 0.5;
                float distanceFromCenter = length(centeredUv);
                half rim = 1.0h - smoothstep(0.16, 0.48, distanceFromCenter);
                half scanline = sin((input.uv.y + input.uv.x * 0.35) * 80.0) * 0.5 + 0.5;
                half glow = saturate(rim * _GlowStrength + scanline * 0.08);
                half3 color = _BaseColor.rgb + _GlowColor.rgb * glow;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
