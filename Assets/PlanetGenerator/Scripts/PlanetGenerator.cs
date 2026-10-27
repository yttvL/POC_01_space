using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Planet Generator — 只负责生成 Mesh（完全不管理材质）。
///
/// 【职责边界】
/// - PlanetGenerator：生成球面拓扑，预计算地形高度/梯度，写入 Mesh 顶点通道。
/// - MeshRenderer：引用用户手动创建的 .mat；颜色、Normal、Height Strength 都在材质中调。
/// - PlanetSurface.shader：在顶点阶段读取 Mesh 地形数据并位移；片元阶段负责颜色和光照。
///
/// 【数据流】
/// IcosphereBuilder.Build(subdivisions) -> 单位球 Mesh
/// PlanetHeight.CreateContext(seed, settings) -> 生成本星球的随机噪声与陨石坑配置
/// Evaluate(dir, context) -> (baseHeight, mountainHeight, craterHeight, biomeWarp)
/// EvaluateGradients(...) -> base/mountain/crater 的球面梯度
/// SetUVs(2/3/4/5, ...) -> TEXCOORD2/3/4/5 -> Shader 的 BuildPlanetGeometry()
///
/// 【何时 Rebuild】
/// Seed、Noise 频率、Octaves、Crater 数量/分布、细分级数改变后需要 Rebuild。
/// Material 内的 Strength / Colors / Normal 参数改变后，不需要 Rebuild 和 Refresh。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class PlanetGenerator : MonoBehaviour
{
    [Header("Sphere and Terrain / 球体与地形（修改后需 Rebuild）")]
    [Tooltip("Sphere subdivisions; changing this requires Rebuild. / 球体细分级数；修改后需要 Rebuild。")]
    [SerializeField, Range(2, 6)] private int subdivisions = 5;
    [Tooltip("Random seed; changing this requires Rebuild. / 随机种子；修改后需要 Rebuild。")]
    [SerializeField] private int seed = 42;
    [Tooltip("CPU terrain shape settings; changing these requires Rebuild. / CPU 地形形状参数；修改后需要 Rebuild。")]
    [SerializeField] private PlanetHeight.Settings heightSettings = new PlanetHeight.Settings();

    [Tooltip("Optional baked Mesh asset; skips CPU terrain generation at startup. / 可选的烘焙 Mesh；启动时直接复用，不执行 CPU Noise。")]
    [SerializeField] private Mesh bakedMesh;

    [Header("Culling Bounds / 剔除包围盒（物体空间）")]
    [Tooltip("Object-space culling bounds radius for GPU displacement. Prefer Transform Scale for size. / 为 GPU 位移预留的本地包围盒半径；调整大小优先使用 Transform Scale。")]
    [SerializeField, Min(1.0f)] private float boundsRadius = 2.5f;

    // 只有生成器自己创建的临时 Mesh 才归它释放；绝不销毁 Asset 或 Renderer 的 Material。
    [NonSerialized] private Mesh ownedMesh;
    [NonSerialized] private MeshFilter meshFilter;

    /// <summary>输出：MeshFilter 当前使用的 Mesh（临时预览或已烘焙 Asset）。</summary>
    public Mesh CurrentMesh
    {
        get
        {
            CacheComponents();
            return meshFilter.sharedMesh;
        }
    }

    /// <summary>
    /// 输入：无。输出：确保 MeshFilter 引用了一个可用 Mesh。
    /// 启动时有 bakedMesh 就直接挂它；否则仅在必要时生成一次地形。
    /// 此方法不会读取、创建、更改或销毁任何 Material。
    /// </summary>
    private void OnEnable()
    {
        CacheComponents();
        if (bakedMesh != null)
        {
            ReleaseOwnedMesh();
            meshFilter.sharedMesh = bakedMesh;
        }
        else if (meshFilter.sharedMesh == null || Application.isPlaying)
        {
            // 没有 Bake 的星球进入 Play Mode 时重算一次；正式游戏建议事先 Bake。
            Rebuild();
        }
    }

    /// <summary>输入：无。输出：缓存 MeshFilter 引用；不分配 Mesh。</summary>
    private void CacheComponents()
    {
        if (meshFilter == null)
            meshFilter = GetComponent<MeshFilter>();
    }

    /// <summary>
    /// 输入：subdivisions、seed、heightSettings、boundsRadius（均为 Inspector 字段）。
    /// 输出：一个全新的高细分单位球 Mesh，其中 UV2~UV5 保存预计算地形信息。
    /// 运行代价：按顶点多次 CPU Noise/Crater 采样；请勿每帧调用。
    /// 注意：顶点在 CPU 上仍是半径 1 的球；真正位移由材质的 Vertex Shader 完成。
    /// 注意：完全不触碰 MeshRenderer 的 sharedMaterial/material。
    /// </summary>
    [ContextMenu("Rebuild Mesh / 重新生成网格")]
    public void Rebuild()
    {
        CacheComponents();
        // 明确重新生成表示放弃上次 Bake 的引用；原资产不会被改写。
        bakedMesh = null;
        Mesh newMesh = IcosphereBuilder.Build(subdivisions);
        var context = PlanetHeight.CreateContext(seed, heightSettings);
        Vector3[] vertices = newMesh.vertices;

        // UV2 ↔ TEXCOORD2：四个标量 (base, mountain, crater, biomeWarp)。
        var heights = new List<Vector4>(vertices.Length);
        // UV3~UV5 ↔ TEXCOORD3~5：三种高度函数的 3D 球面梯度。
        var gradsBase = new List<Vector3>(vertices.Length);
        var gradsMountain = new List<Vector3>(vertices.Length);
        var gradsCrater = new List<Vector3>(vertices.Length);

        // 相对单位球的差分距离；越小不代表一定越准确。
        const float gradientEpsilon = 0.002f;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 direction = vertices[i].normalized;
            Vector4 h = PlanetHeight.Evaluate(direction, context);
            PlanetHeight.EvaluateGradients(direction, context, gradientEpsilon,
                out Vector3 gradBase, out Vector3 gradMountain, out Vector3 gradCrater);
            heights.Add(h);
            gradsBase.Add(gradBase);
            gradsMountain.Add(gradMountain);
            gradsCrater.Add(gradCrater);
        }

        // 将计算结果写到 Mesh；这些数据是 Attribute，并不代表 UV Texture 坐标。
        newMesh.SetUVs(2, heights);
        newMesh.SetUVs(3, gradsBase);
        newMesh.SetUVs(4, gradsMountain);
        newMesh.SetUVs(5, gradsCrater);

        // GPU 形变并不会修改 CPU Mesh Bounds；提前保留宽松包围盒以防被剔除。
        float extent = Mathf.Max(1f, boundsRadius);
        newMesh.bounds = new Bounds(Vector3.zero, Vector3.one * (2f * extent));
        newMesh.name = $"PlanetGenerator_Seed{seed}_Sub{subdivisions}_Generated";

        ReleaseOwnedMesh();
        ownedMesh = newMesh;
        meshFilter.sharedMesh = ownedMesh;

        Debug.Log($"[Planet Generator] Rebuild complete / 网格重建完成：{newMesh.vertexCount} vertices / 顶点，" +
                  $"{newMesh.triangles.Length / 3} triangles / 三角面，Seed={seed}。" +
                  "Material unchanged / 材质未修改。", this);
    }

    /// <summary>
    /// 输入：无（内部随机生成本次 Seed 和地形参数）。
    /// 输出：更新 Inspector 中的 Seed / FBM / Mountain / Crater 参数，随后调用 Rebuild()。
    /// 数据流：随机参数 -> heightSettings -> Rebuild() -> CPU 计算高度/梯度 -> Mesh UV2~UV5。
    ///
    /// 不随机 Subdivisions：它决定顶点精度与 CPU 重建成本，应该由制作人固定。
    /// 不随机 Bounds Radius / Material：它们分别属于剔除设置与渲染外观。
    /// 注意 Rebuild 会解除 bakedMesh 的绑定，但不会修改磁盘上已有的 .asset。
    /// </summary>
    [ContextMenu("Random Generate / 随机生成并重建")]
    public void RandomizeAndRebuild()
    {
        // Guid 为每次点击提供不同的初始状态，不依赖或改变 UnityEngine.Random 的全局状态。
        var rng = new System.Random(Guid.NewGuid().GetHashCode());
        if (heightSettings == null)
            heightSettings = new PlanetHeight.Settings();

        seed = rng.Next(1, int.MaxValue);

        // 输入：指定区间的参数随机值。输出：生成器的新参数（均须经 Rebuild 生效）。
        // 控制范围比字段允许的最大范围更保守，让一键生成的结果通常能直接用作视觉原型。
        heightSettings.baseFrequency = RandomRange(rng, 1.5f, 3.3f);
        heightSettings.baseOctaves = rng.Next(3, 6);             // 3~5 层
        // 顶点较少时限制最高频率，避免高频山脊超过 Mesh 可采样的空间分辨率。
        heightSettings.mountainFrequency = subdivisions >= 6
            ? RandomRange(rng, 3.6f, 6.7f)
            : RandomRange(rng, 3.2f, 5.3f);
        heightSettings.mountainOctaves = subdivisions >= 6
            ? rng.Next(3, 6)                                   // 3~5 层
            : rng.Next(3, 5);                                  // 3~4 层
        heightSettings.maskFrequency = RandomRange(rng, 1.1f, 2.8f);
        heightSettings.mountainCoverage = RandomRange(rng, -0.10f, 0.18f);

        // 陨石坑以较小半径为主，减少「一个坑占据星球很大面积」的视觉感受。
        // Icosphere 细分较低时，太小的坑只有几个顶点，轮廓会变尖/变碎，
        // 因而依据固定的 Subdivisions 调整随机半径下限，避免细分 5 时抽到过小的坑。
        heightSettings.craterCount = rng.Next(32, 61);          // 32~60 个
        if (subdivisions >= 6)
            heightSettings.craterMinRadius = RandomRange(rng, 0.055f, 0.085f);
        else if (subdivisions >= 5)
            heightSettings.craterMinRadius = RandomRange(rng, 0.075f, 0.100f);
        else
            heightSettings.craterMinRadius = RandomRange(rng, 0.12f, 0.16f);

        heightSettings.craterMaxRadius = RandomRange(rng,
            Mathf.Max(heightSettings.craterMinRadius + 0.045f, 0.125f),
            Mathf.Max(heightSettings.craterMinRadius + 0.070f, 0.185f));

        Rebuild();
        Debug.Log($"[Planet Generator] Random Generate / 随机生成完成：Seed={seed}，" +
                  $"Craters={heightSettings.craterCount}，" +
                  $"Radius={heightSettings.craterMinRadius:F3}~{heightSettings.craterMaxRadius:F3}。" +
                  "Material and subdivisions unchanged / 材质和细分未修改。", this);
    }

    /// <summary>
    /// 输入：System.Random 与范围 [min,max]。输出：指定范围内均匀分布的浮点值。
    /// 这是 CPU 编辑阶段的参数随机化，不是 3D Terrain Noise。
    /// </summary>
    private static float RandomRange(System.Random rng, float min, float max)
    {
        return min + (max - min) * (float)rng.NextDouble();
    }

    /// <summary>
    /// 输入：新的 Seed。输出：更新 Seed 并重新计算 UV2~UV5 数据。
    /// 这是昂贵操作，适用于编辑器修改或偶发的星球生成，不适合每帧调用。
    /// </summary>
    public void SetSeedAndRebuild(int newSeed)
    {
        seed = newSeed;
        Rebuild();
    }

    /// <summary>
    /// 输入：Project 中持久化的 Mesh Asset。输出：把该 Mesh 挂到 MeshFilter 并记录为 Bake。
    /// 不会修改它的顶点/Bounds，不接触材质。主要供编辑器 Bake 按钮使用。
    /// </summary>
    public void SetBakedMesh(Mesh mesh)
    {
        CacheComponents();
        ReleaseOwnedMesh();
        bakedMesh = mesh;
        meshFilter.sharedMesh = bakedMesh;
    }

    /// <summary>输入：无。输出：销毁且仅销毁当前 Generator 创建的临时 Mesh。</summary>
    private void ReleaseOwnedMesh()
    {
        if (ownedMesh == null) return;
        CacheComponents();
        if (meshFilter.sharedMesh == ownedMesh)
            meshFilter.sharedMesh = null;
        if (Application.isPlaying)
            Destroy(ownedMesh);
        else
            DestroyImmediate(ownedMesh);
        ownedMesh = null;
    }

    private void OnDestroy()
    {
        ReleaseOwnedMesh();
        // 特意没有 Destroy(Material)：材质的生命周期现在由 Unity/用户管理。
    }
}
