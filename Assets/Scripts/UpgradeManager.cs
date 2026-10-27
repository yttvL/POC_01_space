using System;
using UnityEngine;

/// <summary>
/// Handles the three prototype upgrades, purchase order, costs and effects.
/// 全局只挂一份。购买顺序固定为 AutoProduction -&gt; MiningYield -&gt; Spaceship。
/// UI 只请求 TryPurchase，不自行扣钱、改生产率、改飞船等级。
/// 本周控制 scope，不做复杂的升级图结构或随机节点生成，但资源类型用 ResourceDefinition 引用。
/// 后面科技树变大时可把 UpgradeId / Inspector 三组参数迁移到 UpgradeDefinition 数据表，
/// 只要保留 TryPurchase / GetState / UpgradesChanged 等接口，UI 可以继续调用。
/// </summary>
public class UpgradeManager : MonoBehaviour
{
    public enum UpgradeId { AutoProduction, MiningYield, Spaceship }
    public enum UpgradeState { Locked, Available, Purchased }

    [Header("References")]
    [SerializeField] private ResourceInventory inventory;
    // 本周升级扣除的资源类型，拖 Wood.asset。
    [SerializeField] private ResourceDefinition woodResource;
    // 本周需要提升速率的全局 AutoProducer，不要拖错误资源的生产器。
    [SerializeField] private AutoProducer woodProducer;
    // Spaceship Upgrade 购买成功后修改这里的飞船等级。
    [SerializeField] private ProgressionState progression;

    [Header("Upgrade 01: Wood Auto Production")]
    [SerializeField, Min(0)] private int autoProductionCost = 20;
    [SerializeField, Min(0f)] private float upgradedWoodPerSecond = 2f;

    [Header("Upgrade 02: Wood Mining Yield")]
    [SerializeField, Min(0)] private int miningYieldCost = 35;
    [SerializeField, Min(1)] private int upgradedWoodPerNode = 2;

    [Header("Upgrade 03: Spaceship")]
    [SerializeField, Min(0)] private int spaceshipCost = 120;
    [SerializeField, Min(1)] private int unlockedShipLevel = 1;

    private bool autoPurchased;
    private bool miningPurchased;
    private bool spaceshipPurchased;

    /// <summary>
    /// Notify UI after a successful purchase.
    /// </summary>
    public event Action UpgradesChanged;
    public ResourceDefinition WoodResource => woodResource;

    /// <summary>
    /// Return the configured Wood cost of a prototype upgrade.
    /// 价格只在这里配置。UI 文本、购买检测都读取同一份价格，避免显示和实际扣款不一致。
    /// </summary>
    /// <param name="id">要查询或购买的升级节点 ID（本周三种之一）。</param>
    /// <returns>该升级在 Inspector 配置的 Wood 价格。</returns>
    public int GetCost(UpgradeId id)
    {
        switch (id)
        {
            case UpgradeId.AutoProduction: return autoProductionCost;
            case UpgradeId.MiningYield: return miningYieldCost;
            case UpgradeId.Spaceship: return spaceshipCost;
            default: return 0;
        }
    }

    /// <summary>
    /// Query whether an upgrade is locked, available or purchased.
    /// Available 仅表示前置已经完成；木头够不够钱由 CanAfford 单独判断。
    /// 第二个必须先买第一个，第三个必须先买第二个。
    /// </summary>
    /// <param name="id">要查询或购买的升级节点 ID（本周三种之一）。</param>
    /// <returns>Locked、Available 或 Purchased 之一；与库存余额分开判断。</returns>
    public UpgradeState GetState(UpgradeId id)
    {
        switch (id)
        {
            case UpgradeId.AutoProduction:
                return autoPurchased ? UpgradeState.Purchased : UpgradeState.Available;
            case UpgradeId.MiningYield:
                if (miningPurchased) return UpgradeState.Purchased;
                return autoPurchased ? UpgradeState.Available : UpgradeState.Locked;
            case UpgradeId.Spaceship:
                if (spaceshipPurchased) return UpgradeState.Purchased;
                return miningPurchased ? UpgradeState.Available : UpgradeState.Locked;
            default:
                return UpgradeState.Locked;
        }
    }

    /// <summary>
    /// Check availability and money without changing anything.
    /// 前置必须 Available，再确认库存够钱；UI 的 Button.interactable 用这个结果。
    /// </summary>
    /// <param name="id">要查询或购买的升级节点 ID（本周三种之一）。</param>
    /// <returns>true 表示前置已购买且当前 Wood 足够；不会扣费。</returns>
    public bool CanAfford(UpgradeId id)
    {
        return GetState(id) == UpgradeState.Available &&
               inventory != null && inventory.Has(woodResource, GetCost(id));
    }

    /// <summary>
    /// Attempt to purchase a node and apply its effect once.
    /// Button OnClick -&gt; UI -&gt; TryPurchase -&gt; 检查顺序 -&gt; TrySpend -&gt; 应用效果 -&gt; 通知 UI。
    /// 如果钱不够或节点尚未解锁，直接返回 false，不扣费，也不更改升级状态。
    /// </summary>
    /// <param name="id">要查询或购买的升级节点 ID（本周三种之一）。</param>
    /// <returns>true 表示扣费、升级效果和状态变更都成功；false 表示失败且不应用升级。</returns>
    public bool TryPurchase(UpgradeId id)
    {
        if (!CanAfford(id)) return false;
        if (id == UpgradeId.AutoProduction && (woodProducer == null || woodProducer.OutputResource != woodResource))
        {
            Debug.LogError("UpgradeManager: Wood Producer missing or targeting a different resource.", this);
            return false;
        }
        if (id == UpgradeId.Spaceship && progression == null)
        {
            Debug.LogError("UpgradeManager: ProgressionState missing.", this);
            return false;
        }
        if (!inventory.TrySpend(woodResource, GetCost(id))) return false;

        switch (id)
        {
            case UpgradeId.AutoProduction:
                autoPurchased = true;
                woodProducer.SetProductionRate(upgradedWoodPerSecond);
                break;
            case UpgradeId.MiningYield:
                miningPurchased = true;
                break;
            case UpgradeId.Spaceship:
                spaceshipPurchased = true;
                progression.UnlockShipLevel(unlockedShipLevel);
                break;
        }
        UpgradesChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Resolve a mined node's final payout for a resource.
    /// ResourceNode 原始数量通常是 1；Mining Yield 升级只影响 Wood，不影响未来的 Iron。
    /// 将来如果有不同资源的多个采集升级，扩展这个统一入口，不要去改每个 Node Prefab。
    /// </summary>
    /// <param name="resource">要查询、增加或扣除的资源定义资产。</param>
    /// <param name="baseYield">资源点未升级时的基础采集数量。</param>
    /// <returns>当前资源节点一次采集应发放的最终数量。</returns>
    public int GetMiningYield(ResourceDefinition resource, int baseYield)
    {
        if (resource == woodResource && miningPurchased) return upgradedWoodPerNode;
        return Mathf.Max(1, baseYield);
    }

    /// <summary>
    /// Convenience query for UI text.
    /// UI 的生产速度 Text 读取当前实际速率，不从升级按钮猜测数值。
    /// </summary>
    /// <returns>当前 Wood AutoProducer 的每秒生产速率；引用缺失时为零。</returns>
    public float GetAutoProductionRate()
    {
        return woodProducer != null ? woodProducer.ProductionPerSecond : 0f;
    }
}
