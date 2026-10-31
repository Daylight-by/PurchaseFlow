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
    public DbSet<PurchaseRequestLine> PurchaseRequestLines { get; set; }
    public DbSet<SupplierQuote> SupplierQuotes { get; set; }
    // P0-1：报价逐项明细成为独立表，与 SupplierQuote 级联删除。
    public DbSet<SupplierQuoteLine> SupplierQuoteLines { get; set; }
    // 第二轮第 1 批：供应商/物料主数据档案。
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<Material> Materials { get; set; }
    public DbSet<ReviewEvent> ReviewEvents { get; set; }
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
    public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
    public DbSet<GoodsReceipt> GoodsReceipts { get; set; }
    public DbSet<GoodsReceiptLine> GoodsReceiptLines { get; set; }

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
            // The purchase amount is derived from the lines, it is never stored.
            b.Ignore(x => x.Amount);
            b.HasIndex(x => new { x.TenantId, x.OrderReference }).IsUnique();
            // SQLite allows multiple NULLs in a unique composite index. Host
            // requests have a NULL TenantId, so they need a filtered index.
            b.HasIndex(x => x.OrderReference)
                .HasDatabaseName("IX_AppPurchaseRequests_HostOrderReference")
                .IsUnique()
                .HasFilter("\"TenantId\" IS NULL");
        });

        builder.Entity<PurchaseRequestLine>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "PurchaseRequestLines", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            // 第二轮主数据：MaterialId 可空、不建外键 —— 档案可停用/更名，历史明细
            // 必须保留名称/规格快照；这里只存 Id 做溯源，约束由应用层负责。
            b.Property(x => x.MaterialId);
            b.Property(x => x.ItemName).IsRequired().HasMaxLength(100);
            b.Property(x => x.Specification).HasMaxLength(100);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
            // Subtotal is calculated from quantity and unit price.
            b.Ignore(x => x.Subtotal);
            b.HasOne<PurchaseRequest>()
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.PurchaseRequestId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.PurchaseRequestId });
        });

        builder.Entity<SupplierQuote>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "SupplierQuotes", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            // 第二轮主数据：SupplierId 可空、不建外键 —— SupplierName 是历史名称快照，
            // 档案改名/停用不能影响历史报价，这里只存 Id 做溯源。
            b.Property(x => x.SupplierId);
            b.Property(x => x.SupplierName).IsRequired().HasMaxLength(100);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasOne<PurchaseRequest>()
                .WithMany(x => x.Quotes)
                .HasForeignKey(x => x.PurchaseRequestId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.PurchaseRequestId });
        });

        // P0-1：逐项报价明细。表结构与 PurchaseOrderLine 同构；Subtotal 由
        // 数量 × 单价派生，不落库。PurchaseRequestLineId 只做溯源快照，
        // 刻意不建外键：需求明细允许被整体替换/删除，历史报价必须保留。
        builder.Entity<SupplierQuoteLine>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "SupplierQuoteLines", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ItemName).IsRequired().HasMaxLength(100);
            b.Property(x => x.Specification).HasMaxLength(100);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
            b.Ignore(x => x.Subtotal);
            b.HasOne<SupplierQuote>()
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.SupplierQuoteId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.SupplierQuoteId });
        });

        // 第二轮第 1 批：供应商档案。Code 是业务唯一键：
        // (TenantId, Code) 复合唯一索引保证租户内唯一；SQLite 唯一索引允许多个 NULL，
        // 因此 host 档案（TenantId IS NULL）另建过滤唯一索引，沿用 PurchaseRequests 的写法。
        builder.Entity<Supplier>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "Suppliers", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(32);
            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.Property(x => x.ContactName).HasMaxLength(50);
            b.Property(x => x.ContactPhone).HasMaxLength(30);
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.Code)
                .HasDatabaseName("IX_AppSuppliers_HostCode")
                .IsUnique()
                .HasFilter("\"TenantId\" IS NULL");
        });

        // 第二轮第 1 批：物料档案。Code 唯一索引口径与供应商一致（租户内唯一 + host 过滤唯一）。
        builder.Entity<Material>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "Materials", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Code).IsRequired().HasMaxLength(32);
            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.Property(x => x.Specification).HasMaxLength(100);
            b.Property(x => x.Unit).IsRequired().HasMaxLength(20);
            b.Property(x => x.Category).HasMaxLength(50);
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasIndex(x => x.Code)
                .HasDatabaseName("IX_AppMaterials_HostCode")
                .IsUnique()
                .HasFilter("\"TenantId\" IS NULL");
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

        builder.Entity<PurchaseOrder>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "PurchaseOrders", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            // 第二轮主数据：SupplierId 可空、不建外键；订单保留供应商名称快照。
            b.Property(x => x.SupplierId);
            // P1-1："PO-" + OrderReference(64) 最长 67，原 64 上限会拒绝合法单号；
            // 单号上限扩到 128，OrderReference 本身仍保持 64。
            b.Property(x => x.OrderNumber).IsRequired().HasMaxLength(128);
            b.Property(x => x.SupplierName).IsRequired().HasMaxLength(100);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.ReceiptVersion).HasDefaultValue(0).IsConcurrencyToken();
            b.HasIndex(x => new { x.TenantId, x.OrderNumber }).IsUnique();
            b.HasIndex(x => x.PurchaseRequestId).IsUnique();
        });

        builder.Entity<PurchaseOrderLine>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "PurchaseOrderLines", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.ItemName).IsRequired().HasMaxLength(100);
            b.Property(x => x.Specification).HasMaxLength(100);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.Property(x => x.ReceivedQuantity).HasPrecision(18, 3).HasDefaultValue(0m);
            b.Property(x => x.UnitPrice).HasPrecision(18, 2);
            b.Ignore(x => x.Subtotal);
            b.HasOne<PurchaseOrder>()
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.TenantId, x.PurchaseOrderId });
        });

        builder.Entity<GoodsReceipt>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "GoodsReceipts", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            b.Property(x => x.RequestFingerprint).IsRequired().HasMaxLength(64);
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasOne<PurchaseOrder>()
                .WithMany()
                .HasForeignKey(x => x.PurchaseOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.TenantId, x.PurchaseOrderId, x.CreatorId, x.IdempotencyKey })
                .IsUnique();
            b.HasIndex(x => new { x.PurchaseOrderId, x.CreatorId, x.IdempotencyKey })
                .HasDatabaseName("IX_AppGoodsReceipts_HostOrderCreatorIdempotency")
                .IsUnique()
                .HasFilter("\"TenantId\" IS NULL");
            b.HasIndex(x => new { x.TenantId, x.PurchaseOrderId, x.CreationTime });
        });

        builder.Entity<GoodsReceiptLine>(b =>
        {
            b.ToTable(PurchaseFlowConsts.DbTablePrefix + "GoodsReceiptLines", PurchaseFlowConsts.DbSchema);
            b.ConfigureByConvention();
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.HasOne<GoodsReceipt>()
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.GoodsReceiptId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasOne<PurchaseOrderLine>()
                .WithMany()
                .HasForeignKey(x => x.PurchaseOrderLineId)
                .OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.GoodsReceiptId, x.PurchaseOrderLineId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.PurchaseOrderLineId });
        });
    }
}

