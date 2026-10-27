using UnityEngine;

/// <summary>
/// Per-planet identity, camera setup, landing range and unlock query.
/// 挂不旋转的 Planet Root；Mesh Instance 仍单独放在 Root 子物体。
/// 这个脚本不负责生产、生成节点或按键，它只把该星球的配置和查询接口给外部用。
/// 新增 ProgressionState + Required Ship Level，不要求隐藏锁住的星球。
/// Planet 01 Required=0，Planet 02 Required=1。未来 Explore Block 也调用 IsUnlocked。
/// </summary>
public class PlanetController : MonoBehaviour
{
    [Header("Identity")]
    // 用于识别星球的稳定 ID；未来存档和配置不要只靠 GameObject 名字。
    [SerializeField] private string planetId = "planet_01";
    // 接近星球时 InteractionPrompt 的第一行文本。
    [SerializeField] private string displayName = "PLANET 01";

    [Header("Farming view")]
    // Root 下不自转的空物体，用于切到 FarmingCamera 的预设视角。
    [SerializeField] private Transform farmingCameraAnchor;
    [SerializeField, Min(0.1f)] private float farmingOrthoSize = 5f;
    [SerializeField] private PlanetSpinController spinController;

    [Header("Landing proximity")]
    // 拖现有 Baked Mesh 的 Renderer，用世界 Bounds 估算中心与半径。
    [SerializeField] private Renderer planetRenderer;
    [SerializeField, Min(0f)] private float landingRangeFromSurface = 5f;
    [SerializeField, Min(0f)] private float radiusOverride = 0f;

    [Header("Progression (future Explore Block interface)")]
    // 拖 GameSystems 中的全局 ProgressionState，检查解锁所需等级。
    [SerializeField] private ProgressionState progression;
    // Planet 01 设 0、Planet 02 设 1；仅控制着陆与资源生成，不产生空气墙。
    [SerializeField, Min(0)] private int requiredShipLevel = 0;

    public string PlanetId => planetId;
    public string DisplayName => displayName;
    public Transform FarmingCameraAnchor => farmingCameraAnchor;
    public float FarmingOrthoSize => farmingOrthoSize;
    public PlanetSpinController SpinController => spinController;
    public float LandingRangeFromSurface => landingRangeFromSurface;
    public ProgressionState Progression => progression;
    public int RequiredShipLevel => requiredShipLevel;

    /// <summary>
    /// Unlock check shared by landing, spawning and future travel boundaries.
    /// 这是权限接口，不负责实际阻挡 Player 移动。本周 Explorer Block 尚未实现。
    /// required=0 时默认允许；其他等级必须已连接 ProgressionState 且等级足够。
    /// </summary>
    public bool IsUnlocked => requiredShipLevel == 0 ||
                              (progression != null && progression.CanTravelToLevel(requiredShipLevel));

    /// <summary>
    /// Approximate planet center based on renderer bounds.
    /// </summary>
    public Vector3 Center => planetRenderer != null ? planetRenderer.bounds.center : transform.position;

    // Approximate world-space radius for proximity checks.
    // 用 Renderer.bounds 的最大半轴估计半径；不规则形状的表面距离不是精确值。
    public float ApproximateWorldRadius
    {
        get
        {
            if (radiusOverride > 0f) return radiusOverride;
            if (planetRenderer == null) return 1f;
            Vector3 e = planetRenderer.bounds.extents;
            return Mathf.Max(e.x, e.y, e.z);
        }
    }

    /// <summary>
    /// Check proximity to the approximate planet surface.
    /// 交互检测器每帧调用。只回答距离，解锁资格在 IsUnlocked 单独判断。
    /// </summary>
    /// <param name="worldPosition">要判断的世界空间位置，通常是 Player.position。</param>
    /// <returns>true 表示玩家进入该星球预设的近似地表交互距离。</returns>
    public bool IsInLandingRange(Vector3 worldPosition)
    {
        float distanceOutsideSurface = Vector3.Distance(worldPosition, Center) - ApproximateWorldRadius;
        return distanceOutsideSurface <= landingRangeFromSurface;
    }

    /// <summary>
    /// Compare nearby planets by estimated distance to surface.
    /// 返回玩家到近似星球表面的非负距离，供 Detector 在多个可交互星球之间选择最近目标。
    /// </summary>
    /// <param name="worldPosition">要判断的世界空间位置，通常是 Player.position。</param>
    /// <returns>玩家到估算地表的距离；若已进入半径内部则返回零。</returns>
    public float DistanceFromSurface(Vector3 worldPosition)
    {
        return Mathf.Max(0f, Vector3.Distance(worldPosition, Center) - ApproximateWorldRadius);
    }

    /// <summary>
    /// True if farming is configured and this planet is unlocked.
    /// 以前这个属性只检查 Inspector；现在再检查 IsUnlocked，防止直接调用 TryLand 绕过进度。
    /// </summary>
    public bool CanEnterFarming => farmingCameraAnchor != null && spinController != null && IsUnlocked;

    /// <summary>
    /// Try to populate convenient references on component reset.
    /// 在 Unity 添加组件或点击 Reset 时尝试自动寻找同物体的 SpinController 和子物体 MeshRenderer。运行时不能依赖它代替 Inspector 配置。
    /// </summary>
    private void Reset()
    {
        spinController = GetComponent<PlanetSpinController>();
        planetRenderer = GetComponentInChildren<Renderer>(true);
    }
}
