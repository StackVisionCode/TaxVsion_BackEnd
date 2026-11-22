using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Anota el token de idempotencia de cada SMS que sale.</summary>
internal sealed class CapturingSms : ISmsSender
{
    public List<string?> Tokens { get; } = [];

    public Task<Result> SendAsync(
        string phoneNumber,
        string text,
        CancellationToken ct,
        string? templateKey,
        string? idempotencyToken,
        string? sourceRef = null
    )
    {
        Tokens.Add(idempotencyToken);
        return Task.FromResult(Result.Success());
    }
}
