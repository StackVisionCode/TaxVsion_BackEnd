using BuildingBlocks.Messaging;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Commands;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

public sealed class AttendeeResponseConflictRetryTests
{
    [Fact]
    public async Task AConflict_IsRetriedWithANewInvocation()
    {
        var calls = 0;

        var result = await AttendeeResponseConflictRetry.InvokeAsync(
            _ => ++calls == 1 ? throw Conflict() : Task.FromResult("saved"),
            3,
            CancellationToken.None
        );

        Assert.Equal("saved", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task WhenAttemptsRunOut_TheConflictGoesOutAs409()
    {
        var calls = 0;

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            AttendeeResponseConflictRetry.InvokeAsync<string>(
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
    public async Task AnotherConflictCode_IsNotRetried()
    {
        var calls = 0;

        await Assert.ThrowsAsync<ConflictException>(() =>
            AttendeeResponseConflictRetry.InvokeAsync<string>(
                _ =>
                {
                    calls++;
                    throw new ConflictException("Calendar.Appointment.ScheduleBusy", "Busy.");
                },
                3,
                CancellationToken.None
            )
        );

        Assert.Equal(1, calls);
    }

    private static ConflictException Conflict() => new(WolverineConflictPolicy.ConcurrencyConflictCode, "Rival.");
}
