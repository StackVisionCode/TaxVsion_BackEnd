using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Abstractions;

public interface IPaymentOutcomeVerifier
{
    /// <summary>
    /// Authoritatively verifies a late payment success against the Payment owner.
    /// A transport event or caller-provided flag is not sufficient to authorize a late commit.
    /// </summary>
    Task<Result> VerifySucceededAsync(
        Guid tenantId,
        PaymentReference payment,
        Guid sourceEventId,
        SnapshotHash expectedSnapshotHash,
        CancellationToken ct = default
    );
}
