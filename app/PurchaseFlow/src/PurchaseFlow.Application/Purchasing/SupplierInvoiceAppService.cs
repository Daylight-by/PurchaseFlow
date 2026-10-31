using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using PurchaseFlow.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮（三单匹配）：供应商发票应用服务。
/// 登记发票时执行“订单 ↔ 收货 ↔ 发票”逐行匹配：明细必须属于该订单、发票单价必须
/// 等于订单成交单价、该行必须已有收货、本次开票数量不能超过“已收货 − 已开票”。
/// 不匹配直接拒绝，绝不保存“先存后批”的灰色发票。
///
/// 三单匹配需要跨聚合查询（订单/收货/历史发票），因此校验放在应用层；
/// 领域层负责金额=明细合计与状态单向流转。
/// </summary>
[Authorize(PurchaseFlowPermissions.ViewInvoices)]
public class SupplierInvoiceAppService : ApplicationService, ISupplierInvoiceAppService
{
    private readonly IRepository<PurchaseOrder, Guid> _orders;
    private readonly IRepository<PurchaseOrderLine, Guid> _orderLines;
    private readonly IRepository<GoodsReceipt, Guid> _receipts;
    private readonly IRepository<GoodsReceiptLine, Guid> _receiptLines;
    private readonly IRepository<SupplierInvoice, Guid> _invoices;
    private readonly IRepository<SupplierInvoiceLine, Guid> _invoiceLines;
    private readonly IRepository<PaymentApplication, Guid> _payments;

    public SupplierInvoiceAppService(
        IRepository<PurchaseOrder, Guid> orders,
        IRepository<PurchaseOrderLine, Guid> orderLines,
        IRepository<GoodsReceipt, Guid> receipts,
        IRepository<GoodsReceiptLine, Guid> receiptLines,
        IRepository<SupplierInvoice, Guid> invoices,
        IRepository<SupplierInvoiceLine, Guid> invoiceLines,
        IRepository<PaymentApplication, Guid> payments)
    {
        _orders = orders;
        _orderLines = orderLines;
        _receipts = receipts;
        _receiptLines = receiptLines;
        _invoices = invoices;
        _invoiceLines = invoiceLines;
        _payments = payments;
    }

