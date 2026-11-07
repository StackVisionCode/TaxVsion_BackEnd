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
    private const int BulkChunkSize = 500;

    public static Task Handle(
        CustomerCreatedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        UpsertAsync(
            repository,
            unitOfWork,
            evt.TenantId,
            evt.CustomerId,
            evt.EmailEnabled,
            evt.SmsEnabled,
            evt.Language,
            ct
        );

    public static Task Handle(
        CustomerUpdatedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    ) =>
        UpsertAsync(
            repository,
            unitOfWork,
            evt.TenantId,
            evt.CustomerId,
            evt.EmailEnabled,
            evt.SmsEnabled,
            evt.Language,
            ct
        );

    // La importación no manda CustomerCreated. Todo importado nace con la política por defecto; el idioma
    // lo completa la reconciliación. Lo que un evento ya proyectó no se toca.
    public static async Task Handle(
        CustomersBulkImportedIntegrationEvent evt,
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        foreach (var chunk in evt.CreatedCustomerIds.Distinct().Chunk(BulkChunkSize))
        {
            var existing = await repository.GetExistingCustomerIdsAsync(evt.TenantId, chunk, ct);
            foreach (var customerId in chunk.Except(existing))
                await repository.AddAsync(
                    CustomerContactPolicyProjection.Create(
                        evt.TenantId,
                        customerId,
                        emailEnabled: true,
                        smsEnabled: true
                    ),
                    ct
                );

            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    private static async Task UpsertAsync(
        ICustomerContactPolicyRepository repository,
        IUnitOfWork unitOfWork,
        Guid tenantId,
        Guid customerId,
        bool emailEnabled,
        bool smsEnabled,
        string language,
        CancellationToken ct
    )
    {
        var existing = await repository.GetByCustomerIdAsync(tenantId, customerId, ct);
        if (existing is null)
            await repository.AddAsync(
                CustomerContactPolicyProjection.Create(tenantId, customerId, emailEnabled, smsEnabled, language),
                ct
            );
        else
            existing.Reconcile(emailEnabled, smsEnabled, language);

        await unitOfWork.SaveChangesAsync(ct);
    }
}
