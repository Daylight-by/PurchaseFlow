using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PurchaseFlow.Purchasing;
using PurchaseFlow.Security;
using Volo.Abp.Security.Claims;
using Xunit;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// R18: a database that already contains approved requests, quotes and
/// purchase orders is upgraded with the migration chain of the project. The old
/// documents keep their ids, amounts and order line ids, the receipt tables
/// are added, and a receipt can be registered on an order of the old database.
/// P0-1: the quote-lines migration also backfills an itemized summary line for
/// a historical quote and an amount-adjustment line for a historical order, so
/// every document satisfies "amount == sum of its lines" without rewriting the
/// historical amounts.
/// </summary>
public class LegacyDatabaseUpgradeTests : PurchaseFlowTestBase<LegacyUpgradeTestModule>
{
    private readonly LegacyUpgradeTestModule _module;
    private readonly SwitchableCurrentPrincipalAccessor _principalAccessor;
    private readonly IGoodsReceiptAppService _receiptService;

    private static readonly Guid LegacyRequestId = Guid.NewGuid();
    private static readonly Guid LegacyRequestLineId = Guid.NewGuid();
    private static readonly Guid LegacyQuoteId = Guid.NewGuid();
    private static readonly Guid LegacyOrderId = Guid.NewGuid();
    private static readonly Guid LegacyOrderLineId = Guid.NewGuid();
    private const string LegacyOrderNumber = "PO-SO-LEGACY-0001";
    private const string LegacyOrderReference = "SO-LEGACY-0001";

    public LegacyDatabaseUpgradeTests()
    {
        _module = GetRequiredService<LegacyUpgradeTestModule>();
        _principalAccessor = GetRequiredService<SwitchableCurrentPrincipalAccessor>();
        _receiptService = GetRequiredService<IGoodsReceiptAppService>();
    }

    [Fact]
    public async Task A_database_with_old_business_data_is_upgraded_and_keeps_its_data()
    {
        // 1. The database of the user before the receipt feature.
        var connectionString = await _module.MigrateToAsync(LegacyUpgradeTestModule.MigrationBeforeReceipts);
        await SeedLegacyBusinessDataAsync(connectionString);

        var before = await ReadLegacyRowsAsync(connectionString);
        Assert.Equal(1, before.Requests);
        Assert.Equal(1, before.RequestLines);
        Assert.Equal(1, before.Quotes);
        Assert.Equal(0, before.QuoteLines);
        Assert.Equal(1, before.Orders);
        Assert.Equal(1, before.OrderLines);
        Assert.Equal(0, before.ReceiptTables);

        // 2. The migration chain of the project upgrades the same file.
        await _module.MigrateToLatestAsync();

        // 3. The old documents are unchanged.
        var after = await ReadLegacyRowsAsync(connectionString);
        Assert.Equal(before.Requests, after.Requests);
        Assert.Equal(before.RequestLines, after.RequestLines);
        Assert.Equal(before.Quotes, after.Quotes);
        Assert.Equal(before.Orders, after.Orders);
        // P0-1: the migration backfills one summary quote line and one
        // amount-adjustment order line, so the new line tables are populated
        // while the old header rows and their amounts stay untouched.
        Assert.Equal(1, after.QuoteLines);
        Assert.Equal(before.OrderLines + 1, after.OrderLines);
        Assert.Equal(2, after.ReceiptTables);

        var order = await ReadLegacyOrderAsync(connectionString);
        Assert.Equal(LegacyOrderId, order.Id);
        Assert.Equal(LegacyOrderNumber, order.OrderNumber);
        Assert.Equal(1234.56m, order.Amount);
        Assert.Equal(10m, order.LineQuantity);
        Assert.Equal(0m, order.LineReceivedQuantity);
        Assert.Equal(0, order.ReceiptVersion);
        // 金额铁律：历史订单金额仍然保持 1234.56，同时明细合计也补到同一金额。
        Assert.Equal(order.Amount, order.LinesTotal);

        var quote = await ReadLegacyQuoteAsync(connectionString);
        Assert.Equal(1234.56m, quote.Amount);
        Assert.Equal(1, quote.LineCount);
        Assert.Equal(quote.Amount, quote.LinesTotal);
    }