    [Authorize(PurchaseFlowPermissions.RegisterInvoices)]
    public async Task<SupplierInvoiceDto> RegisterAsync(CreateSupplierInvoiceDto input)
    {
        // 1. 输入归一化与基础校验：发票号 trim、日期只取日期部分、明细必须非空。
        var invoiceNumber = (input.InvoiceNumber ?? string.Empty).Trim();
        if (input.Lines is null || input.Lines.Count == 0)
            throw new BusinessException(PurchaseFlowErrors.InvoiceLinesRequired);

        // 发票日期是业务日期：default 视为非法（页面日期控件为空时不能静默当成 0001-01-01）。
        if (input.InvoiceDate == default)
            throw new BusinessException(PurchaseFlowErrors.InvalidInvoiceDate);
        var invoiceDate = input.InvoiceDate.Date;

        // 同一条订单行在一张发票里重复出现会让数量/金额无法解释，直接拒绝。
        var submittedLineIds = input.Lines.Select(x => x.PurchaseOrderLineId).ToList();
        if (submittedLineIds.Count != submittedLineIds.Distinct().Count())
            throw new BusinessException(PurchaseFlowErrors.DuplicateLineId);

        // 2. 订单必须存在（发票只能挂在真实采购订单上）。
        var order = await _orders.FindAsync(input.PurchaseOrderId)
            ?? throw new EntityNotFoundException(typeof(PurchaseOrder), input.PurchaseOrderId);

        // 3. 同一订单下发票号唯一（应用层查重 + DB 唯一索引兜底）。
        var invoiceQuery = await _invoices.GetQueryableAsync();
        if (await AsyncExecuter.AnyAsync(invoiceQuery.Where(x =>
                x.TenantId == CurrentTenant.Id &&
                x.PurchaseOrderId == order.Id &&
                x.InvoiceNumber == invoiceNumber)))
            throw new BusinessException(PurchaseFlowErrors.InvoiceNumberDuplicate);

        // 4. 订单行、收货累计、历史已开票累计（都要按当前租户过滤）。
        var orderLineQuery = await _orderLines.GetQueryableAsync();
        var orderLines = await AsyncExecuter.ToListAsync(orderLineQuery
            .Where(x => x.PurchaseOrderId == order.Id && x.TenantId == CurrentTenant.Id));
        var orderLinesById = orderLines.ToDictionary(x => x.Id);
        var orderLineIds = orderLines.Select(x => x.Id).ToList();

        var receiptLineQuery = await _receiptLines.GetQueryableAsync();
        // 收货累计只统计“本订单的收货单”下的明细：先按订单取收货单 Id，
        // 避免其它订单（理论上不会同 Id）或脏数据干扰三单匹配口径。
        var receiptQuery = await _receipts.GetQueryableAsync();
        var receiptIds = await AsyncExecuter.ToListAsync(receiptQuery
            .Where(x => x.PurchaseOrderId == order.Id && x.TenantId == CurrentTenant.Id)
            .Select(x => x.Id));
        var receivedByLine = new Dictionary<Guid, decimal>();
        if (receiptIds.Count > 0)
        {
            receivedByLine = (await AsyncExecuter.ToListAsync(receiptLineQuery
                    .Where(x => x.TenantId == CurrentTenant.Id && receiptIds.Contains(x.GoodsReceiptId))))
                .GroupBy(x => x.PurchaseOrderLineId)
                .ToDictionary(group => group.Key, group => group.Sum(x => x.Quantity));
        }

        // 历史已开票数量覆盖所有状态（Matched/PaymentRequested/Paid），
        // 否则“已申请付款/已付款”的发票数量会被重复开票。
        var invoiceLineQuery = await _invoiceLines.GetQueryableAsync();
        var invoicedByLine = (await AsyncExecuter.ToListAsync(invoiceLineQuery
                .Where(x => x.TenantId == CurrentTenant.Id && orderLineIds.Contains(x.PurchaseOrderLineId))))
            .GroupBy(x => x.PurchaseOrderLineId)
            .ToDictionary(group => group.Key, group => group.Sum(x => x.Quantity));

        // 5. 创建发票（状态 Matched、金额由明细汇总）。
        var invoice = new SupplierInvoice(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            order.Id,
            order.OrderNumber,
            order.SupplierId,
            order.SupplierName,
            invoiceNumber,
            invoiceDate,
            input.Note);

        foreach (var line in input.Lines)
        {
            if (!orderLinesById.TryGetValue(line.PurchaseOrderLineId, out var orderLine))
                throw new BusinessException(PurchaseFlowErrors.InvoiceLineNotInOrder);

            // 发票单价必须与订单成交单价逐分一致：金额口径不允许“近似匹配”或按行重定价。
            if (line.UnitPrice != orderLine.UnitPrice)
                throw new BusinessException(PurchaseFlowErrors.InvoicePriceMismatch);

            var received = receivedByLine.TryGetValue(orderLine.Id, out var receivedQuantity)
                ? receivedQuantity
                : 0m;
            if (received <= 0)
                throw new BusinessException(PurchaseFlowErrors.InvoiceNeedsReceivedGoods);

            var alreadyInvoiced = invoicedByLine.TryGetValue(orderLine.Id, out var invoicedQuantity)
                ? invoicedQuantity
                : 0m;
            var availableToInvoice = received - alreadyInvoiced;
            if (line.Quantity > availableToInvoice)
                throw new BusinessException(PurchaseFlowErrors.InvoiceQuantityExceedsReceived);

            // 物料/规格取订单行快照，避免发票与订单的物料描述不一致。
            invoice.AddLine(
                GuidGenerator.Create(),
                orderLine.Id,
                orderLine.ItemName,
                orderLine.Specification,
                line.Quantity,
                line.UnitPrice);
        }

        // 先在同一 UoW 内落库（发票 + 明细），再走统一 DTO 映射。
        // 为什么必须显式 SaveChanges：InsertAsync 默认 autoSave=false，映射查询是走数据库的，
        // 若不先落库，刚加入的明细行在查询里不可见，RegisterAsync 返回的 DTO 会缺少 Lines。
        await _invoices.InsertAsync(invoice);
        var currentUnitOfWork = CurrentUnitOfWork
            ?? throw new InvalidOperationException("A unit of work is required to register an invoice.");
        await currentUnitOfWork.SaveChangesAsync();

        return (await MapAsync(new[] { invoice })).Single();
    }

