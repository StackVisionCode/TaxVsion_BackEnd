using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Domain.Customers;

namespace TaxVision.Sms.Application.Customers.Consumers;

/// <summary>
/// Proyecta la política de contacto (canal SMS) desde los mismos eventos de Customer que usa Notification.
/// La fila refleja la última foto conocida.
/// </summary>
public static class CustomerContactPolicyConsumer
{
    public static Task Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => UpsertAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, evt.SmsEnabled, ct);

    public static Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) => UpsertAsync(repository, unitOfWork, evt.TenantId, evt.CustomerId, evt.SmsEnabled, ct);

    private static async Task UpsertAsync(
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        Guid customerId,
        bool smsEnabled,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);
        if (existing is null)
            await repository.AddAsync(CustomerContactPolicyProjection.Create(tenantId, customerId, smsEnabled), ct);
        else
            existing.Reconcile(smsEnabled);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
