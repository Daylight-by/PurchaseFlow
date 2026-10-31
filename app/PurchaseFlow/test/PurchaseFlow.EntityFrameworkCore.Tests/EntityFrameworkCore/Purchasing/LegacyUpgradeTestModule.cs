using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PurchaseFlow.Security;
using Volo.Abp;
using Volo.Abp.Autofac;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Testing;
using Volo.Abp.Uow;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// A test module that uses a real SQLite file instead of the shared in memory
/// database of the other tests. It is used to prove that an existing database
/// with business data is upgraded by the migration chain of the project: the
/// old purchase requests, quotes and purchase orders keep their ids and
/// amounts, and the new receipt tables are added to the same database.
/// </summary>
[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpTestBaseModule),
    typeof(PurchaseFlowApplicationModule),
    typeof(PurchaseFlowEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
)]
public class LegacyUpgradeTestModule : AbpModule
{
    /// <summary>
    /// The migration that represents the database of the user before the goods
    /// receipt feature was added.
    /// </summary>
    public const string MigrationBeforeReceipts = "20261030173528_AddPurchaseOrders";

    private readonly string _databaseFile;
    private readonly string _connectionString;
    public string ConnectionString => _connectionString;

    public LegacyUpgradeTestModule()
    {
        _databaseFile = Path.Combine(Path.GetTempPath(),
            $"purchaseflow-legacy-{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_databaseFile}";
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // This is an upgrade/compatibility test, not an authorization test;
        // authorization is covered by GoodsReceiptAuthorizationTests.
        context.Services.AddAlwaysAllowAuthorization();

        // The global unit of work transaction stays disabled, exactly like in
        // the running application.
        context.Services.AddAlwaysDisableUnitOfWorkTransaction();

        // The test signs in as a fixed user, so the receipts have an operator.
        context.Services.RemoveAll<ICurrentPrincipalAccessor>();
        context.Services.AddScoped<SwitchableCurrentPrincipalAccessor>();
        context.Services.AddScoped<ICurrentPrincipalAccessor>(provider =>
            provider.GetRequiredService<SwitchableCurrentPrincipalAccessor>());

        context.Services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = _connectionString;
        });

        context.Services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(context =>
            {
                context.UseSqlite();
            });
        });
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        DeleteDatabaseFile();
    }

    /// <summary>
    /// Migrates the database of the test to the given migration and returns the
    /// connection string, so the test can fill it with business data.
    /// </summary>
    public async Task<string> MigrateToAsync(string targetMigration)
    {
        var options = new DbContextOptionsBuilder<PurchaseFlowDbContext>()
            .UseSqlite(_connectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(PurchaseFlowDbContext).Assembly.FullName))
            .Options;

        await using var context = new PurchaseFlowDbContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
        return _connectionString;
    }

    /// <summary>
    /// Applies every migration that is still missing on the database.
    /// </summary>
    public async Task MigrateToLatestAsync()
    {
        var options = new DbContextOptionsBuilder<PurchaseFlowDbContext>()
            .UseSqlite(_connectionString, sqlite =>
                sqlite.MigrationsAssembly(typeof(PurchaseFlowDbContext).Assembly.FullName))
            .Options;

        await using var context = new PurchaseFlowDbContext(options);
        // 不再硬编码“最新迁移”名称：旧库升级测试必须把数据库升级到迁移链
        // 的最后一版，否则以后新增的迁移（例如报价明细迁移）不会被覆盖。
        var available = context.Database.GetMigrations().ToArray();
        var latest = available.Last();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(latest);
        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        if (!applied.Contains(latest))
            throw new InvalidOperationException($"Latest migration was not applied. Applied: {string.Join(", ", applied)}");
    }

    private void DeleteDatabaseFile()
    {
        try
        {
            if (File.Exists(_databaseFile))
                File.Delete(_databaseFile);
        }
        catch (IOException)
        {
            // The file is left in the temp folder if it is still locked.
        }
    }
}
