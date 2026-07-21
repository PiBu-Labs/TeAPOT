Shader "Custom/WireframeGrid"
{
    Properties
    {
        _FillColor   ("Fill Color", Color) = (0.9, 0.9, 0.9, 1)
        _LineColor   ("Line Color", Color) = (0, 0, 0, 1)
        _LineWidthPx ("Line Width (px)", Range(0.5, 4)) = 1.25
        _GridSize    ("Grid Cells (x, y)", Vector) = (100, 100, 0, 0) // quads across mesh
        _PlaneSize   ("Plane Size (x, z in object units)", Vector) = (1, 1, 0, 0)
        [Toggle] _DrawDiag    ("Draw Diagonal", Float) = 0

        _HeightMin   ("Min Height", Float) = 0
        _HeightMax   ("Max Height", Float) = 1
        _HeightWMin  ("Min Weight to Render", Float) = 0.1

        _LightDir    ("Light Direction (world)", Vector) = (0.5, 1, 0.5, 0)
        _AmbientMin  ("Ambient Min", Range(0, 1)) = 0.5
        _Smoothness  ("Smoothness", Range(0, 1)) = 0.0   // optional spec
        _Metallic    ("Metallic", Range(0, 1)) = 0.0
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Geometry"
            "RenderType"="Opaque"
        }

        Pass
        {
            Tags { "LightMode"="UniversalForward" }

            Blend Off
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            // URP includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _FillColor;
            float4 _LineColor;
            float  _LineWidthPx;
            float4 _GridSize;   // number of cells horizontally/vertically
            float4 _PlaneSize;
            float  _LOD;
            float  _DrawDiag;   // 0 or 1
            float  _HeightMin;
            float  _HeightMax;
            float  _HeightWMin;
            float4 _LightDir;
            float  _AmbientMin;
            float  _Smoothness;
            float  _Metallic;
            CBUFFER_END

            sampler2D _HeightMu;
            float4    _HeightMu_TexelSize;

            // Weight / confidence map — zero means cell not yet observed
            sampler2D _HeightW;
            float4    _HeightW_TexelSize;

            sampler2D _DbgOut;
            float4 _DbgOut_TexelSize;

            struct Attributes {
                float3 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };
            struct Varyings {
                float4 positionHCS : SV_Position;
                float3 normalWS    : TEXCOORD0;
                float2 uv          : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            inline float3 SampleDbgOut(float2 uv)
            {
                return tex2Dlod(_DbgOut, float4(uv, 0, _LOD)).rgb;
            }

            // Height sampling with optional explicit LOD
            inline float SampleHeight(float2 uv)
            {
                // return SampleKnown(uv) > 0.75 ? tex2Dlod(_OutHeight, float4(uv, 0, 0)).r : 0;
                // return tex2Dlod(_OutHeight, float4(uv, 0, _LOD)).r;

                return tex2Dlod(_HeightMu, float4(uv, 0, _LOD)).r;
            }

            // Compute height gradient ∇h in plane (u,v) coordinates
            // Returns ∂h/∂u and ∂h/∂v in meters per meter of plane distance
            inline void HeightGrad(float2 uv, out float dh_du, out float dh_dv)
            {
                // Sample at neighboring texels (at the current LOD)
                float lodScale = exp2(_LOD);
                float du = _HeightMu_TexelSize.x * lodScale;  // texture step in U
                float dv = _HeightMu_TexelSize.y * lodScale;  // texture step in V

                float2 uvL = float2(max(0.0, uv.x - du), uv.y);
                float2 uvR = float2(min(1.0, uv.x + du), uv.y);
                float2 uvD = float2(uv.x, max(0.0, uv.y - dv));
                float2 uvU = float2(uv.x, min(1.0, uv.y + dv));

                float hL = SampleHeight(uvL);
                float hR = SampleHeight(uvR);
                float hD = SampleHeight(uvD);
                float hU = SampleHeight(uvU);

                // Actual span between samples, accounting for border clamping
                float du_plane = (uvR.x - uvL.x) * _PlaneSize.x;
                float dv_plane = (uvU.y - uvD.y) * _PlaneSize.y;

                dh_du = (hR - hL) / max(1e-6, du_plane);  // ∂h/∂u (meters/meter)
                dh_dv = (hU - hD) / max(1e-6, dv_plane);  // ∂h/∂v (meters/meter)
            }

            Varyings vert (Attributes IN)
            {
                float w = tex2Dlod(_HeightW, float4(IN.uv, 0, 0)).r;

                // Sample height at this UV; stay flat at y=0 if not yet observed
                float h = w >= _HeightWMin ? SampleHeight(IN.uv) : 0.0;

                // The mesh is in object space where X maps to plane U, Z maps to plane V
                // IN.positionOS.xz already contains the plane (u,v) coordinates
                // Update Y with the height
                float3 p = IN.positionOS;
                p.y = h;

                // Compute height gradient and derive surface normal
                float dhdu, dhdv;
                HeightGrad(IN.uv, dhdu, dhdv);
                float3 dPos_du = float3(1.0, dhdu, 0.0);
                float3 dPos_dv = float3(0.0, dhdv, 1.0);
                float3 n = normalize(cross(dPos_dv, dPos_du));

                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(p);
                OUT.normalWS = normalize(TransformObjectToWorldNormal(n));
                OUT.uv = IN.uv;
                OUT.positionWS  = TransformObjectToWorld(p);
                return OUT;
            }

            float edgeMaskFromUV(float2 uv, float2 grid, float widthPx, float drawDiag)
            {
                float2 uvGrid = uv * grid;    // cell space
                float2 g = frac(uvGrid);
                float2 a = min(g, 1.0 - g);   // distance to nearest V/H edge

                // pixel-consistent width
                float2 duv = fwidth(uvGrid);
                float wx = max(1e-4, duv.x * widthPx); // width for vertical lines
                float wy = max(1e-4, duv.y * widthPx); // width for horizontal lines

                float mask = max(
                    1.0 - smoothstep(0.0, wx, a.x),
                    1.0 - smoothstep(0.0, wy, a.y)
                );

                // optional triangle diagonal (same orientation everywhere)
                if (drawDiag > 0.5)
                {
                    float wDiag = max(1e-4, max(duv.x, duv.y) * widthPx);
                    float dDiag = abs(g.x - g.y) * 0.70710678; // /sqrt(2)
                    float md = 1.0 - smoothstep(0.0, wDiag, dDiag);
                    mask = max(mask, md);
                }

                float dToBorder = min(
                    min(uv.x, 1.0 - uv.x),
                    min(uv.y, 1.0 - uv.y)
                    );   // 0 at outer border, 0.5 at center

                float tCenter = saturate(dToBorder * 2.0);    // 0 at border, 1 at center

                // 1 at border, _InnerLineStrength at center
                // float borderWeight = lerp(0.0, 1.0, tCenter);

                mask *= tCenter;

                return mask; // 0 interior … 1 on edges
            }

            float3 heightmapColor(float t)
            {
                // t in [0,1]
                t = saturate(t);

                // 0.00–0.20: deep water (dark blue to teal)
                // 0.20–0.40: coast (teal to green)
                // 0.40–0.60: lowland (green to yellow)
                // 0.60–0.80: highland (yellow to brown)
                // 0.80–1.00: mountain (brown to white)

                float3 c0 = float3(0.00, 0.05, 0.35); // deep water
                float3 c1 = float3(0.00, 0.40, 0.70); // shallow water / coast
                float3 c2 = float3(0.05, 0.50, 0.15); // grass green
                float3 c3 = float3(0.70, 0.65, 0.25); // dry / yellow
                float3 c4 = float3(0.45, 0.30, 0.15); // rock / brown
                float3 c5 = float3(0.95, 0.95, 0.95); // snow

                if (t < 0.2)
                {
                    float u = t / 0.2;
                    return lerp(c0, c1, u);
                }
                if (t < 0.4)
                {
                    float u = (t - 0.2) / 0.2;
                    return lerp(c1, c2, u);
                }
                if (t < 0.6)
                {
                    float u = (t - 0.4) / 0.2;
                    return lerp(c2, c3, u);
                }
                if (t < 0.8)
                {
                    float u = (t - 0.6) / 0.2;
                    return lerp(c3, c4, u);
                }
                {
                    float u = (t - 0.8) / 0.2;
                    return lerp(c4, c5, u);
                }
            }

            float4 frag (Varyings IN) : SV_Target
            {
                float mask = edgeMaskFromUV(IN.uv, _GridSize.xy, _LineWidthPx, _DrawDiag);

                // Height color — vertex already displaced by _HeightMu, so positionWS.y == height
                float h = IN.positionWS.y;
                float range = max(1e-4, _HeightMax - _HeightMin);
                float ht = saturate((h - _HeightMin) / range);
                float3 hColor = heightmapColor(ht);

                // Normal-based diffuse shading
                float3 N = normalize(IN.normalWS);
                float3 L = normalize(_LightDir.xyz);
                float NoL = saturate(dot(N, L));
                float lighting = lerp(_AmbientMin, 1.0, NoL);

                // Confidence fade-in: cells blend from a "scanning" teal to full height color
                float w = tex2D(_HeightW, IN.uv).r;
                float confidence = smoothstep(_HeightWMin, _HeightWMin * 8.0, w);
                float3 scanColor = float3(0.15, 0.55, 0.75);
                float3 fillRgb = lerp(scanColor, hColor * lighting, confidence);

                // Wireframe overlay
                float3 rgb = lerp(fillRgb, _LineColor.rgb, mask);
                return float4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
