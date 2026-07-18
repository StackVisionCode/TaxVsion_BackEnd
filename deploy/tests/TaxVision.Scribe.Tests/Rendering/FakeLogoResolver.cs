using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;

namespace TaxVision.Scribe.Tests.Rendering;

internal sealed class FakeLogoResolver(LogoAsset asset) : ILogoResolver
{
    public Task<LogoAsset> ResolveAsync(LogoScope logoScope, Guid? tenantId, CancellationToken ct = default) =>
        Task.FromResult(asset);
}
