using BuildingBlocks.Results;
using TaxVision.Referrals.Application.Abstractions;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal interface IFakeReferralTransactionalResource
{
    object CaptureState();

    void RestoreState(object snapshot);
}

internal sealed class FakeReferralIdempotencyExecutor(params IFakeReferralTransactionalResource[] resources)
    : IReferralIdempotencyExecutor
{
    private readonly Dictionary<(string Operation, Guid ScopeId, string Key), StoredResponse> _responses = [];

    internal int ClaimedCount { get; private set; }
    internal int ExecutedBodyCount { get; private set; }
    internal int StoredResponseCount => _responses.Count;
    internal Error? FailNextCommit { get; set; }

    public async Task<Result<TResponse>> ExecuteAsync<TResponse>(
        Guid tenantId,
        string operation,
        Guid scopeId,
        string idempotencyKey,
        string payloadFingerprint,
        Func<CancellationToken, Task<Result<TResponse>>> operationBody,
        CancellationToken ct = default
    )
    {
        _ = tenantId; // el fake ignora el tenant — el key ya es único por scopeId+operation+idempotencyKey
        var storageKey = (operation, scopeId, idempotencyKey);
        if (_responses.TryGetValue(storageKey, out var stored))
        {
            if (!string.Equals(stored.PayloadFingerprint, payloadFingerprint, StringComparison.Ordinal))
            {
                return Result.Failure<TResponse>(
                    new Error(
                        "Referrals.IdempotencyConflict",
                        "The idempotency key was already used with a different payload."
                    )
                );
            }

            return Result.Success((TResponse)stored.Response);
        }

        ClaimedCount++;
        var snapshots = resources.Select(resource => resource.CaptureState()).ToArray();

        try
        {
            ExecutedBodyCount++;
            var result = await operationBody(ct);
            if (result.IsFailure)
            {
                Restore(snapshots);
                return result;
            }

            if (FailNextCommit is { } commitFailure)
            {
                FailNextCommit = null;
                Restore(snapshots);
                return Result.Failure<TResponse>(commitFailure);
            }

            _responses[storageKey] = new StoredResponse(payloadFingerprint, result.Value!);
            return result;
        }
        catch
        {
            Restore(snapshots);
            throw;
        }
    }

    private void Restore(IReadOnlyList<object> snapshots)
    {
        for (var index = resources.Length - 1; index >= 0; index--)
            resources[index].RestoreState(snapshots[index]);
    }

    private sealed record StoredResponse(string PayloadFingerprint, object Response);
}
