using TaxVision.Auth.Domain.Offboarding;

namespace TaxVision.Auth.Application.Offboarding;

public interface IOffboardingRunRepository
{
    Task AddAsync(OffboardingRun run, CancellationToken ct);

    /// <summary>La corrida del usuario en el tenant dado (filtro explícito, no el del contexto).</summary>
    Task<OffboardingRun?> GetByUserAsync(Guid tenantId, Guid userId, CancellationToken ct);

    Task<OffboardingRun?> GetByIdAsync(Guid tenantId, Guid runId, CancellationToken ct);

    /// <summary>Corridas de todos los tenants iniciadas antes del corte, sin marcar y con pasos pendientes.</summary>
    Task<IReadOnlyList<OverdueOffboardingRun>> ListOverdueAsync(
        DateTime startedBeforeUtc,
        int take,
        CancellationToken ct
    );
}

public sealed record OverdueOffboardingRun(Guid TenantId, Guid RunId);

/// <summary>Exclusión entre réplicas para el barrido (try-lock: quien no lo obtiene salta el tick).</summary>
public interface IOffboardingSweepLock
{
    /// <summary>null = otra réplica lo tiene.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken ct);
}