    [Fact]
    public async Task A_receipt_can_be_registered_on_an_order_of_the_upgraded_database()
    {
        var connectionString = await _module.MigrateToAsync(LegacyUpgradeTestModule.MigrationBeforeReceipts);
        await SeedLegacyBusinessDataAsync(connectionString);
        await _module.MigrateToLatestAsync();

        var appIds = await WithUnitOfWorkAsync(async () =>
        {
            var context = GetRequiredService<PurchaseFlowDbContext>();
            var appOrder = await context.PurchaseOrders.IgnoreQueryFilters()
                .SingleAsync(order => order.OrderNumber == LegacyOrderNumber);
            return (OrderId: appOrder.Id, LineId: LegacyOrderLineId);
        });
        Assert.Equal(LegacyOrderId, appIds.OrderId);
        Assert.Equal(LegacyOrderLineId, appIds.LineId);

        var userId = Guid.NewGuid();
        using (_principalAccessor.SwitchTo(userId, "legacy.receiver", "receiver"))
        {
            var receipt = await WithUnitOfWorkAsync(() => _receiptService.CreateAsync(new CreateGoodsReceiptDto
            {
                PurchaseOrderId = appIds.OrderId,
                IdempotencyKey = "legacy-upgrade-receipt",
                ReceiptDate = new DateTime(2026, 10, 31),
                Note = "旧库升级后登记收货",
                Lines = new List<GoodsReceiptLineInputDto>
                {
                    new GoodsReceiptLineInputDto
                    {
                        PurchaseOrderLineId = appIds.LineId,
                        Quantity = 4m
                    }
                }
            }));

            Assert.True(receipt.Id != Guid.Empty);
            Assert.Equal(userId, receipt.CreatorId);
        }

        var order = await ReadLegacyOrderAsync(connectionString);
        Assert.Equal(4m, order.LineReceivedQuantity);
        Assert.Equal(6m, order.LineQuantity - order.LineReceivedQuantity);

        var receiptRows = await ReadReceiptRowsAsync(connectionString);
        Assert.Equal(1, receiptRows.Receipts);
        Assert.Equal(1, receiptRows.ReceiptLines);
        Assert.Equal(4m, receiptRows.ReceiptQuantity);
    }

    private static async Task SeedLegacyBusinessDataAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection,
            @"INSERT INTO AppPurchaseRequests
              (Id, TenantId, BuyerUserId, OrderReference, Description, SaleAmount, Status,
               SelectedQuoteId, SelectionReason, ExtraProperties, ConcurrencyStamp, CreationTime,
               CreatorId, LastModificationTime, LastModifierId, IsDeleted, DeleterId, DeletionTime)
              VALUES (@id, NULL, @buyer, @reference, '旧库升级样例', 2000.00, 2,
               @quote, '价格更低', '{}', 'legacy-stamp', '2026-01-05 08:00:00',
               NULL, NULL, NULL, 0, NULL, NULL)",
            ("@id", LegacyRequestId),
            ("@buyer", Guid.NewGuid()),
            ("@reference", LegacyOrderReference),
            ("@quote", LegacyQuoteId));

        await ExecuteAsync(connection,
            @"INSERT INTO AppPurchaseRequestLines
              (Id, TenantId, PurchaseRequestId, ItemName, Specification, Quantity, UnitPrice)
              VALUES (@id, NULL, @request, '旧库轴承', '6205-2RS', 10.000, 25.50)",
            ("@id", LegacyRequestLineId),
            ("@request", LegacyRequestId));

        await ExecuteAsync(connection,
            @"INSERT INTO AppSupplierQuotes
              (Id, TenantId, PurchaseRequestId, SupplierName, Amount, DeliveryDays, Note)
              VALUES (@id, NULL, @request, '旧库供应商', 1234.56, 7, '旧库报价')",
            ("@id", LegacyQuoteId),
            ("@request", LegacyRequestId));

        await ExecuteAsync(connection,
            @"INSERT INTO AppPurchaseOrders
              (Id, TenantId, OrderNumber, PurchaseRequestId, SupplierName, Amount, DeliveryDays, Status,
               ExtraProperties, ConcurrencyStamp, CreationTime, CreatorId, LastModificationTime,
               LastModifierId, IsDeleted, DeleterId, DeletionTime)
              VALUES (@id, NULL, @number, @request, '旧库供应商', 1234.56, 7, 0,
               '{}', 'legacy-stamp', '2026-01-05 09:00:00', NULL, NULL, NULL, 0, NULL, NULL)",
            ("@id", LegacyOrderId),
            ("@number", LegacyOrderNumber),
            ("@request", LegacyRequestId));

        await ExecuteAsync(connection,
            @"INSERT INTO AppPurchaseOrderLines
              (Id, TenantId, PurchaseOrderId, ItemName, Specification, Quantity, UnitPrice)
              VALUES (@id, NULL, @order, '旧库轴承', '6205-2RS', 10.000, 25.50)",
            ("@id", LegacyOrderLineId),
            ("@order", LegacyOrderId));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<(int Requests, int RequestLines, int Quotes, int QuoteLines, int Orders, int OrderLines, int ReceiptTables)>
        ReadLegacyRowsAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();
        return (
            await CountAsync(connection, "AppPurchaseRequests"),
            await CountAsync(connection, "AppPurchaseRequestLines"),
            await CountAsync(connection, "AppSupplierQuotes"),
            await CountAsync(connection, "AppSupplierQuoteLines"),
            await CountAsync(connection, "AppPurchaseOrders"),
            await CountAsync(connection, "AppPurchaseOrderLines"),
            (await TableExistsAsync(connection, "AppGoodsReceipts") ? 1 : 0)
            + (await TableExistsAsync(connection, "AppGoodsReceiptLines") ? 1 : 0));
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        command.Parameters.AddWithValue("@name", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync()) != 0;
    }

    private static async Task<int> CountAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @name";
        command.Parameters.AddWithValue("@name", table);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0)
            return 0;

        command.Parameters.Clear();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task<(Guid Id, string OrderNumber, decimal Amount, decimal LineQuantity,
        decimal LineReceivedQuantity, int ReceiptVersion, decimal LinesTotal)> ReadLegacyOrderAsync(string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // P0-1: read the original legacy order line explicitly (the migration adds
        // an adjustment line) and compute the sum of all order lines.
        command.CommandText =
            @"SELECT o.Id, o.OrderNumber, o.Amount, l.Quantity, l.ReceivedQuantity, o.ReceiptVersion,
                     (SELECT IFNULL(SUM(ROUND(x.Quantity * x.UnitPrice, 2)), 0)
                      FROM AppPurchaseOrderLines x WHERE x.PurchaseOrderId = o.Id)
              FROM AppPurchaseOrders o
              INNER JOIN AppPurchaseOrderLines l ON l.PurchaseOrderId = o.Id
              WHERE o.Id = @id AND l.Id = @lineId";
        command.Parameters.AddWithValue("@id", LegacyOrderId);
        command.Parameters.AddWithValue("@lineId", LegacyOrderLineId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetDecimal(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetInt32(5),
            reader.GetDecimal(6));
    }

    private static async Task<(decimal Amount, int LineCount, decimal LinesTotal)> ReadLegacyQuoteAsync(
        string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT q.Amount,
                     (SELECT COUNT(*) FROM AppSupplierQuoteLines l WHERE l.SupplierQuoteId = q.Id),
                     (SELECT IFNULL(SUM(ROUND(l.Quantity * l.UnitPrice, 2)), 0)
                      FROM AppSupplierQuoteLines l WHERE l.SupplierQuoteId = q.Id)
              FROM AppSupplierQuotes q
              WHERE q.Id = @id";
        command.Parameters.AddWithValue("@id", LegacyQuoteId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetDecimal(0), reader.GetInt32(1), reader.GetDecimal(2));
    }

    private static async Task<(int Receipts, int ReceiptLines, decimal ReceiptQuantity)> ReadReceiptRowsAsync(
        string connectionString)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            @"SELECT (SELECT COUNT(*) FROM AppGoodsReceipts),
                     (SELECT COUNT(*) FROM AppGoodsReceiptLines),
                     (SELECT IFNULL(SUM(Quantity), 0) FROM AppGoodsReceiptLines)";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2));
    }
}
