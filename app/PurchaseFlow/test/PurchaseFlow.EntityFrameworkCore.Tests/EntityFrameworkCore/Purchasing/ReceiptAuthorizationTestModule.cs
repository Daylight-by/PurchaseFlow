using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PurchaseFlow.Security;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Autofac;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement;
using Volo.Abp.Testing;
using Volo.Abp.Uow;

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// A test module that keeps the real ABP authorization stack. The shared test
/// base registers an always-allow authorization service, which would hide a
/// missing or wrong permission check, so the goods receipt permissions are
/// verified here with real roles, real grants and the real permission checker.
/// It deliberately does not depend on the shared test base module.
/// </summary>
[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpTestBaseModule),
    typeof(AbpAuthorizationModule),
    typeof(PurchaseFlowApplicationModule),
    typeof(PurchaseFlowEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
)]
public class ReceiptAuthorizationTestModule : AbpModule
{
    private AbpUnitTestSqliteDatabase _database = null!;

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });

        // The role grants have to be persisted and read back, otherwise the
        // permission checker would only see the static definitions.
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = true;
        });

        Configure<SettingManagementOptions>(options =>
        {
            options.SaveStaticSettingsToDatabase = false;
            options.IsDynamicSettingStoreEnabled = false;
        });

        // The application keeps the global SQLite unit of work transaction
        // disabled and relies on the local transaction of the receipt service.
        context.Services.AddAlwaysDisableUnitOfWorkTransaction();

        // The test has to act as different users, so the ambient principal is
        // replaced by an accessor that the test can switch.
        context.Services.RemoveAll<ICurrentPrincipalAccessor>();
        context.Services.AddScoped<SwitchableCurrentPrincipalAccessor>();
        context.Services.AddScoped<ICurrentPrincipalAccessor>(provider =>
            provider.GetRequiredService<SwitchableCurrentPrincipalAccessor>());

        ConfigureInMemorySqlite(context.Services);
    }

    private void ConfigureInMemorySqlite(IServiceCollection services)
    {
        _database = new AbpUnitTestSqliteDatabase();
        _database.CreateTables(
            new PurchaseFlowDbContext(new DbContextOptionsBuilder<PurchaseFlowDbContext>().UseSqlite(_database.ConnectionString).Options));

        services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = _database.ConnectionString;
        });

        services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(context =>
            {
                context.UseSqlite();
            });
        });
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _database?.Dispose();
    }
}
