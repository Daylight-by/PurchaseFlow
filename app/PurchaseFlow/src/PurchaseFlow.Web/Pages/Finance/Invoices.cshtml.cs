using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using PurchaseFlow.Permissions;
using PurchaseFlow.Purchasing;

namespace PurchaseFlow.Web.Pages.Finance;

/// <summary>
/// 第三轮：发票总览（只读）。为什么单独一页：三单匹配与付款状态需要在财务视角集中核对，
/// 但所有写操作（登记发票/申请、批准、登记付款）都留在采购订单详情页，避免两处入口状态不一致。
/// </summary>
[Authorize(PurchaseFlowPermissions.ViewInvoices)]
public class InvoicesModel : PurchaseFlowPageModel
{
    private const int PageSize = 50;

    private readonly ISupplierInvoiceAppService _service;

    public InvoiceListDto Result { get; private set; } = new();
    public InvoiceListInput Filter { get; private set; } = new();
    public int TotalPages => Math.Max(1, (Result.TotalCount + Math.Max(1, Result.PageSize) - 1) / Math.Max(1, Result.PageSize));

    public InvoicesModel(ISupplierInvoiceAppService service)
    {
        _service = service;
    }

    public async Task OnGetAsync(string? keyword, SupplierInvoiceStatus? status, int pageNumber = 1)
    {
        Filter = new InvoiceListInput
        {
            Keyword = keyword,
            Status = status,
            Page = Math.Max(1, pageNumber),
            PageSize = PageSize
        };
        Result = await _service.GetListAsync(Filter);
    }

    /// <summary>第三轮：发票匹配状态中文文案。</summary>
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
}
