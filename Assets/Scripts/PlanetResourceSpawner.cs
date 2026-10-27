using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns collectable resource nodes on the baked mesh of one unlocked planet.
/// 每颗星球 Root 挂一份，彼此独立的上限和计时，不受当前相机正在看哪颗星球影响。
/// PlanetController.IsUnlocked=false 时一个资源点都不生成；解锁瞬间生成 Initial Nodes。
/// 数量未满时每隔 Spawn Interval 补 1 个，达到上限就停止并清零计时。
/// 从满额被采集到不足上限后，必须重新等待一个完整的 Spawn Interval 才补货。
/// Surface Mesh Filter 指向旋转的 Baked Mesh；Node Parent 必须放在旋转 Mesh 的层级下面。
/// Baked Mesh 必须允许 CPU 读取（mesh.isReadable），否则无法做三角形采样。
/// 本脚本不会控制星球旋转，也不直接处理鼠标；节点采集由 PlanetSpinController -&gt; ResourceNode。
/// </summary>
public class PlanetResourceSpawner : MonoBehaviour
{
    [Header("References")]
    // 本 Spawner 所属星球，用 IsUnlocked 判断何时生成。
    [SerializeField] private PlanetController planet;
    // 拖现有 Baked Mesh 的 MeshFilter；运行时须允许读取顶点和三角形。
    [SerializeField] private MeshFilter surfaceMeshFilter;
    // 拖旋转 Mesh 下的 ResourceNodes，生成节点就能跟着星球转。
    [SerializeField] private Transform nodeParent;
    // 带 ResourceNode 和 Collider 的资源点 Prefab。
    [SerializeField] private ResourceNode nodePrefab;
    // 本周拖 Wood.asset；同一脚本以后可生成 Iron、Crystal 等。
    [SerializeField] private ResourceDefinition resourceType;
    // 采矿奖励进入全局库存，Spawner 自身不存玩家资源数量。
    [SerializeField] private ResourceInventory inventory;
    // 点击采矿时由 ResourceNode 查询 Mining Yield 的当前倍率。
    [SerializeField] private UpgradeManager upgrades;

    [Header("Spawning")]
    // 星球刚解锁时立即生成的数量，不会超过 Max Nodes。
    [SerializeField, Min(0)] private int initialNodes = 3;
    // 这颗星球同时存在的资源点上限，与其他星球独立。
    [SerializeField, Min(1)] private int maxNodes = 6;
    // 低于上限时每隔多久补充一个节点，满额时计时重置。
    [SerializeField, Min(0.05f)] private float spawnIntervalSeconds = 5f;
    // 升级前每个节点可采集的基础数量。
    [SerializeField, Min(1)] private int baseResourcePerNode = 1;
    // 沿采样地表的法线外移一定距离，避免资源模型陷进星球。
    [SerializeField, Min(0f)] private float surfaceOffset = 0.25f;
    // 两个资源点在世界空间中的最小间隔，避免重叠。
    [SerializeField, Min(0f)] private float minimumWorldSpacing = 0.5f;
    // 每次生成最多尝试多少个候选点，次数用完会跳过本次补充。
    [SerializeField, Range(1, 60)] private int maxPlacementAttempts = 24;

    private readonly List<ResourceNode> activeNodes = new List<ResourceNode>();
    private Vector3[] vertices;
    private int[] triangles;
    private float[] cumulativeAreas;
    private float totalArea;
    private float timer;
    private bool hasStarted;
    private bool wasUnlocked;
    private bool sampleReady;

    public int ActiveNodeCount => activeNodes.Count;
    public int MaxNodeCount => maxNodes;

    /// <summary>
    /// Resolve local planet reference; no node generation before Start.
    /// </summary>
    private void Awake()
    {
        if (planet == null) planet = GetComponent<PlanetController>();
    }

    /// <summary>
    /// Subscribe to the global ship-level event.
    /// 只监听自己所属 Planet 的 ProgressionState；升级飞船后这里会立即收到事件。
    /// </summary>
    private void OnEnable()
    {
        if (planet != null && planet.Progression != null)
            planet.Progression.ShipLevelChanged += OnShipLevelChanged;
        if (hasStarted && sampleReady) SyncUnlockState();
    }

