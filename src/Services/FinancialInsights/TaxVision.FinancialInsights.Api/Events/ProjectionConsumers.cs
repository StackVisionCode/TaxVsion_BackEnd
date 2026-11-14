using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Domain.Projections;
using TaxVision.FinancialInsights.Infrastructure.Persistence;

namespace TaxVision.FinancialInsights.Api.Events;

public static class ProjectionConsumer
{
    public static async Task Handle(UserRolesChangedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct)
    {
        var row = await db.UserPermissionsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == evt.TenantId && x.UserId == evt.UserId, ct);
        if (row is null)
            db.UserPermissionsProjections.Add(UserPermissionsProjection.Create(evt.TenantId, evt.UserId,
                evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds));
        else row.ApplyIfNewer(evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds);
        await db.SaveChangesAsync(ct);
    }

    // Role changes are not unioned into user permissions. Auth sends each affected user a
    // UserRolesChanged event containing roles minus that user's explicit denies.
    public static async Task Handle(RolePermissionsChangedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct)
    {
        var row = await db.RolePermissionsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == evt.TenantId && x.Id == evt.RoleId, ct);
        if (row is null)
            db.RolePermissionsProjections.Add(RolePermissionsProjection.Create(evt.TenantId, evt.RoleId,
                evt.RoleName, evt.PermissionsVersion, evt.PermissionCodes));
        else row.ApplyIfNewer(evt.RoleName, evt.PermissionsVersion, evt.PermissionCodes);
        await db.SaveChangesAsync(ct);
    }

    public static Task Handle(UserDeactivatedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        SetUserActive(evt.TenantId, evt.UserId, false, evt.OccurredOn, db, ct);
    public static Task Handle(UserOffboardedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        SetUserActive(evt.TenantId, evt.UserId, false, evt.OccurredOn, db, ct);
    public static Task Handle(UserReactivatedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        SetUserActive(evt.TenantId, evt.UserId, true, evt.OccurredOn, db, ct);

    private static async Task SetUserActive(Guid tenantId, Guid userId, bool active, DateTime observedAtUtc,
        FinancialInsightsDbContext db, CancellationToken ct)
    {
        var row = await db.UserPermissionsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);
        if (row is null)
        {
            if (active) return; // Let the first request pull a complete Auth snapshot.
            row = UserPermissionsProjection.CreateInactive(tenantId, userId, observedAtUtc);
            db.UserPermissionsProjections.Add(row);
        }
        row.SetActive(active, observedAtUtc);
        await db.SaveChangesAsync(ct);
    }

    public static async Task Handle(TenantEntitlementsChangedIntegrationEvent evt, FinancialInsightsDbContext db,
        FinancialEntitlementsCacheInvalidator cache, CancellationToken ct)
    {
        var row = await db.TenantEntitlementsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == evt.TenantId, ct);
        var modules = evt.ExtractEnabledModules();
        if (row is null)
            db.TenantEntitlementsProjections.Add(TenantEntitlementsProjection.Create(evt.TenantId,
                evt.PlanCode, evt.RevisionNumber, modules));
        else row.ApplyIfNewer(evt.PlanCode, evt.RevisionNumber, modules);
        await db.SaveChangesAsync(ct);
        await cache.InvalidateAsync(evt.TenantId, ct);
    }

    public static async Task Handle(TenantCreatedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct)
    {
        if (evt.TenantId != evt.NewTenantId) return;
        var row = await db.TenantDirectoryProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == evt.TenantId, ct);
        if (row is null) db.TenantDirectoryProjections.Add(TenantDirectoryProjection.Create(evt.TenantId, evt.Name, evt.OccurredOn));
        else row.ApplyName(evt.Name, null, evt.OccurredOn);
        await db.SaveChangesAsync(ct);
    }

    public static async Task Handle(TenantUpdatedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct)
    {
        if (evt.TenantId != evt.UpdatedTenantId) return;
        var row = await db.TenantDirectoryProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == evt.TenantId, ct);
        if (row is null) return; // An update does not prove that this tenant is active.
        row.ApplyName(evt.Name, evt.TenantVersion, evt.UpdatedAtUtc);
        await db.SaveChangesAsync(ct);
    }

    public static async Task Handle(TenantStatusChangedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct)
    {
        if (evt.TenantId != evt.ChangedTenantId) return;
        var row = await db.TenantDirectoryProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == evt.TenantId, ct);
        if (row is null)
        {
            row = TenantDirectoryProjection.Create(evt.TenantId, string.Empty, evt.OccurredOn);
            db.TenantDirectoryProjections.Add(row);
        }
        row.ApplyStatus(evt.Status, evt.IsActive, evt.TenantVersion, evt.OccurredOn);
        await db.SaveChangesAsync(ct);
    }

    public static Task Handle(CustomerCreatedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        UpsertCustomer(evt.TenantId, evt.CustomerId, evt.DisplayName, true, evt.OccurredOn, db, ct);
    public static Task Handle(CustomerUpdatedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        UpsertCustomer(evt.TenantId, evt.CustomerId, evt.DisplayName, null, evt.OccurredOn, db, ct);
    public static Task Handle(CustomerActivatedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        UpsertCustomer(evt.TenantId, evt.CustomerId, null, true, evt.ActivatedAtUtc, db, ct);
    public static Task Handle(CustomerReactivatedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        UpsertCustomer(evt.TenantId, evt.CustomerId, null, true, evt.ReactivatedAtUtc, db, ct);
    public static Task Handle(CustomerDeactivatedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        UpsertCustomer(evt.TenantId, evt.CustomerId, null, false, evt.DeactivatedAtUtc, db, ct);
    public static Task Handle(CustomerArchivedIntegrationEvent evt, FinancialInsightsDbContext db, CancellationToken ct) =>
        UpsertCustomer(evt.TenantId, evt.CustomerId, null, false, evt.OccurredOn, db, ct);

    private static async Task UpsertCustomer(Guid tenantId, Guid customerId, string? name, bool? active,
        DateTime observedAtUtc, FinancialInsightsDbContext db, CancellationToken ct)
    {
        var row = await db.CustomerDirectoryProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.CustomerId == customerId, ct);
        if (row is null)
        {
            row = CustomerDirectoryProjection.Create(tenantId, customerId, name, observedAtUtc);
            db.CustomerDirectoryProjections.Add(row);
        }
        row.Apply(name, active ?? row.IsActive, observedAtUtc);
        await db.SaveChangesAsync(ct);
    }
}
