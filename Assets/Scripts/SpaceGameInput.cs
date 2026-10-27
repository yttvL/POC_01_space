using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Central keyboard and mouse input adapter. No game rules here.
/// 所有游戏脚本通过这里读按键，不要到各自的脚本里再重复判断新旧输入系统。
/// 数据流：键鼠 -&gt; SpaceGameInput -&gt; Flight / PlanetSpin / Interaction / GameMode / UpgradeTreeUI。
/// 以后加手柄或改 Input Actions 时，尽量保持下面属性名称不变，其他系统就不用一起改。
/// ENABLE_INPUT_SYSTEM 优先；项目只启用旧输入时走 ENABLE_LEGACY_INPUT_MANAGER。
/// </summary>
public static class SpaceGameInput
{
    /// <summary>
    /// Read normalized WASD movement input.
    /// 返回 X 左右、Y 前后的归一化输入方向；只提供方向，实际飞行位移由 SpaceFlightController.Update 计算。
    /// </summary>
    public static Vector2 Move
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#else
            return Vector2.zero;
#endif
        }
    }

    /// <summary>
    /// Read Space/Ctrl vertical flight input.
    /// Space 返回 +1，Ctrl 返回 -1；只提供上下方向，不加入重力或碰撞处理。
    /// </summary>
    public static float VerticalMove
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k == null) return 0f;
            return (k.spaceKey.isPressed ? 1f : 0f) -
                   (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed ? 1f : 0f);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return (Input.GetKey(KeyCode.Space) ? 1f : 0f) -
                   (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ? 1f : 0f);
#else
            return 0f;
#endif
        }
    }

    /// <summary>
    /// Read whether Shift is held.
    /// Shift 按住时返回 true，飞行脚本自己乘 Inspector 中的加速倍率。
    /// </summary>
    public static bool BoostHeld
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Read per-frame mouse movement.
    /// 返回鼠标 X/Y 的每帧位移而非绝对坐标。飞行脚本使用它旋转 Player 和 Camera；不要额外乘 deltaTime。
    /// </summary>
    public static Vector2 LookDelta
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 12f;
#else
            return Vector2.zero;
#endif
        }
    }

    /// <summary>
    /// Read the E key pressed this frame.
    /// Explore 模式由 PlanetInteractionDetector 使用 E 发起着陆；Farming 模式由 GameModeManager 使用 E 退出。
    /// </summary>
    public static bool InteractDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.E);
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Read the U key pressed this frame.
    /// 全局 UpgradeTreeUI 使用 U 开关科技树 Overlay，是否允许打开由 UI 和 GameMode 一起判断。
    /// </summary>
    public static bool UpgradeToggleDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.uKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.U);
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Read left mouse press this frame.
    /// 星球输入脚本在左键按下时记住指针起点，开始一次 Click / Drag 手势。
    /// </summary>
    public static bool LeftMouseDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(0);
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Read whether the left mouse button is held.
    /// 拖动期间持续返回 true；只有鼠标松开且没有超过拖拽阈值才会尝试采集。
    /// </summary>
    public static bool LeftMouseHeld
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Read left mouse release this frame.
    /// 星球输入脚本在松开那一帧结束手势，并根据是否拖动决定要不要 Raycast 采矿。
    /// </summary>
    public static bool LeftMouseUp
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(0);
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Read the mouse position in screen pixels.
    /// 提供屏幕像素坐标，供 PlanetSpinController 比较按下和当前坐标，判断是否超过 Drag Threshold。
    /// </summary>
    public static Vector2 MousePosition
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }
    }
}
