using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Application.Rendering;

public interface ILogoResolver
{
    /// <summary><paramref name="audience"/> elige el logo de la oficina: Staff = CRM, Client = Portal → CRM.</summary>
    Task<LogoAsset> ResolveAsync(
        LogoScope logoScope,
        Guid? tenantId,
        RenderAudience audience = RenderAudience.Staff,
        CancellationToken ct = default
    );
}
