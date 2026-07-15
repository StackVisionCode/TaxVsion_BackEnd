namespace TaxVision.Auth.Application.Abstractions;

/// <summary>Motivo por el que un Host no resolvió a un tenant activo (Fase A3).</summary>
public enum TenantResolutionFailureReason
{
    /// <summary>La request no trae Host (o viene vacío).</summary>
    HostMissing,

    /// <summary>No existe ningún TenantDomain Active para ese Host.</summary>
    HostUnknown,

    /// <summary>El TenantDomain existe pero el tenant al que apunta está inactivo.</summary>
    TenantInactive,
}

/// <summary>Resultado de resolver un Host a un tenant candidato (Fase A3).</summary>
public sealed record HostResolutionResult
{
    public bool IsResolved { get; private init; }
    public Guid TenantId { get; private init; }
    public TenantResolutionFailureReason? FailureReason { get; private init; }

    public static HostResolutionResult Resolved(Guid tenantId) => new() { IsResolved = true, TenantId = tenantId };

    public static HostResolutionResult Unresolved(TenantResolutionFailureReason reason) =>
        new() { IsResolved = false, FailureReason = reason };
}

/// <summary>
/// Resuelve un Host HTTP (subdominio de oficina o dominio propio) al tenant candidato
/// (Fase A3). Nunca produce un "tenant por defecto" — un Host desconocido o inactivo
/// siempre resuelve a <see cref="HostResolutionResult.Unresolved"/>. El resultado es
/// solo un candidato: el TenantId autoritativo para requests autenticadas sigue
/// saliendo del claim del JWT.
/// </summary>
public interface ITenantResolver
{
    Task<HostResolutionResult> ResolveAsync(string? host, CancellationToken ct = default);
}
