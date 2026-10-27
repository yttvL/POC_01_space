using UnityEngine;

/// <summary>
/// Continuously adds one configured resource to the global inventory.
/// 全局生产器，跟当前正在 Explore 还是 Farming、身在哪个星球都无关。
/// 本周 outputResource=Wood，baseProductionPerSecond=1；升级后运行时速率变为 2。
/// 计时使用小数累积，保证未来 0.5/s 之类的非整数速度也能正确生产。
/// 以后每种资源单独生产时，可以同时挂多个 AutoProducer，各自指定 Output Resource。
/// 以后如果改成只产 Energy，也不用改库存代码，只换 Output Resource。
/// </summary>
public class AutoProducer : MonoBehaviour
{
    // 产出最终写入这里，Explore/Farming 都使用同一份库存。
    [SerializeField] private ResourceInventory inventory;
    // 本周拖 Wood.asset；将来可以改成 Energy 或另开生产器产 Iron。
    [SerializeField] private ResourceDefinition outputResource;
    // Inspector 中的初始速率，运行时升级不会改回这个字段。
    [SerializeField, Min(0f)] private float baseProductionPerSecond = 1f;

    private float currentProductionPerSecond;
    private double pendingAmount;

    public ResourceDefinition OutputResource => outputResource;
    public float ProductionPerSecond => currentProductionPerSecond;

    /// <summary>
    /// Initialize the runtime production rate from Inspector base rate.
    /// 每次进入 Play 都从 Base Rate 初始化，不把购买产生的临时数值写回资产。
    /// </summary>
    private void Awake()
    {
        currentProductionPerSecond = baseProductionPerSecond;
    }

    /// <summary>
    /// Produce resources over time, including while the upgrade UI is open.
    /// 每帧按 deltaTime 累积；只有累计 &gt;=1 才真的 Add 整数资源。
    /// 用普通 deltaTime，不因 U Panel 打开而暂停。以后若实现离线收益，再新增专门的离线结算。
    /// </summary>
    private void Update()
    {
        if (inventory == null || outputResource == null || currentProductionPerSecond <= 0f) return;
        pendingAmount += Time.deltaTime * currentProductionPerSecond;
        if (pendingAmount < 1d) return;

        int batch = (int)System.Math.Min((double)int.MaxValue, System.Math.Floor(pendingAmount));
        pendingAmount -= batch;
        inventory.Add(outputResource, batch);
    }

    /// <summary>
    /// Runtime API used by upgrades to set production rate.
    /// UpgradeManager 买完节点后调用。这里不检查木头价格，也不决定升级依赖。
    /// 同一个 Producer 的 Output Resource 始终由 Inspector 配置，不会凭升级名称猜测资源。
    /// </summary>
    /// <param name="newRate">新的每秒资源生产速率；负数会限制为零。</param>
    public void SetProductionRate(float newRate)
    {
        currentProductionPerSecond = Mathf.Max(0f, newRate);
    }
}
