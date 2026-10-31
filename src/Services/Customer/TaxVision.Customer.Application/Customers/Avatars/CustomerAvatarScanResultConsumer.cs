using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers.Avatars;

/// <summary>
/// Resultado del escaneo de las fotos de cliente (dueño Customer, carpeta Avatars). Correlaciona por el
/// OwnerId del evento (el customerId) y el FileId pendiente.
/// <list type="bullet">
///   <item><description>Available del pendiente → pasa a vigente; se borra la anterior y sale CustomerUpdated.</description></item>
///   <item><description>Available de la vigente → redelivery, no-op.</description></item>
///   <item><description>Available de otro fileId → huérfana (reemplazada, quitada, vencida o cliente archivado): se pide su borrado.</description></item>
///   <item><description>Infected / BlockedByPolicy / ScanFailed del pendiente → se descarta; la vigente no cambia.</description></item>
/// </list>
/// Los mismos eventos llegan por el fanout para archivos ajenos (imports, otros servicios): lo que no
/// es Customer/Avatars se ignora.
/// </summary>
public static class CustomerAvatarScanResultConsumer
{
    public static async Task Handle(
        FileAvailableIntegrationEvent msg,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<CustomerEntity> logger,
        CancellationToken ct
    )
    {
        if (
            !CustomerAvatarMessages.IsCustomerAvatar(msg.OwnerType, msg.FolderType) || msg.OwnerId is not { } customerId
        )
            return;

        using var _ = correlation.Push(CorrelationOf(msg.CorrelationId, msg.EventId));

        var customer = await repository.GetByIdAsync(customerId, ct);
        if (customer is null || customer.TenantId != msg.TenantId)
        {
            logger.LogWarning(
                "Avatar {FileId} became available for unknown customer {CustomerId}; ignoring.",
                msg.FileId,
                customerId
            );
            return;
        }

        if (customer.ProfilePictureFileId == msg.FileId)
            return;

        var confirmed = customer.ConfirmProfilePicture(msg.FileId);
        if (confirmed.IsFailure)
        {
            logger.LogInformation(
                "Avatar {FileId} of customer {CustomerId} is no longer pending; requesting its deletion.",
                msg.FileId,
                customerId
            );
            await bus.PublishAsync(
                CustomerAvatarMessages.Deletion(
                    msg.TenantId,
                    customerId,
                    msg.FileId,
                    CustomerAvatarMessages.Reasons.Orphaned,
                    correlation.CorrelationId
                )
            );
            return;
        }

        await unitOfWork.SaveChangesAsync(ct);

        if (confirmed.Value is { } previousFileId)
            await bus.PublishAsync(
                CustomerAvatarMessages.Deletion(
                    msg.TenantId,
                    customerId,
                    previousFileId,
                    CustomerAvatarMessages.Reasons.Replaced,
                    correlation.CorrelationId
                )
            );

        await bus.PublishAsync(CustomerUpdatedEvents.From(customer, msg.CreatedBy, correlation.CorrelationId));
        logger.LogInformation("Avatar {FileId} confirmed for customer {CustomerId}.", msg.FileId, customerId);
    }

    public static Task Handle(
        FileInfectedDetectedIntegrationEvent msg,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEntity> logger,
        CancellationToken ct
    ) =>
        DiscardAsync(
            new ScanFailure(
                msg.TenantId,
                msg.FileId,
                msg.OwnerType,
                msg.OwnerId,
                msg.FolderType,
                "failed the security scan"
            ),
            msg.CorrelationId,
            msg.EventId,
            repository,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    // T-20: bloqueada por contenido. Se descarta, pero queda constancia para el mensaje del front.
    public static Task Handle(
        FileBlockedByPolicyIntegrationEvent msg,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEntity> logger,
        CancellationToken ct
    ) =>
        ModerateAsync(
            new ScanFailure(
                msg.TenantId,
                msg.FileId,
                msg.OwnerType,
                msg.OwnerId,
                msg.FolderType,
                "was blocked by the content policy"
            ),
            msg.CorrelationId,
            msg.EventId,
            (customer, fileId) => customer.RejectPendingProfilePictureForPolicy(fileId),
            repository,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    // T-20: en revisión humana. La pendiente no vence; si el revisor aprueba, llega FileAvailable.
    public static Task Handle(
        FilePendingReviewIntegrationEvent msg,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEntity> logger,
        CancellationToken ct
    ) =>
        ModerateAsync(
            new ScanFailure(
                msg.TenantId,
                msg.FileId,
                msg.OwnerType,
                msg.OwnerId,
                msg.FolderType,
                "is under content review"
            ),
            msg.CorrelationId,
            msg.EventId,
            (customer, fileId) => customer.HoldPendingProfilePictureForReview(fileId),
            repository,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    // El consumer del logo de Tenant ignora este evento y la foto quedaba "procesando" para siempre.
    public static Task Handle(
        FileScanFailedIntegrationEvent msg,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEntity> logger,
        CancellationToken ct
    ) =>
        DiscardAsync(
            new ScanFailure(
                msg.TenantId,
                msg.FileId,
                msg.OwnerType,
                msg.OwnerId,
                msg.FolderType,
                "could not be scanned"
            ),
            msg.CorrelationId,
            msg.EventId,
            repository,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    private sealed record ScanFailure(
        Guid TenantId,
        Guid FileId,
        string? OwnerType,
        Guid? OwnerId,
        string? FolderType,
        string Reason
    );

    private static Task DiscardAsync(
        ScanFailure failure,
        string? correlationId,
        Guid eventId,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEntity> logger,
        CancellationToken ct
    ) =>
        ModerateAsync(
            failure,
            correlationId,
            eventId,
            (customer, fileId) => customer.DiscardPendingProfilePicture(fileId),
            repository,
            unitOfWork,
            correlation,
            logger,
            ct
        );

    private static async Task ModerateAsync(
        ScanFailure failure,
        string? correlationId,
        Guid eventId,
        Func<CustomerEntity, Guid, bool> apply,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<CustomerEntity> logger,
        CancellationToken ct
    )
    {
        if (
            !CustomerAvatarMessages.IsCustomerAvatar(failure.OwnerType, failure.FolderType)
            || failure.OwnerId is not { } customerId
        )
            return;

        using var _ = correlation.Push(CorrelationOf(correlationId, eventId));

        var customer = await repository.GetByIdAsync(customerId, ct);
        if (customer is null || customer.TenantId != failure.TenantId)
            return;

        if (!apply(customer, failure.FileId))
            return;

        logger.LogWarning(
            "Avatar {FileId} of customer {CustomerId} {Reason}.",
            failure.FileId,
            customerId,
            failure.Reason
        );
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static string CorrelationOf(string? correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
