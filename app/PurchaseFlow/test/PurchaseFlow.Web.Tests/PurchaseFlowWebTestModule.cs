using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Localization.Resources.AbpUi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseFlow.EntityFrameworkCore;
using PurchaseFlow.Localization;
using PurchaseFlow.Web;
using PurchaseFlow.Web.Menus;
using Volo.Abp;
using Volo.Abp.AspNetCore.TestBase;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.OpenIddict;
using Volo.Abp.Threading;
using Volo.Abp.UI.Navigation;
using Volo.Abp.Validation.Localization;

namespace PurchaseFlow;

[DependsOn(
    typeof(AbpAspNetCoreTestBaseModule),
    typeof(PurchaseFlowWebModule),
    typeof(PurchaseFlowApplicationTestModule),
    typeof(PurchaseFlowEntityFrameworkCoreTestModule)
)]
public class PurchaseFlowWebTestModule : AbpModule
{
    /// <summary>
    /// The account that the page level tests sign in with. It only exists in
    /// the isolated test database of the test host.
    /// </summary>
    public const string TestUserName = "web.tester";

    public const string TestUserPassword = "Web@Test2026";

    private AbpUnitTestSqliteDatabase _database = null!;

    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.PreConfigure<IMvcBuilder>(builder =>
        {
            builder.PartManager.ApplicationParts.Add(new CompiledRazorAssemblyPart(typeof(PurchaseFlowWebModule).Assembly));
        });

        context.Services.GetPreConfigureActions<OpenIddictServerBuilder>().Clear();
        PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
        {
            options.AddDevelopmentEncryptionAndSigningCertificate = true;
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureLocalizationServices(context.Services);
        ConfigureNavigationServices(context.Services);

        // The page tests need a real database, but they must never touch the
        // database of a developer or of a running instance.
        ConfigureInMemorySqlite(context.Services);
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        SeedTestUser(context);
    }

    public override void OnApplicationShutdown(ApplicationShutdownContext context)
    {
        _database?.Dispose();
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

    private static void SeedTestUser(ApplicationInitializationContext context)
    {
        AsyncHelper.RunSync(async () =>
        {
            using var scope = context.ServiceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<IdentityUserManager>();
            if (await userManager.FindByNameAsync(TestUserName) is not null)
            {
                return;
            }

            var user = new IdentityUser(Guid.NewGuid(), TestUserName, $"{TestUserName}@example.invalid");
            var result = await userManager.CreateAsync(user, TestUserPassword);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
            }
        });
    }

    private static void ConfigureLocalizationServices(IServiceCollection services)
    {
        var cultures = new List<CultureInfo> { new CultureInfo("en"), new CultureInfo("tr") };
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en");
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
        });

        services.Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<PurchaseFlowResource>()
                .AddBaseTypes(
                    typeof(AbpValidationResource),
                    typeof(AbpUiResource)
                );
        });
    }

    private static void ConfigureNavigationServices(IServiceCollection services)
    {
        services.Configure<AbpNavigationOptions>(options =>
        {
            options.MenuContributors.Add(new PurchaseFlowMenuContributor());
        });
    }
}
