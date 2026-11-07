using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Avatars;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.Archive;

public static class ArchiveCustomerHandler
{
    public static async Task<Result> Handle(
        ArchiveCustomerCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.TenantId, cmd.CustomerId, ct);
        if (customer is null)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        // Archive() retira la foto; se captura antes para pedir su borrado.
        var photoFileId = customer.ProfilePictureFileId;
        var result = customer.Archive(cmd.ArchivedByUserId);
        if (result.IsFailure)
            return result;

        // CustomerArchived sale del evento de dominio; aquí solo el borrado de la foto.
        await unitOfWork.SaveChangesAsync(ct);

        if (photoFileId is { } fileId)
            await bus.PublishAsync(
                CustomerAvatarMessages.Deletion(
                    customer.TenantId,
                    customer.Id,
                    fileId,
                    CustomerAvatarMessages.Reasons.CustomerArchived,
                    correlation.CorrelationId
                )
            );

        return Result.Success();
    }
}
