using System.Collections;
using UnityEngine;

/// <summary>
/// Owns Explore/Landing/Farming/Leaving states, the camera switch and modal input blocking.
/// 保留上一版双 Camera + 不透明 Landing/Leaving Panel 的流程；不加载新 Scene、不做 Lerp。
/// U Panel 不是新 GameMode，而是叠在 Explore 或 Farming 上的一个全局 Overlay。
/// UpgradeTreeUI 调用 SetUpgradeOverlayOpen(true/false) 后，本脚本统一停用或恢复相应的玩家输入。
/// Explore Block 这里暂时不实现，不加 Border、传送或 Warning Panel；只在 TryLand 检查星球 IsUnlocked。
/// </summary>
public class GameModeManager : MonoBehaviour
{
    public enum GameMode { Explore, Landing, Farming, Leaving }

    [Header("Player / Cameras")]
    [SerializeField] private SpaceFlightController playerFlight;
    [SerializeField] private Camera exploreCamera;
    [SerializeField] private Camera farmingCamera;
    [Header("Fullscreen transition panels")]
    [SerializeField] private GameObject landingPanel;
    [SerializeField] private GameObject leavingPanel;
    [SerializeField, Min(0f)] private float landingPanelSeconds = 0.85f;
    [SerializeField, Min(0f)] private float leavingPanelSeconds = 0.7f;

    public GameMode CurrentMode { get; private set; } = GameMode.Explore;
    public PlanetController CurrentPlanet { get; private set; }
    public Camera ActiveCamera => farmingCamera != null && farmingCamera.isActiveAndEnabled ? farmingCamera : exploreCamera;
    public bool IsUpgradeOverlayOpen { get; private set; }

    /// <summary>
    /// Initialize UI, cameras and explore controls.
    /// 保证 Camera GameObject 保持 Active，只轮流切 Camera.enabled，避免空白 Game View。
    /// </summary>
    private void Awake()
    {
        SetPanel(landingPanel, false);
        SetPanel(leavingPanel, false);
        SetActiveCamera(true);
        if (playerFlight != null) playerFlight.SetControlsEnabled(true);
    }

    /// <summary>
    /// Handle E-to-leave while farming and no upgrade UI is open.
    /// Detector 负责 Explore 的 E，这里只处理 Farming 的 E；U Panel 打开时忽略。
    /// </summary>
    private void Update()
    {
        if (CurrentMode == GameMode.Farming && !IsUpgradeOverlayOpen && SpaceGameInput.InteractDown)
            StartCoroutine(LeaveRoutine());
    }

    /// <summary>
    /// External landing request with unlock and reference validation.
    /// 从 PlanetInteractionDetector 收到星球对象，双重检查它已经解锁且相机锚点齐全。
    /// 即使以后鼠标、手柄或 UI 直接调用 TryLand，也不能绕过 Ship Level 解锁。
    /// </summary>
    /// <param name="planet">玩家选择的星球控制器，用于获取 Farming Anchor、Spin Controller 和解锁权限。</param>
    public void TryLand(PlanetController planet)
    {
        if (CurrentMode != GameMode.Explore || IsUpgradeOverlayOpen || planet == null) return;
        if (!planet.CanEnterFarming)
        {
            Debug.LogWarning($"{planet.name}: Planet locked or farming references incomplete.", planet);
            return;
        }
        if (playerFlight == null || exploreCamera == null || farmingCamera == null)
        {
            Debug.LogError("GameModeManager: Missing flight or camera references.", this);
            return;
        }
        StartCoroutine(LandRoutine(planet));
    }

