# Planet Generator

Unity 6.3 URP · Procedural rocky planet · Mesh Generation + Material Shading

## How to Use — English

### A. Create your first planet

1. **Import the folder.** Copy the entire `PlanetGenerator` folder into your Unity project's `Assets` folder. Keep `Scripts`, `Editor`, `Shaders`, and `Textures` together. This project uses **URP**, not the Built-in Render Pipeline.
2. **Create the planet.** In the **Hierarchy**, **right-click → 3D Object → Planet Generator**.
3. **Select the new object** named `Planet Generator`. You should see `Transform`, `MeshFilter`, `MeshRenderer`, and `PlanetGenerator` in its Inspector.
4. **Make a material.** In the **Project** window, **right-click → Create → Material**. Name it `M\_MyPlanet` (or anything you like).
5. **Choose the shader.** Select `M\_MyPlanet`. In its Inspector, set **Shader → Major Studio → Planet → Hybrid Rocky Planet**. The complete shader name is `Major Studio/Planet/Hybrid Rocky Planet`.
6. **Attach the material.** Select `Planet Generator` in the Hierarchy. Find **MeshRenderer → Materials → Element 0**. Drag `M\_MyPlanet.mat` there. **Do not look for a Material Template in PlanetGenerator**; there isn't one.
7. **Set up the normal map.** Select `Assets/PlanetGenerator/Textures/Rock\_Normal.png`. In its Texture Import Settings, set **Texture Type = Normal map**, then **Apply**. Drag this texture into your material's **Rock Normal (Normal Map)** slot.
8. **See the planet.** Keep a **Directional Light** in the scene. If the planet looks too small, change the GameObject's **Transform → Scale** to `(5, 5, 5)` or `(10, 10, 10)`. Leave the material's **Radius** at `1` to begin with.
9. **Generate the shape.** The component normally creates the initial Mesh when added. If you want to generate it again, select the planet and press **1. Rebuild Mesh / 重新生成网格** at the bottom of the PlanetGenerator component.
10. **Adjust its appearance.** Select `M\_MyPlanet.mat` in the Project window and drag the material sliders. You will see changes **immediately**, without pressing Rebuild or Refresh.

### B. Which settings need Rebuild?

|Where to edit|Setting|What to do|
|-|-|-|
|**PlanetGenerator component**|`Seed`|Click **Rebuild Mesh**|
|**PlanetGenerator → Terrain Settings**|`Base Frequency`, `Base Octaves`|Click **Rebuild Mesh**|
|**PlanetGenerator → Terrain Settings**|`Mountain Frequency`, `Mountain Octaves`, `Mask Frequency`, `Mountain Coverage`|Click **Rebuild Mesh**|
|**PlanetGenerator → Terrain Settings**|`Crater Count`, `Crater Min Radius`, `Crater Max Radius`|Click **Rebuild Mesh**|
|**PlanetGenerator component**|`Subdivisions`|Click **Rebuild Mesh**|
|**Material**|`Base / Mountain / Crater Height Strength`|**Updates instantly**|
|**Material**|`Lowland / Midland / Highland / Steep Rock Color` and other color/rock thresholds|**Updates instantly**|
|**Material**|`Rock Normal`, `Normal Tiling`, `Rock Normal Strength`|**Updates instantly**|
|**Material**|`Graphic Mode Blend`, Graphic colors/thresholds/grain|**Updates instantly**|
|**Transform**|Position, Rotation, Scale|**Updates instantly**|

**Simple rule:** Edit the **PlanetGenerator** to change *where* mountains and craters appear; edit the **Material** to change *how the planet looks*, including terrain height strengths. **Rebuild only regenerates Mesh data. It never resets your material.**

### C. Save it for use in the game

1. When the planet's generated **shape** looks right, select the `Planet Generator` GameObject.
2. Click **2. Bake Mesh Asset / 烘焙网格资产** on its `PlanetGenerator` component. Choose an `Assets/` location and save the new `.asset` file.
3. Check that **Baked Mesh** is now assigned on the component and that **MeshFilter** references the baked Mesh. **Save the scene**.
4. If desired, drag the GameObject from the Hierarchy into your Project folder to make a **Prefab**. Make sure the Prefab references the baked Mesh and your `.mat`.
5. You can now use the Prefab normally, including `Instantiate(planetPrefab)` in C#. With a valid baked Mesh, the generator does **not** recompute CPU terrain on startup.
6. Once the Mesh has been baked, you may **remove the PlanetGenerator component** from a runtime-only Prefab. `MeshFilter + MeshRenderer + Material` are enough to display the planet. The Shader still performs the lightweight vertex displacement.

