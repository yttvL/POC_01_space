using UnityEngine;

/// <summary>
/// Defines a resource type, not the player's amount of that resource.
/// 每种资源建一个 ScriptableObject 资产，例如 Wood.asset、Iron.asset。
/// Asset 只保存稳定 ID、显示名和可选 Icon；玩家实际持有的数量放在 ResourceInventory。
/// 以后改成统一 Energy 或每种资源独立自动生产，只是替换 AutoProducer 的 outputResource。
/// </summary>
[CreateAssetMenu(menuName = "Planet Game/Resource", fileName = "NewResource")]
public class ResourceDefinition : ScriptableObject
{
    // Stable ID for saving and data lookup. Do not rename after release.
    // 稳定的资源 ID，未来存档和数据查找靠它；不要把当前库存写到资产里。
    [SerializeField] private string resourceId = "wood";
    // 显示名称，供 HUD 和升级面板使用。
    [SerializeField] private string displayName = "Wood";
    // 可选资源图标，未来手绘 UI 直接读取。
    [SerializeField] private Sprite icon;

    public string ResourceId => resourceId;
    public string DisplayName => displayName;
    public Sprite Icon => icon;
}
