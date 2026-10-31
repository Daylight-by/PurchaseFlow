using System;
using System.Collections.Generic;

namespace PurchaseFlow.Web.Models;

/// <summary>
/// 第三轮（三单匹配）：登记发票表单的一行。
/// 页面只提交“订单行 Id + 本次开票数量 + 单价（隐藏只读）”，
/// 服务端会逐行核对订单成交单价与“已收货 − 已开票”的剩余可开票量，页面不自行放行。
/// </summary>
public class InvoiceLineInput
{
    public Guid PurchaseOrderLineId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// 登记发票表单的展示快照：订购 / 已收 / 已开票 / 剩余可开票。
/// 由订单行、收货汇总与该订单历史发票行组合而成，只用于展示与输入；
/// 真正的匹配校验仍由应用服务在提交时重新执行，避免页面数据被篡改后蒙混过关。
/// </summary>
public class InvoiceableLine
{
    public Guid PurchaseOrderLineId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal InvoicedQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal UnitPrice { get; set; }
}
