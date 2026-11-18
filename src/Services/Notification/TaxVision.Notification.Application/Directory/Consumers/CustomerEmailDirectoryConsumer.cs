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
/// <remarks>
/// El nombre va en singular a proposito: Wolverine descubre por convencion los tipos que terminan en
/// <c>Handler</c> o <c>Consumer</c>, y en plural no los ve. Se llamaba <c>Consumers</c> y por eso este
/// directorio solo se llenaba con el job de reconciliacion — los eventos entraban y salian con un
/// «No known handler» en el log, sin error y sin fila.
/// </remarks>
public static class CustomerEmailDirectoryConsumer
{
    public static Task Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        UpsertAsync(
            repository,
            unitOfWork,
            evt.TenantId,
            evt.CustomerId,
            evt.PrimaryEmail,
            evt.DisplayName,
            isActive: true,
            evt.OccurredOn,
            ct
        );

    public static Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        UpsertAsync(
            repository,
            unitOfWork,
            evt.TenantId,
            evt.CustomerId,
            evt.PrimaryEmail,
            evt.DisplayName,
            IsActiveStatus(evt.Status),
            evt.OccurredOn,
            ct
        );

    // La importación avisa por bloque con el email de cada cliente: ya no se pide a Customer por HTTP.
    public static async Task Handle(
        CustomersImportChunkCommittedIntegrationEvent evt,
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var written = false;
        foreach (var card in evt.Customers)
            written |= await ApplyAsync(
                repository,
                evt.TenantId,
                card.CustomerId,
                card.PrimaryEmail,
                card.DisplayName,
                IsActiveStatus(card.Status),
                evt.OccurredOn,
                ct
            );
        if (written)
            await unitOfWork.SaveChangesAsync(ct);
    }

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

    private static async Task UpsertAsync(
        ICustomerEmailDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        Guid customerId,
        string? email,
        string? displayName,
        bool? isActive,
        DateTime occurredOnUtc,
        CancellationToken ct
    )
    {
        if (await ApplyAsync(repository, tenantId, customerId, email, displayName, isActive, occurredOnUtc, ct))
            await unitOfWork.SaveChangesAsync(ct);
    }

    // E-3: el estado viaja en el aviso; null (mensaje viejo) = no se sabe y se conserva el de la fila.
    private static bool? IsActiveStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ? null : string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sin email no se crea la fila: una entrada vacía haría creer que el cliente es alcanzable y el
    /// envío fallaría en silencio en vez de saltarse el aviso con un motivo claro. Con
    /// <paramref name="occurredOnUtc"/> (eventos) un evento más viejo que el último aplicado no pisa.
    /// <paramref name="isActive"/> null conserva el flag de la fila; una fila nueva sin dato nace activa.
    /// </summary>
    internal static async Task<bool> ApplyAsync(
        ICustomerEmailDirectoryRepository repository,
        Guid tenantId,
        Guid customerId,
        string? email,
        string? displayName,
        bool? isActive,
        DateTime? occurredOnUtc,
        CancellationToken ct
    )
    {
        var normalized = CustomerEmailDirectoryEntry.Normalize(email);
        var existing = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);

        if (existing is not null)
        {
            var active = isActive ?? existing.IsActive;
            if (occurredOnUtc is { } occurredOn)
                return existing.ApplyIfNewer(occurredOn, normalized, displayName ?? string.Empty, active);
            existing.Reconcile(normalized, displayName ?? string.Empty, active);
        }
        else if (normalized.Length > 0)
            await repository.AddAsync(
                CustomerEmailDirectoryEntry.Create(
                    tenantId,
                    customerId,
                    normalized,
                    displayName ?? string.Empty,
                    occurredOnUtc,
                    isActive ?? true
                ),
                ct
            );
        else
            return false;

        return true;
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
