using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class FakeBusinessIdempotencyExecutor : IBusinessIdempotencyExecutor
{
    private readonly Dictionary<(string Operation, Guid ScopeId, string Key), StoredResponse> _responses = [];

    internal int ExecutedBodyCount { get; private set; }

    public async Task<Result<TResponse>> ExecuteAsync<TResponse>(
        string operation,
        Guid scopeId,
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        Func<CancellationToken, Task<Result<TResponse>>> operationBody,
        CancellationToken ct = default
    )
    {
        var storageKey = (operation, scopeId, idempotencyKey.Value);
        if (_responses.TryGetValue(storageKey, out var stored))
        {
            if (!string.Equals(stored.Fingerprint, payloadFingerprint.Value, StringComparison.Ordinal))
                return Result.Failure<TResponse>(
                    new Error(
                        "Codes.Idempotency.Conflict",
                        "The idempotency key was already used with a different payload."
                    )
                );

            return Result.Success((TResponse)stored.Response);
        }

        ExecutedBodyCount++;
        var result = await operationBody(ct);
        if (result.IsSuccess)
            _responses[storageKey] = new StoredResponse(payloadFingerprint.Value, result.Value!);

        return result;
    }

    private sealed record StoredResponse(string Fingerprint, object Response);
}
