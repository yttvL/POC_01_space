using System;
using UnityEngine;

/// <summary>
/// 静态 CPU 地形数学。不引用 Mesh / Material：同一方向 d 永远得到相同的生成结果。
/// 重要约定：
/// - 输入 d：星球中心指向地表的单位方向，Object Space；不使用 UV，经线没有接缝。
/// - 输出 Evaluate(d).xyz：三个【无强度】高度通道：base / mountain / crater。
/// - 输出 Evaluate(d).w：颜色扰动通道；不参与几何形变。
/// - 真正的顶点位置由 Shader 算：d * R * (1 + base*a + mountain*b + crater*c)。
/// </summary>
public static class PlanetHeight
{
    [Serializable]
    public class Settings
    {
        [Header("Base Terrain / 低频起伏")]
        [Tooltip("Spatial frequency of low-frequency terrain. Rebuild after editing. / 低频地形的空间频率；修改后需 Rebuild。")]
        [Min(0.01f)] public float baseFrequency = 2.2f;
        [Tooltip("Number of FBM octaves. More octaves cost more CPU time when rebuilding. / FBM 叠加层数；越多则重建时 CPU 开销越大。")]
        [Range(1, 8)] public int baseOctaves = 4;

        [Header("Mountains / 山脉形态")]
        [Tooltip("Spatial frequency of the ridged mountains. / 山脊噪声频率。")]
        [Min(0.01f)] public float mountainFrequency = 5.0f;
        [Tooltip("Number of ridge FBM octaves. / 山脊 FBM 叠加层数。")]
        [Range(1, 8)] public int mountainOctaves = 4;
        [Tooltip("Frequency of the mountain distribution mask. / 山脉分布遮罩频率。")]
        [Min(0.01f)] public float maskFrequency = 1.7f;
        [Tooltip("Threshold controlling where mountains appear. / 控制山脉出现区域的阈值。")]
        [Range(-1, 1)] public float mountainCoverage = 0.05f;

        [Header("Craters (Rebuild Required) / 陨石坑分布（需重建）")]
        [Tooltip("Number of random craters on the sphere. / 球面随机陨石坑数量。")]
        [Range(0, 100)] public int craterCount = 28;
        [Tooltip("Minimum crater chord radius on the unit sphere. / 单位球上陨石坑最小弦半径。")]
        [Range(0.025f, 0.6f)] public float craterMinRadius = 0.045f;
        [Tooltip("Maximum crater chord radius on the unit sphere. / 单位球上陨石坑最大弦半径。")]
        [Range(0.04f, 0.7f)] public float craterMaxRadius = 0.25f;
    }

    public struct Crater
    {
        public Vector3 center;
        public float chordRadius;   // 单位球的弦距离，而不是世界单位/UV 距离
        public float depth;
    }

    public struct Context
    {
        public Settings settings;
        public Vector3 offset;
        public Crater[] craters;
    }

    /// <summary>
    /// 输入：用户 Seed 与 Settings。
    /// 输出：包含随机坐标偏移与固定 Crater 列表的 Context。
    /// 这个方法只在生成阶段调用一次，不要放到每个顶点循环内部。
    /// </summary>
    public static Context CreateContext(int seed, Settings settings)
    {
        var rng = new System.Random(seed);
        var result = new Context
        {
            settings = settings,
            offset = new Vector3(
                (float)rng.NextDouble() * 100f + 10f,
                (float)rng.NextDouble() * 100f + 20f,
                (float)rng.NextDouble() * 100f + 30f),
            craters = new Crater[settings.craterCount]
        };

        float minRadius = Mathf.Min(settings.craterMinRadius, settings.craterMaxRadius);
        float maxRadius = Mathf.Max(settings.craterMinRadius, settings.craterMaxRadius);
        for (int i = 0; i < result.craters.Length; ++i)
        {
            // 在球面上均匀取方向：z ∈ [-1,1]，方位角 ∈ [0,2π]。
            float z = (float)rng.NextDouble() * 2f - 1f;
            float theta = (float)rng.NextDouble() * Mathf.PI * 2f;
            float xy = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            float biasedRadius = Mathf.Pow((float)rng.NextDouble(), 1.7f);
            result.craters[i] = new Crater
            {
                center = new Vector3(xy * Mathf.Cos(theta), xy * Mathf.Sin(theta), z),
                chordRadius = Mathf.Lerp(minRadius, maxRadius, biasedRadius),
                depth = Mathf.Lerp(0.7f, 1.3f, (float)rng.NextDouble())
            };
        }
        return result;
    }

