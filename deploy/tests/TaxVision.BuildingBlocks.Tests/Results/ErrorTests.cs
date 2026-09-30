using System.Text.Json;
using BuildingBlocks.Results;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Results;

public sealed class ErrorTests
{
    [Fact]
    public void WithRetryAfter_rounds_up_to_whole_seconds_and_never_below_one()
    {
        var error = new Error("Test.Throttled", "Wait.");

        Assert.Equal(43, error.WithRetryAfter(TimeSpan.FromSeconds(42.1)).RetryAfterSeconds);
        Assert.Equal(1, error.WithRetryAfter(TimeSpan.Zero).RetryAfterSeconds);
    }

    [Fact]
    public void WithRetryAfter_keeps_the_code_and_message()
    {
        var error = new Error("Test.Throttled", "Wait.").WithRetryAfter(TimeSpan.FromSeconds(5));

        Assert.Equal("Test.Throttled", error.Code);
        Assert.Equal("Wait.", error.Message);
    }

    [Fact]
    public void Serialized_body_includes_the_wait_only_when_there_is_one()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var plain = JsonSerializer.Serialize(new Error("Test.Failed", "Nope."), options);
        var throttled = JsonSerializer.Serialize(
            new Error("Test.Throttled", "Wait.").WithRetryAfter(TimeSpan.FromSeconds(30)),
            options
        );

        Assert.DoesNotContain("retryAfterSeconds", plain);
        Assert.Contains("\"retryAfterSeconds\":30", throttled);
    }
}
