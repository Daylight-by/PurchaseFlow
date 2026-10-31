using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 1 批：供应商档案应用服务。查看与维护分离（MasterData / ManageMasterData），
/// 报价录入页只需要查看权限即可读取可选供应商。
/// </summary>
public interface ISupplierAppService : IApplicationService
{
    Task<SupplierListDto> GetListAsync(SupplierListInput input);
    Task<SupplierDto> GetAsync(Guid id);
    Task<SupplierDto> CreateAsync(CreateSupplierDto input);
    Task<SupplierDto> UpdateAsync(Guid id, UpdateSupplierDto input);
    Task<SupplierDto> SetActiveAsync(Guid id, bool isActive);
}
