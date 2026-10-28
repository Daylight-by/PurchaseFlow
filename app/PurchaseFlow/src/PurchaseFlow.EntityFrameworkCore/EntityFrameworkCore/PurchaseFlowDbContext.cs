using Microsoft.EntityFrameworkCore;
using Volo.Abp.AuditLogging.EntityFrameworkCore;
using PurchaseFlow.Purchasing;
using Volo.Abp.BackgroundJobs.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;
using Volo.Abp.FeatureManagement.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.OpenIddict.EntityFrameworkCore;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.SettingManagement.EntityFrameworkCore;
using Volo.Abp.TenantManagement;
using Volo.Abp.TenantManagement.EntityFrameworkCore;

namespace PurchaseFlow.EntityFrameworkCore;

[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(ITenantManagementDbContext))]
[ConnectionStringName("Default")]
public class PurchaseFlowDbContext :
    AbpDbContext<PurchaseFlowDbContext>,
    IIdentityDbContext,
    ITenantManagementDbContext
{
    public DbSet<PurchaseRequest> PurchaseRequests { get; set; }
    public DbSet<SupplierQuote> SupplierQuotes { get; set; }
    public DbSet<ReviewEvent> ReviewEvents { get; set; }

    #region Entities from the modules

    /* Notice: We only implemented IIdentityDbContext and ITenantManagementDbContext
     * and replaced them for this DbContext. This allows you to perform JOIN
     * queries for the entities of these modules over the repositories easily. You
     * typically don't need that for other modules. But, if you need, you can
     * implement the DbContext interface of the needed module and use ReplaceDbContext
     * attribute just like IIdentityDbContext and ITenantManagementDbContext.
     *
     * More info: Replacing a DbContext of a module ensures that the related module
     * uses this DbContext on runtime. Otherwise, it will use its own DbContext class.
     */

    //Identity
    public DbSet<IdentityUser> Users { get; set; }
    public DbSet<IdentityRole> Roles { get; set; }
    public DbSet<IdentityClaimType> ClaimTypes { get; set; }
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; }
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; }
    public DbSet<IdentityLinkUser> LinkUsers { get; set; }
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; }
    public DbSet<IdentitySession> Sessions { get; set; }
    // Tenant Management
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<TenantConnectionString> TenantConnectionStrings { get; set; }

    #endregion

    public PurchaseFlowDbContext(DbContextOptions<PurchaseFlowDbContext> options)
        : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        /* Include modules to your migration db context */

        builder.ConfigurePermissionManagement();
        builder.ConfigureSettingManagement();
        builder.ConfigureBackgroundJobs();
        builder.ConfigureAuditLogging();
        builder.ConfigureIdentity();
        builder.ConfigureOpenIddict();
        builder.ConfigureFeatureManagement();
        builder.ConfigureTenantManagement();

        builder.Entity<PurchaseRequest>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "PurchaseRequests", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.OrderReference).IsRequired().HasMaxLength(64);
            b.Property(x => x.Description).IsRequired().HasMaxLength(500);
            b.Property(x => x.SaleAmount).HasPrecision(18, 2);
            b.Property(x => x.SelectionReason).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.OrderReference }).IsUnique();
            // SQLite allows multiple NULLs in a unique composite index. Host
            // requests have a NULL TenantId, so they need a filtered index.
            b.HasIndex(x => x.OrderReference)
                .HasDatabaseName("IX_AppPurchaseRequests_HostOrderReference")
                .IsUnique()
                .HasFilter("\"TenantId\" IS NULL");
        });

        builder.Entity<SupplierQuote>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "SupplierQuotes", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.SupplierName).IsRequired().HasMaxLength(100);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasOne<PurchaseRequest>().WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.PurchaseRequestId });
        });

        builder.Entity<ReviewEvent>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "ReviewEvents", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Action).IsRequired().HasMaxLength(30);
            b.Property(x => x.Comment).HasMaxLength(500);
            b.HasOne<PurchaseRequest>().WithMany().HasForeignKey(x => x.PurchaseRequestId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.PurchaseRequestId, x.OccurredAtUtc });
        });
    }
}

