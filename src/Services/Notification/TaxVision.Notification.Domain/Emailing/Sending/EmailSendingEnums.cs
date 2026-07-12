namespace TaxVision.Notification.Domain.Emailing.Sending;

public enum EmailStatus
{
    Queued,
    Sending,
    Sent,
    Delivered,
    Failed,
    Bounced,
    Cancelled,
}

public enum EmailPriority
{
    Low,
    Normal,
    High,
}

public enum EmailRecipientKind
{
    To,
    Cc,
    Bcc,
}
