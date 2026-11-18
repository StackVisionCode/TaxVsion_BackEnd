using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Domain.Customers;

namespace TaxVision.Sms.Application.Customers.Consumers;

/// <summary>
/// Proyecta la política de contacto (canal SMS) desde los mismos eventos de Customer que usa Notification.
/// La fila refleja la última foto conocida; un evento más viejo que el último aplicado no la pisa.
/// </summary>
public static class CustomerContactPolicyConsumer
{
    public static async Task Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await ApplyAsync(repository, evt.TenantId, evt.CustomerId, evt.SmsEnabled, evt.OccurredOn, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await ApplyAsync(repository, evt.TenantId, evt.CustomerId, evt.SmsEnabled, evt.OccurredOn, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    // La importación avisa por bloque con la política real de cada cliente (nuevo o actualizado).
    public static async Task Handle(
        CustomersImportChunkCommittedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        foreach (var card in evt.Customers)
            await ApplyAsync(repository, evt.TenantId, card.CustomerId, card.SmsEnabled, evt.OccurredOn, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static async Task ApplyAsync(
        ICustomerContactPolicyRepository repository,
        Guid tenantId,
        Guid customerId,
        bool smsEnabled,
        DateTime occurredOnUtc,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);
        if (existing is null)
            await repository.AddAsync(
                CustomerContactPolicyProjection.Create(tenantId, customerId, smsEnabled, occurredOnUtc),
                ct
            );
        else
            existing.ApplyIfNewer(occurredOnUtc, smsEnabled);
    }
}
