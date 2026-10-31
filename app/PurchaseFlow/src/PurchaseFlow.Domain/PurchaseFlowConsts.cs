namespace PurchaseFlow;

public static class PurchaseFlowConsts
{
    public const string DbTablePrefix = "App";

    public const string DbSchema = null;

    /// <summary>
    /// 第二轮第 2 批（分级审批）：采购金额超过 5 万元时必须两级审批
    /// （审核员一级 + 管理员二级）。阈值放领域常量而不是页面配置，
    /// 保证领域规则是唯一口径；后续要支持按租户配置可再迁移到设置系统。
    /// </summary>
    public const decimal HighValueApprovalThreshold = 50000m;
}

