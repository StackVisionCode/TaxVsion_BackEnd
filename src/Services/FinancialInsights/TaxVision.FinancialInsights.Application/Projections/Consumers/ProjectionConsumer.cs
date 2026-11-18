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
                        documentsPerMonth,
                        evt.CurrentPeriodStartUtc
                    )
                );
            else
                row.ApplyIfNewer(
                    evt.PlanCode,
                    evt.RevisionNumber,
                    modules,
                    documentsPerMonth,
                    evt.CurrentPeriodStartUtc
                );
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
                projections.AddTenantDirectory(
                    TenantDirectoryProjection.Create(evt.TenantId, evt.Name, evt.OccurredOn)
                );
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
    ) =>
        UpsertCustomer(
            evt,
            evt.CustomerId,
            evt.DisplayName,
            true,
            evt.OccurredOn,
            projections,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        UpsertCustomer(
            evt,
            evt.CustomerId,
            evt.DisplayName,
            null,
            evt.OccurredOn,
            projections,
            unitOfWork,
            correlation,
            ct
        );

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
    ) =>
        UpsertCustomer(evt, evt.CustomerId, null, true, evt.ReactivatedAtUtc, projections, unitOfWork, correlation, ct);

    public static Task Handle(
        CustomerDeactivatedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        UpsertCustomer(
            evt,
            evt.CustomerId,
            null,
            false,
            evt.DeactivatedAtUtc,
            projections,
            unitOfWork,
            correlation,
            ct
        );

    public static Task Handle(
        CustomerArchivedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => UpsertCustomer(evt, evt.CustomerId, null, false, evt.OccurredOn, projections, unitOfWork, correlation, ct);

    /// <summary>
    /// La importación avisa por bloque con la ficha de cada cliente: el nuevo entra activo y con nombre (como un
    /// alta); el existente se actualiza sin tocar su estado (como un cambio). Una sola transacción por bloque.
    /// </summary>
    public static async Task Handle(
        CustomersImportChunkCommittedIntegrationEvent evt,
        IFinancialProjectionRepository projections,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(CorrelationOf(evt)))
        {
            foreach (var card in evt.Customers)
                await ApplyCustomerAsync(
                    evt.TenantId,
                    card.CustomerId,
                    card.DisplayName,
                    card.IsNew ? true : null,
                    evt.OccurredOn,
                    projections,
                    ct
                );
            await unitOfWork.SaveChangesAsync(ct);
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
            await ApplyCustomerAsync(evt.TenantId, customerId, name, active, observedAtUtc, projections, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static async Task ApplyCustomerAsync(
        Guid tenantId,
        Guid customerId,
        string? name,
        bool? active,
        DateTime observedAtUtc,
        IFinancialProjectionRepository projections,
        CancellationToken ct
    )
    {
        var row = await projections.FindCustomerAsync(tenantId, customerId, ct);
        if (row is null)
        {
            row = CustomerDirectoryProjection.Create(tenantId, customerId, name, observedAtUtc);
            projections.AddCustomer(row);
        }
        row.Apply(name, active ?? row.IsActive, observedAtUtc);
    }

    internal static string CorrelationOf(IIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;
}
