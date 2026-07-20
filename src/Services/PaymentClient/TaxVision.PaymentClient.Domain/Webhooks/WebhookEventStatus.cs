namespace TaxVision.PaymentClient.Domain.Webhooks;

public enum WebhookEventStatus
{
    Received = 1,
    Processing = 2,
    Applied = 3,
    Duplicate = 4,
    Rejected = 5,
    Failed = 6,
    Stale = 7,
}
