namespace TaxVision.Tasks.Infrastructure.Jobs;

public sealed class OverdueTaskSweepOptions
{
    public const string SectionName = "Tasks:OverdueSweep";

    public int IntervalMinutes { get; set; } = 60;

    public int BatchSize { get; set; } = 200;
}
