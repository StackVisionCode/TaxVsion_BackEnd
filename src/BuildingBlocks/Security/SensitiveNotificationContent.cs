namespace BuildingBlocks.Security;

public static class SensitiveNotificationContent
{
    public const string Redacted = "[redacted]";

    private static readonly HashSet<string> SensitiveTemplateKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "auth.otp_code",
        "onboarding.otp_code",
        "sig.verification-challenge.v1",
    };

    public static bool IsSensitive(string? templateKey) =>
        !string.IsNullOrWhiteSpace(templateKey) && SensitiveTemplateKeys.Contains(templateKey);

    public static string ForStorage(string? templateKey, string value) => IsSensitive(templateKey) ? Redacted : value;
}