**Important:** Bake saves **precomputed terrain data**, not the final GPU-displaced surface. Material height strengths still work after baking. There is **no physical terrain collider** in this version.

\---

## How to Use — 中文

### A. 从零创建一颗星球

1. **导入资源。** 把整个 `PlanetGenerator` 文件夹放进 Unity 项目的 `Assets/`，保留 `Scripts`、`Editor`、`Shaders`、`Textures` 子目录。本项目需要 **URP**。
2. **创建 GameObject。** 在 **Hierarchy 空白处右键 → 3D Object → Planet Generator**。
3. **选中创建出来的 `Planet Generator`**，Inspector 里应该有 `Transform`、`MeshFilter`、`MeshRenderer` 和 `PlanetGenerator`。
4. **新建材质。** 在 **Project** 窗口右键 → **Create → Material**，命名 `M\_MyPlanet`。
5. **选 Shader。** 点击 `M\_MyPlanet`，将 Shader 设为 **Major Studio → Planet → Hybrid Rocky Planet**。
6. **把材质挂到星球上。** 选中 Hierarchy 里的星球，把 `M\_MyPlanet.mat` 拖进 **MeshRenderer → Materials → Element 0**。不要找 `Material Template`，这个版本已经没有 Template 和临时 Instance 管理。
7. **导入 Normal Map。** 选中 `Assets/PlanetGenerator/Textures/Rock\_Normal.png`，Inspector 中设置 **Texture Type = Normal map**，点击 **Apply**；然后将它拖进 Material 的 **Rock Normal (Normal Map)**。
8. **检查光照和大小。** 场景里放一个 **Directional Light**。球太小就把 GameObject 的 Transform Scale 改成 `(5, 5, 5)` 或 `(10, 10, 10)`。材质的 `Radius` 先保持 `1`。
9. **生成地形。** 组件第一次添加时通常会自动创建 Mesh；需要重新生成时，点击 `PlanetGenerator` 底下的 **1. Rebuild Mesh / 重新生成网格**。
10. **调外观。** 去 Project 中选择 `M\_MyPlanet.mat`，直接修改颜色、Height Strength、Normal 和 Graphic Shading 参数，画面**实时变化**，不需要点 Rebuild / Refresh。

### B. 哪些参数改完要 Rebuild？

|修改位置|参数|是否需要 Rebuild？|
|-|-|-|
|`PlanetGenerator`|`Seed`、`Subdivisions`|**需要**|
|`PlanetGenerator → Terrain Settings`|Base Noise 的 Frequency、Octaves|**需要**|
|`PlanetGenerator → Terrain Settings`|Mountain Frequency、Octaves、Mask Frequency、Coverage|**需要**|
|`PlanetGenerator → Terrain Settings`|Crater Count、Min/Max Radius|**需要**|
|**Material**|Base / Mountain / Crater Height Strength|**不需要，实时变化**|
|**Material**|地表颜色、坡度阈值、Biome Color Warp|**不需要，实时变化**|
|**Material**|Triplanar Normal、Normal Tiling / Strength|**不需要，实时变化**|
|**Material**|Graphic Mode、三色色盘、阈值、Print Grain|**不需要，实时变化**|
|**Transform**|平移、旋转、缩放|**不需要**|

简单记：**Generator 决定“坑和山在哪里”；Material 决定“它们有多高、长什么样”。** Rebuild 只重新生成 Mesh 数据，**不会覆盖 Material**。

### C. Bake 和放进游戏里

1. 在编辑器中调好星球地形后，选择 `Planet Generator`。
2. 点击 **2. Bake Mesh Asset / 烘焙网格资产**，保存为`.asset`。
3. 确认 Inspector 的 **Baked Mesh** 已绑定刚保存的 Mesh，并保存 Scene。
4. 如果要做 Prefab，可以把这个 GameObject 从 Hierarchy 拖到 Project 窗口，确认 Prefab 的 MeshFilter 指向刚保存的 Mesh Asset，MeshRenderer 指向你的 `.mat`。
5. 现在可以在游戏中通过 `Instantiate(prefab)` 创建它。**有 Baked Mesh 时，不会在启动时重新执行 CPU Noise。**
6. 如果游戏里不需要再次换 Seed / 重建形状，甚至可以把 Prefab 上的 `PlanetGenerator` 组件删掉，仅保留 `MeshFilter + MeshRenderer`。视觉形变和 Graphic Shading 仍由 Material Shader 处理。

