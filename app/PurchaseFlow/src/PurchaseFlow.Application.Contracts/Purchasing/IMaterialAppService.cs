using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 1 批：物料档案应用服务。采购明细录入页需要查看权限读取可选物料，
/// 只有维护权限才能新增/修改/停用。
/// </summary>
public interface IMaterialAppService : IApplicationService
{
    Task<MaterialListDto> GetListAsync(MaterialListInput input);
    Task<MaterialDto> GetAsync(Guid id);
    Task<MaterialDto> CreateAsync(CreateMaterialDto input);
    Task<MaterialDto> UpdateAsync(Guid id, UpdateMaterialDto input);
    Task<MaterialDto> SetActiveAsync(Guid id, bool isActive);

    /// <summary>
    /// 第二轮第 3 批：查询该物料最近一次采购订单的成交单价（无历史返回 null）。
    /// 报价页面用它显示“上次采购价”，只读参考，不参与金额计算。
    /// </summary>
    Task<MaterialPriceReferenceDto?> GetLastPurchasePriceAsync(Guid materialId);
}
