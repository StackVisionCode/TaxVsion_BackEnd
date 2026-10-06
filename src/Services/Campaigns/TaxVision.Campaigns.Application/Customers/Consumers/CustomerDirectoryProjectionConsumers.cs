using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Campaigns.Application.Contacts.Abstractions;

namespace TaxVision.Campaigns.Application.Customers.Consumers;

/// <summary>
/// Mantiene la proyección local del directorio de clientes (<see cref="ICustomerDirectoryStore"/>) desde los
/// eventos de Customer — event-carried state transfer, version-guarded por <c>OccurredOn</c>. Es la fuente
/// de verdad local de "quién es cliente" para la audiencia y la regla contacto⇔cliente, sin llamar la API.
/// Auto-descubierto por Wolverine (vive en el assembly de la Application).
/// </summary>
public static class CustomerDirectoryProjectionConsumer
{
    public static async Task Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerDirectoryStore store,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (Push(correlation, evt.CorrelationId, evt.EventId))
        {
            if (await IsStaleAsync(store, evt.TenantId, evt.CustomerId, evt.OccurredOn, ct))
                return;
            await store.UpsertAsync(
                evt.TenantId,
                evt.CustomerId,
                evt.DisplayName,
                evt.PrimaryEmail,
                evt.PrimaryPhone,
                CustomerDirectoryStatus.Active,
                evt.OccurredOn,
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static async Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerDirectoryStore store,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (Push(correlation, evt.CorrelationId, evt.EventId))
        {
            if (await IsStaleAsync(store, evt.TenantId, evt.CustomerId, evt.OccurredOn, ct))
                return;
            await store.UpdateDetailsAsync(
                evt.TenantId,
                evt.CustomerId,
                evt.DisplayName,
                evt.PrimaryEmail,
                evt.PrimaryPhone,
                evt.OccurredOn,
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    public static Task Handle(
        CustomerArchivedIntegrationEvent evt,
        ICustomerDirectoryStore store,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => ApplyStatusAsync(evt.TenantId, evt.CustomerId, CustomerDirectoryStatus.Archived, evt.OccurredOn, evt.CorrelationId, evt.EventId, store, unitOfWork, correlation, ct);

    public static Task Handle(
        CustomerDeactivatedIntegrationEvent evt,
        ICustomerDirectoryStore store,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => ApplyStatusAsync(evt.TenantId, evt.CustomerId, CustomerDirectoryStatus.Inactive, evt.OccurredOn, evt.CorrelationId, evt.EventId, store, unitOfWork, correlation, ct);

    public static Task Handle(
        CustomerActivatedIntegrationEvent evt,
        ICustomerDirectoryStore store,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => ApplyStatusAsync(evt.TenantId, evt.CustomerId, CustomerDirectoryStatus.Active, evt.OccurredOn, evt.CorrelationId, evt.EventId, store, unitOfWork, correlation, ct);

    public static Task Handle(
        CustomerReactivatedIntegrationEvent evt,
        ICustomerDirectoryStore store,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    ) => ApplyStatusAsync(evt.TenantId, evt.CustomerId, CustomerDirectoryStatus.Active, evt.OccurredOn, evt.CorrelationId, evt.EventId, store, unitOfWork, correlation, ct);

    private static async Task ApplyStatusAsync(
        Guid tenantId,
        Guid customerId,
        string status,
        DateTime version,
        string correlationId,
        Guid eventId,
        ICustomerDirectoryStore store,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (Push(correlation, correlationId, eventId))
        {
            if (await IsStaleAsync(store, tenantId, customerId, version, ct))
                return;
            await store.SetStatusAsync(tenantId, customerId, status, version, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static async Task<bool> IsStaleAsync(
        ICustomerDirectoryStore store,
        Guid tenantId,
        Guid customerId,
        DateTime version,
        CancellationToken ct
    )
    {
        var applied = await store.GetVersionAsync(tenantId, customerId, ct);
        return applied is { } current && version <= current; // igual o más viejo ⇒ reordenado/duplicado
    }

    private static IDisposable Push(ICorrelationContext correlation, string? correlationId, Guid eventId) =>
        correlation.Push(string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId!);
}
