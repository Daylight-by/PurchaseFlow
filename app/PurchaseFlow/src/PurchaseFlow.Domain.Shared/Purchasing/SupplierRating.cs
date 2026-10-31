namespace PurchaseFlow.Purchasing;

/// <summary>
/// 供应商评级。第二轮主数据只做“可选/可展示”的简单枚举，
/// 不实现自动评级算法（那属于后续独立需求），避免为演示引入无法解释的计算。
/// C 为默认值，A 最优。
/// </summary>
public enum SupplierRating
{
    C = 0,
    B = 1,
    A = 2
}