    /// <summary>
    /// Unsubscribe when the component is disabled.
    /// 解除 ShipLevelChanged 事件订阅，避免禁用组件后继续接收升级通知或重复订阅。
    /// </summary>
    private void OnDisable()
    {
        if (planet != null && planet.Progression != null)
            planet.Progression.ShipLevelChanged -= OnShipLevelChanged;
    }

    /// <summary>
    /// Cache readable mesh triangles, then initialize nodes if unlocked.
    /// 初始资源生成放在 Start，保证其他 Awake（例如 ProgressionState）已初始化完等级。
    /// </summary>
    private void Start()
    {
        sampleReady = BuildSurfaceCache();
        hasStarted = true;
        if (sampleReady) SyncUnlockState();
    }

    /// <summary>
    /// Spawn one node per interval, capped per planet.
    /// 即使 Player 在 Explore 或另一颗星球 Farming，这个 Update 仍照常运行。
    /// 不足上限才开始累计 timer；达到上限清零，避免采掉一个后瞬间补齐。
    /// </summary>
    private void Update()
    {
        if (!hasStarted || !sampleReady || !wasUnlocked) return;
        if (activeNodes.Count >= maxNodes)
        {
            timer = 0f;
            return;
        }
        timer += Time.deltaTime;
        if (timer < spawnIntervalSeconds) return;
        timer = 0f;
        TrySpawnNode();
    }

    /// <summary>
    /// Called after spaceship progression changes.
    /// 响应 ShipLevelChanged 事件后重新查询星球是否解锁，而不是根据数字硬编码 Planet 02。
    /// 由 ProgressionState.ShipLevelChanged 调用。收到等级变化后重新检查自己的 Planet 是否已解锁，不在这里写死 Planet 02 的编号。
    /// </summary>
    /// <param name="newShipLevel">ProgressionState 发出的新飞船等级；本方法通过 PlanetController 再次确认解锁状态。</param>
    private void OnShipLevelChanged(int newShipLevel)
    {
        if (hasStarted && sampleReady) SyncUnlockState();
    }

    /// <summary>
    /// Start the spawner when unlocked; clear nodes if it becomes locked.
    /// 由 Start 和等级变化事件调用。Locked -&gt; Unlocked 才生成 Initial Nodes，
    /// 不会在每一帧重复创建初始资源，也不会因为玩家反复进出 Farming 而重新补满。
    /// </summary>
    private void SyncUnlockState()
    {
        bool unlocked = planet != null && planet.IsUnlocked;
        if (unlocked == wasUnlocked) return;
        wasUnlocked = unlocked;
        timer = 0f;
        if (!unlocked)
        {
            for (int i = 0; i < activeNodes.Count; i++)
                if (activeNodes[i] != null) Destroy(activeNodes[i].gameObject);
            activeNodes.Clear();
            return;
        }
        int count = Mathf.Min(initialNodes, maxNodes);
        for (int i = 0; i < count; i++)
            if (!TrySpawnNode()) break;
    }

