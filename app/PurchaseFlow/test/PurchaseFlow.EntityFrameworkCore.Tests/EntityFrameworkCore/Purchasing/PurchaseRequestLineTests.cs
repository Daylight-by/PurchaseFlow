using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PurchaseFlow.Purchasing;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

[Collection(PurchaseFlowTestConsts.CollectionDefinitionName)]
public class PurchaseRequestLineTests : PurchaseFlowEntityFrameworkCoreTestBase
{
    private readonly IPurchaseRequestAppService _service;
    private readonly IPurchaseRequestRepository _requests;
    private readonly IRepository<PurchaseRequestLine, Guid> _lines;

    public PurchaseRequestLineTests()
    {
        _service = GetRequiredService<IPurchaseRequestAppService>();
        _requests = GetRequiredService<IPurchaseRequestRepository>();
        _lines = GetRequiredService<IRepository<PurchaseRequestLine, Guid>>();
    }

    [Fact]
    public async Task The_amount_of_a_request_is_the_sum_of_its_lines()
    {
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"LINE-{Guid.NewGuid():N}",
            Description = "带明细的采购需求",
            SaleAmount = 120000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "深沟球轴承", Specification = "6205-2RS", Quantity = 10m, UnitPrice = 25.5m },
                new() { ItemName = "骨架油封", Specification = "NBR 20x3", Quantity = 4.5m, UnitPrice = 12m }
            }
        }));

        var detail = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));

        Assert.Equal(2, detail.Lines.Count);
        Assert.Equal(2, detail.LineCount);
        // 10 * 25.5 + 4.5 * 12 = 309.00
        Assert.Equal(309m, detail.Amount);
        Assert.Equal(309m, detail.Lines.Sum(x => x.Subtotal));
        Assert.Equal(120000m, detail.SaleAmount);
        Assert.Equal(
            new[] { "深沟球轴承", "骨架油封" }.OrderBy(x => x),
            detail.Lines.Select(x => x.ItemName).OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Lines_are_persisted_and_read_back_from_the_database()
    {
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"LINE-{Guid.NewGuid():N}",
            Description = "落库校验",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "联轴器", Specification = "梅花型", Quantity = 3m, UnitPrice = 80m }
            }
        }));

        var lines = await WithUnitOfWorkAsync(async () => await (await _lines.GetQueryableAsync()).Where(x => x.PurchaseRequestId == id).ToListAsync());

        var line = Assert.Single(lines);
        Assert.Equal("联轴器", line.ItemName);
        Assert.Equal("梅花型", line.Specification);
        Assert.Equal(3m, line.Quantity);
        Assert.Equal(80m, line.UnitPrice);
        Assert.Equal(240m, line.Subtotal);
    }

    [Fact]
    public async Task Updating_the_lines_replaces_the_previous_set()
    {
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"LINE-{Guid.NewGuid():N}",
            Description = "改明细",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "联轴器", Specification = "梅花型", Quantity = 3m, UnitPrice = 80m }
            }
        }));

        await WithUnitOfWorkAsync(async () => await _service.UpdateAsync(id, new UpdatePurchaseRequestDto
        {
            OrderReference = $"LINE-{Guid.NewGuid():N}",
            Description = "改明细",
            SaleAmount = 2000m,
            Lines = new List<UpdatePurchaseRequestLineDto>
            {
                new() { ItemName = "链轮", Specification = "08B-1", Quantity = 2m, UnitPrice = 45m }
            }
        }));

        var detail = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));

        var line = Assert.Single(detail.Lines);
        Assert.Equal("链轮", line.ItemName);
        Assert.Equal(90m, detail.Amount);

        var lines = await WithUnitOfWorkAsync(async () => await (await _lines.GetQueryableAsync()).Where(x => x.PurchaseRequestId == id).ToListAsync());
        Assert.Single(lines);
    }

    [Fact]
    public async Task Updating_keeps_existing_line_ids_and_adds_new_lines_without_ids()
    {
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"LINE-{Guid.NewGuid():N}",
            Description = "P1-3 混合更新",
            SaleAmount = 5000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "联轴器", Specification = "梅花型", Quantity = 3m, UnitPrice = 80m },
                new() { ItemName = "链轮", Specification = "08B-1", Quantity = 2m, UnitPrice = 45m }
            }
        }));

        var before = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));
        var kept = before.Lines.Single(x => x.ItemName == "联轴器");
        var removed = before.Lines.Single(x => x.ItemName == "链轮");

        // 修改已有行（带 Id）、新增一行（无 Id）、删除一行。
        await WithUnitOfWorkAsync(async () => await _service.UpdateAsync(id, new UpdatePurchaseRequestDto
        {
            OrderReference = before.OrderReference,
            Description = before.Description,
            SaleAmount = before.SaleAmount,
            Lines = new List<UpdatePurchaseRequestLineDto>
            {
                new()
                {
                    Id = kept.Id,
                    ItemName = kept.ItemName,
                    Specification = kept.Specification,
                    Quantity = 5m,
                    UnitPrice = 90m
                },
                new()
                {
                    Id = null,
                    ItemName = "电机",
                    Specification = "YS-90",
                    Quantity = 2m,
                    UnitPrice = 350m
                }
            }
        }));

        var after = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));

        Assert.Equal(2, after.Lines.Count);
        var updated = after.Lines.Single(x => x.Id == kept.Id);
        Assert.Equal(5m, updated.Quantity);
        Assert.Equal(90m, updated.UnitPrice);
        Assert.Equal(450m, updated.Subtotal);
        var added = after.Lines.Single(x => x.ItemName == "电机");
        Assert.NotEqual(Guid.Empty, added.Id);
        Assert.NotEqual(kept.Id, added.Id);
        Assert.NotEqual(removed.Id, added.Id);
        // 5 * 90 + 2 * 350 = 1150.00
        Assert.Equal(1150m, after.Amount);
        Assert.Equal(after.Amount, after.Lines.Sum(x => x.Subtotal));

        // 旧行被替换后数量与主键都稳定地落在数据库里。
        var persisted = await WithUnitOfWorkAsync(async () => await (await _lines.GetQueryableAsync())
            .Where(x => x.PurchaseRequestId == id).ToListAsync());
        Assert.Equal(2, persisted.Count);
        Assert.Contains(persisted, x => x.Id == kept.Id);
        Assert.DoesNotContain(persisted, x => x.Id == removed.Id);
    }

    [Fact]
    public async Task Updating_rejects_a_line_id_of_another_request()
    {
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"LINE-{Guid.NewGuid():N}",
            Description = "陌生 Id 拒绝",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "联轴器", Specification = "梅花型", Quantity = 3m, UnitPrice = 80m }
            }
        }));
        var before = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));

        var exception = await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(async () => await _service.UpdateAsync(id, new UpdatePurchaseRequestDto
            {
                OrderReference = before.OrderReference,
                Description = before.Description,
                SaleAmount = before.SaleAmount,
                Lines = new List<UpdatePurchaseRequestLineDto>
                {
                    new()
                    {
                        // P1-3: 不属于当前需求的 Id 必须报错，不能静默当作新增或覆盖。
                        Id = Guid.NewGuid(),
                        ItemName = "链轮",
                        Specification = "08B-1",
                        Quantity = 2m,
                        UnitPrice = 45m
                    }
                }
            })));

        Assert.Equal(PurchaseFlowErrors.LineNotInRequest, exception.Code);
        var after = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));
        Assert.Single(after.Lines);
        Assert.Equal(before.Lines.Single().Id, after.Lines.Single().Id);
        Assert.Equal(240m, after.Amount);
    }

    [Fact]
    public async Task Updating_rejects_duplicate_line_ids()
    {
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"LINE-{Guid.NewGuid():N}",
            Description = "重复 Id 拒绝",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "联轴器", Specification = "梅花型", Quantity = 3m, UnitPrice = 80m }
            }
        }));
        var before = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));
        var existingId = before.Lines.Single().Id;

        var exception = await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(async () => await _service.UpdateAsync(id, new UpdatePurchaseRequestDto
            {
                OrderReference = before.OrderReference,
                Description = before.Description,
                SaleAmount = before.SaleAmount,
                Lines = new List<UpdatePurchaseRequestLineDto>
                {
                    new() { Id = existingId, ItemName = "联轴器", Specification = "梅花型", Quantity = 4m, UnitPrice = 80m },
                    new() { Id = existingId, ItemName = "链轮", Specification = "08B-1", Quantity = 2m, UnitPrice = 45m }
                }
            })));

        Assert.Equal(PurchaseFlowErrors.DuplicateLineId, exception.Code);
        var after = await WithUnitOfWorkAsync(async () => await _service.GetAsync(id));
        Assert.Single(after.Lines);
        Assert.Equal(3m, after.Lines.Single().Quantity);
    }

    [Fact]
    public async Task The_list_reports_the_amount_calculated_from_the_lines()
    {
        var orderReference = $"LINE-{Guid.NewGuid():N}";
        await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = orderReference,
            Description = "列表金额",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "电机", Specification = "YS-90", Quantity = 2m, UnitPrice = 350m }
            }
        }));

        var list = await WithUnitOfWorkAsync(async () => await _service.GetListAsync(new PurchaseRequestListInput
        {
            Keyword = orderReference
        }));

        var item = Assert.Single(list.Items);
        Assert.Equal(700m, item.Amount);
        Assert.Equal(1, item.LineCount);
    }

    [Fact]
    public async Task A_request_without_lines_can_not_be_submitted()
    {
        var id = await WithUnitOfWorkAsync(async () => await _service.CreateAsync(new CreatePurchaseRequestDto
        {
            OrderReference = $"LINE-{Guid.NewGuid():N}",
            Description = "无明细",
            SaleAmount = 1000m,
            Lines = new List<CreatePurchaseRequestLineDto>
            {
                new() { ItemName = "电机", Specification = "YS-90", Quantity = 2m, UnitPrice = 350m }
            }
        }));

        // Remove every line through the aggregate, then try to submit.
        await WithUnitOfWorkAsync(async () =>
        {
            var request = await _requests.FindWithDetailsAsync(id);
            request!.RemoveLine(request.Lines.First().Id);
            await _requests.UpdateAsync(request, autoSave: true);
        });

        await Assert.ThrowsAsync<BusinessException>(async () =>
            await WithUnitOfWorkAsync(async () => await _service.SubmitAsync(id)));
    }
}
