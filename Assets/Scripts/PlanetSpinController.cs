using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Auto-rotates the planet and handles Farming drag vs click-to-mine input.
/// 挂在静态 Planet Root，Spin Pivot 指向带 MeshFilter 的 Planet Mesh Instance。
/// Explore 和 Farming 都持续世界 Y 轴自转；当前星球处于 Farming 且按住鼠标时暂停自转。
/// 按下后累计移动距离 &gt;= Drag Threshold 才算拖拽；松开时若没拖拽则执行一次采集 Raycast。
/// 本脚本是 Farming 鼠标手势的唯一读取者，不要再另写一个点击脚本与它抢左键。
/// U Panel 打开时，GameModeManager 调用 SetInputSuspended(true)，暂停采集和拖拽，自转仍照常进行。
/// </summary>
public class PlanetSpinController : MonoBehaviour
{
    [Header("Rotating mesh + resource nodes")]
    // 拖真正带 MeshFilter 的 Planet Mesh Instance；不要拖不旋转的 Root 或 Camera Anchor。
    [SerializeField] private Transform spinPivot;
    [Header("Auto rotation (all modes)")]
    // 星球绕世界 Y 轴自转的角速度，Explore 和 Farming 都生效。
    [SerializeField, Min(0f)] private float autoSpinDegreesPerSecond = 4f;
    [Header("Farming mouse input")]
    // Farming 模式下鼠标拖动每像素对应的旋转角度。
    [SerializeField, Min(0f)] private float dragDegreesPerPixel = 0.25f;
    // 超过这个位移才认定 Drag；松开时未达到则视为采集 Click。
    [SerializeField, Min(0f)] private float dragThresholdPixels = 7f;
    // 正交相机的采集射线最大检测距离。
    [SerializeField, Min(0.1f)] private float miningRayDistance = 500f;
    // 射线检测的 LayerMask，需要覆盖 Node Collider 和用于遮挡背面的 Planet Collider。
    [SerializeField] private LayerMask miningRayMask = ~0;

    private bool manualControlEnabled;
    private bool inputSuspended;
    private bool pointerDown;
    private bool wasDragging;
    private Camera farmingCamera;
    private Vector2 pointerStart;
    private Vector2 previousMousePosition;

    /// <summary>
    /// Warn if the mesh Transform is not linked.
    /// 检查 Inspector 是否已给 Spin Pivot 指定真正旋转的 Mesh Instance Transform；未连接会报错，不尝试移动 Mesh 组件。
    /// </summary>
    private void Awake()
    {
        if (spinPivot == null) Debug.LogError($"{name}: Assign Spin Pivot to PlanetSpinController.", this);
    }

    /// <summary>
    /// Enable drag/click controls for the currently farmed planet only.
    /// Manager 进入 Farming 时传 true 和 FarmingCamera；离开时传 false/null。
    /// 其他星球仍自动自转，但不读取鼠标，也不能被点击采集。
    /// </summary>
    /// <param name="enabled">true 开启当前输入控制；false 关闭。</param>
    /// <param name="viewCamera">当前 Farming 使用的 Camera，射线检测和拖拽坐标轴均依赖它。</param>
    public void SetManualControl(bool enabled, Camera viewCamera)
    {
        manualControlEnabled = enabled;
        farmingCamera = enabled ? viewCamera : null;
        ResetPointer();
    }

    /// <summary>
    /// Temporarily block drag/click while a global modal panel is open.
    /// U Panel 期间只禁止鼠标操作，不关闭自转 Update，也不改变手动控制权归属。
    /// </summary>
    /// <param name="suspended">true 暂停鼠标拖拽与采集，false 恢复。</param>
    public void SetInputSuspended(bool suspended)
    {
        inputSuspended = suspended;
        if (suspended) ResetPointer();
    }

