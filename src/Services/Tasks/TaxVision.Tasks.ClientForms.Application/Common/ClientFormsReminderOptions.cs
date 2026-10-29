using System.ComponentModel.DataAnnotations;

namespace TaxVision.Tasks.ClientForms.Application.Common;

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