**注意：** Bake 保存的是 UV2～UV5 中预计算的高度与梯度，不是 GPU 位移后的最终凹凸 Mesh；材质 Strength 仍可动态调整。也没有真正凸凹的物理碰撞体。

\---



## Dev Log

### 1\. 当前结构和职责边界

```text
PlanetGenerator/
├── Scripts/
│   ├── PlanetGenerator.cs       # 生命周期函数、Rebuild、Baked Mesh 引用、UV 数据写入
│   ├── PlanetHeight.cs          # 纯 C# 地形高度函数、陨石坑与球面梯度
│   └── IcosphereBuilder.cs      # 单位 Icosphere 拓扑生成
├── Editor/
│   └── PlanetGeneratorEditor.cs # Hierarchy 创建菜单、参数 Inspector、Rebuild/Bake
├── Shaders/
│   ├── PlanetSurface.shader     # GPU 顶点位移、Height/Slope、Graphic Shading
│   └── TriplanarNormal.hlsl     # 三向 Normal 贴图投影和扰动
├── Textures/
│   └── Rock\_Normal.png
└── README.md
```

**Mesh 和 Material 完全解耦：** `PlanetGenerator` 只更新 `MeshFilter.sharedMesh`，绝不 `Shader.Find`、`new Material` 或修改 `MeshRenderer.sharedMaterial`。材质每颗星球各自创建 `.mat`，编辑 Material 时即时响应。

### 2\. 数据流（保留 Hybrid Geometry）

```text
Rebuild() \[CPU，按需执行]
    ├── IcosphereBuilder.Build(subdivisions)              -> 单位球 Mesh
    ├── PlanetHeight.CreateContext(seed, settings)        -> 噪声偏移、Crater 列表
    └── 对每个球面方向 d：
          Evaluate(d, ctx)                                -> (base, mountain, crater, biomeWarp)
          EvaluateGradients(d, ctx, epsilon, out ...)       -> (gradBase, gradMountain, gradCrater)
                        |
          Mesh.SetUVs(2, heights)                         -> Shader TEXCOORD2 (float4)
          Mesh.SetUVs(3, gradBase)                        -> Shader TEXCOORD3 (float3)
          Mesh.SetUVs(4, gradMountain)                    -> Shader TEXCOORD4 (float3)
          Mesh.SetUVs(5, gradCrater)                      -> Shader TEXCOORD5 (float3)
                        |
Material 每帧实时读取（GPU）
    BuildPlanetGeometry():
      h = base\*BaseStrength + mountain\*MountainStrength + crater\*CraterStrength
      g = gradBase\*BaseStrength + gradMountain\*MountainStrength + gradCrater\*CraterStrength
      P = direction \* Radius \* max(0.1, 1+h)
      N\_geo = normalize(direction \* max(0.1, 1+h) - g)
                        |
    Fragment():
      N\_geo + radialDirection -> Slope -> Rock Mask
      height + biomeWarp      -> Height/Biome Color
      Triplanar Normal        -> N\_detail（只影响光照，不参与地质分类）
      Main Light              -> original diffuse
      Graphic Mode Blend     -> 三档 poster palette + grain 与原光照混合
```

**设计原因：** 复杂的 FBM / Ridged FBM / Crater 是 CPU 一次性运算；Material 改 Height Strength 只做 GPU 顶点乘加，而且法线通过预存的梯度组合恢复。Seed、Frequency、Octaves、Crater 位置/分布等参数会改变函数本身，需要 Rebuild。Geometry Strength 只改变预存通道的权重，不需要重采样。

### 3\. 关键方法的输入 / 输出

