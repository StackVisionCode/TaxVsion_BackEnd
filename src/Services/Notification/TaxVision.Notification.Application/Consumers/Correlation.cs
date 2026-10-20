namespace TaxVision.Notification.Application.Consumers;

internal static class Correlation
{
    public static string From(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
