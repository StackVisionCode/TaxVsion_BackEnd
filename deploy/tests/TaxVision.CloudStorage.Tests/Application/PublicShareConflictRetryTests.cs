using BuildingBlocks.Messaging;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Sharing;

namespace TaxVision.CloudStorage.Tests.Application;

public sealed class PublicShareConflictRetryTests
{
    [Fact]
    public async Task A_conflict_is_retried_with_a_new_invocation()
    {
        var calls = 0;

        var result = await PublicShareConflictRetry.InvokeAsync(
            _ => ++calls == 1 ? throw Conflict() : Task.FromResult("entered"),
            3,
            CancellationToken.None
        );

        Assert.Equal("entered", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task When_attempts_run_out_the_conflict_goes_out_as_409()
    {
        var calls = 0;

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            PublicShareConflictRetry.InvokeAsync<string>(
                _ =>
                {
                    calls++;
                    throw Conflict();
                },
                3,
                CancellationToken.None
            )
        );

        Assert.Equal(WolverineConflictPolicy.ConcurrencyConflictCode, conflict.Code);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Another_conflict_code_is_not_retried()
    {
        var calls = 0;

        await Assert.ThrowsAsync<ConflictException>(() =>
            PublicShareConflictRetry.InvokeAsync<string>(
                _ =>
                {
                    calls++;
                    throw new ConflictException("Persistence.UniqueConstraint", "Duplicate.");
                },
                3,
                CancellationToken.None
            )
        );

        Assert.Equal(1, calls);
    }

    private static ConflictException Conflict() => new(WolverineConflictPolicy.ConcurrencyConflictCode, "Rival.");
}
