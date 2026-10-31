using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace PurchaseFlow.Purchasing;

public interface IGoodsReceiptAppService : IApplicationService
{
    Task<PurchaseOrderReceiptDto> GetOrderAsync(Guid purchaseOrderId);
    Task<GoodsReceiptDto> CreateAsync(CreateGoodsReceiptDto input);
}