    /// <summary>
    /// Process mouse gestures and continuous auto rotation.
    /// 鼠标按下若不在 UI 上，记录位置；只有跨过阈值才开始根据相机轴转动。
    /// 鼠标松开时如果没拖拽，拿 FarmingCamera 生成一条 Ray，检测最近的 Collider 是否 ResourceNode。
    /// 自转暂停条件是当前正在按住有效鼠标手势，不只是已经超过拖拽阈值。
    /// </summary>
    private void Update()
    {
        if (spinPivot == null) return;
        if (manualControlEnabled && !inputSuspended && farmingCamera != null)
            ProcessFarmingMouse();
        if (!pointerDown)
            spinPivot.Rotate(Vector3.up, autoSpinDegreesPerSecond * Time.deltaTime, Space.World);
    }

    /// <summary>
    /// Convert a left mouse gesture into either drag rotation or mining click.
    /// 按下时如果命中 UI 就放弃这次手势；即使从 UI 滑出也不能突然开始旋转。
    /// 拖拽角度由鼠标像素距离和 Drag Degrees Per Pixel 决定，轴使用正交相机的 up/right。
    /// </summary>
    private void ProcessFarmingMouse()
    {
        if (SpaceGameInput.LeftMouseDown && !IsPointerOverUI())
        {
            pointerDown = true;
            wasDragging = false;
            pointerStart = SpaceGameInput.MousePosition;
            previousMousePosition = pointerStart;
        }
        if (!pointerDown) return;

        Vector2 current = SpaceGameInput.MousePosition;
        if (SpaceGameInput.LeftMouseHeld)
        {
            if ((current - pointerStart).sqrMagnitude >= dragThresholdPixels * dragThresholdPixels)
                wasDragging = true;
            if (wasDragging)
            {
                Vector2 delta = current - previousMousePosition;
                Quaternion yaw = Quaternion.AngleAxis(-delta.x * dragDegreesPerPixel, farmingCamera.transform.up);
                Quaternion tilt = Quaternion.AngleAxis(delta.y * dragDegreesPerPixel, farmingCamera.transform.right);
                spinPivot.rotation = yaw * tilt * spinPivot.rotation;
            }
            previousMousePosition = current;
        }

        if (SpaceGameInput.LeftMouseUp)
        {
            if (!wasDragging && !IsPointerOverUI()) TryMineAtMousePosition(current);
            ResetPointer();
        }
    }

    /// <summary>
    /// Cast one ray and mine only the nearest collider if it is a resource node.
    /// 通过最近 Collider 限制背面穿透；Planet Mesh 上最好加 MeshCollider，
    /// 这样鼠标点在星球表面但背面有 Node 时，不会穿透星球采到背面的资源。
    /// ResourceNode.TryMine 才负责计算 Wood 数量、通知 Spawner 和销毁 Node。
    /// </summary>
    /// <param name="screenPosition">鼠标在屏幕上的像素坐标，用于 Farming Camera 创建 Ray。</param>
    private void TryMineAtMousePosition(Vector2 screenPosition)
    {
        Ray ray = farmingCamera.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, miningRayDistance, miningRayMask, QueryTriggerInteraction.Collide))
            return;
        ResourceNode node = hit.collider.GetComponentInParent<ResourceNode>();
        if (node != null) node.TryMine();
    }

    /// <summary>
    /// Check if the pointer is over a Unity UI element.
    /// 通过 EventSystem 检查鼠标是否正落在 UI 上，避免点击按钮时触发星球旋转或采矿。场景应存在 EventSystem。
    /// </summary>
    /// <returns>true 表示当前鼠标正位于 Unity UI 上。</returns>
    private static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>
    /// Clear all temporary gesture state.
    /// 清空本次鼠标手势的临时标记，在鼠标松开、退出 Farming 或 U 面板打开时调用。
    /// </summary>
    private void ResetPointer()
    {
        pointerDown = false;
        wasDragging = false;
    }

    /// <summary>
    /// Remove camera reference when the component is disabled.
    /// 组件停用时清除 Camera 引用、输入许可和正在拖拽的状态，避免下次启用沿用上次手势。
    /// </summary>
    private void OnDisable()
    {
        manualControlEnabled = false;
        inputSuspended = false;
        farmingCamera = null;
        ResetPointer();
    }
}
