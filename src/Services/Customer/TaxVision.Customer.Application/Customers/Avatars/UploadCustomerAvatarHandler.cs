using BuildingBlocks.Common;
using BuildingBlocks.Imaging;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Avatars;

/// <summary>
/// Sube la foto del cliente: valida acceso → normaliza (WebP 512 sin metadatos) → PUT al bucket temporal
/// → guarda la pendiente → recién entonces pide a CloudStorage catalogar y escanear. Guardar antes de
/// publicar: si el resultado del escaneo llegara antes que la fila, la foto quedaría huérfana.
/// </summary>
public static class UploadCustomerAvatarHandler
{
    public static readonly Error StorageUnavailable = new(
        "Customer.Avatar.StorageUnavailable",
        "The picture could not be stored right now. Please try again."
    );

    public static async Task<Result<UploadCustomerAvatarResponse>> Handle(
        UploadCustomerAvatarCommand cmd,
        ICustomerRepository repository,
        IAvatarImageNormalizer normalizer,
        ICustomerAvatarStorage storage,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        TimeProvider time,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        var access = CustomerAvatarAccess.Check(
            customer,
            cmd.TenantId,
            cmd.ActorUserId,
            cmd.Caller,
            cmd.CallerCanViewAllCustomers
        );
        if (access.IsFailure)
            return Result.Failure<UploadCustomerAvatarResponse>(access.Error);

        // Acceso antes que la imagen: decodificar cuesta CPU y memoria, no se gasta en quien no puede.
        using var input = new MemoryStream(cmd.Content, writable: false);
        var normalized = await normalizer.NormalizeAsync(input, ct);
        if (normalized.IsFailure)
            return Result.Failure<UploadCustomerAvatarResponse>(normalized.Error);

        var avatar = normalized.Value;
        var fileId = Guid.NewGuid();
        var stored = await storage.StoreAsync(cmd.TenantId, fileId, avatar.Content, avatar.ContentType, ct);
        if (stored.IsFailure)
            return Result.Failure<UploadCustomerAvatarResponse>(StorageUnavailable);

        var begun = customer!.BeginProfilePictureChange(fileId, cmd.ActorUserId, time.GetUtcNow().UtcDateTime);
        if (begun.IsFailure)
            return Result.Failure<UploadCustomerAvatarResponse>(begun.Error);

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            CustomerAvatarMessages.SaveRequested(
                cmd.TenantId,
                customer.Id,
                fileId,
                stored.Value,
                avatar.ContentType,
                avatar.Content.LongLength,
                cmd.ActorUserId,
                correlation.CorrelationId
            )
        );

        return Result.Success(new UploadCustomerAvatarResponse(fileId, CustomerAvatarStatus.Processing));
    }
}
