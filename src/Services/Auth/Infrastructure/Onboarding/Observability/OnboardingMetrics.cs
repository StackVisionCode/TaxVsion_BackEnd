using System.Diagnostics.Metrics;
using TaxVision.Auth.Application.Onboarding.Abstractions;

namespace TaxVision.Auth.Infrastructure.Onboarding.Observability;

/// <summary>PayFlow (Fase 17) — Meter singleton de las métricas de onboarding, consumido por
/// DI en los handlers/consumers/scheduler y recolectado por el <c>MeterProvider</c> vía
/// <c>AddTaxVisionOpenTelemetry(..., OnboardingMetrics.MeterName)</c> en Program.cs.</summary>
public sealed class OnboardingMetrics : IOnboardingMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Auth.Onboarding";

    private readonly Meter _meter;
    private readonly Counter<long> _startedTotal;
    private readonly Counter<long> _completedTotal;
    private readonly Counter<long> _failedTotal;
    private readonly Counter<long> _manualReviewTotal;
    private readonly Histogram<double> _durationSeconds;

    public OnboardingMetrics()
    {
        _meter = new Meter(MeterName);

        _startedTotal = _meter.CreateCounter<long>("onboarding.started_total");
        _completedTotal = _meter.CreateCounter<long>("onboarding.completed_total");
        _failedTotal = _meter.CreateCounter<long>("onboarding.failed_total");
        _manualReviewTotal = _meter.CreateCounter<long>("onboarding.manual_review_total");
        _durationSeconds = _meter.CreateHistogram<double>("onboarding.duration_seconds", unit: "s");
    }

    public void RecordStarted() => _startedTotal.Add(1);

    public void RecordCompleted() => _completedTotal.Add(1);

    public void RecordFailed(string step) => _failedTotal.Add(1, new KeyValuePair<string, object?>("step", step));

    public void RecordManualReview() => _manualReviewTotal.Add(1);

    public void RecordDurationSeconds(double seconds, string outcome) =>
        _durationSeconds.Record(seconds, new KeyValuePair<string, object?>("outcome", outcome));

    public void Dispose() => _meter.Dispose();
}
