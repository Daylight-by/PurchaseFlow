using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace PurchaseFlow.Purchasing;

public interface IPurchaseRequestAppService : IApplicationService
{
    Task<PurchaseRequestListDto> GetListAsync(PurchaseRequestListInput input);
    Task<PurchaseDashboardDto> GetDashboardAsync();
    Task<PurchaseRequestDetailDto> GetAsync(Guid id);
    Task<Guid> CreateAsync(CreatePurchaseRequestDto input);
    Task UpdateAsync(Guid id, UpdatePurchaseRequestDto input);
    Task<Guid> AddQuoteAsync(Guid id, AddSupplierQuoteDto input);
    Task DeleteQuoteAsync(Guid id, Guid quoteId);
    Task SelectQuoteAsync(Guid id, SelectSupplierQuoteDto input);
    Task SubmitAsync(Guid id);
    Task ApproveAsync(Guid id, ReviewPurchaseRequestDto input);
    Task ReturnAsync(Guid id, ReviewPurchaseRequestDto input);
}

