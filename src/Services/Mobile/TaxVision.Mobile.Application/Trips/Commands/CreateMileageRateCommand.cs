using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Commands;

public sealed record CreateMileageRateCommand(Guid TenantId, CreateRateInput Input);

public static class CreateMileageRateHandler
{
    public static async Task<Result<MileageRateResponse>> Handle(
        CreateMileageRateCommand cmd,
        ITripRepository trips,
        IUnitOfWork work,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var created = MileageRate.Create(
            cmd.TenantId,
            cmd.Input.Purpose,
            cmd.Input.EffectiveFrom,
            cmd.Input.RatePerMile,
            cmd.Input.Currency,
            clock.GetUtcNow().UtcDateTime
        );
        if (created.IsFailure)
            return Result.Failure<MileageRateResponse>(created.Error);
        trips.AddRate(created.Value);
        await work.SaveChangesAsync(ct);
        return Result.Success(MileageRateResponse.From(created.Value));
    }
}
