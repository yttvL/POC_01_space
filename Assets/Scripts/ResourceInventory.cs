using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared inventory; tracks quantities for any ResourceDefinition.
/// 全局只挂一份。Mining 和 AutoProducer 都通过 Add 入库；UpgradeManager 通过 TrySpend 扣款。
/// 内部使用 ResourceDefinition 资产作为 Key，本周只有 Wood 也不要写死 woodCount。
/// 数量变化触发 ResourceChanged，UI 订阅事件而不是每帧重新遍历库存。
/// 当前是运行时数据；未来存档时用 ResourceId 映射数量，不要直接保存资产的内存引用。
/// </summary>
public class ResourceInventory : MonoBehaviour
{
    private readonly Dictionary<ResourceDefinition, int> amounts = new Dictionary<ResourceDefinition, int>();

    /// <summary>
    /// Invoked after a resource quantity changes: (type, newAmount).
    /// </summary>
    public event Action<ResourceDefinition, int> ResourceChanged;

    /// <summary>
    /// Return zero for unknown or unowned resources.
    /// 库存里没有记录的资源默认数量为零，方便以后无缝新增资源类型。
    /// </summary>
    /// <param name="resource">要查询、增加或扣除的资源定义资产。</param>
    /// <returns>玩家当前拥有的该资源数量；未持有或输入无效时为零。</returns>
    public int GetAmount(ResourceDefinition resource)
    {
        if (resource == null) return 0;
        return amounts.TryGetValue(resource, out int value) ? value : 0;
    }

    /// <summary>
    /// Add a non-negative amount and broadcast the new value.
    /// 仅用于正式发放资源。参数必须大于 0，避免 UI 收到无变化事件。
    /// </summary>
    /// <param name="resource">要查询、增加或扣除的资源定义资产。</param>
    /// <param name="amount">本次操作的资源数量；具体方法会判断负值或零是否合法。</param>
    public void Add(ResourceDefinition resource, int amount)
    {
        if (resource == null || amount <= 0) return;
        int current = GetAmount(resource);
        int next = (int)Math.Min((long)int.MaxValue, (long)current + amount);
        if (next == current) return;
        amounts[resource] = next;
        ResourceChanged?.Invoke(resource, next);
    }

    /// <summary>
    /// Check whether an amount is affordable without changing inventory.
    /// 只查询，不扣钱，供 UpgradeManager.CanAfford 检查按钮能不能购买。
    /// </summary>
    /// <param name="resource">要查询、增加或扣除的资源定义资产。</param>
    /// <param name="amount">本次操作的资源数量；具体方法会判断负值或零是否合法。</param>
    /// <returns>库存是否足以支付指定数量；不会实际扣除。</returns>
    public bool Has(ResourceDefinition resource, int amount)
    {
        return resource != null && amount >= 0 && GetAmount(resource) >= amount;
    }

    /// <summary>
    /// Spend only if affordable; returns whether it succeeded.
    /// 升级购买时调用。先判断是否够，再一次性扣除，避免出现资源不足也获得效果。
    /// </summary>
    /// <param name="resource">要查询、增加或扣除的资源定义资产。</param>
    /// <param name="amount">本次操作的资源数量；具体方法会判断负值或零是否合法。</param>
    /// <returns>true 表示扣费成功或合法的零金额支付；false 表示扣费失败。</returns>
    public bool TrySpend(ResourceDefinition resource, int amount)
    {
        if (!Has(resource, amount)) return false;
        if (amount == 0) return true;
        int next = GetAmount(resource) - amount;
        amounts[resource] = next;
        ResourceChanged?.Invoke(resource, next);
        return true;
    }
}