    /// <summary>
    /// 输入：单位方向 d、事先构造好的 Context。
    /// 输出：Vector4(base, mountain, crater, colorWarp)。
    /// 注意：这里【不乘】对应 Strength，运行时强度交给 Vertex Shader 处理。
    /// </summary>
    public static Vector4 Evaluate(Vector3 d, Context ctx)
    {
        Settings s = ctx.settings;
        Vector3 p = d + ctx.offset;

        float baseNoise = Fbm(p * s.baseFrequency, s.baseOctaves);
        float maskNoise = Fbm((d + ctx.offset * 0.23f) * s.maskFrequency, 3);
        float mask = Smoothstep(s.mountainCoverage - 0.17f, s.mountainCoverage + 0.34f, maskNoise);
        float ridges = RidgedFbm((d + ctx.offset * 0.51f) * s.mountainFrequency, s.mountainOctaves);
        float mountain = ridges * mask;
        float craters = EvaluateCraters(d, ctx.craters);
        float warp = Fbm((d + ctx.offset * 0.61f) * 3.0f, 2);
        return new Vector4(baseNoise, mountain, craters, warp);
    }

    /// <summary>
    /// 输入：单位方向 d、Context、差分步长 epsilon（单位球方向中的小扰动）。
    /// 输出：三组切向球面梯度，即 ∇_S(base/mountain/crater)，分别在 world/object 同方向轴中。
    /// 计算方式：选择 d 的两条正交切向方向 t1/t2，对每个高度函数做中心差分。
    /// Shader 使用 N = normalize((1+h)*d - ∇_S h) 恢复随 Strength 改变的几何法线。
    /// 色彩 warp 无需梯度。
    /// </summary>
    public static void EvaluateGradients(Vector3 d, Context ctx, float epsilon,
        out Vector3 gradBase, out Vector3 gradMountain, out Vector3 gradCrater)
    {
        // 避免方向接近世界 Y 轴时叉积退化。
        Vector3 axis = Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right;
        Vector3 t1 = Vector3.Cross(axis, d).normalized;
        Vector3 t2 = Vector3.Cross(d, t1).normalized;

        Vector4 p1 = Evaluate((d + t1 * epsilon).normalized, ctx);
        Vector4 m1 = Evaluate((d - t1 * epsilon).normalized, ctx);
        Vector4 p2 = Evaluate((d + t2 * epsilon).normalized, ctx);
        Vector4 m2 = Evaluate((d - t2 * epsilon).normalized, ctx);
        float inv = 0.5f / epsilon;

        gradBase = t1 * ((p1.x - m1.x) * inv) + t2 * ((p2.x - m2.x) * inv);
        gradMountain = t1 * ((p1.y - m1.y) * inv) + t2 * ((p2.y - m2.y) * inv);
        gradCrater = t1 * ((p1.z - m1.z) * inv) + t2 * ((p2.z - m2.z) * inv);
    }

