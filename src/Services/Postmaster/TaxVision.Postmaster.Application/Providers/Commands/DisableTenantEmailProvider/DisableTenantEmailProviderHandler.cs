using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Providers;

namespace TaxVision.Postmaster.Application.Providers.Commands.DisableTenantEmailProvider;

public static class DisableTenantEmailProviderHandler
{
    public static async Task<Result> Handle(
        DisableTenantEmailProviderCommand cmd,
        ITenantEmailProviderRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var lookup = await repository.GetByCodeAsync(cmd.TenantId, cmd.ProviderCode, ct);
        if (lookup.IsFailure)
            return Result.Failure(lookup.Error);

        lookup.Value.Disable(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
