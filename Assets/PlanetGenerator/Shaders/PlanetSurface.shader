Shader "MajorStudio/Planet"
{
    Properties
    {
        [Header(Geometry Runtime)]
        _Radius("Radius (Object Space)", Float) = 1
        _BaseStrength("Base Height Strength", Range(0, 0.45)) = 0.09
        _MountainStrength("Mountain Height Strength", Range(0, 0.45)) = 0.18
        _CraterStrength("Crater Height Strength", Range(0, 0.45)) = 0.12

        [Header(Colors)]
        _LowColor("Lowland Color", Color) = (0.13, 0.17, 0.20, 1)
        _MidColor("Midland Color", Color) = (0.48, 0.38, 0.28, 1)
        _HighColor("Highland Color", Color) = (0.83, 0.74, 0.59, 1)
        _RockColor("Steep Rock Color", Color) = (0.23, 0.25, 0.26, 1)
        _HeightLow("Low to Mid Center", Range(-0.25, 0.25)) = 0.00
        _HeightHigh("Mid to High Center", Range(-0.25, 0.40)) = 0.075
        _ColorBandWidth("Height Transition Width", Range(0.001, 0.15)) = 0.050
        _ColorWarpStrength("Biome Color Warp", Range(0, 0.12)) = 0.045
        _SlopeStart("Rock Slope Start", Range(0, 0.8)) = 0.04
        _SlopeEnd("Rock Slope End", Range(0.01, 1)) = 0.28
        _RockCoverage("Rock Color Coverage", Range(0, 1)) = 0.80

        [Header(Triplanar Normal)]
        [Normal] _RockNormal("Rock Normal (Normal Map)", 2D) = "bump" {}
        _NormalTiling("Normal Tiling", Range(1, 80)) = 16
        _NormalBlendSharpness("Projection Sharpness", Range(1, 16)) = 4
        _NormalStrength("Rock Normal Strength", Range(0, 3)) = 0.85
        _FlatNormalStrength("Flat Normal Strength", Range(0, 1)) = 0.12

        [Header(Lighting)]
        _AmbientColor("Ambient Color", Color) = (0.24, 0.29, 0.39, 1)
        _AmbientStrength("Ambient Strength", Range(0, 2)) = 0.65
        _LightStrength("Directional Light Strength", Range(0, 3)) = 1.15
        _Wrap("Diffuse Wrap", Range(0, 1)) = 0.12

        // 【新增：Graphic Shading】整个区块只改变 Fragment 的最终颜色。
        // 0 = 完全保持原来的写实/风格化光照，1 = 完全使用海报式 3 档光照。
        // 原有 Geometry、Height/Slope Colors、Triplanar Normal 参数保持不变。
        [Header(Graphic Poster Shading)]
        _GraphicBlend("Graphic Mode Blend 0 Original 1 Graphic", Range(0, 1)) = 1
        _GraphicShadowColor("Graphic Shadow Ink", Color) = (0.035, 0.012, 0.006, 1)
        _GraphicMidColor("Graphic Mid Orange", Color) = (0.82, 0.27, 0.040, 1)
        _GraphicHighColor("Graphic Highlight Gold", Color) = (1.00, 0.75, 0.31, 1)
        _GraphicShadowThreshold("Graphic Shadow To Mid", Range(0, 1)) = 0.25
        _GraphicHighlightThreshold("Graphic Mid To Highlight", Range(0, 1)) = 0.68
        _GraphicBandSoftness("Graphic Transition Softness", Range(0.001, 0.2)) = 0.024
        _GraphicNormalInfluence("Graphic Normal Detail Influence", Range(0, 1)) = 0.60
        _GraphicTerrainDetail("Graphic Keep Terrain Color Detail", Range(0, 1)) = 0.52
        _GraphicGrainAmount("Graphic Print Grain", Range(0, 0.25)) = 0.085
        _GraphicGrainPixelSize("Graphic Grain Pixel Size", Range(1, 8)) = 2

        _DebugMode("Debug 0 Final 1 Height 2 Slope 3 Geometric Normal", Range(0, 3)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        // 所有被 Material 和脚本控制的 scalar/color 属性全部在同一个 CBuffer 中。
        // 这样可以保持 URP SRP Batcher-compatible（前提是不用 MPB）。
        CBUFFER_START(UnityPerMaterial)
            float _Radius;
            float _BaseStrength;
            float _MountainStrength;
            float _CraterStrength;
            float4 _LowColor;
            float4 _MidColor;
            float4 _HighColor;
            float4 _RockColor;
            float _HeightLow;
            float _HeightHigh;
            float _ColorBandWidth;
            float _ColorWarpStrength;
            float _SlopeStart;
            float _SlopeEnd;
            float _RockCoverage;
            float _NormalTiling;
            float _NormalBlendSharpness;
            float _NormalStrength;
            float _FlatNormalStrength;
            float4 _AmbientColor;
            float _AmbientStrength;
            float _LightStrength;
            float _Wrap;
            // Graphic 模式的所有 Material 属性，与已有字段共享 UnityPerMaterial CBUFFER。
            // 保证这些属性不会破坏 URP SRP Batcher 兼容性。
            float _GraphicBlend;
            float4 _GraphicShadowColor;
            float4 _GraphicMidColor;
            float4 _GraphicHighColor;
            float _GraphicShadowThreshold;
            float _GraphicHighlightThreshold;
            float _GraphicBandSoftness;
            float _GraphicNormalInfluence;
            float _GraphicTerrainDetail;
            float _GraphicGrainAmount;
            float _GraphicGrainPixelSize;
            float _DebugMode;
        CBUFFER_END

        TEXTURE2D(_RockNormal);
        SAMPLER(sampler_RockNormal);

        // Unity Mesh.SetUVs(2..5) 对应 TEXCOORD2..5。
        // 这里不是 UV 贴图坐标，而是 CPU 预计算出来的每顶点数据：
        // h.xyz = base / mountain / crater；h.w = biome warp。
        // g0/g1/g2 = 对应三种高度的单位球面梯度。
        struct Attributes
        {
            float3 positionOS : POSITION;      // 基础单位球，长度约为 1
            float4 h          : TEXCOORD2;
            float3 g0         : TEXCOORD3;
            float3 g1         : TEXCOORD4;
            float3 g2         : TEXCOORD5;
        };

        struct PlanetGeometry
        {
            float3 positionOS;
            float3 normalOS;
            float height;
            float warp;
        };

        /// 输入：基础球面顶点+预计算的高度及梯度+材质中的 Strength。
        /// 输出：真正形变后的 positionOS、正确几何法线 normalOS 和着色数据。
        /// 没有任何 FBM、Perlin 或 Crater 循环：每顶点只有 dot/mul/add/normalize。
        PlanetGeometry BuildPlanetGeometry(Attributes v)
        {
            PlanetGeometry result;
            float3 dir = normalize(v.positionOS);
            float height = dot(v.h.xyz, float3(_BaseStrength, _MountainStrength, _CraterStrength));
            float3 grad = v.g0 * _BaseStrength + v.g1 * _MountainStrength + v.g2 * _CraterStrength;
            float relativeRadius = max(0.1, 1.0 + height);

            // 球面的径向高度函数：P = R * (1+h) * dir
            result.positionOS = dir * (_Radius * relativeRadius);
            // 上式的球面几何法线（不是近似 lerp 原始/最终 normal）。
            result.normalOS = normalize(dir * relativeRadius - grad);
            result.height = height;
            result.warp = v.h.w;
            return result;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardPlanet"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "TriplanarNormal.hlsl"

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 positionOS   : TEXCOORD1;
                float3 geometricNOS: TEXCOORD2;
                float3 geometricNWS: TEXCOORD3;
                float3 directionOS  : TEXCOORD4;
                float2 heightWarp   : TEXCOORD5;
            };

            Varyings Vert(Attributes IN)
            {
                PlanetGeometry g = BuildPlanetGeometry(IN);
                Varyings OUT;
                OUT.positionOS = g.positionOS;
                OUT.positionWS = TransformObjectToWorld(g.positionOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.geometricNOS = g.normalOS;
                OUT.geometricNWS = TransformObjectToWorldNormal(g.normalOS);
                OUT.directionOS = normalize(IN.positionOS);
                OUT.heightWarp = float2(g.height, g.warp);
                return OUT;
            }

            // 【便宜的屏幕空间颗粒 Hash】
            // 输入：屏幕像素格子坐标（屏幕空间单位，非 World/Object）。
            // 输出：0~1 的伪随机数；无需噪声贴图、纹理采样、sin 或循环。
            // 用途：打散 Graphic 明暗交界，并模拟参考图的印刷颗粒。
            // 注意：颗粒固定在屏幕上；相机移动时它不会贴在行星表面。
            float HashPixel(float2 cell)
            {
                float3 p3 = frac(float3(cell.x, cell.y, cell.x) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 输入：材质计算出的地表 baseColor、
            //       几何法线 nGeoWS、Triplanar 法线 nDetailWS、主方向光 mainLight，
            //       以及 IN.positionCS 里的屏幕像素坐标。
            // 输出：3 档限制色盘的 RGB。只修改 Fragment，完全不影响顶点位置/阴影形状。
            float3 ApplyGraphicShading(float3 baseColor, float3 nGeoWS, float3 nDetailWS,
                                       Light mainLight, float2 pixelPosition)
            {
                float3 L = normalize(mainLight.direction);

                // 主体的大片明暗来自几何法线；细碎明暗来自原有 Triplanar Normal。
                // Influence=0: 最清晰的两三块大光影；=1: 细碎纹理也决定分层边界。
                float geoDiffuse = saturate((dot(normalize(nGeoWS), L) + _Wrap) / (1.0 + _Wrap));
                float detailDiffuse = saturate((dot(normalize(nDetailWS), L) + _Wrap) / (1.0 + _Wrap));
                float lightSignal = lerp(geoDiffuse, detailDiffuse, _GraphicNormalInfluence);

                // 光源强度和主光阴影仍然参与分层。没有额外灯光循环。
                float lightLuminance = dot(mainLight.color, float3(0.2126, 0.7152, 0.0722));
                lightSignal *= mainLight.shadowAttenuation *
                               saturate(lightLuminance * (_LightStrength / 1.15));

                // 将像素坐标量化成小方格并 Hash，避免噪点在 subpixel 高频下闪烁。
                // 新增计算是纯 ALU，没有新纹理采样。
                float2 grainCell = floor(pixelPosition / max(_GraphicGrainPixelSize, 1.0));
                float grain = HashPixel(grainCell) - 0.5;
                float thresholdSignal = saturate(lightSignal + grain * _GraphicGrainAmount);

                // 两条阈值产生 3 档颜色：Shadow / Mid / Highlight。
                // Softness 越小越有硬边海报感；若太小且 Normal 太强，边缘会闪烁。
                float soft = max(_GraphicBandSoftness, 0.001);
                float midWeight = smoothstep(_GraphicShadowThreshold - soft,
                                             _GraphicShadowThreshold + soft, thresholdSignal);
                float highThreshold = max(_GraphicHighlightThreshold,
                                          _GraphicShadowThreshold + 0.002);
                float highWeight = smoothstep(highThreshold - soft,
                                              highThreshold + soft, thresholdSignal);
                float3 palette = lerp(_GraphicShadowColor.rgb, _GraphicMidColor.rgb, midWeight);
                palette = lerp(palette, _GraphicHighColor.rgb, highWeight);

                // 为避免变成只剩三种纯色的平面圆球：继续保留原始地质着色的区别。
                // Color=0: 严格三色色盘；Color=1: 用原有 Height/Slope 配色调制色盘。
                // 各通道分别调制，岩石/低地仍有区别，但不会变回连续 PBR 光照。
                float3 terrainTint = saturate(baseColor * 1.35 + 0.28);
                float3 paletteWithTerrain = palette * lerp(float3(1.0, 1.0, 1.0), terrainTint, _GraphicTerrainDetail);

                // 明亮区域仍有微弱的印刷密度变化；暗部保持较扎实的墨色。
                float printMod = 1.0 + grain * _GraphicGrainAmount * 0.50;
                return max(float3(0.0, 0.0, 0.0), paletteWithTerrain * printMod);
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                // 【几何法线】决定地质分层和岩石坡度；细节 Normal 不参与地貌分类。
                float3 nGeoOS = normalize(IN.geometricNOS);
                float3 dirOS = normalize(IN.directionOS);
                float slope = saturate(1.0 - dot(nGeoOS, dirOS));
                float rockMask = smoothstep(_SlopeStart, max(_SlopeStart + 0.001, _SlopeEnd), slope);

                // 高度层之间故意留比较宽的过渡；warp 使等高线不规则。
                float coloredHeight = IN.heightWarp.x + IN.heightWarp.y * _ColorWarpStrength;
                float lowToMid = smoothstep(_HeightLow - _ColorBandWidth,
                                            _HeightLow + _ColorBandWidth, coloredHeight);
                float midToHigh = smoothstep(_HeightHigh - _ColorBandWidth,
                                             _HeightHigh + _ColorBandWidth, coloredHeight);
                float3 baseColor = lerp(_LowColor.rgb, _MidColor.rgb, lowToMid);
                baseColor = lerp(baseColor, _HighColor.rgb, midToHigh);
                baseColor = lerp(baseColor, _RockColor.rgb, rockMask * _RockCoverage);

                // 【细节法线】只改 Lighting；对陡坡效果更强，对低坡保留轻微质感。
                float normalWeight = lerp(_FlatNormalStrength, 1.0, rockMask);
                float3 nDetailOS = SamplePlanetTriplanarNormal(
                    IN.positionOS / max(_Radius, 0.001), nGeoOS,
                    _NormalTiling, _NormalBlendSharpness, _NormalStrength * normalWeight);
                float3 N = normalize(TransformObjectToWorldNormal(nDetailOS));

                // 仅使用主 Directional Light + 风格化 Ambient；没有额外 lights 循环。
                // 无大气、海洋或屏幕空间 pass。
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndotl = dot(N, normalize(mainLight.direction));
                float diffuse = saturate((ndotl + _Wrap) / (1.0 + _Wrap));
                float3 lighting = _AmbientColor.rgb * _AmbientStrength +
                                  mainLight.color * diffuse * _LightStrength *
                                  mainLight.shadowAttenuation;
                float3 originalColor = baseColor * lighting;

                // 【新增：Graphic 模式】原 Normal 计算和色彩分类不变，
                // 仅用二段阈值、三档色盘替换最终的连续光照。Blend=0 就是旧版效果。
                // 使用插值的几何 WS normal（不是 normal map）决定大明暗轮廓。
                float3 color = originalColor;
                // 这是整个材质共用的 Uniform 分支：GraphicBlend=0 时，
                // 可跳过三色 Ramp/屏幕 Hash 的计算（不会改变 Triplanar 的采样数）。
                [branch] if (_GraphicBlend > 0.001)
                {
                    float3 graphicColor = ApplyGraphicShading(baseColor, normalize(IN.geometricNWS),
                                                              N, mainLight, IN.positionCS.xy);
                    color = lerp(originalColor, graphicColor, saturate(_GraphicBlend));
                }

                // Debug 模式：1=高度 2=坡度 3=几何法线 (OS)，定位数据流问题用。
                if (_DebugMode > 2.5) return float4(nGeoOS * 0.5 + 0.5, 1);
                if (_DebugMode > 1.5) return float4(slope * 4.0, slope * 4.0, slope * 4.0, 1);
                if (_DebugMode > 0.5)
                {
                    float hv = saturate(IN.heightWarp.x * 3.0 + 0.5);
                    return float4(hv, hv, hv, 1);
                }
                return float4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowVaryings { float4 positionCS : SV_POSITION; };

            ShadowVaryings ShadowVert(Attributes IN)
            {
                PlanetGeometry g = BuildPlanetGeometry(IN); // 与主 Forward Pass 完全同一份形变！
                float3 ws = TransformObjectToWorld(g.positionOS);
                float3 nWS = TransformObjectToWorldNormal(g.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lWS = normalize(_LightPosition - ws);
                #else
                    float3 lWS = _LightDirection;
                #endif
                ShadowVaryings OUT;
                OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(ws, nWS, lWS));
                // 兼容反转 Z 的 Near Plane 裁剪。
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, OUT.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    Fallback Off
}
