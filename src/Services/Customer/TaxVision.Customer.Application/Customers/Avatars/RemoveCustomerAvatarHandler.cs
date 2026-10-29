using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Avatars;

/// <summary>
/// Quita la foto (y cualquier subida en curso). Idempotente: sin foto ni pendiente no guarda ni publica.
/// La foto quitada se borra en CloudStorage por evento y las proyecciones se enteran por CustomerUpdated.
/// </summary>
public static class RemoveCustomerAvatarHandler
{
    public static async Task<Result> Handle(
        RemoveCustomerAvatarCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
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
            return access;

        var hadPending = customer!.PendingProfilePictureFileId is not null;
        var removed = customer.RemoveProfilePicture(cmd.ActorUserId);
        if (removed.IsFailure)
            return removed;
        if (removed.Value is null && !hadPending)
            return Result.Success();

        await unitOfWork.SaveChangesAsync(ct);

        // Solo cambió lo visible si había foto vigente; una pendiente quitada llegará huérfana y se borra sola.
        if (removed.Value is { } removedFileId)
        {
            await bus.PublishAsync(
                CustomerAvatarMessages.Deletion(
                    customer.TenantId,
                    customer.Id,
                    removedFileId,
                    CustomerAvatarMessages.Reasons.Removed,
                    correlation.CorrelationId
                )
            );
            await bus.PublishAsync(CustomerUpdatedEvents.From(customer, cmd.ActorUserId, correlation.CorrelationId));
        }

        return Result.Success();
    }
}
