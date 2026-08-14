using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Directory.Consumers;

/// <summary>
/// Los seis eventos de Customer que mueven la dirección de un cliente. Van juntos porque el cuerpo es
/// el mismo upsert y separarlos en seis archivos de ocho líneas escondería que comparten invariante:
/// la fila refleja la última foto conocida y sólo cambia el flag de activo.
/// </summary>
public static class CustomerEmailDirectoryConsumers
{
    public static Task Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => UpsertAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, evt.PrimaryEmail, evt.DisplayName, true, ct);

    public static Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => UpsertAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, evt.PrimaryEmail, evt.DisplayName, true, ct);

    public static Task Handle(
        CustomerReactivatedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => SetActiveAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, true, ct);

    public static Task Handle(
        CustomerActivatedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => SetActiveAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, true, ct);

    public static Task Handle(
        CustomerDeactivatedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => SetActiveAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, false, ct);

    public static Task Handle(
        CustomerArchivedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => SetActiveAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, false, ct);

    /// <summary>
    /// Sin email no se crea la fila: una entrada vacía haría creer que el cliente es alcanzable y el
    /// envío fallaría en silencio en vez de saltarse el aviso con un motivo claro.
    /// </summary>
    private static async Task UpsertAsync(
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        Guid customerId,
        string? email,
        string? displayName,
        bool isActive,
        CancellationToken ct
    )
    {
        var normalized = CustomerEmailDirectoryEntry.Normalize(email);
        var existing = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);

        if (existing is not null)
            existing.Reconcile(normalized, displayName ?? string.Empty, isActive);
        else if (normalized.Length > 0)
            await repository.AddAsync(
                CustomerEmailDirectoryEntry.Create(tenantId, customerId, normalized, displayName ?? string.Empty),
                ct
            );
        else
            return;

        await unitOfWork.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Baja y alta sólo mueven el flag: esos eventos no traen correo, y pisar el guardado con vacío
    /// dejaría al cliente sin dirección justo cuando vuelve a estar activo.
    /// </summary>
    private static async Task SetActiveAsync(
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        Guid customerId,
        bool isActive,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);
        if (existing is null)
            return;

        existing.Reconcile(existing.NormalizedEmail, existing.DisplayName, isActive);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
