using UnityEngine;

/// <summary>
/// First-person zero-gravity flight with WASD, mouse look, Space/Ctrl and Shift.
/// 挂 Player，不是 ExploreCamera。ExploreCamera 是 Player 子物体。
/// 鼠标 X 旋转 Player 自己的 Y，鼠标 Y 只旋转 ExploreCamera.localRotation 的 X。
/// 数据流：SpaceGameInput -&gt; Update -&gt; CharacterController.Move / 两个 Transform 的旋转。
/// GameModeManager.SetOverlayInputBlocked 和 Landing/Leaving 会调用 SetControlsEnabled。
/// 为未来 Explore Block 保留 SetTravelBlocked 和 TeleportToSafeAnchor 两个对外接口。
/// 本周没有边界判定器，所以正常运行时这两个接口不会被调用。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class SpaceFlightController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform exploreCameraTransform;
    [Header("Movement (no gravity)")]
    [SerializeField, Min(0f)] private float moveSpeed = 8f;
    [SerializeField, Min(1f)] private float shiftMultiplier = 2.5f;
    [Header("Mouse Look")]
    [SerializeField, Min(0f)] private float lookSensitivity = 0.12f;
    [SerializeField, Range(10f, 89.9f)] private float maxPitch = 85f;

    private CharacterController characterController;
    private float pitch;
    private bool controlsEnabled = true;
    private bool travelBlocked;

    /// <summary>
    /// Resolve references and starting camera pitch.
    /// 如果 Camera 引用留空，尝试从 Player 子物体里找；仍建议 Inspector 手动拖。
    /// </summary>
    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        if (exploreCameraTransform == null)
        {
            Camera child = GetComponentInChildren<Camera>(true);
            if (child != null) exploreCameraTransform = child.transform;
        }
        if (exploreCameraTransform != null)
            pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, exploreCameraTransform.localEulerAngles.x), -maxPitch, maxPitch);
    }

    /// <summary>
    /// Update movement and view only while Explore input is enabled.
    /// 先旋转，再按当前视角前后左右移动。没有重力，所以前进方向可以带上下分量。
    /// 若 Mouse X 正常但 Player 不转，重点检查 Explore Camera Transform 是否误拖了 Player 本身：
    /// 下面最后一句写 Camera.localRotation，如果引用为 Player，会覆盖前面的水平旋转。
    /// </summary>
    private void Update()
    {
        if (!controlsEnabled || travelBlocked || exploreCameraTransform == null) return;
        Vector2 mouse = SpaceGameInput.LookDelta;
        float yaw = transform.eulerAngles.y + mouse.x * lookSensitivity;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        pitch = Mathf.Clamp(pitch - mouse.y * lookSensitivity, -maxPitch, maxPitch);
        exploreCameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        Vector2 move = SpaceGameInput.Move;
        float vertical = SpaceGameInput.VerticalMove;
        Vector3 direction = exploreCameraTransform.forward * move.y +
                            exploreCameraTransform.right * move.x + Vector3.up * vertical;
        direction = Vector3.ClampMagnitude(direction, 1f);
        float speed = moveSpeed * (SpaceGameInput.BoostHeld ? shiftMultiplier : 1f);
        characterController.Move(direction * speed * Time.deltaTime);
    }

    /// <summary>
    /// External input enable/disable API.
    /// 外部只通过这个接口控制飞行，不直接禁用脚本或者 Player GameObject。
    /// 开启时锁定鼠标用于 FPS 转头；关闭时释放鼠标供 U 面板按钮或 Farming 使用。
    /// </summary>
    /// <param name="enabled">true 开启当前输入控制；false 关闭。</param>
    public void SetControlsEnabled(bool enabled)
    {
        controlsEnabled = enabled;
        ApplyCursorState();
    }

    /// <summary>
    /// Future Travel Block hook. Freeze or resume flight without changing game mode.
    /// 预留给未来 Explore Block。在玩家越界时传 true，禁止移动和鼠标转头；
    /// 解除限制时传 false。与 U 面板的 controlsEnabled 分开保存，避免关闭 U 面板误解锁越界限制。
    /// 本周暂时没有 TravelBoundary 会调用它。
    /// </summary>
    /// <param name="blocked">true 表示被未来的旅行边界阻挡；false 解除阻挡。</param>
    public void SetTravelBlocked(bool blocked)
    {
        travelBlocked = blocked;
        ApplyCursorState();
    }

    /// <summary>
    /// Future Travel Block hook. Teleport the CharacterController to a safe anchor.
    /// 预留给未来 Explore Block。它会先临时禁用 CharacterController，再把 Player 放到安全锚点，
    /// 最后恢复组件状态，避免用 Move() 做强制传送时被碰撞系统挡住。
    /// 只改变 Player 的世界位置/水平朝向，不修改相机局部 pitch，后面边界系统可自行决定复位视角。
    /// </summary>
    /// <param name="safeAnchor">安全返回点的 Transform，包含目标世界位置和旋转。</param>
    public void TeleportToSafeAnchor(Transform safeAnchor)
    {
        if (safeAnchor == null) return;
        bool wasEnabled = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;
        transform.SetPositionAndRotation(safeAnchor.position, safeAnchor.rotation);
        if (characterController != null) characterController.enabled = wasEnabled;
    }

    /// <summary>
    /// Restore the cursor behavior after the application regains focus.
    /// Unity 应用获得焦点时重新应用鼠标锁定状态；失焦时解锁并显示鼠标，防止在编辑器切窗口后鼠标不可用。
    /// </summary>
    /// <param name="focused">Unity 应用窗口是否获得输入焦点。</param>
    private void OnApplicationFocus(bool focused)
    {
        if (focused) ApplyCursorState();
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    /// <summary>
    /// Apply look-cursor lock according to gameplay input state.
    /// 根据 controlsEnabled 和 travelBlocked 一起判断是否锁定光标。U 面板和未来的探索边界分别维护状态，不能只检查其中一个。
    /// </summary>
    private void ApplyCursorState()
    {
        bool shouldLock = controlsEnabled && !travelBlocked;
        Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !shouldLock;
    }

    /// <summary>
    /// Leave the cursor usable when this component is disabled.
    /// 禁用飞行组件时释放鼠标锁定，防止停止 Play 或切换组件后光标留在隐藏状态。
    /// </summary>
    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
