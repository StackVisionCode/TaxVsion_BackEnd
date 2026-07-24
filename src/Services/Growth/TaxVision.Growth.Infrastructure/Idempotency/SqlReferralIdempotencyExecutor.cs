using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Growth.Infrastructure.Idempotency;

/// <summary>
/// Referrals adapter over the same tenant-scoped SQL executor used by Codes.
/// This guarantees identical insert-first, rollback and exact-response replay semantics
/// without introducing a dependency from Referrals.Application to Codes.Domain.
/// </summary>
public sealed class SqlReferralIdempotencyExecutor(SqlBusinessIdempotencyExecutor executor)
    : IReferralIdempotencyExecutor
{
    public Task<Result<TResponse>> ExecuteAsync<TResponse>(
        Guid tenantId,
        string operation,
        Guid scopeId,
        string idempotencyKey,
        string payloadFingerprint,
        Func<CancellationToken, Task<Result<TResponse>>> operationBody,
        CancellationToken ct = default
    )
    {
        var key = IdempotencyKey.Create(idempotencyKey);
        if (key.IsFailure)
        {
            return Task.FromResult(
                Result.Failure<TResponse>(
                    new Error(
                        "Referrals.Idempotency.InvalidKey",
                        "IdempotencyKey is required and must be 200 characters or fewer."
                    )
                )
            );
        }

        var fingerprint = PayloadFingerprint.Create(payloadFingerprint);
        if (fingerprint.IsFailure)
        {
            return Task.FromResult(
                Result.Failure<TResponse>(
                    new Error(
                        "Referrals.Idempotency.InvalidFingerprint",
                        "PayloadFingerprint must be a 64-character SHA-256 hex digest."
                    )
                )
            );
        }

        return executor.ExecuteAsync(tenantId, operation, scopeId, key.Value, fingerprint.Value, operationBody, ct);
    }
}