    /// <summary>
    /// Show the landing panel, switch to the orthographic camera, then enable planet control.
    /// 先禁止自由飞行并盖住屏幕，Panel 停留结束后才切换相机，随后允许当前星球鼠标控制。
    /// 星球仍是场景里原来的 Baked Mesh，没有加载新对象。
    /// </summary>
    /// <param name="planet">玩家选择的星球控制器，用于获取 Farming Anchor、Spin Controller 和解锁权限。</param>
    private IEnumerator LandRoutine(PlanetController planet)
    {
        CurrentMode = GameMode.Landing;
        playerFlight.SetControlsEnabled(false);
        SetPanel(landingPanel, true);
        yield return new WaitForSecondsRealtime(landingPanelSeconds);

        CurrentPlanet = planet;
        farmingCamera.orthographic = true;
        farmingCamera.orthographicSize = planet.FarmingOrthoSize;
        farmingCamera.transform.SetPositionAndRotation(
            planet.FarmingCameraAnchor.position,
            planet.FarmingCameraAnchor.rotation);
        SetActiveCamera(false);
        planet.SpinController.SetManualControl(true, farmingCamera);
        SetPanel(landingPanel, false);
        CurrentMode = GameMode.Farming;
    }

    /// <summary>
    /// Show leaving panel, switch back to player view and restore movement.
    /// 不修改 Player.position 和 ExploreCamera 的 Transform，所以返回原来所在的空间位置。
    /// </summary>
    private IEnumerator LeaveRoutine()
    {
        CurrentMode = GameMode.Leaving;
        if (CurrentPlanet != null && CurrentPlanet.SpinController != null)
            CurrentPlanet.SpinController.SetManualControl(false, null);
        SetPanel(leavingPanel, true);
        yield return new WaitForSecondsRealtime(leavingPanelSeconds);

        SetActiveCamera(true);
        SetPanel(leavingPanel, false);
        CurrentPlanet = null;
        playerFlight.SetControlsEnabled(true);
        CurrentMode = GameMode.Explore;
    }

    /// <summary>
    /// Open or close the globally shared upgrade overlay without pausing simulation.
    /// U Panel 由 UpgradeTreeUI 管理显示；本方法只负责输入状态和光标。
    /// Explore 打开时禁用 Flight、解锁鼠标；Farming 打开时暂停拖拽/采集。
    /// 关闭后根据 CurrentMode 恢复，而不是盲目打开 WASD；AutoProducer、Spawner 和自转不会暂停。
    /// </summary>
    /// <param name="open">true 打开 UI / 输入覆盖状态；false 关闭。</param>
    public void SetUpgradeOverlayOpen(bool open)
    {
        if (open && CurrentMode != GameMode.Explore && CurrentMode != GameMode.Farming) return;
        IsUpgradeOverlayOpen = open;
        if (playerFlight != null)
            playerFlight.SetControlsEnabled(CurrentMode == GameMode.Explore && !open);
        if (CurrentPlanet != null && CurrentPlanet.SpinController != null)
            CurrentPlanet.SpinController.SetInputSuspended(open);
    }

    /// <summary>
    /// Switch between two Base cameras, enabling the next before disabling the previous.
    /// 两台 Camera 对象都保持 Active；只切换 Camera.enabled，不使用 URP Camera Stacking。
    /// AudioListener 也要同步切换，保证只有一台在监听。
    /// </summary>
    /// <param name="explore">true 切 Explore Camera；false 切 Farming Camera。</param>
    private void SetActiveCamera(bool explore)
    {
        if (exploreCamera == null || farmingCamera == null) return;
        exploreCamera.gameObject.SetActive(true);
        farmingCamera.gameObject.SetActive(true);
        Camera next = explore ? exploreCamera : farmingCamera;
        Camera previous = explore ? farmingCamera : exploreCamera;

        AudioListener oldAudio = previous.GetComponent<AudioListener>();
        AudioListener newAudio = next.GetComponent<AudioListener>();
        if (oldAudio != null) oldAudio.enabled = false;
        next.enabled = true;
        previous.enabled = false;
        if (newAudio != null) newAudio.enabled = true;
    }

    /// <summary>
    /// Set an optional transition panel active or inactive.
    /// 只切换指定全屏 Panel 的 Active 状态，不控制 Camera、不做透明渐变。Landing 和 Leaving 复用这个入口。
    /// </summary>
    /// <param name="panel">需要显示或隐藏的 UI GameObject。</param>
    /// <param name="visible">true 显示 Panel；false 隐藏。</param>
    private static void SetPanel(GameObject panel, bool visible)
    {
        if (panel != null) panel.SetActive(visible);
    }
}
