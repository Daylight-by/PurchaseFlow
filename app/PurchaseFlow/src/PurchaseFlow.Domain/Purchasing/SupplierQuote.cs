using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

public class SupplierQuote : Entity<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid PurchaseRequestId { get; private set; }

    /// <summary>
    /// 第二轮主数据：关联的供应商档案 Id（可空）。SupplierName 仍是名称快照，
    /// 档案改名不影响历史报价；刻意不建外键，档案停用/删除历史报价仍可读。
    /// </summary>
    public Guid? SupplierId { get; private set; }

    public string SupplierName { get; private set; } = string.Empty;

    /// <summary>
    /// 报价总额。P0-1：金额改为“由逐项报价明细汇总”，因此这里是存储属性而不是
    /// 计算属性 —— Dashboard 只查报价表（不 include 明细）时仍要能取到金额，
    /// 迁移前的历史报价也依赖这一列；唯一写入点是 <see cref="RecalculateAmount"/>，
    /// private set 保证外部无法把它改成与明细不一致的数字。
    /// </summary>
    public decimal Amount { get; private set; }

    public int DeliveryDays { get; private set; }
    public string? Note { get; private set; }

    /// <summary>
    /// 第二轮第 3 批：报价有效期（业务日期，含当天）。null 表示长期有效。
    /// 领域层不做“必须晚于今天”的限制 —— 导入历史报价/测试需要写入过去日期，
    /// “新增报价不得创建已过期报价”的入口限制放在应用层；这里只负责过期判定，
    /// 选择/提交报价时由领域强制校验，防止过期报价进入成交链路。
    /// </summary>
    public DateTime? ValidUntil { get; private set; }

    /// <summary>逐项报价明细；报价总额必须等于这些小计合计。</summary>
    public ICollection<SupplierQuoteLine> Lines { get; private set; } = new List<SupplierQuoteLine>();

    protected SupplierQuote() { }

    /// <summary>
    /// P0-1：构造不再接收手填金额（amount 参数已删除），报价金额初始为 0，
    /// 完全由随后 <see cref="AddLine"/> 加入的明细重算得出。
    /// 第二轮主数据：supplierId 放在最后且可选，旧调用（不关联档案）不用改。
    /// 第二轮第 3 批：validUntil 再追加在最后且可选，旧调用同样不受影响。
    /// </summary>
    public SupplierQuote(Guid id, Guid? tenantId, Guid purchaseRequestId, string supplierName, int deliveryDays, string? note,
        Guid? supplierId = null, DateTime? validUntil = null)
        : base(id)
    {
        if (deliveryDays < 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidAmount);
        TenantId = tenantId;
        PurchaseRequestId = purchaseRequestId;
        SupplierId = supplierId;
        SupplierName = Check.NotNullOrWhiteSpace(supplierName, nameof(supplierName), maxLength: 100).Trim();
        Amount = 0m;
        DeliveryDays = deliveryDays;
        Note = string.IsNullOrWhiteSpace(note) ? null : Check.Length(note.Trim(), nameof(note), maxLength: 500);
        ValidUntil = validUntil;
    }

    /// <summary>
    /// 第二轮第 3 批：报价在给定时间点是否已过期。按 UTC 日期比较，
    /// 即 ValidUntil 当天仍有效，从次日 00:00 UTC 起失效（业务口径“含当天”）。
    /// </summary>
    public bool IsExpired(DateTime asOfUtc)
    {
        return ValidUntil.HasValue && ValidUntil.Value.Date < asOfUtc.Date;
    }

    /// <summary>
    /// 选择/提交报价前的硬校验：过期报价不能被选中或提交，避免把失效价格带入成交。
    /// </summary>
    public void EnsureNotExpired(DateTime asOfUtc)
    {
        if (IsExpired(asOfUtc))
            throw new BusinessException(PurchaseFlowErrors.QuoteExpired);
    }

    /// <summary>
    /// 追加一条逐项报价明细，并在明细变化后立即重算报价总额，
    /// 保证“报价总额 = 明细合计”永远成立（不允许出现两者不一致的中间态落库）。
    /// </summary>
    public SupplierQuoteLine AddLine(
        Guid id,
        Guid? purchaseRequestLineId,
        string itemName,
        string specification,
        decimal quantity,
        decimal unitPrice)
    {
        var line = new SupplierQuoteLine(id, TenantId, Id, purchaseRequestLineId, itemName, specification, quantity, unitPrice);
        Lines.Add(line);
        RecalculateAmount();
        return line;
    }

    /// <summary>
    /// 报价总额 = 所有明细小计之和。只在明细变化处调用；History 的金额口径
    /// 与订单明细小计保持一致，避免再出现“总额 82000 / 明细合计 309”的漂移。
    /// </summary>
    public void RecalculateAmount()
    {
        Amount = Lines.Sum(x => x.Subtotal);
    }
}
