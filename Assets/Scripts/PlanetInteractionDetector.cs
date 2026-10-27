using TMPro;
using UnityEngine;

/// <summary>
/// Detect the closest nearby planet and show the E-to-land prompt.
/// 挂在 Player。每帧只在 Explore、且全局 U Panel 关闭时查找最近星球。
/// 已锁星球仍显示名称，第二行改为 SHIP UPGRADE REQUIRED，不允许按 E。
/// 这里不会直接开关相机，按 E 只调用 GameModeManager.TryLand。
/// 以后 Explore Block 做真正的边界阻挡，也不要在本脚本里修改 Player 位置。
/// </summary>
public class PlanetInteractionDetector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameModeManager gameMode;
    [SerializeField] private Transform playerTransform;
    [SerializeField] private PlanetController[] planets;

    [Header("Two-line prompt")]
    [SerializeField] private GameObject promptRoot;
    [SerializeField] private TMP_Text planetNameText;
    [SerializeField] private TMP_Text pressEText;

    private PlanetController nearbyPlanet;

    /// <summary>
    /// Resolve player reference and hide the prompt at startup.
    /// 若未指定 Player Transform 则默认使用脚本所在物体；启动时隐藏 E 提示，避免游戏一开始闪出错误内容。
    /// </summary>
    private void Awake()
    {
        if (playerTransform == null) playerTransform = transform;
        HidePrompt();
    }

    /// <summary>
    /// Display and handle E only in Explore without an open modal UI.
    /// U Panel 打开后不显示 Press E，也不处理 E，避免玩家点按钮同时意外 Landing。
    /// </summary>
    private void Update()
    {
        if (gameMode == null || gameMode.CurrentMode != GameModeManager.GameMode.Explore || gameMode.IsUpgradeOverlayOpen)
        {
            nearbyPlanet = null;
            HidePrompt();
            return;
        }

        nearbyPlanet = FindClosestPlanet();
        if (nearbyPlanet == null)
        {
            HidePrompt();
            return;
        }
        if (promptRoot != null && !promptRoot.activeSelf) promptRoot.SetActive(true);
        if (planetNameText != null) planetNameText.text = nearbyPlanet.DisplayName;
        if (pressEText != null)
            pressEText.text = nearbyPlanet.IsUnlocked ? "Press E to Land" : "SHIP UPGRADE REQUIRED";

        if (nearbyPlanet.IsUnlocked && SpaceGameInput.InteractDown)
        {
            HidePrompt();
            gameMode.TryLand(nearbyPlanet);
        }
    }

    /// <summary>
    /// Find the nearest planet within its configured landing range.
    /// 距离相同优先数组中较前的对象；候选只根据距离决定，是否解锁在 UI 与 TryLand 再判断。
    /// </summary>
    /// <returns>范围内距表面最近的星球；没有符合条件的目标时返回 null。</returns>
    private PlanetController FindClosestPlanet()
    {
        if (planets == null || playerTransform == null) return null;
        PlanetController best = null;
        float bestDistance = float.PositiveInfinity;
        Vector3 pos = playerTransform.position;
        foreach (PlanetController planet in planets)
        {
            if (planet == null || !planet.IsInLandingRange(pos)) continue;
            float d = planet.DistanceFromSurface(pos);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = planet;
            }
        }
        return best;
    }

    /// <summary>
    /// Hide the shared two-line prompt.
    /// 隐藏共用的双行 UI 提示。Landing 开始、附近没有星球或打开 U 面板时都会调用，不改变星球解锁状态。
    /// </summary>
    public void HidePrompt()
    {
        if (promptRoot != null && promptRoot.activeSelf) promptRoot.SetActive(false);
    }
}
