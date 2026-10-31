using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// 第二轮第 1 批（主数据）的持久化/应用层集成测试。
/// 重点：档案 CRUD 与唯一性、需求明细关联物料后的名称快照覆盖、
/// 报价关联供应商后的名称快照覆盖、订单继承供应商、停用档案拒绝新引用。
/// 这些规则跨领域/应用/数据库三层，只有走真实 SQLite 才能证明接得起来。
/// </summary>
[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class MasterDataTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly ISupplierAppService _suppliers;
    private readonly IMaterialAppService _materials;
    private readonly IPurchaseRequestAppService _requests;
    private readonly IRepository<Supplier, Guid> _supplierRepository;
    private readonly IRepository<Material, Guid> _materialRepository;
    private readonly IRepository<PurchaseRequestLine, Guid> _lineRepository;

    public MasterDataTests()
    {
        _suppliers = GetRequiredService<ISupplierAppService>();
        _materials = GetRequiredService<IMaterialAppService>();
        _requests = GetRequiredService<IPurchaseRequestAppService>();
        _supplierRepository = GetRequiredService<IRepository<Supplier, Guid>>();
        _materialRepository = GetRequiredService<IRepository<Material, Guid>>();
        _lineRepository = GetRequiredService<IRepository<PurchaseRequestLine, Guid>>();
    }

    [Fact]
    public async Task Supplier_and_material_master_data_are_persisted_and_codes_are_normalized()
    {
        var supplier = await WithUnitOfWorkAsync(() => _suppliers.CreateAsync(new CreateSupplierDto
        {
            Code = "  sup-101 ",
            Name = "测试供应商",
            ContactName = "王五",
            ContactPhone = "13600000000",
            PaymentTermsDays = 30,
            Rating = SupplierRating.A,
            Note = "集成测试"
        }));
        var material = await WithUnitOfWorkAsync(() => _materials.CreateAsync(new CreateMaterialDto
        {
            Code = " mat-101 ",
            Name = "测试物料",
            Specification = "规格-1",
            Unit = "个",
            Category = "测试"
        }));

        Assert.Equal("SUP-101", supplier.Code);
        Assert.Equal(SupplierRating.A, supplier.Rating);
        Assert.True(supplier.IsActive);
        Assert.Equal("MAT-101", material.Code);
        Assert.True(material.IsActive);

        var loadedSupplier = await WithUnitOfWorkAsync(() => _suppliers.GetAsync(supplier.Id));
        var loadedMaterial = await WithUnitOfWorkAsync(() => _materials.GetAsync(material.Id));
        Assert.Equal("测试供应商", loadedSupplier.Name);
        Assert.Equal("测试物料", loadedMaterial.Name);

        var supplierList = await WithUnitOfWorkAsync(() => _suppliers.GetListAsync(new SupplierListInput
        {
            Keyword = "sup-101"
        }));
        Assert.Single(supplierList.Items);
        var materialList = await WithUnitOfWorkAsync(() => _materials.GetListAsync(new MaterialListInput
        {
            Keyword = "mat-101"
        }));
        Assert.Single(materialList.Items);
    }

    [Fact]
    public async Task Duplicate_supplier_and_material_codes_are_rejected()
    {
        await WithUnitOfWorkAsync(() => _suppliers.CreateAsync(new CreateSupplierDto
        {
            Code = "SUP-DUP",
            Name = "重复供应商"
        }));
        await WithUnitOfWorkAsync(() => _materials.CreateAsync(new CreateMaterialDto
        {
            Code = "MAT-DUP",
            Name = "重复物料",
            Unit = "个"
        }));

        var supplierException = await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(() => _suppliers.CreateAsync(new CreateSupplierDto
            {
                Code = "sup-dup",
                Name = "大小写不同的重复供应商"
            })));
        Assert.Equal(PurchaseFlowErrors.DuplicateSupplierCode, supplierException.Code);

        var materialException = await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(() => _materials.CreateAsync(new CreateMaterialDto
            {
                Code = "MAT-DUP",
                Name = "重复物料 2",
                Unit = "个"
            })));
        Assert.Equal(PurchaseFlowErrors.DuplicateMaterialCode, materialException.Code);
    }

    [Fact]
    public async Task Database_unique_index_rejects_a_duplicate_host_supplier_code()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await _supplierRepository.InsertAsync(
                new Supplier(Guid.NewGuid(), null, "SUP-HOST-DUP", "host 供应商 1"), autoSave: true);
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await WithUnitOfWorkAsync(async () =>
            {
                await _supplierRepository.InsertAsync(
                    new Supplier(Guid.NewGuid(), null, "SUP-HOST-DUP", "host 供应商 2"), autoSave: true);
            }));
    }

    [Fact]
    public async Task A_request_line_linked_to_a_material_uses_the_master_snapshot()
    {
        var material = await WithUnitOfWorkAsync(() => _materials.CreateAsync(new CreateMaterialDto
        {
            Code = "MAT-SNAP",
            Name = "档案物料名称",
            Specification = "档案规格",
            Unit = "台"
        }));

        var requestId = await WithUnitOfWorkAsync(() => _requests.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"MD-LINE-{Guid.NewGuid():N}",
            Description = "物料快照测试",
            SaleAmount = 10000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new()
                {
                    MaterialId = material.Id,
                    // 客户端文本故意与档案不同：应用层必须用档案名称/规格覆盖快照。
                    ItemName = "客户端乱填的名称",
                    Specification = "客户端乱填的规格",
                    Quantity = 2m,
                    UnitPrice = 500m
                }
            }
        }));

        var detail = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var line = Assert.Single(detail.Lines);
        Assert.Equal(material.Id, line.MaterialId);
        Assert.Equal("档案物料名称", line.ItemName);
        Assert.Equal("档案规格", line.Specification);

        var persisted = await WithUnitOfWorkAsync(async () =>
            await (await _lineRepository.GetQueryableAsync()).Where(x => x.PurchaseRequestId == requestId).ToListAsync());
        var persistedLine = Assert.Single(persisted);
        Assert.Equal(material.Id, persistedLine.MaterialId);
        Assert.Equal("档案物料名称", persistedLine.ItemName);
    }

    [Fact]
    public async Task An_inactive_material_can_not_be_used_for_a_new_request_line()
    {
        var material = await WithUnitOfWorkAsync(() => _materials.CreateAsync(new CreateMaterialDto
        {
            Code = "MAT-OFF",
            Name = "停用物料",
            Unit = "个"
        }));
        await WithUnitOfWorkAsync(() => _materials.SetActiveAsync(material.Id, false));

        var exception = await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(() => _requests.CreateAsync(new CreatePurchaseRequestDto
            {
                OrderReference = $"MD-OFF-{Guid.NewGuid():N}",
                Description = "停用物料应被拒绝",
                SaleAmount = 1000m,
                Lines = new List<CreatePurchaseRequestLineDto>
                {
                    new() { MaterialId = material.Id, ItemName = "x", Quantity = 1m, UnitPrice = 10m }
                }
            })));
        Assert.Equal(PurchaseFlowErrors.MaterialInactive, exception.Code);
    }

    [Fact]
    public async Task Database_unique_index_rejects_a_duplicate_host_material_code()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            await _materialRepository.InsertAsync(
                new Material(Guid.NewGuid(), null, "MAT-HOST-DUP", "host 物料 1", unit: "个"), autoSave: true);
        });

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
            await WithUnitOfWorkAsync(async () =>
            {
                await _materialRepository.InsertAsync(
                    new Material(Guid.NewGuid(), null, "MAT-HOST-DUP", "host 物料 2", unit: "个"), autoSave: true);
            }));
    }

    [Fact]
    public async Task Updating_a_supplier_to_an_existing_code_is_rejected()
    {
        await WithUnitOfWorkAsync(() => _suppliers.CreateAsync(new CreateSupplierDto
        {
            Code = "SUP-UPD-A",
            Name = "供应商 A"
        }));
        var second = await WithUnitOfWorkAsync(() => _suppliers.CreateAsync(new CreateSupplierDto
        {
            Code = "SUP-UPD-B",
            Name = "供应商 B"
        }));

        var exception = await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(() => _suppliers.UpdateAsync(second.Id, new UpdateSupplierDto
            {
                Code = "SUP-UPD-A",
                Name = "供应商 B 改重复编码"
            })));
        Assert.Equal(PurchaseFlowErrors.DuplicateSupplierCode, exception.Code);
    }

    [Fact]
    public async Task A_quote_linked_to_a_supplier_uses_the_master_snapshot_and_the_order_inherits_it()
    {
        var supplier = await WithUnitOfWorkAsync(() => _suppliers.CreateAsync(new CreateSupplierDto
        {
            Code = "SUP-ORDER",
            Name = "档案供应商",
            PaymentTermsDays = 30
        }));

        var requestId = await WithUnitOfWorkAsync(() => _requests.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"MD-SUP-{Guid.NewGuid():N}",
            Description = "供应商快照测试",
            SaleAmount = 10000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "手工录入物料", Specification = "规格", Quantity = 2m, UnitPrice = 100m }
            }
        }));
        var detail = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var requestLine = Assert.Single(detail.Lines);

        var quoteId = await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId, new AddSupplierQuoteDto
        {
            SupplierId = supplier.Id,
            // SupplierName 是 DTO 必填项；服务端必须用档案名称覆盖它。
            SupplierName = "客户端乱填的供应商",
            DeliveryDays = 5,
            Lines = new List<SupplierQuoteLineInputDto>
            {
                new() { RequestLineId = requestLine.Id, UnitPrice = 120m }
            }
        }));

        var quoted = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var quote = quoted.Quotes.Single(x => x.Id == quoteId);
        Assert.Equal(supplier.Id, quote.SupplierId);
        Assert.Equal("档案供应商", quote.SupplierName);

        var secondQuoteId = await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId, new AddSupplierQuoteDto
        {
            SupplierName = "另一家供应商",
            DeliveryDays = 6,
            Lines = new List<SupplierQuoteLineInputDto>
            {
                new() { RequestLineId = requestLine.Id, UnitPrice = 125m }
            }
        }));
        Assert.NotEqual(secondQuoteId, quoteId);

        await WithUnitOfWorkAsync(() => _requests.SelectQuoteAsync(requestId, new SelectSupplierQuoteDto
        {
            QuoteId = quoteId,
            Reason = "档案供应商价格更低"
        }));
        await WithUnitOfWorkAsync(() => _requests.SubmitAsync(requestId));
        await WithUnitOfWorkAsync(() => _requests.ApproveAsync(requestId, new ReviewPurchaseRequestDto
        {
            Comment = "同意"
        }));

        var approved = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var order = Assert.IsType<PurchaseOrderDto>(approved.PurchaseOrder);
        Assert.Equal(supplier.Id, order.SupplierId);
        Assert.Equal("档案供应商", order.SupplierName);
        Assert.Equal(240m, order.Amount);
        Assert.Equal(order.Lines.Sum(x => x.Subtotal), order.Amount);
    }

    [Fact]
    public async Task An_inactive_supplier_can_not_be_used_for_a_new_quote()
    {
        var supplier = await WithUnitOfWorkAsync(() => _suppliers.CreateAsync(new CreateSupplierDto
        {
            Code = "SUP-OFF",
            Name = "停用供应商"
        }));
        await WithUnitOfWorkAsync(() => _suppliers.SetActiveAsync(supplier.Id, false));

        var requestId = await WithUnitOfWorkAsync(() => _requests.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"MD-OFF-SUP-{Guid.NewGuid():N}",
            Description = "停用供应商应被拒绝",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "物料", Quantity = 1m, UnitPrice = 10m }
            }
        }));
        var detail = await WithUnitOfWorkAsync(() => _requests.GetAsync(requestId));
        var line = Assert.Single(detail.Lines);

        var exception = await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(() => _requests.AddQuoteAsync(requestId, new AddSupplierQuoteDto
            {
                SupplierId = supplier.Id,
                SupplierName = "停用供应商",
                DeliveryDays = 5,
                Lines = new List<SupplierQuoteLineInputDto>
                {
                    new() { RequestLineId = line.Id, UnitPrice = 12m }
                }
            })));
        Assert.Equal(PurchaseFlowErrors.SupplierInactive, exception.Code);
    }
}
