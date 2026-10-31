using System;
using System.Threading.Tasks;
using PurchaseFlow.Purchasing;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;

namespace PurchaseFlow.DbMigrator;

/// <summary>
/// 第二轮第 1 批：主数据演示种子（host-only）。
///
/// 为什么放 DbMigrator：演示环境首次启动就需要可选的供应商/物料档案，
/// 否则报价页、需求录入页的下拉为空，无法演示“从档案选择”的主流程。
/// 按 Code 幂等：重复执行（每次容器启动都会跑种子）不会产生重复档案。
/// </summary>
public class DemoMasterDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<Supplier, Guid> _suppliers;
    private readonly IRepository<Material, Guid> _materials;
    private readonly IGuidGenerator _guidGenerator;

    public DemoMasterDataSeedContributor(
        IRepository<Supplier, Guid> suppliers,
        IRepository<Material, Guid> materials,
        IGuidGenerator guidGenerator)
    {
        _suppliers = suppliers;
        _materials = materials;
        _guidGenerator = guidGenerator;
    }

    public async Task SeedAsync(DataSeedContext context)
    {
        // 只给 host 播种：租户侧主数据由各租户自行维护，演示数据不应污染租户库。
        if (context.TenantId.HasValue) return;

        await EnsureSupplierAsync("SUP-001", "华东办公家具", paymentTermsDays: 30, SupplierRating.A);
        await EnsureSupplierAsync("SUP-002", "南方办公", paymentTermsDays: 15, SupplierRating.B);
        await EnsureSupplierAsync("SUP-003", "北方机电", paymentTermsDays: 45, SupplierRating.B);

        await EnsureMaterialAsync("MAT-001", "办公椅", "人体工学", "把");
        await EnsureMaterialAsync("MAT-002", "会议桌", "2.4m", "张");
        await EnsureMaterialAsync("MAT-003", "深沟球轴承", "6205-2RS", "个");
        await EnsureMaterialAsync("MAT-004", "骨架油封", "NBR 20x3", "个");
    }

    /// <summary>按 Code 幂等：已存在则跳过，避免每次启动重复插入。</summary>
    private async Task EnsureSupplierAsync(string code, string name, int paymentTermsDays, SupplierRating rating)
    {
        if (await _suppliers.FindAsync(x => x.Code == code) is not null)
            return;

        await _suppliers.InsertAsync(new Supplier(
            _guidGenerator.Create(),
            tenantId: null,
            code: code,
            name: name,
            paymentTermsDays: paymentTermsDays,
            rating: rating));
    }

    /// <summary>按 Code 幂等：已存在则跳过。</summary>
    private async Task EnsureMaterialAsync(string code, string name, string specification, string unit)
    {
        if (await _materials.FindAsync(x => x.Code == code) is not null)
            return;

        await _materials.InsertAsync(new Material(
            _guidGenerator.Create(),
            tenantId: null,
            code: code,
            name: name,
            specification: specification,
            unit: unit));
    }
}
