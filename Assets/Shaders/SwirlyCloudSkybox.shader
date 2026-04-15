Shader "Skybox/SwirlyClouds"
{
    Properties
    {
        _VoidColor ("Void Color", Color) = (0.0, 0.0, 0.0, 1.0)
        _TopColor ("Top Color", Color) = (0.09, 0.19, 0.48, 1.0)
        _MidColor ("Mid Color", Color) = (0.30, 0.18, 0.56, 1.0)
        _BottomColor ("Bottom Color", Color) = (0.03, 0.05, 0.14, 1.0)
        _HighlightColor ("Highlight Color", Color) = (0.68, 0.76, 1.0, 1.0)
        _SwirlTint ("Swirl Tint", Color) = (0.52, 0.34, 0.88, 1.0)
        _Exposure ("Exposure", Range(0.0, 4.0)) = 1.15
        _CloudScale ("Cloud Scale", Range(0.2, 6.0)) = 2.4
        _DetailScale ("Detail Scale", Range(0.5, 5.0)) = 2.3
        _WarpScale ("Warp Scale", Range(0.1, 4.0)) = 1.35
        _PrimarySpeed ("Primary Speed", Range(0.0, 0.3)) = 0.035
        _SecondarySpeed ("Secondary Speed", Range(0.0, 0.3)) = 0.055
        _SwirlSpeed ("Swirl Speed", Range(0.0, 0.3)) = 0.05
        _SwirlStrength ("Swirl Strength", Range(0.0, 2.0)) = 0.85
        _HorizonFog ("Horizon Fog", Range(0.0, 1.0)) = 0.18
        _Contrast ("Contrast", Range(0.4, 3.0)) = 1.3
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 viewDir : TEXCOORD0;
            };

            float4 _VoidColor;
            float4 _TopColor;
            float4 _MidColor;
            float4 _BottomColor;
            float4 _HighlightColor;
            float4 _SwirlTint;
            float _Exposure;
            float _CloudScale;
            float _DetailScale;
            float _WarpScale;
            float _PrimarySpeed;
            float _SecondarySpeed;
            float _SwirlSpeed;
            float _SwirlStrength;
            float _HorizonFog;
            float _Contrast;

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 p)
            {
                float3 cell = floor(p);
                float3 local = frac(p);
                float3 smooth = local * local * (3.0 - 2.0 * local);

                float n000 = Hash(cell + float3(0.0, 0.0, 0.0));
                float n100 = Hash(cell + float3(1.0, 0.0, 0.0));
                float n010 = Hash(cell + float3(0.0, 1.0, 0.0));
                float n110 = Hash(cell + float3(1.0, 1.0, 0.0));
                float n001 = Hash(cell + float3(0.0, 0.0, 1.0));
                float n101 = Hash(cell + float3(1.0, 0.0, 1.0));
                float n011 = Hash(cell + float3(0.0, 1.0, 1.0));
                float n111 = Hash(cell + float3(1.0, 1.0, 1.0));

                float nx00 = lerp(n000, n100, smooth.x);
                float nx10 = lerp(n010, n110, smooth.x);
                float nx01 = lerp(n001, n101, smooth.x);
                float nx11 = lerp(n011, n111, smooth.x);
                float nxy0 = lerp(nx00, nx10, smooth.y);
                float nxy1 = lerp(nx01, nx11, smooth.y);
                return lerp(nxy0, nxy1, smooth.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0.0;
                float amplitude = 0.5;

                [unroll]
                for (int i = 0; i < 5; i++)
                {
                    sum += Noise(p) * amplitude;
                    p = p * 2.03 + float3(13.1, 7.7, 11.3);
                    amplitude *= 0.5;
                }

                return sum;
            }

            float2 Rotate2D(float2 p, float angle)
            {
                float s = sin(angle);
                float c = cos(angle);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.viewDir = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 dir = normalize(i.viewDir);
                float time = _Time.y;

                float2 swirlPlane = dir.xz;
                float swirlRadius = length(swirlPlane);
                float swirlMask = smoothstep(1.25, 0.0, swirlRadius);
                float swirlAngle = (time * _SwirlSpeed + swirlRadius * 3.0 + dir.y * 1.5) * _SwirlStrength;
                swirlPlane = Rotate2D(swirlPlane, swirlAngle * (0.35 + swirlMask));

                float3 samplePos = float3(swirlPlane.x, dir.y, swirlPlane.y) * _CloudScale;
                float3 warpPos = samplePos * _WarpScale + float3(time * 0.04, -time * 0.025, time * 0.03);
                float3 warp = float3(
                    Fbm(warpPos + float3(8.3, 1.2, 5.7)),
                    Fbm(warpPos + float3(2.4, 9.1, 3.8)),
                    Fbm(warpPos + float3(6.9, 4.4, 7.2))
                ) - 0.5;

                float3 baseField = samplePos + warp * 1.45;
                float primary = Fbm(baseField + float3(time * _PrimarySpeed, -time * _PrimarySpeed * 0.35, time * _PrimarySpeed * 0.2));
                float secondary = Fbm(baseField * _DetailScale + float3(-time * _SecondarySpeed, time * _SecondarySpeed * 0.2, time * _SecondarySpeed * 0.45));
                float wisps = Fbm(baseField * (_DetailScale * 1.9) + float3(time * 0.06, time * 0.035, -time * 0.045));
                float nebula = Fbm(baseField * 0.75 - float3(time * 0.015, time * 0.01, time * 0.02));

                float cloudField = saturate(primary * 0.72 + secondary * 0.2 + wisps * 0.18);
                cloudField = smoothstep(0.2, 0.88, pow(cloudField, _Contrast));

                float horizonMist = pow(1.0 - abs(dir.y), 2.2) * _HorizonFog;
                float purplePush = saturate(secondary * 1.3 + nebula * 0.35 - 0.3);
                float highlight = saturate(primary * 0.85 + wisps * 0.3);

                float vertical = saturate(dir.y * 0.5 + 0.5);
                float3 baseSky = lerp(_BottomColor.rgb, _TopColor.rgb, pow(vertical, 0.75));
                float backgroundMix = saturate(horizonMist * 1.8 + nebula * 0.08 + 0.06);
                float3 background = lerp(_VoidColor.rgb, baseSky, backgroundMix);
                float3 cloudColor = lerp(_MidColor.rgb, _HighlightColor.rgb, highlight);
                cloudColor = lerp(cloudColor, _SwirlTint.rgb, purplePush * 0.45);

                float cloudAlpha = saturate(cloudField * 1.1 + horizonMist);
                float glow = saturate(nebula * 0.65 + swirlMask * 0.15) * 0.2;

                float3 color = lerp(background, cloudColor, cloudAlpha);
                color += _HighlightColor.rgb * glow;
                color += _MidColor.rgb * horizonMist * 0.18;
                color *= _Exposure;

                return float4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
