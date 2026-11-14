using System.Linq.Expressions;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Infrastructure.Persistence;

public sealed class FinancialInsightsDbContext(DbContextOptions<FinancialInsightsDbContext> options, ITenantContext tenantContext) : DbContext(options), IUnitOfWork
{
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportDocument> ImportDocuments => Set<ImportDocument>();
    public DbSet<FinancialMovement> FinancialMovements => Set<FinancialMovement>();
    public DbSet<AiProcessingReservation> AiProcessingReservations => Set<AiProcessingReservation>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();
    public DbSet<TenantEntitlementsProjection> TenantEntitlementsProjections => Set<TenantEntitlementsProjection>();
    public DbSet<TenantDirectoryProjection> TenantDirectoryProjections => Set<TenantDirectoryProjection>();
    public DbSet<CustomerDirectoryProjection> CustomerDirectoryProjections => Set<CustomerDirectoryProjection>();
    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportBatch>(b =>
        {
            b.ToTable("ImportBatches"); b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.CustomerId, x.CreatedAtUtc });
            b.HasMany(x => x.Documents).WithOne().HasForeignKey(x => x.BatchId);
        });
        modelBuilder.Entity<ImportDocument>(b =>
        {
            b.ToTable("ImportDocuments"); b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.FileId }).IsUnique();
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(x => x.FailureReason).HasMaxLength(128);
        });
        modelBuilder.Entity<FinancialMovement>(b =>
        {
            b.ToTable("FinancialMovements"); b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.CustomerId, x.TransactionDate });
            b.HasIndex(x => new { x.TenantId, x.ImportDocumentId });
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Amount).HasPrecision(19, 4);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Direction).HasMaxLength(8);
            b.Property(x => x.SuggestedCategory).HasMaxLength(120);
        });
        modelBuilder.Entity<AiProcessingReservation>(b =>
        {
            b.ToTable("AiProcessingReservations"); b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.ImportDocumentId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.MonthStartUtc });
            b.Property(x => x.Model).HasMaxLength(100);
        });
        modelBuilder.Entity<UserPermissionsProjection>(b =>
        {
            b.ToTable("UserPermissionsProjections"); b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            b.Property(x => x.PermissionCodesJson).HasColumnType("nvarchar(max)");
            b.Property(x => x.RoleIdsJson).HasMaxLength(2000);
        });
        modelBuilder.Entity<RolePermissionsProjection>(b =>
        {
            b.ToTable("RolePermissionsProjections"); b.HasKey(x => x.Id);
            b.Property(x => x.RoleName).HasMaxLength(100);
            b.Property(x => x.PermissionCodesJson).HasColumnType("nvarchar(max)");
        });
        modelBuilder.Entity<TenantEntitlementsProjection>(b =>
        {
            b.ToTable("TenantEntitlementsProjections"); b.HasKey(x => x.Id);
            b.Property(x => x.PlanCode).HasMaxLength(100);
            b.Property(x => x.EnabledModulesJson).HasColumnType("nvarchar(max)");
            b.Ignore(x => x.EnabledModules);
        });
        modelBuilder.Entity<TenantDirectoryProjection>(b =>
        {
            b.ToTable("TenantDirectoryProjections"); b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Status).HasMaxLength(32);
        });
        modelBuilder.Entity<CustomerDirectoryProjection>(b =>
        {
            b.ToTable("CustomerDirectoryProjections"); b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.CustomerId }).IsUnique();
            b.Property(x => x.DisplayName).HasMaxLength(240);
        });
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType)) continue;
            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantId = Expression.Property(parameter, nameof(ITenantOwned.TenantId));
            var context = Expression.Property(Expression.Constant(this), nameof(EffectiveTenantId));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(Expression.Equal(tenantId, context), parameter));
        }
    }
}
