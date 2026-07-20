using System.Diagnostics.Metrics;

namespace TaxVision.Growth.Infrastructure.Observability;

/// <summary>
/// Low-cardinality metrics shared by the Codes and Referrals bounded contexts.
/// Tenant, code, payment and subject identifiers must never be added as tags.
/// </summary>
public sealed class GrowthMetrics : IDisposable
{
    public const string MeterName = "TaxVision.Growth";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _quotes;
    private readonly Counter<long> _reservations;
    private readonly Counter<long> _compensations;
    private readonly Counter<long> _attributions;
    private readonly Counter<long> _qualifications;
    private readonly Counter<long> _rewards;
    private readonly Counter<long> _inboxDuplicates;
    private readonly Histogram<double> _outboxLagSeconds;

    public GrowthMetrics()
    {
        _quotes = _meter.CreateCounter<long>("growth.codes.quotes_total");
        _reservations = _meter.CreateCounter<long>("growth.codes.reservations_total");
        _compensations = _meter.CreateCounter<long>("growth.codes.compensations_total");
        _attributions = _meter.CreateCounter<long>("growth.referrals.attributions_total");
        _qualifications = _meter.CreateCounter<long>("growth.referrals.qualifications_total");
        _rewards = _meter.CreateCounter<long>("growth.referrals.rewards_total");
        _inboxDuplicates = _meter.CreateCounter<long>("growth.integration.inbox_duplicates_total");
        _outboxLagSeconds = _meter.CreateHistogram<double>(
            "growth.integration.outbox_lag_seconds",
            unit: "s"
        );
    }

    public void RecordQuote(string outcome, string kind) =>
        _quotes.Add(1, Tag("outcome", outcome), Tag("kind", kind));

    public void RecordReservation(string outcome, string source) =>
        _reservations.Add(1, Tag("outcome", outcome), Tag("source", source));

    public void RecordCompensation(string type) => _compensations.Add(1, Tag("type", type));

    public void RecordAttribution(string programType, string outcome) =>
        _attributions.Add(1, Tag("program_type", programType), Tag("outcome", outcome));

    public void RecordQualification(string programType, string outcome) =>
        _qualifications.Add(1, Tag("program_type", programType), Tag("outcome", outcome));

    public void RecordReward(string type, string state) =>
        _rewards.Add(1, Tag("type", type), Tag("state", state));

    public void RecordInboxDuplicate(string consumer) => _inboxDuplicates.Add(1, Tag("consumer", consumer));

    public void RecordOutboxLag(double seconds, string destination) =>
        _outboxLagSeconds.Record(seconds, Tag("destination", destination));

    private static KeyValuePair<string, object?> Tag(string name, string value) => new(name, value);

    public void Dispose() => _meter.Dispose();
}
