using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Projections.Abstractions;
using TaxVision.FinancialInsights.Domain.Projections;

namespace TaxVision.FinancialInsights.Application.Projections.Consumers;

/// <summary>
/// Proyecciones locales de permisos, plan, oficina y clientes. Usan el tenant del evento y descartan revisiones
/// viejas, así una reentrega o un desorden no deshace un cambio más nuevo.
/// </summary>
public static class ProjectionConsumer
{
    public static async Task Handle(
        UserRolesChangedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt)))
        {
            var row = await projections.FindUserPermissionsAsync(evt.TenantId, evt.UserId, ct);
            if (row is null)
                projections.AddUserPermissions(
                    UserPermissionsProjection.Create(
                        evt.TenantId,
                        evt.UserId,
                        evt.PermissionsVersion,
                        evt.PermissionCodes,
                        evt.RoleIds
                    )
                );
            else
                row.ApplyIfNewer(evt.PermissionsVersion, evt.PermissionCodes, evt.RoleIds);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    // Auth manda a cada usuario afectado su UserRolesChanged ya sin los denies; este evento nunca suma permisos.
    public static async Task Handle(
        RolePermissionsChangedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt)))
        {
            var row = await projections.FindRolePermissionsAsync(evt.TenantId, evt.RoleId, ct);
            if (row is null)
                projections.AddRolePermissions(
                    RolePermissionsProjection.Create(
                        evt.TenantId,
                        evt.RoleId,
                        evt.RoleName,
                        evt.PermissionsVersion,
                        evt.PermissionCodes
                    )
                );
            else
                row.ApplyIfNewer(evt.RoleName, evt.PermissionsVersion, evt.PermissionCodes);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static async Task Handle(
        TenantEntitlementsChangedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ITenantPlanCodeCacheInvalidator cache,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt)))
        {
            var modules = evt.ExtractEnabledModules();
            var documentsPerMonth = FinancialEntitlements.ParseDocumentsPerMonth(evt.EntitlementValues);
            var row = await projections.FindTenantEntitlementsAsync(evt.TenantId, ct);
            if (row is null)
                projections.AddTenantEntitlements(
                    TenantEntitlementsProjection.Create(
                        evt.TenantId,
                        evt.PlanCode,
                        evt.RevisionNumber,
                        modules,
                        documentsPerMonth
                    )
                );
            else
                row.ApplyIfNewer(evt.PlanCode, evt.RevisionNumber, modules, documentsPerMonth);
            await unitOfWork.SaveChangesAsync(ct);
            await cache.InvalidateAsync(evt.TenantId, ct);
        }
    }

    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.TenantId != evt.NewTenantId)
            return;
        using (correlation.Push(CorrelationOf(evt)))
        {
            var row = await projections.FindTenantDirectoryAsync(evt.TenantId, ct);
            if (row is null)
                projections.AddTenantDirectory(TenantDirectoryProjection.Create(evt.TenantId, evt.Name, evt.OccurredOn));
            else
                row.ApplyName(evt.Name, null, evt.OccurredOn);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static async Task Handle(
        TenantUpdatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.TenantId != evt.UpdatedTenantId)
            return;
        using (correlation.Push(CorrelationOf(evt)))
        {
            // Una actualización no prueba que la oficina esté activa.
            var row = await projections.FindTenantDirectoryAsync(evt.TenantId, ct);
            if (row is null)
                return;
            row.ApplyName(evt.Name, evt.TenantVersion, evt.UpdatedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static async Task Handle(
        TenantStatusChangedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.TenantId != evt.ChangedTenantId)
            return;
        using (correlation.Push(CorrelationOf(evt)))
        {
            var row = await projections.FindTenantDirectoryAsync(evt.TenantId, ct);
            if (row is null)
            {
                row = TenantDirectoryProjection.Create(evt.TenantId, string.Empty, evt.OccurredOn);
                projections.AddTenantDirectory(row);
            }
            row.ApplyStatus(evt.Status, evt.IsActive, evt.TenantVersion, evt.OccurredOn);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static Task Handle(
        CustomerCreatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => UpsertCustomer(evt, evt.CustomerId, evt.DisplayName, true, evt.OccurredOn, projections, unitOfWork, correlation, ct);

    public static Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => UpsertCustomer(evt, evt.CustomerId, evt.DisplayName, null, evt.OccurredOn, projections, unitOfWork, correlation, ct);

    public static Task Handle(
        CustomerActivatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => UpsertCustomer(evt, evt.CustomerId, null, true, evt.ActivatedAtUtc, projections, unitOfWork, correlation, ct);

    public static Task Handle(
        CustomerReactivatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => UpsertCustomer(evt, evt.CustomerId, null, true, evt.ReactivatedAtUtc, projections, unitOfWork, correlation, ct);

    public static Task Handle(
        CustomerDeactivatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => UpsertCustomer(evt, evt.CustomerId, null, false, evt.DeactivatedAtUtc, projections, unitOfWork, correlation, ct);

    public static Task Handle(
        CustomerArchivedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => UpsertCustomer(evt, evt.CustomerId, null, false, evt.OccurredOn, projections, unitOfWork, correlation, ct);

    /// <summary>
    /// El evento masivo no trae nombres (sin PII): agrega los clientes que faltan, activos y sin nombre; la
    /// reconciliación del directorio completa el nombre después. Los ya conocidos no se tocan.
    /// </summary>
    public static async Task Handle(
        CustomersBulkImportedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt)))
        {
            var ids = evt.CreatedCustomerIds.Concat(evt.UpdatedCustomerIds).Distinct().ToArray();
            foreach (var chunk in ids.Chunk(500))
            {
                var known = (await projections.FindKnownCustomerIdsAsync(evt.TenantId, chunk, ct)).ToHashSet();
                foreach (var id in chunk.Where(id => !known.Contains(id)))
                    projections.AddCustomer(CustomerDirectoryProjection.Create(evt.TenantId, id, null, evt.CompletedAtUtc));
                await unitOfWork.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task UpsertCustomer(
        IIntegrationEvent evt,
        Guid customerId,
        string? name,
        bool? active,
        DateTime observedAtUtc,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt)))
        {
            var row = await projections.FindCustomerAsync(evt.TenantId, customerId, ct);
            if (row is null)
            {
                row = CustomerDirectoryProjection.Create(evt.TenantId, customerId, name, observedAtUtc);
                projections.AddCustomer(row);
            }
            row.Apply(name, active ?? row.IsActive, observedAtUtc);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    internal static string CorrelationOf(IIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
