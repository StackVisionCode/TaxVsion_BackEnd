using BuildingBlocks.Results;
using TaxVision.Signature.Application.Requests.Public;

namespace TaxVision.Signature.Tests.Requests.Public;

/// <summary>SIG-1: solo un conflicto de versión se reintenta, y solo hasta el tope configurado.</summary>
public sealed class PublicSigningConflictRetryTests
{
    private const string ConflictCode = "Persistence.ConcurrencyConflict";
    private const int MaxAttempts = 3;

    [Fact]
    public async Task ConflictOnce_RunsAgainAndReturnsTheResult()
    {
        var calls = 0;

        var result = await PublicSigningConflictRetry.InvokeAsync(
            _ => ++calls == 1 ? throw new ConflictException(ConflictCode, "Conflict.") : Task.FromResult("done"),
            MaxAttempts,
            CancellationToken.None
        );

        Assert.Equal("done", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ConflictOnEveryAttempt_StopsAtMaxAttemptsAndRethrows()
    {
        var calls = 0;

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            PublicSigningConflictRetry.InvokeAsync<string>(
                _ =>
                {
                    calls++;
                    throw new ConflictException(ConflictCode, "Conflict.");
                },
                MaxAttempts,
                CancellationToken.None
            )
        );

        Assert.Equal(ConflictCode, conflict.Code);
        Assert.Equal(MaxAttempts, calls);
    }

    [Fact]
    public async Task OtherConflict_IsNotRetried()
    {
        var calls = 0;

        await Assert.ThrowsAsync<ConflictException>(() =>
            PublicSigningConflictRetry.InvokeAsync<string>(
                _ =>
                {
                    calls++;
                    throw new ConflictException("Persistence.UniqueConstraint", "Duplicate.");
                },
                MaxAttempts,
                CancellationToken.None
            )
        );

        Assert.Equal(1, calls);
    }
}
