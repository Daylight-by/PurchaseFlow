using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using PurchaseFlow.Permissions;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace PurchaseFlow.Purchasing;

/// <summary>
/// 第二轮第 1 批：物料档案应用服务。
/// 查看与维护拆成两个权限；采购明细录入页只需要 MasterData 就能读取可选物料。
/// Code 唯一性属于跨实体规则，放应用层（领域实体不直接查仓储）。
/// </summary>
[Authorize(PurchaseFlowPermissions.MasterData)]
public class MaterialAppService : ApplicationService, IMaterialAppService
{
    private readonly IRepository<Material, Guid> _materials;

    // 第二轮第 3 批：历史采购价要查“订单明细 join 采购订单”，因此注入订单侧仓储只读查询。
    private readonly IRepository<PurchaseOrderLine, Guid> _orderLines;
    private readonly IRepository<PurchaseOrder, Guid> _orders;

    public MaterialAppService(
        IRepository<Material, Guid> materials,
        IRepository<PurchaseOrderLine, Guid> orderLines,
        IRepository<PurchaseOrder, Guid> orders)
    {
        _materials = materials;
        _orderLines = orderLines;
        _orders = orders;
    }

    public async Task<MaterialListDto> GetListAsync(MaterialListInput input)
    {
        var query = await _materials.GetQueryableAsync();
        // 主数据按租户隔离：host（TenantId=null）与租户各自维护自己的档案。
        query = query.Where(x => x.TenantId == CurrentTenant.Id);

        if (input.IsActive.HasValue)
            query = query.Where(x => x.IsActive == input.IsActive.Value);

        var keyword = input.Keyword?.Trim();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            // Code 统一存大写，关键字按大写匹配编码；Name/规格原样匹配。
            var codeKeyword = keyword.ToUpperInvariant();
            query = query.Where(x =>
                x.Code.Contains(codeKeyword) || x.Name.Contains(keyword) || x.Specification.Contains(keyword));
        }

        var result = new MaterialListDto { PageSize = Math.Clamp(input.PageSize, 1, 200) };
        result.TotalCount = await AsyncExecuter.CountAsync(query);
        var lastPage = Math.Max(1, (result.TotalCount + result.PageSize - 1) / result.PageSize);
        result.Page = Math.Clamp(input.Page, 1, lastPage);
        var items = await AsyncExecuter.ToListAsync(query
            .OrderBy(x => x.Code)
            .Skip((result.Page - 1) * result.PageSize)
            .Take(result.PageSize));
        result.Items = items.Select(Map).ToList();
        return result;
    }

    public async Task<MaterialDto> GetAsync(Guid id)
    {
        var material = await _materials.FindAsync(id);
        if (material is null)
            throw new EntityNotFoundException(typeof(Material), id);
        return Map(material);
    }

    [Authorize(PurchaseFlowPermissions.ManageMasterData)]
    public async Task<MaterialDto> CreateAsync(CreateMaterialDto input)
    {
        // 先构造实体：复用领域层的编码规范化（trim+大写）与格式校验，
        // 再用规范化后的 Code 做租户内查重。
        var material = new Material(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Code,
            input.Name,
            input.Specification,
            input.Unit,
            input.Category,
            input.Note);

        await EnsureCodeUniqueAsync(material.Code);
        await _materials.InsertAsync(material);
        return Map(material);
    }

    [Authorize(PurchaseFlowPermissions.ManageMasterData)]
    public async Task<MaterialDto> UpdateAsync(Guid id, UpdateMaterialDto input)
    {
        var material = await _materials.GetAsync(id);
        // 领域方法内部会重新规范化/校验编码；查重时排除自身。
        material.Update(input.Code, input.Name, input.Specification, input.Unit, input.Category, input.Note);
        await EnsureCodeUniqueAsync(material.Code, id);
        await _materials.UpdateAsync(material);
        return Map(material);
    }

    [Authorize(PurchaseFlowPermissions.ManageMasterData)]
    public async Task<MaterialDto> SetActiveAsync(Guid id, bool isActive)
    {
        var material = await _materials.GetAsync(id);
        if (isActive)
            material.Activate();
        else
            material.Deactivate();
        await _materials.UpdateAsync(material);
        return Map(material);
    }

    /// <summary>
    /// 第二轮第 3 批：查询该物料最近一次已生成采购订单的成交明细，作为报价录入时的
    /// 历史价格参考（只读，不参与任何金额计算）。
    /// 实现：订单明细 join 采购订单，按明细上的 MaterialId 过滤（该字段在订单生成时
    /// 从报价来源需求行继承），再按订单创建时间倒序取第一条；无历史返回 null。
    /// 沿用类级 MasterData 查看权限（所有业务角色都有）。
    /// </summary>
    public async Task<MaterialPriceReferenceDto?> GetLastPurchasePriceAsync(Guid materialId)
    {
        var orderLinesQuery = await _orderLines.GetQueryableAsync();
        var ordersQuery = await _orders.GetQueryableAsync();

        var latest = await AsyncExecuter.FirstOrDefaultAsync(
            from line in orderLinesQuery
            join order in ordersQuery on line.PurchaseOrderId equals order.Id
            where line.MaterialId == materialId
            orderby order.CreationTime descending
            select new MaterialPriceReferenceDto
            {
                MaterialId = materialId,
                UnitPrice = line.UnitPrice,
                Quantity = line.Quantity,
                OrderNumber = order.OrderNumber,
                SupplierName = order.SupplierName,
                OrderedAt = order.CreationTime
            });

        return latest;
    }

    /// <summary>
    /// 当前租户内 Code 唯一校验。创建/修改都走这里；数据库还有 (TenantId, Code)
    /// 唯一索引作为并发兜底。
    /// </summary>
    private async Task EnsureCodeUniqueAsync(string code, Guid? exceptId = null)
    {
        var query = await _materials.GetQueryableAsync();
        var exists = await AsyncExecuter.AnyAsync(query.Where(x =>
            x.TenantId == CurrentTenant.Id &&
            x.Code == code &&
            (!exceptId.HasValue || x.Id != exceptId.Value)));
        if (exists)
            throw new BusinessException(PurchaseFlowErrors.DuplicateMaterialCode);
    }

    private static MaterialDto Map(Material material) => new()
    {
        Id = material.Id,
        Code = material.Code,
        Name = material.Name,
        Specification = material.Specification,
        Unit = material.Unit,
        Category = material.Category,
        IsActive = material.IsActive,
        Note = material.Note,
        CreationTime = material.CreationTime
    };
}
