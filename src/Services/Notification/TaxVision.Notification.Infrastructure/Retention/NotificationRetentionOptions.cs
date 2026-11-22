namespace TaxVision.Notification.Infrastructure.Retention;

public sealed class NotificationRetentionOptions
{
    public const string SectionName = "Notification:Retention";

    public bool Enabled { get; init; } = true;
    public int RetentionMonths { get; init; } = 12;
    public int BatchSize { get; init; } = 500;
    public int IntervalHours { get; init; } = 24;
    public int LockMinutes { get; init; } = 30;

    // Tokens de push: revocados se borran a los 90 días; activos sin alta en 30 días se apagan (D-N37).
    public int PushRevokedRetentionDays { get; init; } = 90;
    public int PushIdleDays { get; init; } = 30;
}
