using Xunit;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Lee el marcador de una fecha tipada que Notification manda a Scribe.</summary>
internal static class TypedDates
{
    public static string? Instant(object? value) =>
        Assert.IsAssignableFrom<IDictionary<string, object?>>(value)["$instant"] as string;
}
