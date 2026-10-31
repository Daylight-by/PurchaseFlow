using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第三轮（三单匹配）：供应商发票应用服务。
/// 登记时逐行执行“订单单价 = 发票单价、发票数量 ≤ 已收货未开票数量”的匹配校验，
/// 不匹配直接拒绝，避免把对不上的发票带进付款流程。
/// </summary>
public interface ISupplierInvoiceAppService : IApplicationService
{
    Task<InvoiceListDto> GetListAsync(InvoiceListInput input);
    Task<SupplierInvoiceDto> GetAsync(Guid id);
    Task<List<SupplierInvoiceDto>> GetOrderInvoicesAsync(Guid purchaseOrderId);
    Task<SupplierInvoiceDto> RegisterAsync(CreateSupplierInvoiceDto input);
}