    public async Task<InvoiceListDto> GetListAsync(InvoiceListInput input)
    {
        var query = await _invoices.GetQueryableAsync();
        query = query.Where(x => x.TenantId == CurrentTenant.Id);

        if (input.Status.HasValue)
            query = query.Where(x => x.Status == input.Status.Value);

        var keyword = input.Keyword?.Trim();
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(x =>
                x.InvoiceNumber.Contains(keyword) ||
                x.PurchaseOrderNumber.Contains(keyword) ||
                x.SupplierName.Contains(keyword));

        var result = new InvoiceListDto { PageSize = Math.Clamp(input.PageSize, 1, 200) };
        result.TotalCount = await AsyncExecuter.CountAsync(query);
        var lastPage = Math.Max(1, (result.TotalCount + result.PageSize - 1) / result.PageSize);
        result.Page = Math.Clamp(input.Page, 1, lastPage);
        var invoices = await AsyncExecuter.ToListAsync(query
            .OrderByDescending(x => x.CreationTime).ThenByDescending(x => x.Id)
            .Skip((result.Page - 1) * result.PageSize).Take(result.PageSize));
        result.Items = await MapAsync(invoices);
        return result;
    }

    public async Task<SupplierInvoiceDto> GetAsync(Guid id)
    {
        var invoice = await _invoices.FindAsync(id)
            ?? throw new EntityNotFoundException(typeof(SupplierInvoice), id);
        return (await MapAsync(new[] { invoice })).Single();
    }

    public async Task<List<SupplierInvoiceDto>> GetOrderInvoicesAsync(Guid purchaseOrderId)
    {
        var query = await _invoices.GetQueryableAsync();
        var invoices = await AsyncExecuter.ToListAsync(query
            .Where(x => x.PurchaseOrderId == purchaseOrderId && x.TenantId == CurrentTenant.Id)
            .OrderByDescending(x => x.CreationTime).ThenByDescending(x => x.Id));
        return await MapAsync(invoices);
    }

    /// <summary>
    /// 统一 DTO 映射：一次批量加载明细与付款申请，避免逐张发票 N+1 查询；
    /// 付款信息（PaymentId/Status/Amount）内嵌在发票 DTO 里，页面无需再发请求。
    /// </summary>
    private async Task<List<SupplierInvoiceDto>> MapAsync(IReadOnlyList<SupplierInvoice> invoices)
    {
        if (invoices.Count == 0)
            return new List<SupplierInvoiceDto>();

        var invoiceIds = invoices.Select(x => x.Id).ToList();

        var invoiceLineQuery = await _invoiceLines.GetQueryableAsync();
        var lines = await AsyncExecuter.ToListAsync(invoiceLineQuery
            .Where(x => x.TenantId == CurrentTenant.Id && invoiceIds.Contains(x.SupplierInvoiceId)));
        var linesByInvoice = lines
            .GroupBy(x => x.SupplierInvoiceId)
            .ToDictionary(group => group.Key, group => group.OrderBy(x => x.ItemName).ToList());

        var paymentQuery = await _payments.GetQueryableAsync();
        var payments = await AsyncExecuter.ToListAsync(paymentQuery
            .Where(x => x.TenantId == CurrentTenant.Id && invoiceIds.Contains(x.SupplierInvoiceId)));
        var paymentByInvoice = payments
            .GroupBy(x => x.SupplierInvoiceId)
            .ToDictionary(group => group.Key, group => group.First());

        return invoices.Select(invoice =>
        {
            paymentByInvoice.TryGetValue(invoice.Id, out var payment);
            linesByInvoice.TryGetValue(invoice.Id, out var invoiceLines);
            return new SupplierInvoiceDto
            {
                Id = invoice.Id,
                PurchaseOrderId = invoice.PurchaseOrderId,
                PurchaseOrderNumber = invoice.PurchaseOrderNumber,
                SupplierId = invoice.SupplierId,
                SupplierName = invoice.SupplierName,
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate,
                Amount = invoice.Amount,
                Status = invoice.Status,
                Note = invoice.Note,
                CreationTime = invoice.CreationTime,
                Lines = (invoiceLines ?? new List<SupplierInvoiceLine>()).Select(line => new SupplierInvoiceLineDto
                {
                    Id = line.Id,
                    PurchaseOrderLineId = line.PurchaseOrderLineId,
                    ItemName = line.ItemName,
                    Specification = line.Specification,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    Subtotal = line.Subtotal
                }).ToList(),
                PaymentId = payment?.Id,
                PaymentStatus = payment?.Status,
                PaymentAmount = payment?.Amount
            };
        }).ToList();
    }
}
