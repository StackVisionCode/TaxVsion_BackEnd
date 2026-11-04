using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending;

/// <summary>
/// Nombres de "Email activity" que viajan al front (fila y filtro). La regla vive en el dominio
/// (<see cref="SentMessage.Activity"/>): acá solo se nombra y se lee.
/// </summary>
public static class SentMessageActivityStatus
{
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Deferred = "Deferred";
    public const string Delivered = "Delivered";
    public const string Bounced = "Bounced";
    public const string Complained = "Complained";
    public const string Failed = "Failed";
    public const string Suppressed = "Suppressed";

    private static readonly Dictionary<string, SentMessageActivity> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        [Sending] = SentMessageActivity.Sending,
        [Sent] = SentMessageActivity.Sent,
        [Deferred] = SentMessageActivity.Deferred,
        [Delivered] = SentMessageActivity.Delivered,
        [Bounced] = SentMessageActivity.Bounced,
        [Complained] = SentMessageActivity.Complained,
        [Failed] = SentMessageActivity.Failed,
        [Suppressed] = SentMessageActivity.Suppressed,
    };

    public static IReadOnlyCollection<string> All => ByName.Keys;

    public static string Of(SentMessage message) => NameOf(message.Activity);

    /// <summary>Explícito y no con Enum.TryParse: "3" o un nombre nuevo del enum no deben colarse.</summary>
    public static bool TryParse(string value, out SentMessageActivity activity) =>
        ByName.TryGetValue(value.Trim(), out activity);

    private static string NameOf(SentMessageActivity activity) =>
        activity switch
        {
            SentMessageActivity.Sending => Sending,
            SentMessageActivity.Sent => Sent,
            SentMessageActivity.Deferred => Deferred,
            SentMessageActivity.Delivered => Delivered,
            SentMessageActivity.Bounced => Bounced,
            SentMessageActivity.Complained => Complained,
            SentMessageActivity.Failed => Failed,
            _ => Suppressed,
        };
}
