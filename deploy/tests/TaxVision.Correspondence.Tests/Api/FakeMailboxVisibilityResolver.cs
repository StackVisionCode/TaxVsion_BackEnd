using System.Security.Claims;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Tests.Api;

/// <summary>Fake: por defecto devuelve null (ve todo). Los tests que ejerciten el gate setean Visible.</summary>
internal sealed class FakeMailboxVisibilityResolver : IMailboxVisibilityResolver
{
    public IReadOnlyCollection<Guid>? Visible { get; set; }

    public Task<IReadOnlyCollection<Guid>?> ResolveVisibleAccountIdsAsync(
        ClaimsPrincipal user,
        Guid tenantId,
        CancellationToken ct = default
    ) => Task.FromResult(Visible);
}
