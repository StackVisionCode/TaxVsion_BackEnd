using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Directory.Consumers;

/// <summary>
/// Proyecta <c>Customer.ContactPolicy</c> (T7.3) de los eventos de Customer. Mismo patrón que
/// <see cref="CustomerEmailDirectoryConsumer"/>: la fila refleja la última foto conocida; si el
/// cliente aún no tiene fila, la política efectiva es "ambos ON" por default.
/// </summary>
public static class CustomerContactPolicyConsumer
{
    public static Task Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => UpsertAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, evt.EmailEnabled, evt.SmsEnabled, ct);

    public static Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => UpsertAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, evt.EmailEnabled, evt.SmsEnabled, ct);

    private static async Task UpsertAsync(
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        Guid customerId,
        bool emailEnabled,
        bool smsEnabled,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);
        if (existing is null)
            await repository.AddAsync(
                CustomerContactPolicyProjection.Create(tenantId, customerId, emailEnabled, smsEnabled),
                ct
            );
        else
            existing.Reconcile(emailEnabled, smsEnabled);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
