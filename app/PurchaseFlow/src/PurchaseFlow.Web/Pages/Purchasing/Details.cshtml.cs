using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using PurchaseFlow.Localization;
using PurchaseFlow.Permissions;
using PurchaseFlow.Purchasing;
using PurchaseFlow.Web.Models;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Users;
using Volo.Abp.Validation;

namespace PurchaseFlow.Web.Pages.Purchasing;

[Authorize(PurchaseFlowPermissions.Requests)]
public class DetailsModel : PurchaseFlowPageModel
{
    private readonly IPurchaseRequestAppService _service;
    private readonly IGoodsReceiptAppService _receiptService;
    private readonly IMaterialAppService _materialService;
    private readonly ISupplierAppService _supplierService;
    private readonly IAuthorizationService _authorization;
    private readonly ICurrentUser _currentUser;
    private readonly IStringLocalizer<PurchaseFlowResource> _localizer;

    public PurchaseRequestDetailDto PurchaseRequest { get; private set; } = new();
    public PurchaseOrderReceiptDto? PurchaseOrderReceipts { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanReview { get; private set; }
    public bool CanViewReceipts { get; private set; }
    public bool CanRegisterReceipts { get; private set; }
    public string? Error { get; private set; }

    /// <summary>
    /// 第二轮第 1 批：行编辑器的「物料档案」下拉选项（只取启用中的档案）。
    /// 详情页编辑需求时同样允许选择档案，未选则沿用旧的手工文本。
    /// </summary>
    public List<MaterialDto> Materials { get; private set; } = new();

    /// <summary>
    /// 第二轮第 1 批：报价表单的「选择供应商档案」下拉选项（只取启用中的供应商）。
    /// 只用于选择与提示，不在表单提交时回写名称快照 —— 名称覆盖由服务端完成。
    /// </summary>
    public List<SupplierDto> Suppliers { get; private set; } = new();

    [BindProperty]
    public string OrderReference { get; set; } = string.Empty;

    [BindProperty]
    public string Description { get; set; } = string.Empty;

    [BindProperty]
    public decimal SaleAmount { get; set; }

    [BindProperty]
    public List<LineInput> Lines { get; set; } = new();

    /// <summary>
    /// P0-1：“添加报价”表单按当前采购明细逐项录入报价单价。
    /// 页面只提交“需求明细 Id + 该供应商的报价单价”，报价总额由应用层按明细汇总。
    /// </summary>
    [BindProperty]
    public List<QuoteLineInput> QuoteLines { get; set; } = new();

    [BindProperty]
    public DateTime ReceiptDate { get; set; } = DateTime.Today;

    [BindProperty]
    public string ReceiptIdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");

    [BindProperty]
    public string? ReceiptNote { get; set; }

    [BindProperty]
    public List<ReceiptLineInput> ReceiptLines { get; set; } = new();

    public DetailsModel(IPurchaseRequestAppService service, IGoodsReceiptAppService receiptService,
        IMaterialAppService materialService, ISupplierAppService supplierService,
        IAuthorizationService authorization,
        ICurrentUser currentUser, IStringLocalizer<PurchaseFlowResource> localizer)
    {
        _service = service;
        _receiptService = receiptService;
        _materialService = materialService;
        _supplierService = supplierService;
        _authorization = authorization;
        _currentUser = currentUser;
        _localizer = localizer;
    }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        try
        {
            await LoadAsync(id);
            OrderReference = PurchaseRequest.OrderReference;
            Description = PurchaseRequest.Description;
            SaleAmount = PurchaseRequest.SaleAmount;
            Lines = PurchaseRequest.Lines.Select(line => new LineInput
            {
                // P1-3：把已落库的行 Id 带到页面，编辑保存时再回传，应用层才能按 Id 就地更新。
                Id = line.Id,
                // 第二轮第 1 批：回显物料档案关联，编辑时下拉保持选中。
                MaterialId = line.MaterialId,
                ItemName = line.ItemName,
                Specification = line.Specification,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            }).ToList();
            // P0-1：报价表单按当前需求明细初始化，单价默认 0，等待用户逐项填写报价。
            QuoteLines = PurchaseRequest.Lines.Select(line => new QuoteLineInput
            {
                RequestLineId = line.Id,
                UnitPrice = 0m
            }).ToList();
            return Page();
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
    }

    public Task<IActionResult> OnPostUpdateAsync(Guid id) =>
        RunAsync(id, () => _service.UpdateAsync(id, new UpdatePurchaseRequestDto
        {
            OrderReference = OrderReference ?? string.Empty,
            Description = Description ?? string.Empty,
            SaleAmount = SaleAmount,
            // P1-3：更新走 UpdatePurchaseRequestLineDto，并带上行 Id：
            // 有 Id 的行由应用层就地更新，无 Id 的行（页面新增）由服务端生成新 Id。
            Lines = Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.ItemName))
                .Select(line => new UpdatePurchaseRequestLineDto
                {
                    Id = line.Id,
                    // 第二轮第 1 批：带上可选的物料档案 Id；空 Id 时应用层仍按手工文本处理（向后兼容）。
                    MaterialId = line.MaterialId,
                    ItemName = line.ItemName,
                    Specification = line.Specification,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                }).ToList()
        }));

    /// <summary>
    /// P0-1：添加报价改为按当前采购需求明细逐项提交报价单价。
    /// 不再接收手填总额 Amount —— 报价总额由应用层/领域按每条报价明细小计汇总，
    /// 从入口保证“报价总额 = 明细合计”。
    /// 第二轮第 1 批：supplierId 可选；有值时服务端会校验供应商档案并按档案名称覆盖文本快照。
    /// </summary>
    public Task<IActionResult> OnPostAddQuoteAsync(Guid id, string supplierName, Guid? supplierId,
        int deliveryDays, string? note) => RunAsync(id, () => _service.AddQuoteAsync(id, new AddSupplierQuoteDto
        {
            SupplierName = supplierName ?? string.Empty,
            // 第二轮第 1 批：空值表示沿用旧的手工文本供应商（向后兼容）；有值则关联档案。
            SupplierId = supplierId,
            DeliveryDays = deliveryDays,
            Note = note,
            // P0-1：把页面逐项单价映射为报价明细输入；空行 Id 说明该输入无效，直接丢弃，
            // 是否“逐项覆盖全部需求明细”仍由领域层严格校验。
            Lines = (QuoteLines ?? new List<QuoteLineInput>())
                .Where(line => line.RequestLineId != Guid.Empty)
                .Select(line => new SupplierQuoteLineInputDto
                {
                    RequestLineId = line.RequestLineId,
                    UnitPrice = line.UnitPrice
                }).ToList()
        }));

    public Task<IActionResult> OnPostDeleteQuoteAsync(Guid id, Guid quoteId) =>
        RunAsync(id, () => _service.DeleteQuoteAsync(id, quoteId));

    public Task<IActionResult> OnPostSelectAsync(Guid id, Guid quoteId, string reason) =>
        RunAsync(id, () => _service.SelectQuoteAsync(id, new SelectSupplierQuoteDto
        {
            QuoteId = quoteId,
            Reason = reason ?? string.Empty
        }));

    public Task<IActionResult> OnPostSubmitAsync(Guid id) => RunAsync(id, () => _service.SubmitAsync(id));

    public Task<IActionResult> OnPostApproveAsync(Guid id, string? comment) =>
        RunAsync(id, () => _service.ApproveAsync(id, new ReviewPurchaseRequestDto { Comment = comment }));

    public async Task<IActionResult> OnPostReceiveAsync(Guid id)
    {
        try
        {
            var detail = await _service.GetAsync(id);
            if (detail.PurchaseOrder is null)
                throw new BusinessException(PurchaseFlowErrors.ReceiptOrderNotReceivable);

            var lines = (ReceiptLines ?? new List<ReceiptLineInput>())
                .Where(line => line.Selected && line.Quantity > 0)
                .Select(line => new GoodsReceiptLineInputDto
                {
                    PurchaseOrderLineId = line.PurchaseOrderLineId,
                    Quantity = line.Quantity
                })
                .ToList();

            await _receiptService.CreateAsync(new CreateGoodsReceiptDto
            {
                PurchaseOrderId = detail.PurchaseOrder.Id,
                IdempotencyKey = ReceiptIdempotencyKey ?? string.Empty,
                ReceiptDate = ReceiptDate,
                Note = ReceiptNote,
                Lines = lines
            });
            return RedirectToPage("/Purchasing/Details", new { id });
        }
        catch (BusinessException ex)
        {
            Error = _localizer[ex.Code ?? "Error"];
            await LoadAsync(id, preserveReceiptInputs: true);
            return Page();
        }
        catch (AbpValidationException)
        {
            Error = "请检查收货日期、幂等标识和每条收货数量。";
            await LoadAsync(id, preserveReceiptInputs: true);
            return Page();
        }
        catch (AbpAuthorizationException)
        {
            return Forbid();
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
    }

    public Task<IActionResult> OnPostReturnAsync(Guid id, string? comment) =>
        RunAsync(id, () => _service.ReturnAsync(id, new ReviewPurchaseRequestDto { Comment = comment }));

    public static string ActionText(string action) => action switch
    {
        "Submitted" => "提交审核",
        "Approved" => "批准",
        "Returned" => "退回",
        _ => action
    };

    public static string OrderStatusText(PurchaseOrderStatus status) => status switch
    {
        PurchaseOrderStatus.Created => "已创建",
        PurchaseOrderStatus.Issued => "已发出",
        PurchaseOrderStatus.Received => "已收货",
        PurchaseOrderStatus.Closed => "已关闭",
        _ => status.ToString()
    };

    public static string ReceiptStatusText(PurchaseOrderReceiptDto receipt) =>
        receipt.IsFullyReceived ? "已收货" :
        receipt.IsPartiallyReceived ? "部分收货" :
        OrderStatusText(receipt.OrderStatus);

    /// <summary>
    /// 第二轮第 1 批：判断报价/订单关联的供应商档案是否仍在启用列表中。
    /// 只用本页已加载的启用档案做对比，不额外查库；用于在报价行/订单区块显示
    /// “已关联供应商档案”或“档案已停用”的提示。
    /// </summary>
    public bool IsKnownSupplier(Guid? supplierId) =>
        supplierId.HasValue && Suppliers.Any(x => x.Id == supplierId.Value);

    private async Task<IActionResult> RunAsync(Guid id, Func<Task> action)
    {
        try
        {
            await action();
            return RedirectToPage("/Purchasing/Details", new { id });
        }
        catch (BusinessException ex)
        {
            Error = _localizer[ex.Code ?? "Error"];
            await LoadAsync(id);
            return Page();
        }
        catch (AbpValidationException)
        {
            Error = "请检查必填项、金额范围和输入长度。";
            await LoadAsync(id);
            return Page();
        }
        catch (AbpAuthorizationException)
        {
            return Forbid();
        }
        catch (EntityNotFoundException)
        {
            return NotFound();
        }
    }

    private async Task LoadAsync(Guid id, bool preserveReceiptInputs = false)
    {
        PurchaseRequest = await _service.GetAsync(id);

        // 第二轮第 1 批：加载行编辑器的物料档案选项与报价表单的供应商档案选项。
        // 只取启用中的档案（停用档案不能用于新的明细/报价）；旧的手工录入路径保持不变。
        // PageSize 与服务端上限 200 对齐，避免请求 500 被截断造成“选项看起来变少”的误会。
        Materials = (await _materialService.GetListAsync(new MaterialListInput
        {
            IsActive = true,
            Page = 1,
            PageSize = 200
        })).Items;
        Suppliers = (await _supplierService.GetListAsync(new SupplierListInput
        {
            IsActive = true,
            Page = 1,
            PageSize = 200
        })).Items;

        CanEdit = PurchaseRequest.BuyerUserId == _currentUser.Id &&
                  (PurchaseRequest.Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Returned) &&
                  (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.EditRequests)).Succeeded;
        CanReview = PurchaseRequest.Status == PurchaseRequestStatus.PendingReview &&
                    (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ReviewRequests)).Succeeded;
        CanViewReceipts = (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ViewReceipts)).Succeeded;
        CanRegisterReceipts = CanViewReceipts &&
                              (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.RegisterReceipts)).Succeeded;
        PurchaseOrderReceipts = null;

        if (CanViewReceipts && PurchaseRequest.PurchaseOrder is { } order)
        {
            PurchaseOrderReceipts = await _receiptService.GetOrderAsync(order.Id);
            if (!preserveReceiptInputs)
            {
                ReceiptLines = PurchaseOrderReceipts.Lines.Select(line => new ReceiptLineInput
                {
                    PurchaseOrderLineId = line.PurchaseOrderLineId,
                    Quantity = line.RemainingQuantity
                }).ToList();
                ReceiptDate = DateTime.Today;
                ReceiptIdempotencyKey = Guid.NewGuid().ToString("N");
                ReceiptNote = null;
            }
        }
    }
}


