using System.Diagnostics.Metrics;

namespace TaxVision.Connectors.MailboxListener.Observability;

/// <summary>Sin direcciones ni tenants en los tags (PII y cardinalidad).</summary>
public sealed class MailboxListenerMetrics : IDisposable
{
    public const string MeterName = "TaxVision.Connectors.MailboxListener";

    private readonly Meter _meter = new(MeterName);
    private int _sessionsActive;
    private int _sessionsIdle;
    private int _sessionsPolling;
    private int _sessionsBackoff;

    public MailboxListenerMetrics()
    {
        _meter.CreateObservableGauge("sessions_active", () => Volatile.Read(ref _sessionsActive));
        _meter.CreateObservableGauge("sessions_idle", () => Volatile.Read(ref _sessionsIdle));
        _meter.CreateObservableGauge("sessions_polling", () => Volatile.Read(ref _sessionsPolling));
        _meter.CreateObservableGauge("sessions_backoff", () => Volatile.Read(ref _sessionsBackoff));
        ChangesDetected = _meter.CreateCounter<long>("changes_detected_total");
        Reconnects = _meter.CreateCounter<long>("reconnects_total");
        AuthFailures = _meter.CreateCounter<long>("auth_failures_total");
    }

    public Counter<long> ChangesDetected { get; }
    public Counter<long> Reconnects { get; }
    public Counter<long> AuthFailures { get; }

    public void SessionStarted() => Interlocked.Increment(ref _sessionsActive);

    public void SessionStopped() => Interlocked.Decrement(ref _sessionsActive);

    public void Listening(bool idle) => Interlocked.Increment(ref idle ? ref _sessionsIdle : ref _sessionsPolling);

    public void StoppedListening(bool idle) =>
        Interlocked.Decrement(ref idle ? ref _sessionsIdle : ref _sessionsPolling);

    public void BackoffStarted() => Interlocked.Increment(ref _sessionsBackoff);

    public void BackoffEnded() => Interlocked.Decrement(ref _sessionsBackoff);

    public void Dispose() => _meter.Dispose();
}
