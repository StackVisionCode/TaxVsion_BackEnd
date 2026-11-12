using TaxVision.Subscription.Application.Catalog.Revisions;
using TaxVision.Subscription.Infrastructure.Catalog;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>
/// Para los hosts de prueba que arrancan el <c>Program</c> real sin base ni Redis de verdad, o contra la base local:
/// las revisiones del catálogo no se aplican desde ahí. El runner real tiene sus propias pruebas de integración.
/// </summary>
public sealed class SkipCatalogRevisions : ICatalogRevisionRunner
{
    public Task RunAsync(IReadOnlyList<ICatalogRevision> revisions, CancellationToken ct) => Task.CompletedTask;
}
