using System;
using UnityEngine;

/// <summary>
/// Global spaceship progression and travel-permission interface.
/// 本周只保存飞船等级，购买 Spaceship Upgrade I 后从 0 变 1。
/// PlanetController 读取 IsShipLevelEnough 判断能不能 Landing；ResourceSpawner 用同一判断决定是否生成。
/// 以后 Explore Block 调用 CanTravelToLevel(requiredLevel) 即可，不必知道科技树按钮是哪一个。
/// 本周不会创建边界 Collider、移动阻挡、Warning Panel 或玩家安全返回。
/// </summary>
public class ProgressionState : MonoBehaviour
{
    // 本周默认为 0；购买 Spaceship Upgrade I 后运行时变为 1。
    [SerializeField, Min(0)] private int initialShipLevel = 0;
    public int ShipLevel { get; private set; }

    /// <summary>
    /// Invoked whenever the ship level actually changes.
    /// 星球 Spawner 订阅这个事件；解锁后马上生成初始节点，不需要等玩家进入 Farming。
    /// </summary>
    public event Action<int> ShipLevelChanged;

    /// <summary>
    /// Initialize runtime progression from Inspector.
    /// 从 Inspector 的 Initial Ship Level 初始化当前运行时飞船等级。升级只写 ShipLevel，不修改 Inspector 初始值。
    /// </summary>
    private void Awake()
    {
        ShipLevel = initialShipLevel;
    }

    /// <summary>
    /// Shared permission query for planets and future travel boundaries.
    /// 统一比较所需飞船等级和当前 ShipLevel，返回是否拥有权限。当前供 Planet Landing / Resource Spawner 使用；未来 Explore Block 也调用这一接口，但此处不移动或阻挡玩家。
    /// </summary>
    /// <param name="requiredShipLevel">进入该区域或星球最低需要的飞船等级。</param>
    /// <returns>true 表示当前飞船等级满足要求，否则为 false。</returns>
    public bool CanTravelToLevel(int requiredShipLevel)
    {
        return ShipLevel >= requiredShipLevel;
    }

    /// <summary>
    /// Apply an absolute target ship level without decreasing progress.
    /// 本周飞船升级是一次性永久进度，所以只允许等级向上变化。
    /// 未来如需 Debug 重置或读存档，可另加专用接口，避免平时购买意外降级。
    /// </summary>
    /// <param name="targetLevel">要解锁到的目标飞船等级，只允许比当前等级更高。</param>
    public void UnlockShipLevel(int targetLevel)
    {
        if (targetLevel <= ShipLevel) return;
        ShipLevel = targetLevel;
        ShipLevelChanged?.Invoke(ShipLevel);
    }
}
