using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Abstractions;

public interface IPaymentOutcomeVerifier
{
    /// <summary>
    /// Authoritatively verifies a late payment success against the Payment owner.
    /// A transport event or caller-provided flag is not sufficient to authorize a late commit.
    /// </summary>
    Task<Result> VerifySucceededAsync(PaymentOutcomeQuery query, CancellationToken ct = default);
}

/// <summary>Lo que hace falta para encontrar el pago de una reserva: la referencia de pago y, para el onboarding, el
/// sujeto anónimo que lleva el OnboardingId.</summary>
public sealed record PaymentOutcomeQuery(
    Guid TenantId,
    Guid ReservationId,
    PaymentReference Payment,
    SubjectReference Subject,
    Guid SourceEventId,
    SnapshotHash ExpectedSnapshotHash
);
