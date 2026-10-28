using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace PurchaseFlow.EntityFrameworkCore;

/* This class is needed for EF Core console commands
 * (like Add-Migration and Update-Database commands) */
public class PurchaseFlowDbContextFactory : IDesignTimeDbContextFactory<PurchaseFlowDbContext>
{
    public PurchaseFlowDbContext CreateDbContext(string[] args)
    {
        PurchaseFlowEfCoreEntityExtensionMappings.Configure();

        var configuration = BuildConfiguration();

        var builder = new DbContextOptionsBuilder<PurchaseFlowDbContext>()
            .UseSqlite(configuration.GetConnectionString("Default"));

        return new PurchaseFlowDbContext(builder.Options);
    }

    private static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../PurchaseFlow.DbMigrator/"))
            .AddJsonFile("appsettings.json", optional: false);

        return builder.Build();
    }
}

