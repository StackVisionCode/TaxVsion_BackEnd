namespace TaxVision.PaymentClient.Domain.Payouts;

/// <summary>El diseño no enumera este VO explícitamente — se infiere de los dos eventos de
/// webhook que <c>PayoutScheduleItem</c> debe registrar (§23.3: <c>payout.paid</c> /
/// <c>payout.failed</c>).</summary>
public enum PayoutScheduleItemStatus
{
    Paid = 1,
    Failed = 2,
}
