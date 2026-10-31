using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// Aggregate root of the purchasing process. It owns the purchase lines and the
/// supplier quotes, and it guards every rule that has to hold when the request
/// leaves the draft state: lines must exist, at least two quotes must exist and
/// the selected quote must belong to this request.
/// </summary>
public class PurchaseRequest : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }
    public Guid BuyerUserId { get; private set; }
    public string OrderReference { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    /// <summary>
    /// Expected revenue of the related sales order. It is the revenue side of
    /// the gross profit calculation and therefore stays an explicit input.
    /// </summary>
    public decimal SaleAmount { get; private set; }

    public PurchaseRequestStatus Status { get; private set; }
    public Guid? SelectedQuoteId { get; private set; }
    public string? SelectionReason { get; private set; }

    /// <summary>Purchase lines; the amount of the request is derived from them.</summary>
    public ICollection<PurchaseRequestLine> Lines { get; private set; } = new List<PurchaseRequestLine>();

    /// <summary>Supplier quotes collected for this request.</summary>
    public ICollection<SupplierQuote> Quotes { get; private set; } = new List<SupplierQuote>();

    /// <summary>
    /// Purchase amount, always calculated from the lines. It is never typed in
    /// by hand, so the request total can not drift away from its lines.
    /// </summary>
    public decimal Amount => Lines.Sum(x => x.Subtotal);

    protected PurchaseRequest() { }

    public PurchaseRequest(Guid id, Guid? tenantId, Guid buyerUserId, string orderReference, string description, decimal saleAmount)
        : base(id)
    {
        TenantId = tenantId;
        BuyerUserId = buyerUserId;
        SetDetails(orderReference, description, saleAmount);
        Status = PurchaseRequestStatus.Draft;
    }

    public bool IsEditable => Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Returned;

    public void SetDetails(string orderReference, string description, decimal saleAmount)
    {
        if (!IsEditable)
            throw new BusinessException(PurchaseFlowErrors.NotEditable);
        if (saleAmount <= 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidAmount);
        OrderReference = Check.NotNullOrWhiteSpace(orderReference, nameof(orderReference), maxLength: 64).Trim();
        Description = Check.NotNullOrWhiteSpace(description, nameof(description), maxLength: 500).Trim();
        SaleAmount = saleAmount;
    }

    public void EnsureEditable()
    {
        if (!IsEditable)
            throw new BusinessException(PurchaseFlowErrors.NotEditable);
    }

    #region Lines

    /// <summary>
    /// 旧签名保留：不关联物料档案（MaterialId=null），委托给带 materialId 的新重载，
    /// 保证轮次 1 的调用方与旧测试不受影响。
    /// </summary>
    public PurchaseRequestLine AddLine(Guid id, string itemName, string specification, decimal quantity, decimal unitPrice)
    {
        return AddLine(id, itemName, specification, quantity, unitPrice, materialId: null);
    }

    /// <summary>
    /// 第二轮主数据：新增明细时可关联物料档案。ItemName/Specification 仍作为快照
    /// 传入（应用层会优先用档案的名称/规格覆盖），MaterialId 仅做溯源。
    /// </summary>
    public PurchaseRequestLine AddLine(Guid id, string itemName, string specification, decimal quantity, decimal unitPrice,
        Guid? materialId)
    {
        EnsureEditable();
        var line = new PurchaseRequestLine(id, TenantId, Id, itemName, specification, quantity, unitPrice, materialId);
        Lines.Add(line);
        return line;
    }

    public void RemoveLine(Guid lineId)
    {
        EnsureEditable();
        var line = Lines.FirstOrDefault(x => x.Id == lineId);
        if (line is null)
            throw new EntityNotFoundException(typeof(PurchaseRequestLine), lineId);
        Lines.Remove(line);
    }

    /// <summary>
    /// Replaces the whole line set. Lines are validated before anything is
    /// changed, so a rejected edit never leaves the request half updated.
    /// 旧 5 元组重载保留：委托给 6 元组新重载并把 MaterialId 置 null，
    /// 保证轮次 1 的调用方/旧测试不用改（旧数据与手工录入不关联档案）。
    /// </summary>
    public void SetLines(IEnumerable<(Guid Id, string ItemName, string Specification, decimal Quantity, decimal UnitPrice)> lines)
    {
        SetLines((lines ?? Enumerable.Empty<(Guid Id, string ItemName, string Specification, decimal Quantity, decimal UnitPrice)>())
            .Select(line => (line.Id, line.ItemName, line.Specification, line.Quantity, line.UnitPrice, (Guid?)null)));
    }

    /// <summary>
    /// 第二轮主数据：带 MaterialId 的新重载。其余校验与轮次 1（P1-3）完全一致：
    /// 先校验全部行 → 重复 Id 拒绝 → 按 Id 就地更新/新增/删除旧行。
    /// </summary>
    public void SetLines(IEnumerable<(Guid Id, string ItemName, string Specification, decimal Quantity, decimal UnitPrice, Guid? MaterialId)> lines)
    {
        EnsureEditable();
        var drafts = lines?.ToList() ?? new List<(Guid, string, string, decimal, decimal, Guid?)>();

        // 原“先校验全部行、再修改集合”的顺序保持不变：校验失败时聚合根不被改动。
        foreach (var draft in drafts)
        {
            if (draft.Quantity <= 0)
                throw new BusinessException(PurchaseFlowErrors.InvalidQuantity);
            if (draft.UnitPrice < 0)
                throw new BusinessException(PurchaseFlowErrors.InvalidUnitPrice);
            if (string.IsNullOrWhiteSpace(draft.ItemName))
                throw new BusinessException(PurchaseFlowErrors.InvalidItemName);
        }

        // P1-3：输入 Id 重复属于调用方数据错误。重复 Id 无法判断应该更新哪一行，
        // 必须在改动集合之前拒绝（不能靠 EF 去猜）。
        if (drafts.GroupBy(x => x.Id).Any(group => group.Count() > 1))
            throw new BusinessException(PurchaseFlowErrors.DuplicateLineId);

        // P1-3：改为“就地更新 / 新增 / 删除”，而不是 Lines.Clear() 后整体重建。
        // 原因：同 Id 重建会让 EF 把旧行当成删除、新行当成插入，既产生跟踪冲突，
        // 又让行 Id 变化（页面带回的 Id 失去意义）。按 Id 就地 Update 可以保持
        // 实体跟踪与行 Id 稳定；输入里没有的旧行才移除。
        var existingById = Lines.ToDictionary(x => x.Id);
        foreach (var draft in drafts)
        {
            if (existingById.TryGetValue(draft.Id, out var existing))
                existing.Update(draft.ItemName, draft.Specification, draft.Quantity, draft.UnitPrice, draft.MaterialId);
            else
                AddLine(draft.Id, draft.ItemName, draft.Specification, draft.Quantity, draft.UnitPrice, draft.MaterialId);
        }

        var submittedIds = drafts.Select(x => x.Id).ToHashSet();
        foreach (var staleLine in Lines.Where(x => !submittedIds.Contains(x.Id)).ToList())
            Lines.Remove(staleLine);
    }

    #endregion

    #region Quotes

    /// <summary>
    /// P0-1：报价从“手填总额”改为“按需求明细逐项报价”。每条需求明细都必须
    /// 提供一个成交单价，报价总额由明细汇总得出，从根本上保证
    /// “报价合计 = 明细合计”，审批生成的订单金额也才有可信来源。
    /// 第二轮主数据：可选 supplierId 关联供应商档案（SupplierName 仍是名称快照，
    /// 应用层在传档案时已用档案名称覆盖）。
    /// </summary>
    public SupplierQuote AddQuote(
        Guid id,
        string supplierName,
        int deliveryDays,
        string? note,
        IReadOnlyList<(Guid Id, Guid RequestLineId, decimal UnitPrice)> quotedLines,
        Guid? supplierId = null)
    {
        EnsureEditable();

        // 1. 逐项报价不能为空：没有明细就无法得到可与需求核对的价格结构。
        if (quotedLines is null || quotedLines.Count == 0)
            throw new BusinessException(PurchaseFlowErrors.QuoteLineMismatch);

        // 2. 供应商重名校验保持原逻辑不变。
        var name = Check.NotNullOrWhiteSpace(supplierName, nameof(supplierName), maxLength: 100).Trim();
        if (Quotes.Any(x => string.Equals(x.SupplierName, name, StringComparison.OrdinalIgnoreCase)))
            throw new BusinessException(PurchaseFlowErrors.DuplicateSupplier);

        // 3. 报价明细必须一一覆盖当前需求全部明细：
        //    - 每个需求明细恰好对应一条报价明细（数量相等，且去重后同样相等，
        //      杜绝同一条需求被重复报价、另一条被漏报）；
        //    - 每个 RequestLineId 都必须能在当前需求明细里找到（拒绝陌生 Id）。
        var requestLineIds = Lines.Select(x => x.Id).ToHashSet();
        var quotedRequestLineIds = quotedLines.Select(x => x.RequestLineId).Distinct().ToList();
        if (quotedLines.Count != Lines.Count ||
            quotedRequestLineIds.Count != Lines.Count ||
            quotedRequestLineIds.Any(requestLineId => !requestLineIds.Contains(requestLineId)))
            throw new BusinessException(PurchaseFlowErrors.QuoteLineMismatch);

        // 4. 报价明细行 Id 重复：Id 是行主键，重复会导致落库结果不可预期。
        if (quotedLines.Select(x => x.Id).Distinct().Count() != quotedLines.Count)
            throw new BusinessException(PurchaseFlowErrors.DuplicateLineId);

        // 5. 报价单价不能为负（0 元行允许，例如赠品行）。
        if (quotedLines.Any(x => x.UnitPrice < 0))
            throw new BusinessException(PurchaseFlowErrors.InvalidUnitPrice);

        // 6. 按需求明细当前顺序生成报价明细：先建报价（Amount 初始 0），再逐行
        //    AddLine 触发 RecalculateAmount；物料/规格/数量一律取需求行快照，
        //    客户端只提供单价，无法篡改快照内容。
        var quotedByRequestLineId = quotedLines.ToDictionary(x => x.RequestLineId);
        var quote = new SupplierQuote(id, TenantId, Id, name, deliveryDays, note, supplierId);
        foreach (var requestLine in Lines)
        {
            var quoted = quotedByRequestLineId[requestLine.Id];
            quote.AddLine(
                quoted.Id,
                requestLine.Id,
                requestLine.ItemName,
                requestLine.Specification,
                requestLine.Quantity,
                quoted.UnitPrice);
        }

        // 7. 加入聚合并返回。
        Quotes.Add(quote);
        return quote;
    }

    public void RemoveQuote(Guid quoteId)
    {
        EnsureEditable();
        var quote = Quotes.FirstOrDefault(x => x.Id == quoteId)
            ?? throw new EntityNotFoundException(typeof(SupplierQuote), quoteId);
        Quotes.Remove(quote);
        if (SelectedQuoteId == quoteId)
        {
            SelectedQuoteId = null;
            SelectionReason = null;
        }
    }

    public void SelectQuote(Guid quoteId, string reason)
    {
        EnsureEditable();
        if (!Quotes.Any(x => x.Id == quoteId))
            throw new EntityNotFoundException(typeof(SupplierQuote), quoteId);
        if (string.IsNullOrWhiteSpace(reason))
            throw new BusinessException(PurchaseFlowErrors.NeedSelectionReason);
        SelectedQuoteId = quoteId;
        SelectionReason = Check.Length(reason.Trim(), nameof(reason), maxLength: 500);
    }

    public void ClearSelectedQuote(Guid quoteId)
    {
        EnsureEditable();
        if (SelectedQuoteId != quoteId) return;
        SelectedQuoteId = null;
        SelectionReason = null;
    }

    #endregion

    public void Submit()
    {
        EnsureEditable();
        if (Lines.Count == 0)
            throw new BusinessException(PurchaseFlowErrors.NeedPurchaseLines);
        if (Quotes.Count < 2)
            throw new BusinessException(PurchaseFlowErrors.NeedTwoQuotes);
        if (SelectedQuoteId is null)
            throw new BusinessException(PurchaseFlowErrors.NeedSelectedQuote);
        if (string.IsNullOrWhiteSpace(SelectionReason))
            throw new BusinessException(PurchaseFlowErrors.NeedSelectionReason);
        Status = PurchaseRequestStatus.PendingReview;
    }

    public void Approve()
    {
        EnsurePendingReview();
        Status = PurchaseRequestStatus.Approved;
    }

    public void Return()
    {
        EnsurePendingReview();
        Status = PurchaseRequestStatus.Returned;
    }

    private void EnsurePendingReview()
    {
        if (Status != PurchaseRequestStatus.PendingReview)
            throw new BusinessException(PurchaseFlowErrors.InvalidStatus);
    }
}
