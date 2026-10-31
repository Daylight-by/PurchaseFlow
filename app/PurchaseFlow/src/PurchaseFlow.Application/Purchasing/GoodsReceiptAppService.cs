using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using PurchaseFlow.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// Registers immutable goods receipts against purchase-order lines. The method
/// is locally transactional because the application keeps the global SQLite
/// unit-of-work transaction disabled for the login/security-log path.
/// </summary>
[Authorize(PurchaseFlowPermissions.Requests)]
public class GoodsReceiptAppService : ApplicationService, IGoodsReceiptAppService
{
    private readonly IRepository<PurchaseOrder, Guid> _orders;
    private readonly IRepository<PurchaseOrderLine, Guid> _orderLines;
    private readonly IRepository<GoodsReceipt, Guid> _receipts;
    private readonly IRepository<GoodsReceiptLine, Guid> _receiptLines;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public GoodsReceiptAppService(
        IRepository<PurchaseOrder, Guid> orders,
        IRepository<PurchaseOrderLine, Guid> orderLines,
        IRepository<GoodsReceipt, Guid> receipts,
        IRepository<GoodsReceiptLine, Guid> receiptLines,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _orders = orders;
        _orderLines = orderLines;
        _receipts = receipts;
        _receiptLines = receiptLines;
        _unitOfWorkManager = unitOfWorkManager;
    }

    [Authorize(PurchaseFlowPermissions.ViewReceipts)]
    public async Task<PurchaseOrderReceiptDto> GetOrderAsync(Guid purchaseOrderId)
    {
        var order = await FindOrderAsync(purchaseOrderId)
            ?? throw new EntityNotFoundException(typeof(PurchaseOrder), purchaseOrderId);
        var orderLines = await GetOrderLinesAsync(order.Id);
        return await MapOrderAsync(order, orderLines);
    }

