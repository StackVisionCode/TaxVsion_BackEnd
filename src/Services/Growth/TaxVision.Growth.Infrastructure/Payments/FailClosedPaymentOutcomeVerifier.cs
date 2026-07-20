using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Payments;

/// <summary>
/// Deliberately denies late commits until Payment exposes an authenticated, authoritative
/// verification contract. Configuration can describe the future endpoints but cannot turn
/// this placeholder into an implicit approval.
/// </summary>
public sealed class FailClosedPaymentOutcomeVerifier(
    IOptions<PaymentOutcomeVerifierOptions> options
) : IPaymentOutcomeVerifier
{
    public Task<Result> VerifySucceededAsync(
        Guid tenantId,
        PaymentReference payment,
        Guid sourceEventId,
        SnapshotHash expectedSnapshotHash,
        CancellationToken ct = default
    )
    {
        var code = options.Value.Enabled
            ? "Codes.PaymentOutcomeVerifier.NotImplemented"
            : "Codes.PaymentOutcomeVerifier.Disabled";
        var message = options.Value.Enabled
            ? "Authoritative payment verification is not implemented; late commit was denied."
            : "Authoritative payment verification is disabled; late commit was denied.";

        return Task.FromResult(Result.Failure(new Error(code, message)));
    }
}
