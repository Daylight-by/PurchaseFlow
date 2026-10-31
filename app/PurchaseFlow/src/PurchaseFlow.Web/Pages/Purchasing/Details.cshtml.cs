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
    private readonly ISupplierInvoiceAppService _invoiceService;
    private readonly IPaymentAppService _paymentService;
    private readonly IAuthorizationService _authorization;
    private readonly ICurrentUser _currentUser;
    private readonly IStringLocalizer<PurchaseFlowResource> _localizer;

    public PurchaseRequestDetailDto PurchaseRequest { get; private set; } = new();
    public PurchaseOrderReceiptDto? PurchaseOrderReceipts { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanReview { get; private set; }

    /// <summary>
    /// 第二轮第 2 批：当前用户能否做二级批准。只有处于 PendingSecondApproval 且拥有
    /// ApproveHighValue 权限（演示里只有管理员）时才显示二级批准按钮；
    /// 最终“必须换人”的硬规则仍由领域层校验，页面只负责展示与提示。
    /// </summary>
    public bool CanSecondApprove { get; private set; }

    /// <summary>退回在待审核与二级待审两个阶段都允许，因此单独用一个权限标志控制退回按钮。</summary>
    public bool CanReturn { get; private set; }

    public bool CanViewReceipts { get; private set; }
    public bool CanRegisterReceipts { get; private set; }

    /// <summary>
    /// 第三轮（三单匹配）：发票/付款区块的权限。
    /// 查看、登记发票、批准付款拆成三个开关，页面只显示当前用户有权执行的按钮；
    /// 真正的状态与权限校验仍由应用服务 + [Authorize] 强制。
    /// </summary>
    public bool CanViewInvoices { get; private set; }
    public bool CanRegisterInvoices { get; private set; }
    public bool CanApprovePayments { get; private set; }

    /// <summary>该采购订单已登记的发票列表（含付款申请状态），用于详情页内嵌展示。</summary>
    public List<SupplierInvoiceDto> Invoices { get; private set; } = new();

    /// <summary>
    /// 登记发票表单的订单行快照（订购/已收/已开票/剩余可开票）。
    /// 只有订单存在且能看到收货数据时才有值；服务端提交时仍会重新校验匹配规则。
    /// </summary>
    public List<InvoiceableLine> InvoiceableLines { get; private set; } = new();

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

    /// <summary>
    /// 第二轮第 3 批：按物料档案缓存“上次采购价”参考，供报价录入行展示。
    /// 只读参考，不参与任何金额计算；无历史的物料不会出现在字典里（页面显示“暂无历史采购价”）。
    /// </summary>
    public Dictionary<Guid, MaterialPriceReferenceDto> LastPurchasePrices { get; private set; } = new();

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

    // 第三轮：登记发票表单。发票号/日期/备注 + 每行“本次开票数量”；
    // 单价由订单成交价只读带出并隐藏提交，服务端会核对，避免页面改价。
    [BindProperty]
    public string InvoiceNumber { get; set; } = string.Empty;

    [BindProperty]
    public DateTime InvoiceDate { get; set; } = DateTime.Today;

    [BindProperty]
    public string? InvoiceNote { get; set; }

    [BindProperty]
    public List<InvoiceLineInput> InvoiceLines { get; set; } = new();

    public DetailsModel(IPurchaseRequestAppService service, IGoodsReceiptAppService receiptService,
        IMaterialAppService materialService, ISupplierAppService supplierService,
        ISupplierInvoiceAppService invoiceService, IPaymentAppService paymentService,
        IAuthorizationService authorization,
        ICurrentUser currentUser, IStringLocalizer<PurchaseFlowResource> localizer)
    {
        _service = service;
        _receiptService = receiptService;
        _materialService = materialService;
        _supplierService = supplierService;
        _invoiceService = invoiceService;
        _paymentService = paymentService;
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
    /// 第二轮第 3 批：validUntil 可选（业务日期，含当天）；为空表示长期有效，
    /// 是否允许录入过去日期由应用层拦截（页面只负责传值）。
    /// </summary>
    public Task<IActionResult> OnPostAddQuoteAsync(Guid id, string supplierName, Guid? supplierId,
        int deliveryDays, string? note, DateTime? validUntil) => RunAsync(id, () => _service.AddQuoteAsync(id, new AddSupplierQuoteDto
        {
            SupplierName = supplierName ?? string.Empty,
            // 第二轮第 1 批：空值表示沿用旧的手工文本供应商（向后兼容）；有值则关联档案。
            SupplierId = supplierId,
            DeliveryDays = deliveryDays,
            Note = note,
            // 第二轮第 3 批：报价有效期；留空即长期有效（null）。
            ValidUntil = validUntil,
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

    /// <summary>
    /// 第二轮第 2 批：二级审批。高金额需求一级批准后进入 PendingSecondApproval，
    /// 由另一位具备 ApproveHighValue 权限的管理员在这里最终批准并生成订单。
    /// 页面不做换人判断，交由领域层抛 SecondApprovalRequiresDifferentUser，避免 UI 绕过业务硬规则。
    /// </summary>
    public Task<IActionResult> OnPostApproveSecondLevelAsync(Guid id, string? comment) =>
        RunAsync(id, () => _service.ApproveSecondLevelAsync(id, new ReviewPurchaseRequestDto { Comment = comment }));

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

    /// <summary>
    /// 第三轮：登记供应商发票（三单匹配）。页面提交“订单行 + 本次开票数量 + 单价（隐藏只读）”，
    /// 单价/数量/收货匹配校验全部在应用服务内完成；页面只负责把输入原样传下去。
    /// </summary>
    public Task<IActionResult> OnPostRegisterInvoiceAsync(Guid id) => RunAsync(id, async () =>
    {
        var detail = await _service.GetAsync(id);
        var order = detail.PurchaseOrder
                    ?? throw new BusinessException(PurchaseFlowErrors.InvalidStatus);

        var lines = (InvoiceLines ?? new List<InvoiceLineInput>())
            .Where(line => line.PurchaseOrderLineId != Guid.Empty && line.Quantity > 0)
            .Select(line => new CreateSupplierInvoiceLineDto
            {
                PurchaseOrderLineId = line.PurchaseOrderLineId,
                Quantity = line.Quantity,
                // 单价来自订单行只读隐藏字段；即使被篡改，应用服务也会用订单成交单价逐行拒绝。
                UnitPrice = line.UnitPrice
            }).ToList();

        await _invoiceService.RegisterAsync(new CreateSupplierInvoiceDto
        {
            PurchaseOrderId = order.Id,
            InvoiceNumber = InvoiceNumber ?? string.Empty,
            InvoiceDate = InvoiceDate,
            Note = InvoiceNote,
            Lines = lines
        });
    });

    /// <summary>
    /// 第三轮：对“已匹配且未申请付款”的发票发起付款申请；金额留空表示按发票全额申请，
    /// 服务端会校验金额范围与“同一发票只能一笔付款申请”。
    /// </summary>
    public Task<IActionResult> OnPostRequestPaymentAsync(Guid id, Guid invoiceId, decimal? amount, string? note) =>
        RunAsync(id, () => _paymentService.RequestAsync(invoiceId, new CreatePaymentApplicationDto
        {
            Amount = amount,
            Note = note
        }));

    /// <summary>
    /// 第三轮：管理员批准付款申请（ApprovePayments 权限由应用服务与页面按钮双重控制）。
    /// </summary>
    public Task<IActionResult> OnPostApprovePaymentAsync(Guid id, Guid paymentId, string? comment) =>
        RunAsync(id, () => _paymentService.ApproveAsync(paymentId, new ReviewPaymentApplicationDto
        {
            Comment = comment
        }));

    /// <summary>
    /// 第三轮：管理员登记付款。未选择日期时默认今天；状态流转（Approved→Paid + 发票 Paid）
    /// 由领域层强制，重复登记会被拒绝。
    /// </summary>
    public Task<IActionResult> OnPostRegisterPaymentAsync(Guid id, Guid paymentId, DateTime paymentDate, string? paymentNote) =>
        RunAsync(id, () => _paymentService.RegisterPaymentAsync(paymentId, new RegisterPaymentDto
        {
            PaymentDate = paymentDate,
            Note = paymentNote
        }));

    public static string ActionText(string action) => action switch
    {
        "Submitted" => "提交审核",
        "Approved" => "批准",
        // 第二轮第 2 批：分级审批在时间线上分别显示一级/二级，便于审计“谁批的哪一级”。
        "ApprovedLevel1" => "一级批准",
        "ApprovedSecondLevel" => "二级批准",
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

    /// <summary>第三轮：发票状态中文文案（Matched=已匹配 / PaymentRequested=已申请付款 / Paid=已付款）。</summary>
    public static string InvoiceStatusText(SupplierInvoiceStatus status) => status switch
    {
        SupplierInvoiceStatus.Matched => "已匹配",
        SupplierInvoiceStatus.PaymentRequested => "已申请付款",
        SupplierInvoiceStatus.Paid => "已付款",
        _ => status.ToString()
    };

    /// <summary>第三轮：付款申请状态中文文案；null 表示尚未申请付款。</summary>
    public static string PaymentStatusText(PaymentApplicationStatus? status) => status switch
    {
        null => "未申请付款",
        PaymentApplicationStatus.PendingApproval => "等待批准",
        PaymentApplicationStatus.Approved => "已批准待付款",
        PaymentApplicationStatus.Paid => "已付款",
        _ => status.Value.ToString()
    };

    /// <summary>
    /// 第二轮第 1 批：判断报价/订单关联的供应商档案是否仍在启用列表中。
    /// 只用本页已加载的启用档案做对比，不额外查库；用于在报价行/订单区块显示
    /// “已关联供应商档案”或“档案已停用”的提示。
    /// </summary>
    public bool IsKnownSupplier(Guid? supplierId) =>
        supplierId.HasValue && Suppliers.Any(x => x.Id == supplierId.Value);

    /// <summary>
    /// 第二轮第 3 批：取某物料档案的“上次采购价”参考；未关联档案或无历史订单时返回 null，
    /// 页面据此显示“暂无历史采购价”。只读信息，不参与金额计算。
    /// </summary>
    public MaterialPriceReferenceDto? GetLastPurchasePrice(Guid? materialId) =>
        materialId.HasValue && LastPurchasePrices.TryGetValue(materialId.Value, out var reference)
            ? reference
            : null;

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

        // 第二轮第 3 批：报价录入行展示“上次采购价”（最近一次已生成采购订单的成交单价）。
        // 对当前明细里不同的物料档案各查一次，缓存成字典避免视图里重复调用服务；
        // 查询结果只读参考，不参与任何金额计算；无历史的物料不写入字典，页面显示空态。
        LastPurchasePrices = new Dictionary<Guid, MaterialPriceReferenceDto>();
        var materialIds = PurchaseRequest.Lines
            .Where(line => line.MaterialId.HasValue)
            .Select(line => line.MaterialId!.Value)
            .Distinct()
            .ToList();
        foreach (var materialId in materialIds)
        {
            var reference = await _materialService.GetLastPurchasePriceAsync(materialId);
            if (reference is not null)
            {
                LastPurchasePrices[materialId] = reference;
            }
        }

        CanEdit = PurchaseRequest.BuyerUserId == _currentUser.Id &&
                  (PurchaseRequest.Status is PurchaseRequestStatus.Draft or PurchaseRequestStatus.Returned) &&
                  (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.EditRequests)).Succeeded;
        CanReview = PurchaseRequest.Status == PurchaseRequestStatus.PendingReview &&
                    (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ReviewRequests)).Succeeded;
        // 第二轮第 2 批：二级审批按钮只在“二级待审 + 有 ApproveHighValue 权限”时显示；
        // 是否换人由领域层最终校验，页面只提示“必须换人”。
        CanSecondApprove = PurchaseRequest.Status == PurchaseRequestStatus.PendingSecondApproval &&
                           (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ApproveHighValue)).Succeeded;
        // 退回在待审核与二级待审都允许，因此用独立标志控制退回按钮，避免二级待审页缺少退回入口。
        CanReturn = (PurchaseRequest.Status is PurchaseRequestStatus.PendingReview or PurchaseRequestStatus.PendingSecondApproval) &&
                    (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ReviewRequests)).Succeeded;
        CanViewReceipts = (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ViewReceipts)).Succeeded;
        CanRegisterReceipts = CanViewReceipts &&
                              (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.RegisterReceipts)).Succeeded;

        // 第三轮（三单匹配）：发票/付款区块权限。查看与登记/批准分开，
        // 页面只显示当前用户能执行的按钮；应用服务仍按权限与状态强制校验。
        CanViewInvoices = (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ViewInvoices)).Succeeded;
        CanRegisterInvoices = CanViewInvoices &&
                              (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.RegisterInvoices)).Succeeded;
        CanApprovePayments = CanViewInvoices &&
                             (await _authorization.AuthorizeAsync(User, PurchaseFlowPermissions.ApprovePayments)).Succeeded;
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

        // 第三轮：加载该采购订单的发票列表，并按“订购 / 已收 / 已开票 / 剩余可开票”组装登记表单快照。
        // 只有订单存在且有发票查看权限时才查；登记发票还需要收货数据（已收数量），
        // 因此没有收货查看权限时表单不展示可输入行。
        Invoices = new List<SupplierInvoiceDto>();
        InvoiceableLines = new List<InvoiceableLine>();
        if (CanViewInvoices && PurchaseRequest.PurchaseOrder is { } purchaseOrder)
        {
            Invoices = await _invoiceService.GetOrderInvoicesAsync(purchaseOrder.Id);
            var invoicedByLine = Invoices
                .SelectMany(invoice => invoice.Lines)
                .GroupBy(line => line.PurchaseOrderLineId)
                .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));

            if (PurchaseOrderReceipts is not null)
            {
                InvoiceableLines = PurchaseOrderReceipts.Lines.Select(receiptLine =>
                {
                    invoicedByLine.TryGetValue(receiptLine.PurchaseOrderLineId, out var invoiced);
                    var remaining = receiptLine.ReceivedQuantity - invoiced;
                    if (remaining < 0) remaining = 0;
                    return new InvoiceableLine
                    {
                        PurchaseOrderLineId = receiptLine.PurchaseOrderLineId,
                        ItemName = receiptLine.ItemName,
                        Specification = receiptLine.Specification,
                        OrderedQuantity = receiptLine.OrderedQuantity,
                        ReceivedQuantity = receiptLine.ReceivedQuantity,
                        InvoicedQuantity = invoiced,
                        RemainingQuantity = remaining,
                        UnitPrice = purchaseOrder.Lines
                            .FirstOrDefault(line => line.Id == receiptLine.PurchaseOrderLineId)?.UnitPrice ?? 0m
                    };
                }).ToList();

                // 表单校验失败回到页面时保留用户已填数量；否则按订单行初始化默认空行。
                if (InvoiceLines.Count == 0)
                {
                    InvoiceLines = InvoiceableLines.Select(line => new InvoiceLineInput
                    {
                        PurchaseOrderLineId = line.PurchaseOrderLineId,
                        UnitPrice = line.UnitPrice
                    }).ToList();
                    InvoiceDate = DateTime.Today;
                }
            }
        }
    }
}


