using System.ComponentModel.DataAnnotations;
using TaxVision.DataCollection.Engine;

namespace TaxVision.Tasks.ClientForms.Application.Common;

public sealed class ClientFormsOptions
{
    public const string SectionName = "ClientForms";

    public bool Enabled { get; init; }

    [Required]
    public DataCollectionLimits Limits { get; init; } = new();

    [Required]
    public ClientFormsAttachmentOptions Attachments { get; init; } = new();

    [Required]
    public ClientFormsReminderOptions Reminders { get; init; } = new();

    [Required]
    public ClientFormsExternalOptions External { get; init; } = new();
}

public sealed class ClientFormsExternalOptions
{
    [Range(1, 100_000)]
    public int MaxIntakeSubmissionsPerTenantPerDay { get; init; } = 200;

    [Range(1, 100_000)]
    public int MaxOtpIssuesPerTenantPerHour { get; init; } = 300;
}

public sealed class ClientFormsAttachmentOptions
{
    [Range(1, 1440)]
    public int PendingExpiryMinutes { get; init; } = 60;

    [Range(1, 1440)]
    public int ReconciliationIntervalMinutes { get; init; } = 5;

    [Range(1, 1440)]
    public int ReconcileAfterMinutes { get; init; } = 10;
}

public sealed class ClientFormsReminderOptions
{
    public bool Enabled { get; init; } = true;
    public int[] DaysBeforeDue { get; init; } = [3, 1];

    [Range(1, 365)]
    public int OverdueEveryDays { get; init; } = 3;

    [Range(1, 100)]
    public int MaxReminders { get; init; } = 5;

    [Range(1, 1440)]
    public int JobIntervalMinutes { get; init; } = 60;
}
