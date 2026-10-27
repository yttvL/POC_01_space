using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Toggles a hand-built upgrade panel with U and binds three buttons to the upgrade manager.
/// 挂在一直 Active 的 GameSystems 或 CanvasController 上，不要挂在默认隐藏的 UpgradeTreePanel 上。
/// 升级 Panel 由美术手工摆放：一个 TMP_Text + 三个 Button，不自动画节点、不自动连线。
/// 数据流：U -&gt; 显示 Panel -&gt; GameModeManager 锁定玩家操作；Button -&gt; UpgradeManager.TryPurchase。
/// Inventory.ResourceChanged / UpgradeManager.UpgradesChanged -&gt; RefreshUI，只更新文字和按钮状态。
/// 后面换成美术手绘 UI，只需换 Sprite 和排版；Button 引用与逻辑都不用改。
/// </summary>
public class UpgradeTreeUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameModeManager gameMode;
    [SerializeField] private UpgradeManager upgrades;
    [SerializeField] private ResourceInventory inventory;
    [SerializeField] private ResourceDefinition displayedResource;

    [Header("Hand-built UI")]
    // 默认隐藏的全局 UI Panel；这个脚本本身必须挂在始终 Active 的对象上。
    [SerializeField] private GameObject upgradeTreePanel;
    // 显示当前资源总量及每秒自动生产速率的 TMP 文本。
    [SerializeField] private TMP_Text autoFarmingText;
    [SerializeField] private Button upgrade01Button;
    [SerializeField] private Button upgrade02Button;
    [SerializeField] private Button spaceshipUpgradeButton;
    [SerializeField] private TMP_Text upgrade01Text;
    [SerializeField] private TMP_Text upgrade02Text;
    [SerializeField] private TMP_Text spaceshipUpgradeText;

    private bool isOpen;

    /// <summary>
    /// Hide the panel and register button callbacks once.
    /// 这里用 AddListener 绑定，Button 的 Inspector On Click 不必再手动配置。
    /// 注意脚本本身不能随 Panel 一起隐藏，否则无法收到下一次 U 键把它打开。
    /// </summary>
    private void Awake()
    {
        if (upgradeTreePanel != null) upgradeTreePanel.SetActive(false);
        if (upgrade01Button != null) upgrade01Button.onClick.AddListener(BuyAutoProduction);
        if (upgrade02Button != null) upgrade02Button.onClick.AddListener(BuyMiningYield);
        if (spaceshipUpgradeButton != null) spaceshipUpgradeButton.onClick.AddListener(BuySpaceship);
    }

    /// <summary>
    /// Subscribe to resource and upgrade changes.
    /// 自动生产会不断 Add(Wood)，事件通知 UI 及时刷新数量；不需要靠每帧重建 UI。
    /// </summary>
    private void OnEnable()
    {
        if (inventory != null) inventory.ResourceChanged += OnResourceChanged;
        if (upgrades != null) upgrades.UpgradesChanged += RefreshUI;
    }

    /// <summary>
    /// Populate the initial UI even if the panel starts hidden.
    /// 确保初始 Wood 数量、生产速率和三颗升级按钮的状态正确显示；UI Panel 此时可以仍然隐藏。
    /// </summary>
    private void Start()
    {
        RefreshUI();
    }

    /// <summary>
    /// Unsubscribe from inventory and upgrade change events.
    /// 解绑库存变化和升级变化事件。脚本之后重新启用时会在 OnEnable 重新订阅，避免重复刷新。
    /// </summary>
    private void OnDisable()
    {
        if (inventory != null) inventory.ResourceChanged -= OnResourceChanged;
        if (upgrades != null) upgrades.UpgradesChanged -= RefreshUI;
    }

    /// <summary>
    /// Remove button callbacks when this UI controller is destroyed.
    /// 在对象销毁时移除 Awake 添加的三个 Button.onClick 监听。
    /// 与 OnDisable 解绑库存和升级事件不同，这里释放的是按钮回调，避免旧对象被销毁后保留事件引用。
    /// </summary>
    private void OnDestroy()
    {
        if (upgrade01Button != null) upgrade01Button.onClick.RemoveListener(BuyAutoProduction);
        if (upgrade02Button != null) upgrade02Button.onClick.RemoveListener(BuyMiningYield);
        if (spaceshipUpgradeButton != null) spaceshipUpgradeButton.onClick.RemoveListener(BuySpaceship);
    }

    /// <summary>
    /// Toggle the global panel only from Explore or Farming, except closing is always allowed.
    /// Landing/Leaving 期间不接受 U，避免相机正在切换时操作 UI。
    /// </summary>
    private void Update()
    {
        if (!SpaceGameInput.UpgradeToggleDown || gameMode == null) return;
        if (!isOpen && gameMode.CurrentMode != GameModeManager.GameMode.Explore &&
            gameMode.CurrentMode != GameModeManager.GameMode.Farming) return;
        SetPanelOpen(!isOpen);
    }

    /// <summary>
    /// Show/hide the visual panel and request input blocking from GameModeManager.
    /// 此方法不暂停 Time.timeScale，全球自动生产和星球资源补充继续运行。
    /// </summary>
    /// <param name="open">true 打开 UI / 输入覆盖状态；false 关闭。</param>
    private void SetPanelOpen(bool open)
    {
        if (upgradeTreePanel == null || gameMode == null) return;
        isOpen = open;
        upgradeTreePanel.SetActive(isOpen);
        gameMode.SetUpgradeOverlayOpen(isOpen);
        if (isOpen) RefreshUI();
    }

    /// <summary>
    /// Refresh when the displayed inventory resource changes.
    /// 只有变化的资源和当前 UI 显示的资源相同，才刷新库存文本和各个按钮的是否可购买状态。
    /// </summary>
    /// <param name="resource">要查询、增加或扣除的资源定义资产。</param>
    /// <param name="amount">事件提供的最新资源库存数量，本回调无需直接使用这个数值。</param>
    private void OnResourceChanged(ResourceDefinition resource, int amount)
    {
        if (resource == displayedResource) RefreshUI();
    }

    /// <summary>
    /// Refresh the rate readout and three fixed button statuses.
    /// 按钮状态由 UpgradeManager 计算；Locked、缺钱、Purchased 都不能点。
    /// 这里不修改产量或库存，避免 UI 与 Gameplay 互相依赖内部字段。
    /// </summary>
    public void RefreshUI()
    {
        if (upgrades == null || inventory == null) return;
        if (autoFarmingText != null)
        {
            string resourceName = displayedResource != null ? displayedResource.DisplayName : "Resource";
            autoFarmingText.text = $"{resourceName}: {inventory.GetAmount(displayedResource)}\nAUTO FARMING: +{upgrades.GetAutoProductionRate():0.##} {resourceName}/s";
        }
        RefreshButton(upgrade01Button, upgrade01Text, UpgradeManager.UpgradeId.AutoProduction, "AUTO FARMING I");
        RefreshButton(upgrade02Button, upgrade02Text, UpgradeManager.UpgradeId.MiningYield, "MINING YIELD I");
        RefreshButton(spaceshipUpgradeButton, spaceshipUpgradeText, UpgradeManager.UpgradeId.Spaceship, "SPACESHIP UPGRADE I");
    }

    /// <summary>
    /// Display a button state and its current configured Wood cost.
    /// 不强制改美术图片，只更新 Button.interactable 和可选 TMP_Text。
    /// 美术替换按钮 Sprite 后，这个方法不影响图片布局。
    /// </summary>
    /// <param name="button">要更新交互状态的 Unity UI Button。</param>
    /// <param name="label">要更新文案的 TMP 文本；为空时尝试从 Button 子物体查找。</param>
    /// <param name="id">要查询或购买的升级节点 ID（本周三种之一）。</param>
    /// <param name="title">按钮对应的显示标题，由 UI 提供。</param>
    private void RefreshButton(Button button, TMP_Text label, UpgradeManager.UpgradeId id, string title)
    {
        UpgradeManager.UpgradeState state = upgrades.GetState(id);
        bool canBuy = upgrades.CanAfford(id);
        if (button != null) button.interactable = canBuy;
        if (label == null && button != null) label = button.GetComponentInChildren<TMP_Text>(true);
        if (label == null) return;
        string status = state == UpgradeManager.UpgradeState.Purchased ? "PURCHASED" :
                        state == UpgradeManager.UpgradeState.Locked ? "LOCKED" :
                        $"COST: {upgrades.GetCost(id)} WOOD";
        label.text = $"{title}\n{status}";
    }

    /// <summary>
    /// First upgrade button callback.
    /// 三个函数只发购买请求，真正扣 Wood 的逻辑都在 UpgradeManager。
    /// </summary>
    private void BuyAutoProduction() => upgrades?.TryPurchase(UpgradeManager.UpgradeId.AutoProduction);
    /// <summary>
    /// Second upgrade button callback.
    /// 第二个 Button 的点击入口：只请求购买 Mining Yield I；必须先购买 Auto Farming I，验证和扣费都由 UpgradeManager 处理。
    /// </summary>
    private void BuyMiningYield() => upgrades?.TryPurchase(UpgradeManager.UpgradeId.MiningYield);
    /// <summary>
    /// Third upgrade button callback.
    /// 第三个 Button 的点击入口：只请求购买 Spaceship Upgrade I；成功后 ProgressionState 增级并通知 Planet 02 Spawner。
    /// </summary>
    private void BuySpaceship() => upgrades?.TryPurchase(UpgradeManager.UpgradeId.Spaceship);
}
