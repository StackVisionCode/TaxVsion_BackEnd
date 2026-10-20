namespace TaxVision.Notification.Infrastructure.Retention;

public sealed class NotificationRetentionOptions
{
    public const string SectionName = "Notification:Retention";

    public bool Enabled { get; init; } = true;
    public int RetentionMonths { get; init; } = 12;
    public int BatchSize { get; init; } = 500;
    public int IntervalHours { get; init; } = 24;
    public int LockMinutes { get; init; } = 30;
}
