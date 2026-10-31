using System;
using System.Collections.Generic;
using PurchaseFlow.Purchasing;

namespace PurchaseFlow.Web.Models;

/// <summary>
/// One row of the purchase line editor. It is bound from the form and mapped
/// to the purchase line DTO of the application layer.
/// </summary>
public class LineInput
{
    /// <summary>
    /// P1-3：编辑已存在的明细时必须把行 Id 带回服务端，应用层才能“按 Id 就地更新”，
    /// 而不是把整批明细删掉重建（会破坏 EF 跟踪与行 Id 稳定性）。
    /// 为空表示这是页面新增的行，服务端会生成新 Id。
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// 第二轮第 1 批：物料档案关联（可选）。为空表示沿用旧数据的手工录入；
    /// 有值时服务端会用档案的物料名称/规格覆盖文本，作为单据快照保存，
    /// 因此档案改名不会影响历史单据。
    /// </summary>
    public Guid? MaterialId { get; set; }

    public string ItemName { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// View model of the shared line editor partial.
/// </summary>
public class LineEditorModel
{
    public string Prefix { get; set; } = "Lines";
    public List<LineInput> Rows { get; set; } = new();

    /// <summary>
    /// 第二轮第 1 批：「物料档案」下拉选项。页面模型只传启用中的档案；
    /// 明细行可以继续手工录入（空选项），因此旧数据/旧流程不受影响。
    /// </summary>
    public List<MaterialDto> Materials { get; set; } = new();
}

/// <summary>
/// P0-1：详情页“添加报价”表单里的一行逐项报价。
/// RequestLineId 指回当前采购需求的一条明细，UnitPrice 是该供应商对这一行的报价单价。
/// 物料/规格/数量由服务端从需求明细快照，客户端只提交“哪一行 + 报价单价”，
/// 报价总额由应用层按明细小计汇总，页面不再手填总额。
/// </summary>
public class QuoteLineInput
{
    public Guid RequestLineId { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// One selectable PO line in the receipt form. Selection is a UI concern; the
/// application service still validates all submitted IDs and quantities.
/// </summary>
public class ReceiptLineInput
{
    public Guid PurchaseOrderLineId { get; set; }
    public bool Selected { get; set; }
    public decimal Quantity { get; set; }
}
