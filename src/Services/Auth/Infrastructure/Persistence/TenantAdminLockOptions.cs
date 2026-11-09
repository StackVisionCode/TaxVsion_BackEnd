namespace TaxVision.Auth.Infrastructure.Persistence;

/// <summary>Espera máxima del candado de la regla del último admin.</summary>
public sealed class TenantAdminLockOptions
{
    public const string SectionName = "Auth:TenantAdminLock";

    public int TimeoutMilliseconds { get; set; } = 10_000;
}
