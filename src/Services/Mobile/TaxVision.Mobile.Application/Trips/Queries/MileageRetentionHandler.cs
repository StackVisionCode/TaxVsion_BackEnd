using BuildingBlocks.Results;
using TaxVision.Mobile.Application.Trips.Abstractions;

namespace TaxVision.Mobile.Application.Trips.Queries;

public static class MileageRetentionHandler
{
    public static async Task<Result<MileageRetentionResponse>> Handle(
        MileageRetentionQuery q,
        ITripRepository trips,
        CancellationToken ct
    )
    {
        var policy = await trips.RetentionPolicyAsync(q.TenantId, ct);
        return Result.Success(MileageRetentionResponse.From(policy?.RetentionDays));
    }
}
