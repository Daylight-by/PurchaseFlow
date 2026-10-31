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

    /// <summary>一级审批（普通审核员权限）。高金额请求在这里进入二级待审，不直接生成订单。</summary>
    Task ApproveAsync(Guid id, ReviewPurchaseRequestDto input);

    /// <summary>二级审批（仅高金额请求，需 ApproveHighValue 权限，且必须与一级审批人不同）。</summary>
    Task ApproveSecondLevelAsync(Guid id, ReviewPurchaseRequestDto input);

    Task ReturnAsync(Guid id, ReviewPurchaseRequestDto input);
}

