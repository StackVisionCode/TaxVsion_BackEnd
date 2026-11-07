namespace TaxVision.Auth.Application.TenantDomains;

/// <summary>Reservas de subdominio hechas fuera de este módulo: el comprador en pleno onboarding pago-primero.</summary>
public interface ISubdomainHoldReader
{
    Task<bool> IsHeldAsync(string slug, DateTime nowUtc, CancellationToken ct = default);
}
