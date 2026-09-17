using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Captura en memoria las mediciones del ciclo de vida de add-ons para aserciones.</summary>
public sealed class FakeSubscriptionMetrics : ISubscriptionMetrics
{
    public List<string> Purchased { get; } = [];
    public List<string> Absorbed { get; } = [];
    public List<string> Expired { get; } = [];
    public List<(string Code, long AmountCents)> Billed { get; } = [];
    public List<(string SeatType, int Quantity)> SeatsPurchased { get; } = [];
    public List<(string SeatType, long AmountCents)> SeatsBilled { get; } = [];
    public List<(string From, string To, string Reason)> StatusTransitions { get; } = [];
    public List<string> SelfServiceRenewals { get; } = [];

    public void RecordAddOnPurchased(string addOnCode) => Purchased.Add(addOnCode);

    public void RecordAddOnAbsorbed(string addOnCode) => Absorbed.Add(addOnCode);

    public void RecordAddOnExpired(string addOnCode) => Expired.Add(addOnCode);

    public void RecordAddOnBilled(string addOnCode, long amountCents) => Billed.Add((addOnCode, amountCents));

    public void RecordSeatsPurchased(string seatType, int quantity) => SeatsPurchased.Add((seatType, quantity));

    public void RecordSeatsBilled(string seatType, long amountCents) => SeatsBilled.Add((seatType, amountCents));

    public void RecordStatusTransition(string from, string to, string reason) =>
        StatusTransitions.Add((from, to, reason));

    public void RecordSelfServiceRenewal(string outcome) => SelfServiceRenewals.Add(outcome);
}