    private static float EvaluateCraters(Vector3 d, Crater[] craters)
    {
        float height = 0f;
        foreach (Crater crater in craters)
        {
            // |d - center|² = 2(1 - dot(d,center))，避免在无关坑上进行 sqrt。
            float dist2 = Mathf.Max(0f, 2f * (1f - Vector3.Dot(d, crater.center)));
            float radius2 = crater.chordRadius * crater.chordRadius;
            if (dist2 > radius2 * 1.6f * 1.6f) continue;

            float x = Mathf.Sqrt(dist2) / crater.chordRadius;
            // Bowl 负责中心凹陷；Rim 负责 x≈1 的圈状隆起。
            float bowl = -Mathf.Pow(1f - Smoothstep(0f, 0.96f, x), 1.5f);
            float rim = Smoothstep(0.72f, 1.0f, x) *
                        (1f - Smoothstep(1.0f, 1.27f, x)) * 0.32f;
            float ejecta = (1f - Smoothstep(1.05f, 1.60f, x)) * 0.025f;
            height += (bowl + rim + ejecta) * crater.depth;
        }
        return height;
    }

    private static float RidgedFbm(Vector3 p, int octaves)
    {
        float total = 0f, amplitude = 1f, sum = 0f;
        for (int i = 0; i < octaves; ++i)
        {
            float ridge = 1f - Mathf.Abs(Perlin3D(p));
            total += ridge * ridge * amplitude;
            sum += amplitude;
            p *= 2.0f;
            amplitude *= 0.5f;
        }
        return total / Mathf.Max(sum, 0.0001f);
    }

    private static float Fbm(Vector3 p, int octaves)
    {
        float total = 0f, amplitude = 1f, sum = 0f;
        for (int i = 0; i < octaves; ++i)
        {
            total += Perlin3D(p) * amplitude;
            sum += amplitude;
            p *= 2.03f;     // lacunarity（每层频率倍率）
            amplitude *= 0.5f; // persistence（每层幅度衰减）
        }
        return total / Mathf.Max(sum, 0.0001f);
    }

    /// <summary>
    /// 纯 C# 3D Gradient Perlin：输入任意 3D 坐标，输出大致 [-1,1] 的连续噪声。
    /// 不用 Mathf.PerlinNoise，因为它只接受 2D 输入，贴球时不方便。
    /// </summary>
    private static float Perlin3D(Vector3 p)
    {
        int ix = Mathf.FloorToInt(p.x), iy = Mathf.FloorToInt(p.y), iz = Mathf.FloorToInt(p.z);
        float x = p.x - ix, y = p.y - iy, z = p.z - iz;
        float u = Fade(x), v = Fade(y), w = Fade(z);

        float x00 = Mathf.Lerp(Grad(Hash(ix, iy, iz), x, y, z),
                               Grad(Hash(ix + 1, iy, iz), x - 1f, y, z), u);
        float x10 = Mathf.Lerp(Grad(Hash(ix, iy + 1, iz), x, y - 1f, z),
                               Grad(Hash(ix + 1, iy + 1, iz), x - 1f, y - 1f, z), u);
        float x01 = Mathf.Lerp(Grad(Hash(ix, iy, iz + 1), x, y, z - 1f),
                               Grad(Hash(ix + 1, iy, iz + 1), x - 1f, y, z - 1f), u);
        float x11 = Mathf.Lerp(Grad(Hash(ix, iy + 1, iz + 1), x, y - 1f, z - 1f),
                               Grad(Hash(ix + 1, iy + 1, iz + 1), x - 1f, y - 1f, z - 1f), u);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
    }

    private static uint Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 2246822519u;
            h ^= h >> 13;
            h *= 1274126177u;
            return h ^ (h >> 16);
        }
    }

    private static float Grad(uint h, float x, float y, float z)
    {
        uint b = h & 15u;
        float u = b < 8u ? x : y;
        float v = b < 4u ? y : (b == 12u || b == 14u ? x : z);
        return ((b & 1u) == 0u ? u : -u) + ((b & 2u) == 0u ? v : -v);
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Smoothstep(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / Mathf.Max(0.00001f, b - a));
        return t * t * (3f - 2f * t);
    }
}
