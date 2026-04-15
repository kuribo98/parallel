Shader "Skybox/DistantCloudVolume"
{
    Properties
    {
        _VoidColor ("Void Color", Color) = (0.0, 0.0, 0.0, 1.0)
        _HorizonColor ("Horizon Color", Color) = (0.02, 0.03, 0.08, 1.0)
        _BlueCloudColor ("Blue Cloud Color", Color) = (0.16, 0.42, 0.95, 1.0)
        _PurpleCloudColor ("Purple Cloud Color", Color) = (0.56, 0.26, 0.94, 1.0)
        _HighlightColor ("Highlight Color", Color) = (0.82, 0.9, 1.0, 1.0)
        _Exposure ("Exposure", Range(0.0, 4.0)) = 1.4
        _CloudScale ("Cloud Scale", Range(0.2, 6.0)) = 1.6
        _CloudDepth ("Cloud Depth", Range(0.2, 6.0)) = 2.5
        _DensityThreshold ("Density Threshold", Range(0.0, 1.0)) = 0.54
        _DensityStrength ("Density Strength", Range(0.1, 3.0)) = 1.2
        _Softness ("Softness", Range(0.01, 0.4)) = 0.12
        _WarpScale ("Warp Scale", Range(0.1, 5.0)) = 1.4
        _WarpStrength ("Warp Strength", Range(0.0, 2.0)) = 0.65
        _PrimarySpeed ("Primary Speed", Range(0.0, 0.3)) = 0.022
        _SecondarySpeed ("Secondary Speed", Range(0.0, 0.3)) = 0.036
        _HorizonGlow ("Horizon Glow", Range(0.0, 1.0)) = 0.08
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

            #define CLOUD_STEPS 4

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
            float4 _HorizonColor;
            float4 _BlueCloudColor;
            float4 _PurpleCloudColor;
            float4 _HighlightColor;
            float _Exposure;
            float _CloudScale;
            float _CloudDepth;
            float _DensityThreshold;
            float _DensityStrength;
            float _Softness;
            float _WarpScale;
            float _WarpStrength;
            float _PrimarySpeed;
            float _SecondarySpeed;
            float _HorizonGlow;

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
                for (int i = 0; i < 4; i++)
                {
                    sum += Noise(p) * amplitude;
                    p = p * 2.02 + float3(11.7, 7.3, 5.9);
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

                float horizon = pow(1.0 - abs(dir.y), 3.4);
                float3 color = lerp(_VoidColor.rgb, _HorizonColor.rgb, horizon * _HorizonGlow);

                float driftAngle = time * (_PrimarySpeed + _SecondarySpeed) * 0.2;
                float2 driftPlane = Rotate2D(dir.xz, driftAngle);
                float stepSize = _CloudDepth / CLOUD_STEPS;
                float transmittance = 1.0;

                [loop]
                for (int step = 0; step < CLOUD_STEPS; step++)
                {
                    float t = (step + 0.5) * stepSize + 0.6;
                    float3 samplePos = float3(driftPlane.x, dir.y * 0.85 + 0.18, driftPlane.y) * (t * _CloudScale);
                    samplePos += float3(time * _PrimarySpeed, 0.0, -time * _SecondarySpeed);

                    float3 warpPos = samplePos * _WarpScale;
                    float2 warp = float2(
                        Noise(warpPos + float3(3.2, 0.8, 1.7)),
                        Noise(warpPos + float3(8.4, 2.1, 5.6))
                    ) - 0.5;

                    float3 shapedPos = samplePos + float3(warp.x, warp.y * 0.65, -warp.x) * _WarpStrength;
                    float baseNoise = Fbm(shapedPos);
                    float detailNoise = Noise(shapedPos * 2.0 + float3(6.2, 1.4, 3.8));
                    float purpleNoise = Noise(shapedPos * 1.35 + float3(4.8, -2.6, 7.1));

                    float shelf = saturate(1.0 - abs(dir.y * 0.9 + shapedPos.y * 0.08));
                    float densityField = baseNoise * 0.78 + detailNoise * 0.22;
                    float density = smoothstep(_DensityThreshold - _Softness, _DensityThreshold + _Softness, densityField);
                    density *= shelf;

                    float blueMask = density * saturate(1.0 - smoothstep(0.42, 0.78, purpleNoise));
                    float purpleMask = density * smoothstep(0.38, 0.72, purpleNoise);
                    purpleMask *= smoothstep(0.3, 0.85, detailNoise + baseNoise * 0.35);

                    float layerDensity = saturate(max(blueMask, purpleMask) * _DensityStrength * stepSize);
                    if (layerDensity <= 0.001)
                    {
                        continue;
                    }

                    float lighting = saturate(baseNoise * 0.55 + detailNoise * 0.35 + 0.2);
                    float3 blueCloud = lerp(_BlueCloudColor.rgb * 0.35, _BlueCloudColor.rgb, lighting);
                    float3 purpleCloud = lerp(_PurpleCloudColor.rgb * 0.35, _PurpleCloudColor.rgb, lighting);
                    float3 layerColor = blueCloud * blueMask + purpleCloud * purpleMask;

                    float highlight = saturate((detailNoise * 0.6 + baseNoise * 0.4) * max(blueMask, purpleMask));
                    layerColor += _HighlightColor.rgb * highlight * 0.22;

                    color += layerColor * layerDensity * transmittance;
                    transmittance *= saturate(1.0 - layerDensity * 1.1);

                    if (transmittance < 0.03)
                    {
                        break;
                    }
                }

                color *= _Exposure;
                return float4(color, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
