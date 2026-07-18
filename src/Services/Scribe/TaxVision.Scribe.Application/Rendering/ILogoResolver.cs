using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Application.Rendering;

public interface ILogoResolver
{
    Task<LogoAsset> ResolveAsync(LogoScope logoScope, Guid? tenantId, CancellationToken ct = default);
}
