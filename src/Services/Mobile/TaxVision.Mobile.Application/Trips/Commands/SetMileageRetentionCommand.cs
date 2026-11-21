using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;
using TaxVision.Mobile.Domain.Trips;

namespace TaxVision.Mobile.Application.Trips.Commands;

public sealed record SetMileageRetentionCommand(Guid TenantId, int? RetentionDays);

public static class SetMileageRetentionHandler
{
    public static async Task<Result<MileageRetentionResponse>> Handle(
        SetMileageRetentionCommand cmd,
        ITripRepository trips,
        IUnitOfWork work,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var policy = await trips.RetentionPolicyAsync(cmd.TenantId, ct);
        if (policy is null)
        {
            // Dos primeras escrituras a la vez chocan con la fila única de la oficina: la segunda sale como conflicto.
            var created = MileageRetentionPolicy.Create(cmd.TenantId, cmd.RetentionDays, now);
            if (created.IsFailure)
                return Result.Failure<MileageRetentionResponse>(created.Error);
            trips.AddRetentionPolicy(created.Value);
        }
        else
        {
            var updated = policy.Update(cmd.RetentionDays, now);
            if (updated.IsFailure)
                return Result.Failure<MileageRetentionResponse>(updated.Error);
        }
        await work.SaveChangesAsync(ct);
        return Result.Success(MileageRetentionResponse.From(cmd.RetentionDays));
    }
}
