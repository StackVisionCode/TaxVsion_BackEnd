using System.Security.Claims;
using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Tests.Api;

/// <summary>Null = ve todos los clientes; un id = solo los asignados a ese usuario.</summary>
internal sealed class FakeCustomerAccessResolver : ICustomerAccessResolver
{
    public Guid? RestrictTo { get; set; }

    public Task<Guid?> ResolveRestrictedUserIdAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
        Task.FromResult(RestrictTo);
}