|方法|输入|输出 / 副作用|
|-|-|-|
|`IcosphereBuilder.Build(subdivisions)`|细分级数|新的单位球 Mesh（顶点、三角面）|
|`PlanetHeight.CreateContext(seed, settings)`|随机种子、地形参数|带 Noise Offset 和 Crater 列表的 Context|
|`PlanetHeight.Evaluate(d, ctx)`|球面单位方向 d、Context|`Vector4(base, mountain, crater, colorWarp)`|
|`PlanetHeight.EvaluateGradients(d, ctx, epsilon, out ...)`|单位方向、Context、差分步长|三组切向高度梯度（out 参数）|
|`PlanetGenerator.Rebuild()`|当前序列化设置|重新生成临时 Mesh，写 UV2–UV5，替换 MeshFilter，清空 `bakedMesh` 引用|
|`PlanetGenerator.SetSeedAndRebuild(seed)`|新随机种子|更新 seed 并调用 Rebuild；昂贵，不要每帧执行|
|`PlanetGenerator.SetBakedMesh(mesh)`|预先保存的 Mesh Asset|将其存入 `bakedMesh` 并赋给 MeshFilter|
|`BuildPlanetGeometry(attributes)`|Mesh POSITION + UV2–UV5 + Material 强度|位移后的位置、动态几何法线、高度/颜色数据|
|`SamplePlanetTriplanarNormal(...)`|Object Position、Geom Normal、Tiling 等|Object-space 细节法线|
|`ApplyGraphicShading(...)`|底色、几何/细节法线、主光、像素坐标|三档海报式 RGB，不修改 Mesh|

### 4\. Bake / 生命周期 / Prefab

* 首次添加 `PlanetGenerator` 时，`OnEnable()` 缓存 MeshFilter：如果 `bakedMesh` 存在就直接挂 Asset；否则在缺少 Mesh 时生成一次。
* **没有 Bake 的星球进入 Play Mode 会触发一次 Rebuild**（这是当前版本的逻辑）。不会每帧 Rebuild，但实例很多时加载可能卡顿。
* `Bake Mesh Asset` 会克隆当前预计算 Mesh 成 `.asset` 并绑定至 `bakedMesh`。原始临时 Mesh 会由 Generator 回收；**不会重置或修改 `.mat`**。
* Rebuild 会清除 `bakedMesh` 引用，重新按目前 Inspector 数值生成；**原 `.asset` 不会被自动覆盖**。如需保存新形状，重新 Bake 成新 Asset。
* 烘焙后的 Prefab 可以移除 Generator 组件。**不要误以为 Bake 包含了 CPU 可见的地形位移**：Physics / MeshCollider 仍然只看到未形变的单位球，后续要碰撞需单独生成。

### 5\. 游戏中修改每颗星球的参数

组内每个星球有独立 `.mat` 时，编辑器里直接在 Project 修改材质即可。运行时若要动态修改又不想污染 Project 材质资源，可以在实例初始化时复制一份运行时 Material，并由控制脚本持有：

```csharp
using UnityEngine;

public class PlanetAppearanceController : MonoBehaviour
{
    private Material runtimeMat;

    private void Awake()
    {
        // 输入：Renderer 共享材质。输出：仅此实例使用的运行时副本。
        var renderer = GetComponent<MeshRenderer>();
        runtimeMat = new Material(renderer.sharedMaterial);
        renderer.sharedMaterial = runtimeMat;
    }

    public void SetMountainStrength(float value)
    {
        // 只更新 Shader Float，不调用 Rebuild，不改变 Mesh 顶点数据。
        runtimeMat.SetFloat("\_MountainStrength", value);
    }

    private void OnDestroy()
    {
        if (runtimeMat != null) Destroy(runtimeMat);
    }
}
```

这是**使用示例，不是包里另一个自动运行的组件**。如果确实每颗星球单独 `.mat`，也可由 Gameplay 系统直接管理其运行时参数。MPB 也能传值，但需要权衡 URP SRP Batcher 的兼容性。

### 6\. 性能与边界

* Icosphere `Subdivisions=4/5/6` 对应约 `2,562 / 10,242 / 40,962` 顶点。细分级数越高，Rebuild 越慢，Mesh 显存越大。
* CPU `EvaluateGradients` 做 4 次偏移采样 + 1 次原值采样，包含 FBM、Mountain 和 Crater。只在 Rebuild 运行。
* GPU Vertex Shader 每次绘制仅组合已存高度和梯度；`ShadowCaster` 也做相同位移。通过 Material Strength 改形变，不重算 CPU FBM。
* Fragment 的 Triplanar Normal 使用同一张 Normal Map 三方向采样（通常每像素三次）；Graphic Shading 额外主要是 ALU 和无贴图 Hash，不增加新的纹理采样或 Render Pass。
* 无 LOD / Ocean / Atmosphere / Collider，也没有额外的 DepthOnly/DepthNormals Pass；需要 SSAO、复杂深度预通道或物理交互时应另行扩展。
* `Bounds Radius` 是 GPU 形变的剔除安全范围；若视觉地形半径远超 Bounds，可能会被错误剔除。通常保持 Shader Radius=1、通过 Transform 缩放较省心。

