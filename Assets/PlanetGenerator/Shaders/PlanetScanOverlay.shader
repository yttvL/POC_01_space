Shader "Major Studio/Planet/Topographic Scan Overlay"
{
    Properties
    {
        [Header(Geometry Matching)]
        // 这四项必须与底层 PlanetSurface 材质对应值相同，否则白线外壳会错位。
        _Radius("Planet Radius / 星球半径", Float) = 1
        _BaseStrength("Base Height Strength / 基础高度", Range(0, 0.45)) = 0.09
        _MountainStrength("Mountain Height Strength / 山脉高度", Range(0, 0.45)) = 0.18
        _CraterStrength("Crater Height Strength / 陨石坑高度", Range(0, 0.45)) = 0.12
        _ShellOffset("Shell Offset / 外壳偏移", Range(0.0005, 1)) = 0.003

        [Header(Contour Lines)]
        _LineColor("Line Color / 线条颜色", Color) = (0.94, 0.97, 1, 1)
        _LineOpacity("Line Opacity / 透明度", Range(0, 1)) = 0.75
        _LineBrightness("Line Brightness / 亮度", Range(0, 4)) = 1.0
        _ContourFrequency("Contour Density / 等高线密度", Range(1, 300)) = 110
        _LineWidthPixels("Line Width Pixels / 线宽像素", Range(0.3, 3)) = 0.85
        _ContourWarp("Contour Warp / 线条扰动", Range(0, 0.08)) = 0.007
        _DistantFade("Anti Aliasing Fade / 远距离抗锯齿", Range(0, 1)) = 1

        [Header(Optional Scan Sweep)]
        _SweepEnable("Sweep Mask 0 Off 1 On / 扫描带", Range(0, 1)) = 0
        _SweepSpeed("Sweep Speed / 扫描速度", Range(0.05, 5)) = 0.55
        _SweepWidth("Sweep Width / 扫描带宽度", Range(0.02, 1.5)) = 0.32
        _SweepBaseVisibility("Outside Band Visibility / 扫描带外可见度", Range(0, 1)) = 0.08
    }

    SubShader
    {
        // 透明层：不会替换原本 PlanetSurface 的颜色和光照。
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }

        Pass
        {
            Name "TopographicScanOverlay"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZTest LEqual
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            // 与略微外移的 ShellOffset 配合，减少深度精度不够产生的闪烁。
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 所有标量 / 颜色都放进同一个材质 CBUFFER，兼容 SRP Batcher。
            CBUFFER_START(UnityPerMaterial)
                float _Radius;
                float _BaseStrength;
                float _MountainStrength;
                float _CraterStrength;
                float _ShellOffset;
                float4 _LineColor;
                float _LineOpacity;
                float _LineBrightness;
                float _ContourFrequency;
                float _LineWidthPixels;
                float _ContourWarp;
                float _DistantFade;
                float _SweepEnable;
                float _SweepSpeed;
                float _SweepWidth;
                float _SweepBaseVisibility;
            CBUFFER_END

            // 输入来自同一份 Baked Mesh（不是单纯的 Sphere 网格）。
            // Mesh.SetUVs(2) -> TEXCOORD2: xyz 是三个高度，w 是预计算的 biome warp。
            // Mesh.SetUVs(3..5) -> TEXCOORD3..5: 三种高度对应的球面梯度。
            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 heightWarp : TEXCOORD2;
                float3 gradBase   : TEXCOORD3;
                float3 gradMount  : TEXCOORD4;
                float3 gradCrater : TEXCOORD5;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 heightWarp : TEXCOORD0;
                float3 sphereDirOS: TEXCOORD1;
            };

            // 输入：带高度通道的顶点 + 当前 Overlay Material 的地形强度参数。
            // 输出：Clip Space 位置 / 实际高度 / warp / 单位球方向。
            // 这里故意复制 PlanetSurface 的廉价位移，而不重新计算 FBM/Crater。
            // 这样外壳可以准确贴着底层起伏，且无需改动旧 Shader 或 Generator。
            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 dir = normalize(IN.positionOS);
                float height = dot(IN.heightWarp.xyz,
                    float3(_BaseStrength, _MountainStrength, _CraterStrength));
                float relativeRadius = max(0.1, 1.0 + height);

                // 高度和底层星球一致，仅增加一个很小的径向外移。
                // 这里不是把整个球体直接缩放，否则不同高度的偏移量会不一致。
                float3 displacedOS = dir * (_Radius * (relativeRadius + _ShellOffset));
                OUT.positionCS = TransformObjectToHClip(displacedOS);
                OUT.heightWarp = float2(height, IN.heightWarp.w);
                OUT.sphereDirOS = dir;
                return OUT;
            }

            // 输入：顶点阶段插值过来的组合高度和预计算 warp。
            // 输出：0..1 的线条覆盖率；不产生任何实心表面颜色。
            // 等高线本质是 height = n / frequency 的等值线。
            float ContourCoverage(float height, float warp)
            {
                float level = (height + warp * _ContourWarp) * _ContourFrequency;

                // 每次 level 经过整数，就代表经过一条高度等值线。
                // frac(level + 0.5) 让负高度也能够正确处理。
                float distanceToLine = abs(frac(level + 0.5) - 0.5);

                // fwidth 估计当前一个屏幕像素内 level 的变化。
                // 让线宽是屏幕像素单位，而不是高度函数单位；远景也不会无限变细闪烁。
                float pixelSpan = max(fwidth(level), 1e-5);
                float halfWidth = 0.5 * max(_LineWidthPixels, 0.1) * pixelSpan;
                float feather = 0.65 * pixelSpan;
                float coverage = 1.0 - smoothstep(
                    max(0.0, halfWidth - feather), halfWidth + feather, distanceToLine);

                // 当一个像素跨过不止一条等高线时，降低线条对比，减少 aliasing。
                // 这是近似的 anti-aliasing 策略，不能替代真正的低分辨率 LOD。
                float fade = 1.0 - smoothstep(0.60, 1.40, pixelSpan);
                return coverage * lerp(1.0, fade, _DistantFade);
            }

            // 输入：插值后的高度及球面方向。
            // 输出：透明白线 RGBA。alpha=0 的区域完全透出底层彩色星球。
            // 不引用 Main Light，不采样 Normal Map，不参与实时阴影。
            half4 Frag(Varyings IN) : SV_Target
            {
                float _line = ContourCoverage(IN.heightWarp.x, IN.heightWarp.y);

                // 可选的移动扫描带：按球体自身 Y 方向滑过表面。
                // SweepEnable=0 时完整显示全部等高线；=1 时只强调移动扫描带。
                float sweepCenter = 1.5 - frac(_Time.y * _SweepSpeed / 3.0) * 3.0;
                float dist = (IN.sphereDirOS.y - sweepCenter) / max(_SweepWidth, 0.02);
                float band = exp2(-4.0 * dist * dist);
                float sweepMask = lerp(1.0, lerp(_SweepBaseVisibility, 1.0, band), _SweepEnable);

                float alpha = saturate(_line * _LineOpacity * sweepMask);
                clip(alpha - 0.002);
                return half4(_LineColor.rgb * _LineBrightness, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
