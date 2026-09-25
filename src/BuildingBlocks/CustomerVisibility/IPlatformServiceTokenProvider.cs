namespace BuildingBlocks.CustomerVisibility;

/// <summary>
/// Seam de token M2M para la reconciliación: cada servicio provee el token de la identidad de la
/// PlatformTenant (única que acepta <c>GET internal/customers/assignments/reconciliation</c>), con su
/// propio mecanismo M2M (Billing: IServiceTokenProvider "Platform"; Signature: IServiceTokenAcquirer).
/// </summary>
public interface IPlatformServiceTokenProvider
{
    Task<string?> GetPlatformTokenAsync(CancellationToken ct = default);
}
