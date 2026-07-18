using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Postmaster.Domain.Providers;

public enum ProviderKind
{
    System,
    Tenant,
}

public enum ProviderHealth
{
    Healthy,
    Degraded,
    Unhealthy,
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen,
}

/// <summary>
/// Circuit breaker de salud por provider (uno por <c>(ProviderKind, TenantId?, ProviderCode)</c>).
/// <see cref="Domain.Providers.ProviderResolver"/> (Fase 3.5) usa <see cref="CircuitBreakerState"/>
/// para decidir <c>ProviderUnhealthy</c> sin reintentar un provider que viene fallando.
/// </summary>
public sealed class ProviderHealthStatus : BaseEntity
{
    private const int ConsecutiveFailuresToOpen = 3;

    private ProviderHealthStatus() { }

    public ProviderKind ProviderKind { get; private set; }
    public Guid? TenantId { get; private set; }
    public string ProviderCode { get; private set; } = default!;
    public int ConsecutiveFailures { get; private set; }
    public int ConsecutiveSuccesses { get; private set; }
    public ProviderHealth Status { get; private set; }
    public CircuitBreakerState CircuitBreakerState { get; private set; }
    public DateTime? CircuitBreakerOpenedAtUtc { get; private set; }
    public DateTime LastCheckAtUtc { get; private set; }
    public DateTime? LastSuccessAtUtc { get; private set; }
    public DateTime? LastFailureAtUtc { get; private set; }

    public static Result<ProviderHealthStatus> Create(
        ProviderKind providerKind,
        Guid? tenantId,
        string providerCode,
        DateTime nowUtc
    )
    {
        if (providerKind == ProviderKind.Tenant && tenantId is null)
            return Result.Failure<ProviderHealthStatus>(
                new Error("ProviderHealthStatus.Tenant", "TenantId is required when ProviderKind is Tenant.")
            );

        if (providerKind == ProviderKind.System && tenantId is not null)
            return Result.Failure<ProviderHealthStatus>(
                new Error("ProviderHealthStatus.Tenant", "TenantId must be null when ProviderKind is System.")
            );

        if (string.IsNullOrWhiteSpace(providerCode))
            return Result.Failure<ProviderHealthStatus>(
                new Error("ProviderHealthStatus.ProviderCode", "ProviderCode is required.")
            );

        return Result.Success(
            new ProviderHealthStatus
            {
                Id = Guid.NewGuid(),
                ProviderKind = providerKind,
                TenantId = tenantId,
                ProviderCode = providerCode,
                Status = ProviderHealth.Healthy,
                CircuitBreakerState = CircuitBreakerState.Closed,
                LastCheckAtUtc = nowUtc,
            }
        );
    }

    /// <summary>Registra un envío exitoso. Si el breaker estaba HalfOpen, lo cierra de inmediato.</summary>
    public void RecordSuccess(DateTime nowUtc)
    {
        ConsecutiveSuccesses++;
        ConsecutiveFailures = 0;
        LastSuccessAtUtc = nowUtc;
        LastCheckAtUtc = nowUtc;

        if (CircuitBreakerState == CircuitBreakerState.HalfOpen)
            CloseCircuitBreaker();
        else
            Status = ProviderHealth.Healthy;
    }

    /// <summary>Registra un fallo de envío. Abre el breaker tras <see cref="ConsecutiveFailuresToOpen"/> fallos seguidos.</summary>
    public void RecordFailure(DateTime nowUtc)
    {
        ConsecutiveFailures++;
        ConsecutiveSuccesses = 0;
        LastFailureAtUtc = nowUtc;
        LastCheckAtUtc = nowUtc;

        if (ConsecutiveFailures >= ConsecutiveFailuresToOpen)
            OpenCircuitBreaker(nowUtc);
        else
            Status = ProviderHealth.Degraded;
    }

    /// <summary>Permite un intento de prueba tras el cool-down, sin cerrar el breaker todavía.</summary>
    public void TransitionToHalfOpen(DateTime nowUtc)
    {
        CircuitBreakerState = CircuitBreakerState.HalfOpen;
        Status = ProviderHealth.Degraded;
        LastCheckAtUtc = nowUtc;
    }

    private void OpenCircuitBreaker(DateTime nowUtc)
    {
        CircuitBreakerState = CircuitBreakerState.Open;
        Status = ProviderHealth.Unhealthy;
        CircuitBreakerOpenedAtUtc = nowUtc;
    }

    private void CloseCircuitBreaker()
    {
        CircuitBreakerState = CircuitBreakerState.Closed;
        Status = ProviderHealth.Healthy;
        CircuitBreakerOpenedAtUtc = null;
    }
}
