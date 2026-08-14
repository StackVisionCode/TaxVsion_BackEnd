using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Tests.Domain;

public sealed class DueDateTests
{
    private static readonly DateTime AprilFifteenth = new(2026, 4, 15, 3, 59, 0, DateTimeKind.Utc);

    [Fact]
    public void A_due_date_keeps_both_the_instant_and_the_zone_it_was_written_in()
    {
        var result = DueDate.Create(AprilFifteenth, "America/New_York", isStatutory: true);

        Assert.True(result.IsSuccess);
        Assert.Equal(AprilFifteenth, result.Value.DueAtUtc);
        Assert.Equal("America/New_York", result.Value.TimeZoneId);
        Assert.True(result.Value.IsStatutory);
    }

    /// <summary>
    /// Un <c>DateTime</c> con <c>Kind.Local</c> o <c>Unspecified</c> significa cosas distintas según
    /// dónde corra el proceso: aceptarlo mueve el vencimiento al desplegar en otra región.
    /// </summary>
    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void A_due_instant_that_is_not_utc_is_rejected(DateTimeKind kind)
    {
        var result = DueDate.Create(DateTime.SpecifyKind(AprilFifteenth, kind), "America/New_York", false);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Due.NotUtc", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Eastern Standard Time")] // id de Windows, no IANA
    [InlineData("Marte/Olympus_Mons")]
    public void A_time_zone_that_is_not_a_valid_iana_id_is_rejected(string? timeZoneId)
    {
        var result = DueDate.Create(AprilFifteenth, timeZoneId, false);

        Assert.True(result.IsFailure);
        Assert.Equal("Task.Due.TimeZoneInvalid", result.Error.Code);
    }

    [Fact]
    public void The_time_zone_id_is_trimmed()
    {
        var result = DueDate.Create(AprilFifteenth, "  America/New_York  ", false);

        Assert.True(result.IsSuccess);
        Assert.Equal("America/New_York", result.Value.TimeZoneId);
    }
}
