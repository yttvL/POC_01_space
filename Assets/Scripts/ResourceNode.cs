using UnityEngine;

/// <summary>
/// Represents one clickable resource point spawned on a planet.
/// 挂在资源点 Prefab 的根物体上，Prefab 上必须有 Collider（可在子物体）。
/// ResourceSpawner 生成实例后调用 Initialize，把资源类型、基础产量、库存、升级表传进来。
/// 本脚本不直接读取鼠标：鼠标 Click/Drag 由 PlanetSpinController 统一判定，
/// 真正 Click 命中本组件时才调用 TryMine，避免长按拖拽被错误识别为采集。
/// </summary>
public class ResourceNode : MonoBehaviour
{
    private ResourceDefinition resource;
    private int baseYield;
    private ResourceInventory inventory;
    private UpgradeManager upgrades;
    private PlanetResourceSpawner ownerSpawner;
    private bool mined;

    /// <summary>
    /// Initialize a newly spawned node from its owning planet spawner.
    /// 只在 Instantiate 后调用一次。Prefab 无需绑死 Wood，资源类型由生成它的星球指定。
    /// ownerSpawner 只负责记录数量；采集奖励直接存进全局 Inventory。
    /// </summary>
    /// <param name="resourceType">此节点实际产出的 ResourceDefinition，由所属 PlanetResourceSpawner 指定。</param>
    /// <param name="baseAmount">节点未升级时每次采集的基础资源数量。</param>
    /// <param name="targetInventory">所有来源共用的全局库存。</param>
    /// <param name="upgradeManager">提供最终采集数量的升级管理器，可为空并使用基础数量。</param>
    /// <param name="spawner">生成该节点的 PlanetResourceSpawner，用来同步活动节点数。</param>
    public void Initialize(ResourceDefinition resourceType, int baseAmount,
        ResourceInventory targetInventory, UpgradeManager upgradeManager, PlanetResourceSpawner spawner)
    {
        resource = resourceType;
        baseYield = Mathf.Max(1, baseAmount);
        inventory = targetInventory;
        upgrades = upgradeManager;
        ownerSpawner = spawner;
        mined = false;
    }

    /// <summary>
    /// Try to grant a resource reward and destroy this node once.
    /// FarmingCamera Raycast 点击到这个节点时调用。如果已经被采集过或引用缺失，不发放资源。
    /// 返回 true 表示成功：计算当前 Mining Yield -&gt; Inventory.Add -&gt; 通知 Spawner -&gt; Destroy。
    /// 节点的初始 yield 不会被购买升级永久写坏；每次点击才查询升级当前状态。
    /// </summary>
    /// <returns>true 表示成功发放奖励并标记删除，false 表示已经采过或缺少依赖。</returns>
    public bool TryMine()
    {
        if (mined || resource == null || inventory == null || ownerSpawner == null) return false;
        mined = true;
        int amount = upgrades != null ? upgrades.GetMiningYield(resource, baseYield) : baseYield;
        inventory.Add(resource, amount);
        ownerSpawner.NotifyNodeMined(this);
        Destroy(gameObject);
        return true;
    }
}
