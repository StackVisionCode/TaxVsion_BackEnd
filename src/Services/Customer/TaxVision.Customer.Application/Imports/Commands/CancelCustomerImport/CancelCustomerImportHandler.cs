using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Imports.Commands.CancelCustomerImport;

public static class CancelCustomerImportHandler
{
    public static async Task<Result> Handle(
        CancelCustomerImportCommand cmd,
        ICustomerImportRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var attempt = await repository.GetByIdAsync(cmd.ImportAttemptId, ct);
        if (attempt is null || attempt.TenantId != cmd.TenantId)
            return Result.Failure(new Error("Import.NotFound", "Import attempt not found."));

        var cancelResult = attempt.RequestCancel(cmd.RequestedByUserId);
        if (cancelResult.IsFailure)
            return cancelResult;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
