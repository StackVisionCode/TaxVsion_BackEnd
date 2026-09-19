using System.Linq.Expressions;
using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Contacts;
using TaxVision.Campaigns.Domain.Permissions;
using TaxVision.Campaigns.Domain.RateLimiting;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Domain.Senders;

namespace TaxVision.Campaigns.Infrastructure.Persistence;

/// <summary>
/// DbContext del servicio Campaigns. Slice 1: solo el aggregate <see cref="Campaign"/>. Filtro
/// global fail-closed por <c>TenantId</c> (safety net EF Core, defense-in-depth) — mismo patrón que
/// <c>NotesDbContext</c>.
/// </summary>
public sealed class CampaignsDbContext(DbContextOptions<CampaignsDbContext> options, ITenantContext tenantContext)
    : DbContext(options),
        IUnitOfWork
{
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignRun> CampaignRuns => Set<CampaignRun>();
    public DbSet<CampaignRecipient> CampaignRecipients => Set<CampaignRecipient>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactList> ContactLists => Set<ContactList>();
    public DbSet<SenderProfile> SenderProfiles => Set<SenderProfile>();
    public DbSet<CampaignSchedule> CampaignSchedules => Set<CampaignSchedule>();
    public DbSet<UserPermissionsProjection> UserPermissionsProjections => Set<UserPermissionsProjection>();
    public DbSet<RolePermissionsProjection> RolePermissionsProjections => Set<RolePermissionsProjection>();
    public DbSet<TenantPlanCodeProjection> TenantPlanCodeProjections => Set<TenantPlanCodeProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        ApplyFailClosedTenantFilter(modelBuilder);
        base.OnModelCreating(modelBuilder);
    }

    private Guid EffectiveTenantId => tenantContext.HasTenant ? tenantContext.TenantId : Guid.Empty;

    private void ApplyFailClosedTenantFilter(ModelBuilder modelBuilder)
    {
        var contextConstant = Expression.Constant(this);
        var effectiveTenantIdAccess = Expression.Property(contextConstant, nameof(EffectiveTenantId));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var tenantProperty = Expression.Property(parameter, nameof(ITenantOwned.TenantId));

            var filter = Expression.Lambda(Expression.Equal(tenantProperty, effectiveTenantIdAccess), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException(
                "Persistence.UniqueConstraint",
                "A record with the same unique values already exists.",
                ex
            );
        }
    }
}
