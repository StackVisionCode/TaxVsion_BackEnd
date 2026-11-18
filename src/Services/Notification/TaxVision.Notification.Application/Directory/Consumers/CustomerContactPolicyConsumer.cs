using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Directory.Consumers;

/// <summary>
/// Proyecta <c>Customer.ContactPolicy</c> (T7.3) de los eventos de Customer. Mismo patrón que
/// <see cref="CustomerEmailDirectoryConsumer"/>: la fila refleja la última foto conocida; si el
/// cliente aún no tiene fila, la política efectiva es "ambos ON" por default. Un evento más viejo que
/// el último aplicado no la pisa.
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
        await ApplyAsync(
            repository,
            evt.TenantId,
            evt.CustomerId,
            evt.EmailEnabled,
            evt.SmsEnabled,
            evt.Language,
            evt.OccurredOn,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await ApplyAsync(
            repository,
            evt.TenantId,
            evt.CustomerId,
            evt.EmailEnabled,
            evt.SmsEnabled,
            evt.Language,
            evt.OccurredOn,
            ct
        );
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
            await ApplyAsync(
                repository,
                evt.TenantId,
                card.CustomerId,
                card.EmailEnabled,
                card.SmsEnabled,
                card.Language,
                evt.OccurredOn,
                ct
            );
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static async Task ApplyAsync(
        ICustomerContactPolicyRepository repository,
        Guid tenantId,
        Guid customerId,
        bool emailEnabled,
        bool smsEnabled,
        string language,
        DateTime occurredOnUtc,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);
        if (existing is null)
            await repository.AddAsync(
                CustomerContactPolicyProjection.Create(
                    tenantId,
                    customerId,
                    emailEnabled,
                    smsEnabled,
                    language,
                    occurredOnUtc
                ),
                ct
            );
        else
            existing.ApplyIfNewer(occurredOnUtc, emailEnabled, smsEnabled, language);
    }
}