    [Authorize(PurchaseFlowPermissions.RegisterReceipts)]
    public async Task<GoodsReceiptDto> CreateAsync(CreateGoodsReceiptDto input)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        try
        {
            var result = await CreateInternalAsync(input);
            await uow.CompleteAsync();
            return result;
        }
        catch (Exception exception) when (IsPersistenceConflict(exception))
        {
            // Explicitly roll back the local transaction before mapping the
            // provider exception. This keeps the head, lines and PO progress
            // atomic even when the caller already has an outer unit of work.
            await uow.RollbackAsync();
            throw new BusinessException(PurchaseFlowErrors.ReceiptConcurrencyConflict);
        }
    }

    private async Task<GoodsReceiptDto> CreateInternalAsync(CreateGoodsReceiptDto input)
    {
        var userId = CurrentUser.Id ?? throw new AbpAuthorizationException();
        var key = NormalizeKey(input.IdempotencyKey);
        var receiptDate = NormalizeReceiptDate(input.ReceiptDate);
        var requestedLines = NormalizeLines(input.Lines);
        var fingerprint = BuildFingerprint(input.PurchaseOrderId, receiptDate, input.Note, requestedLines);

        var existing = await FindExistingAsync(input.PurchaseOrderId, userId, key);
        if (existing is not null)
        {
            if (!existing.HasSameRequest(fingerprint))
                throw new BusinessException(PurchaseFlowErrors.ReceiptIdempotencyConflict);

            var existingOrderLines = await GetOrderLinesAsync(existing.PurchaseOrderId);
            return await MapReceiptAsync(existing, existingOrderLines.ToDictionary(x => x.Id));
        }

        var order = await FindOrderAsync(input.PurchaseOrderId)
            ?? throw new EntityNotFoundException(typeof(PurchaseOrder), input.PurchaseOrderId);
        var orderLines = await GetOrderLinesAsync(order.Id);
        var quantities = requestedLines.ToDictionary(x => x.PurchaseOrderLineId, x => x.Quantity);

        // This validates every submitted line against the actual PO, checks the
        // remaining quantity, increments the protected PO receipt version, and
        // moves the order to Received only when all lines are complete.
        order.RegisterReceipt(orderLines, quantities);

        var receipt = new GoodsReceipt(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            order.Id,
            key,
            fingerprint,
            receiptDate,
            input.Note)
        {
            CreatorId = userId
        };
        foreach (var requestedLine in requestedLines)
            receipt.AddLine(GuidGenerator.Create(), requestedLine.PurchaseOrderLineId, requestedLine.Quantity);

        await _receipts.InsertAsync(receipt, autoSave: false);
        await _receiptLines.InsertManyAsync(receipt.Lines.ToList(), autoSave: false);
        // The order and its lines were queried in this UoW and are already
        // tracked. Calling UpdateAsync here would reset the original
        // concurrency values and defeat the optimistic check.
        var currentUnitOfWork = CurrentUnitOfWork
            ?? throw new InvalidOperationException("A unit of work is required for a goods receipt.");
        await currentUnitOfWork.SaveChangesAsync();

        return await MapReceiptAsync(receipt, orderLines.ToDictionary(x => x.Id));
    }

    private static bool IsPersistenceConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var name = current.GetType().Name;
            if (name is "DbUpdateConcurrencyException" or "DbUpdateException" or "SqliteException")
                return true;
        }

        return false;
    }

    private async Task<PurchaseOrder?> FindOrderAsync(Guid id)
    {
        return await _orders.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == CurrentTenant.Id);
    }

    private async Task<GoodsReceipt?> FindExistingAsync(Guid purchaseOrderId, Guid userId, string key)
    {
        var query = await _receipts.GetQueryableAsync();
        return await AsyncExecuter.FirstOrDefaultAsync(query.Where(x =>
            x.PurchaseOrderId == purchaseOrderId &&
            x.TenantId == CurrentTenant.Id &&
            x.CreatorId == userId &&
            x.IdempotencyKey == key));
    }

    private async Task<List<PurchaseOrderLine>> GetOrderLinesAsync(Guid orderId)
    {
        var query = await _orderLines.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(query
            .Where(x => x.PurchaseOrderId == orderId && x.TenantId == CurrentTenant.Id)
            .OrderBy(x => x.ItemName)
            .ThenBy(x => x.Id));
    }

    private async Task<PurchaseOrderReceiptDto> MapOrderAsync(
        PurchaseOrder order,
        IReadOnlyList<PurchaseOrderLine> orderLines)
    {
        var orderLineById = orderLines.ToDictionary(x => x.Id);
        var receiptQuery = await _receipts.GetQueryableAsync();
        var receipts = await AsyncExecuter.ToListAsync(receiptQuery
            .Where(x => x.PurchaseOrderId == order.Id && x.TenantId == CurrentTenant.Id)
            .OrderByDescending(x => x.CreationTime)
            .ThenByDescending(x => x.Id));
        var receiptIds = receipts.Select(x => x.Id).ToList();
        var receiptLines = receiptIds.Count == 0
            ? new List<GoodsReceiptLine>()
            : await GetReceiptLinesAsync(receiptIds);
        var receiptLinesByReceipt = receiptLines
            .GroupBy(x => x.GoodsReceiptId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<GoodsReceiptLine>)x.ToList());

        var lines = orderLines.Select(MapOrderLine).ToList();
        var fullyReceived = lines.Count > 0 && lines.All(x => x.RemainingQuantity == 0m);
        return new PurchaseOrderReceiptDto
        {
            PurchaseOrderId = order.Id,
            OrderStatus = order.Status,
            IsFullyReceived = fullyReceived,
            IsPartiallyReceived = lines.Any(x => x.ReceivedQuantity > 0m) && !fullyReceived,
            Lines = lines,
            Receipts = receipts.Select(receipt => MapReceipt(
                receipt,
                receiptLinesByReceipt.TryGetValue(receipt.Id, out var linesForReceipt)
                    ? linesForReceipt
                    : Array.Empty<GoodsReceiptLine>(),
                orderLineById)).ToList()
        };
    }

    private async Task<List<GoodsReceiptLine>> GetReceiptLinesAsync(IReadOnlyCollection<Guid> receiptIds)
    {
        var query = await _receiptLines.GetQueryableAsync();
        return await AsyncExecuter.ToListAsync(query
            .Where(x => receiptIds.Contains(x.GoodsReceiptId) && x.TenantId == CurrentTenant.Id)
            .OrderBy(x => x.GoodsReceiptId)
            .ThenBy(x => x.PurchaseOrderLineId));
    }

    private async Task<GoodsReceiptDto> MapReceiptAsync(
        GoodsReceipt receipt,
        IReadOnlyDictionary<Guid, PurchaseOrderLine> orderLines)
    {
        var receiptLines = await GetReceiptLinesAsync(new[] { receipt.Id });
        return MapReceipt(receipt, receiptLines, orderLines);
    }

    private static PurchaseOrderReceiptLineDto MapOrderLine(PurchaseOrderLine line) => new()
    {
        PurchaseOrderLineId = line.Id,
        ItemName = line.ItemName,
        Specification = line.Specification,
        OrderedQuantity = line.Quantity,
        ReceivedQuantity = line.ReceivedQuantity,
        RemainingQuantity = line.RemainingQuantity
    };

    private static GoodsReceiptDto MapReceipt(
        GoodsReceipt receipt,
        IReadOnlyList<GoodsReceiptLine> receiptLines,
        IReadOnlyDictionary<Guid, PurchaseOrderLine> orderLines)
    {
        return new GoodsReceiptDto
        {
            Id = receipt.Id,
            PurchaseOrderId = receipt.PurchaseOrderId,
            ReceiptDate = receipt.ReceiptDate,
            Note = receipt.Note,
            CreatorId = receipt.CreatorId,
            CreationTime = receipt.CreationTime,
            Lines = receiptLines.Select(line => new GoodsReceiptLineDto
            {
                PurchaseOrderLineId = line.PurchaseOrderLineId,
                ItemName = orderLines.TryGetValue(line.PurchaseOrderLineId, out var orderLine)
                    ? orderLine.ItemName
                    : "已删除明细",
                Quantity = line.Quantity
            }).ToList()
        };
    }

    private static string NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new BusinessException(PurchaseFlowErrors.InvalidReceiptKey);
        return key.Trim();
    }

    private static DateTime NormalizeReceiptDate(DateTime receiptDate)
    {
        if (receiptDate == default)
            throw new BusinessException(PurchaseFlowErrors.InvalidReceiptDate);
        return DateTime.SpecifyKind(receiptDate.Date, DateTimeKind.Unspecified);
    }

    private static List<(Guid PurchaseOrderLineId, decimal Quantity)> NormalizeLines(
        IReadOnlyCollection<GoodsReceiptLineInputDto>? lines)
    {
        if (lines is null || lines.Count == 0)
            throw new BusinessException(PurchaseFlowErrors.InvalidReceiptLines);
        if (lines.GroupBy(x => x.PurchaseOrderLineId).Any(x => x.Count() > 1))
            throw new BusinessException(PurchaseFlowErrors.DuplicateReceiptLine);
        if (lines.Any(x => x.PurchaseOrderLineId == Guid.Empty))
            throw new BusinessException(PurchaseFlowErrors.ReceiptLineNotInOrder);
        if (lines.Any(x => x.Quantity <= 0))
            throw new BusinessException(PurchaseFlowErrors.InvalidQuantity);
        if (lines.Any(x => decimal.Round(x.Quantity, 3, MidpointRounding.AwayFromZero) != x.Quantity))
            throw new BusinessException(PurchaseFlowErrors.ReceiptQuantityPrecision);

        return lines
            .Select(x => (x.PurchaseOrderLineId, x.Quantity))
            .OrderBy(x => x.PurchaseOrderLineId)
            .ToList();
    }

    private static string BuildFingerprint(
        Guid purchaseOrderId,
        DateTime receiptDate,
        string? note,
        IReadOnlyCollection<(Guid PurchaseOrderLineId, decimal Quantity)> lines)
    {
        var payload = new StringBuilder()
            .Append(purchaseOrderId.ToString("N"))
            .Append('|')
            .Append(receiptDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .Append('|')
            .Append((note ?? string.Empty).Trim())
            .Append('|');
        foreach (var line in lines)
        {
            payload.Append(line.PurchaseOrderLineId.ToString("N"))
                .Append(':')
                .Append(line.Quantity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
                .Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())));
    }
}