    /// <summary>
    /// Read baked mesh once and build an area-weighted triangle lookup.
    /// 不能直接随机选 Triangle，否则地形细分密的地方节点会过密。
    /// 先计算每个 Triangle 的世界空间面积，生成累计面积表，后面生成时用二分查找选三角形。
    /// 默认不对 Mesh 顶点做任何修改，也不调用 PlanetGenerator。
    /// 如果 Baked Mesh 运行时不可读，需要打开 Read/Write 或重新 Bake 一个可读 Mesh。
    /// </summary>
    /// <returns>true 表示已成功建立可用的地表面积采样缓存。</returns>
    private bool BuildSurfaceCache()
    {
        if (planet == null || surfaceMeshFilter == null || surfaceMeshFilter.sharedMesh == null ||
            nodePrefab == null || resourceType == null || inventory == null)
        {
            Debug.LogError($"{name}: Spawner references incomplete (planet/mesh/prefab/resource/inventory).", this);
            return false;
        }
        if (nodeParent == null) nodeParent = surfaceMeshFilter.transform;
        if (nodeParent != surfaceMeshFilter.transform && !nodeParent.IsChildOf(surfaceMeshFilter.transform))
        {
            Debug.LogError($"{name}: Node Parent must be below the rotating mesh Transform.", this);
            return false;
        }
        Mesh mesh = surfaceMeshFilter.sharedMesh;
        if (!mesh.isReadable)
        {
            Debug.LogError($"{name}: Baked Mesh is not readable. Enable Read/Write on the mesh asset.", this);
            return false;
        }
        vertices = mesh.vertices;
        triangles = mesh.triangles;
        int triCount = triangles.Length / 3;
        if (triCount == 0) return false;

        cumulativeAreas = new float[triCount];
        totalArea = 0f;
        Transform meshTransform = surfaceMeshFilter.transform;
        for (int i = 0; i < triCount; i++)
        {
            int index = i * 3;
            Vector3 a = meshTransform.TransformPoint(vertices[triangles[index]]);
            Vector3 b = meshTransform.TransformPoint(vertices[triangles[index + 1]]);
            Vector3 c = meshTransform.TransformPoint(vertices[triangles[index + 2]]);
            totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            cumulativeAreas[i] = totalArea;
        }
        if (totalArea <= 0f)
        {
            Debug.LogError($"{name}: Surface mesh has zero usable area.", this);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Choose a random area-weighted surface point and instantiate one prefab.
    /// 随机选 Triangle -&gt; sqrt(u) 重心坐标采样 -&gt; 算世界位置和法线 -&gt; 沿法线外移 Offset。
    /// 如果设置了 Minimum World Spacing，会最多尝试 maxPlacementAttempts 次避免节点彼此太近。
    /// 生成的对象必须挂在旋转 Mesh 下面，才能和星球同步自转及拖拽。
    /// </summary>
    /// <returns>true 表示成功生成一个新节点，false 表示达到上限或本次找不到有效位置。</returns>
    private bool TrySpawnNode()
    {
        if (activeNodes.Count >= maxNodes || !sampleReady || !wasUnlocked) return false;
        Transform meshTransform = surfaceMeshFilter.transform;
        for (int attempt = 0; attempt < maxPlacementAttempts; attempt++)
        {
            float pickedArea = Random.value * totalArea;
            int triangleIndex = System.Array.BinarySearch(cumulativeAreas, pickedArea);
            if (triangleIndex < 0) triangleIndex = ~triangleIndex;
            triangleIndex = Mathf.Clamp(triangleIndex, 0, cumulativeAreas.Length - 1);
            int start = triangleIndex * 3;

            Vector3 a = meshTransform.TransformPoint(vertices[triangles[start]]);
            Vector3 b = meshTransform.TransformPoint(vertices[triangles[start + 1]]);
            Vector3 c = meshTransform.TransformPoint(vertices[triangles[start + 2]]);
            float sqrtU = Mathf.Sqrt(Random.value);
            float v = Random.value;
            Vector3 worldPoint = (1f - sqrtU) * a + sqrtU * (1f - v) * b + sqrtU * v * c;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.sqrMagnitude < 0.5f) continue;
            Vector3 finalPosition = worldPoint + normal * surfaceOffset;

            bool tooClose = false;
            foreach (ResourceNode node in activeNodes)
            {
                if (node != null && Vector3.Distance(node.transform.position, finalPosition) < minimumWorldSpacing)
                {
                    tooClose = true;
                    break;
                }
            }
            if (tooClose) continue;

            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);
            ResourceNode created = Instantiate(nodePrefab, finalPosition, rotation, nodeParent);
            created.Initialize(resourceType, baseResourcePerNode, inventory, upgrades, this);
            activeNodes.Add(created);
            if (activeNodes.Count >= maxNodes) timer = 0f;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Remove the mined node from this planet's active count.
    /// 由 ResourceNode.TryMine 调用，只更新数量，不负责发放 Wood。
    /// 如果采集之前是满额状态，现在恢复成不足上限，重置计时为完整一个 Spawn Interval。
    /// </summary>
    /// <param name="node">刚被采集的 ResourceNode 实例，供 Spawner 从活动列表中移除。</param>
    public void NotifyNodeMined(ResourceNode node)
    {
        bool wasFull = activeNodes.Count >= maxNodes;
        if (!activeNodes.Remove(node)) return;
        if (wasFull) timer = 0f;
    }
}
