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
/// 第二轮第 1 批：供应商档案应用服务。
/// 查看与维护拆成两个权限：业务角色能读档案用于选择，只有管理员能维护，
/// 避免“能看档案”被误当成“能改档案”。
/// Code 唯一性属于跨实体规则，放应用层（领域实体不直接查仓储，符合 ABP 分层）。
/// </summary>
[Authorize(PurchaseFlowPermissions.MasterData)]
public class SupplierAppService : ApplicationService, ISupplierAppService
{
    private readonly IRepository<Supplier, Guid> _suppliers;

    public SupplierAppService(IRepository<Supplier, Guid> suppliers)
    {
        _suppliers = suppliers;
    }

    public async Task<SupplierListDto> GetListAsync(SupplierListInput input)
    {
        var query = await _suppliers.GetQueryableAsync();
        // 主数据按租户隔离：host（TenantId=null）与租户各自维护自己的档案。
        query = query.Where(x => x.TenantId == CurrentTenant.Id);

        if (input.IsActive.HasValue)
            query = query.Where(x => x.IsActive == input.IsActive.Value);

        var keyword = input.Keyword?.Trim();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            // Code 统一存大写，关键字按大写匹配编码；Name 原样匹配，
            // 避免用户输入小写编码时搜不到。
            var codeKeyword = keyword.ToUpperInvariant();
            query = query.Where(x => x.Code.Contains(codeKeyword) || x.Name.Contains(keyword));
        }

        var result = new SupplierListDto { PageSize = Math.Clamp(input.PageSize, 1, 200) };
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

    public async Task<SupplierDto> GetAsync(Guid id)
    {
        var supplier = await _suppliers.FindAsync(id);
        if (supplier is null)
            throw new EntityNotFoundException(typeof(Supplier), id);
        return Map(supplier);
    }

    [Authorize(PurchaseFlowPermissions.ManageMasterData)]
    public async Task<SupplierDto> CreateAsync(CreateSupplierDto input)
    {
        // 先构造实体：复用领域层的编码规范化（trim+大写）与格式校验，
        // 再用规范化后的 Code 做租户内查重，避免大小写/空格绕过唯一性。
        var supplier = new Supplier(
            GuidGenerator.Create(),
            CurrentTenant.Id,
            input.Code,
            input.Name,
            input.ContactName,
            input.ContactPhone,
            input.PaymentTermsDays,
            input.Rating,
            input.Note);

        await EnsureCodeUniqueAsync(supplier.Code);
        await _suppliers.InsertAsync(supplier);
        return Map(supplier);
    }

    [Authorize(PurchaseFlowPermissions.ManageMasterData)]
    public async Task<SupplierDto> UpdateAsync(Guid id, UpdateSupplierDto input)
    {
        var supplier = await _suppliers.GetAsync(id);
        // 领域方法内部会重新规范化/校验编码；查重时排除自身。
        supplier.Update(input.Code, input.Name, input.ContactName, input.ContactPhone,
            input.PaymentTermsDays, input.Rating, input.Note);
        await EnsureCodeUniqueAsync(supplier.Code, id);
        await _suppliers.UpdateAsync(supplier);
        return Map(supplier);
    }

    [Authorize(PurchaseFlowPermissions.ManageMasterData)]
    public async Task<SupplierDto> SetActiveAsync(Guid id, bool isActive)
    {
        var supplier = await _suppliers.GetAsync(id);
        if (isActive)
            supplier.Activate();
        else
            supplier.Deactivate();
        await _suppliers.UpdateAsync(supplier);
        return Map(supplier);
    }

    /// <summary>
    /// 当前租户内 Code 唯一校验。创建/修改都走这里；数据库还有 (TenantId, Code)
    /// 唯一索引作为并发兜底。
    /// </summary>
    private async Task EnsureCodeUniqueAsync(string code, Guid? exceptId = null)
    {
        var query = await _suppliers.GetQueryableAsync();
        var exists = await AsyncExecuter.AnyAsync(query.Where(x =>
            x.TenantId == CurrentTenant.Id &&
            x.Code == code &&
            (!exceptId.HasValue || x.Id != exceptId.Value)));
        if (exists)
            throw new BusinessException(PurchaseFlowErrors.DuplicateSupplierCode);
    }

    private static SupplierDto Map(Supplier supplier) => new()
    {
        Id = supplier.Id,
        Code = supplier.Code,
        Name = supplier.Name,
        ContactName = supplier.ContactName,
        ContactPhone = supplier.ContactPhone,
        PaymentTermsDays = supplier.PaymentTermsDays,
        Rating = supplier.Rating,
        IsActive = supplier.IsActive,
        Note = supplier.Note,
        CreationTime = supplier.CreationTime
    };
}
