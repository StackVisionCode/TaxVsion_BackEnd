using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.Recurring;

/// <summary>
/// Backoff configurable por plan — <see cref="Backoffs"/>[<c>schedule.RetryCount</c>] es la
/// espera antes del próximo intento; agotar la lista sin éxito marca el schedule
/// <c>Failed</c>. Cuando <c>TenantRecurringPayment.FailureCount</c> (contador acumulado de
/// schedules que agotaron sus reintentos) llega a <see cref="MaxFailures"/>, el plan se
/// auto-suspende.
/// </summary>
public sealed record RetryPolicy
{
    public int MaxFailures { get; }
    public IReadOnlyList<TimeSpan> Backoffs { get; }

    private RetryPolicy(int maxFailures, IReadOnlyList<TimeSpan> backoffs)
    {
        MaxFailures = maxFailures;
        Backoffs = backoffs;
    }

    public static Result<RetryPolicy> Create(int maxFailures, IReadOnlyList<TimeSpan> backoffs)
    {
        if (maxFailures <= 0)
            return Result.Failure<RetryPolicy>(
                new Error("RetryPolicy.InvalidMaxFailures", "MaxFailures must be greater than zero.")
            );

        if (backoffs.Count == 0 || backoffs.Any(backoff => backoff <= TimeSpan.Zero))
            return Result.Failure<RetryPolicy>(
                new Error("RetryPolicy.InvalidBackoffs", "Backoffs must be a non-empty list of positive durations.")
            );

        return Result.Success(new RetryPolicy(maxFailures, backoffs));
    }

    /// <summary>Default razonable: 1h → 6h → 24h (mismo backoff que
    /// <c>SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc</c> de PaymentApp), 3 fallos de
    /// schedule agotados suspenden el plan.</summary>
    public static RetryPolicy Default { get; } =
        Create(3, [TimeSpan.FromHours(1), TimeSpan.FromHours(6), TimeSpan.FromHours(24)]).Value;
}
