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

namespace PurchaseFlow.EntityFrameworkCore.Purchasing;

/// <summary>
/// 第二轮第 2 批：分级审批的业务规则测试模块。
/// 共享测试基类把当前用户固定成同一个人，无法证明“二级审批必须换人”；
/// 这里替换为可切换 principal accessor，让测试可以分别以采购员、审核员、管理员身份调用应用服务。
/// 授权使用 always-allow（权限点本身由 [Authorize] 与部署角色种子覆盖），
/// 这样测试聚焦在领域/应用层的金额分级与换人规则上。
/// </summary>
[DependsOn(
    typeof(AbpAutofacModule),
    typeof(AbpTestBaseModule),
    typeof(PurchaseFlowApplicationModule),
    typeof(PurchaseFlowEntityFrameworkCoreModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
)]
public class ApprovalFlowTestModule : AbpModule
{
    private AbpUnitTestSqliteDatabase _database = null!;

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddAlwaysAllowAuthorization();

        Configure<FeatureManagementOptions>(options =>
        {
            options.SaveStaticFeaturesToDatabase = false;
            options.IsDynamicFeatureStoreEnabled = false;
        });
        Configure<PermissionManagementOptions>(options =>
        {
            options.SaveStaticPermissionsToDatabase = false;
            options.IsDynamicPermissionStoreEnabled = false;
        });
        Configure<SettingManagementOptions>(options =>
        {
            options.SaveStaticSettingsToDatabase = false;
            options.IsDynamicSettingStoreEnabled = false;
        });

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
            new PurchaseFlowDbContext(new DbContextOptionsBuilder<PurchaseFlowDbContext>()
                .UseSqlite(_database.ConnectionString).Options));

        services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = _database.ConnectionString;
        });
        services.Configure<AbpDbContextOptions>(options =>
        {
            options.Configure(context => context.UseSqlite());
        });
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _database?.Dispose();
    }
}
