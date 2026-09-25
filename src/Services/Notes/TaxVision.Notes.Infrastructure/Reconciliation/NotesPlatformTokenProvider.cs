using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Tenancy;

namespace TaxVision.Notes.Infrastructure.Reconciliation;

// Seam de token del kit de visibilidad para Notes: usa el acquirer M2M compartido (el mismo del rate-limit /
// directorio de clientes, apunta a Auth) con la identidad de la PlatformTenant (única que acepta el endpoint
// de reconciliación de asignaciones de Customer).
internal sealed class NotesPlatformTokenProvider(IServiceTokenAcquirer tokenAcquirer) : IPlatformServiceTokenProvider
{
    public Task<string?> GetPlatformTokenAsync(CancellationToken ct = default) =>
        tokenAcquirer.GetTokenAsync(PlatformTenant.Id, ct);
}
