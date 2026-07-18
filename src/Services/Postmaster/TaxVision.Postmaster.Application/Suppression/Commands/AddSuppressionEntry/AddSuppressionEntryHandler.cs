using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Domain.Suppression;

namespace TaxVision.Postmaster.Application.Suppression.Commands.AddSuppressionEntry;

/// <summary>Upsert: si la dirección ya estaba suprimida, refresca motivo/fecha en vez de duplicar la fila.</summary>
public static class AddSuppressionEntryHandler
{
    public static async Task<Result> Handle(
        AddSuppressionEntryCommand cmd,
        ISuppressionListRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var now = DateTime.UtcNow;
        var existing = await repository.GetByAddressAsync(cmd.TenantId, cmd.Address, ct);
        if (existing.IsSuccess)
        {
            existing.Value.Reactivate(cmd.Reason, cmd.AddedByUserId, cmd.Notes, now);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        var createResult = SuppressionListEntry.Create(
            cmd.TenantId,
            cmd.Address,
            cmd.Reason,
            cmd.AddedByUserId,
            cmd.Notes,
            now
        );
        if (createResult.IsFailure)
            return Result.Failure(createResult.Error);

        await repository.AddAsync(createResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
