namespace PurchaseFlow.Web.Menus;

public class PurchaseFlowMenus
{
    private const string Prefix = "PurchaseFlow";
    public const string Home = Prefix + ".Home";
    public const string PurchaseRequests = Prefix + ".PurchaseRequests";

    // 第二轮第 1 批：主数据（供应商档案 / 物料档案）菜单项。
    // 单独定义名称常量，菜单贡献者与测试都引用常量而不是散落字符串。
    public const string Suppliers = Prefix + ".Suppliers";
    public const string Materials = Prefix + ".Materials";

}
