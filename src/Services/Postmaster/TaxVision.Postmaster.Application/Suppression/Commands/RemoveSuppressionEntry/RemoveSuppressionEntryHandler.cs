using BuildingBlocks.Persistence;
using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Application.Suppression.Commands.RemoveSuppressionEntry;

public static class RemoveSuppressionEntryHandler
{
    public static async Task<Result> Handle(
        RemoveSuppressionEntryCommand cmd,
        ISuppressionListRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var removed = await repository.RemoveAsync(cmd.TenantId, cmd.Address, ct);
        if (!removed)
            return Result.Failure(new Error("SuppressionListEntry.NotFound", $"'{cmd.Address}' is not suppressed."));

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
