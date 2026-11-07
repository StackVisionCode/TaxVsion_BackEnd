namespace TaxVision.Correspondence.Infrastructure.Scheduling;

public sealed class MailboxOwnershipReconciliationOptions
{
    public const string SectionName = "Correspondence:MailboxOwnership";

    public bool Enabled { get; set; } = true;

    public int IntervalHours { get; set; } = 6;
}
