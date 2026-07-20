using BuildingBlocks.Results;

namespace TaxVision.Referrals.Application.Abstractions;

/// <summary>
/// Executes one referral business operation behind an insert-first idempotency claim.
/// Implementations must persist the claim, every mutation performed by
/// <paramref name="operationBody"/>, and the serialized successful response in one
/// atomic transaction. A successful replay must return the exact stored response;
/// a different fingerprint must fail. A failed result or exception must roll back
/// the claim, tracked aggregate changes, repository writes, and quota reservations.
/// </summary>
public interface IReferralIdempotencyExecutor
{
    Task<Result<TResponse>> ExecuteAsync<TResponse>(
        string operation,
        Guid scopeId,
        string idempotencyKey,
        string payloadFingerprint,
        Func<CancellationToken, Task<Result<TResponse>>> operationBody,
        CancellationToken ct = default
    );
}
