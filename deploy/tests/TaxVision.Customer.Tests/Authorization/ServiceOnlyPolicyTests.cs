using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using TaxVision.Customer.Api.Common;

namespace TaxVision.Customer.Tests.Authorization;

/// <summary>
/// Verifica la policy "ServiceOnly" registrada en Customer.Api Program.cs
/// (<c>RequireClaim("actor_type", "Service")</c>) que gatea
/// <see cref="TaxVision.Customer.Api.Controllers.InternalCustomersController"/> — el endpoint
/// M2M agregado para que Correspondence pueda listar clientes sin el claim "Roles" que un token
/// de servicio nunca lleva. No usamos WebApplicationFactory (sin precedente en el repo): se
/// construye la misma <see cref="ClaimsAuthorizationRequirement"/> que produce
/// <c>RequireClaim</c> en Program.cs y se evalúa su condición (<see cref="ClaimsAuthorizationRequirement.AllowedValues"/>
/// contra los claims reales de cada tipo de JWT validado) sin depender del pipeline completo de
/// ASP.NET Core.
/// </summary>
public sealed class ServiceOnlyPolicyTests
{
    private static readonly ClaimsAuthorizationRequirement ServiceOnlyRequirement = (ClaimsAuthorizationRequirement)
        new AuthorizationPolicyBuilder().RequireClaim("actor_type", "Service").Build().Requirements.Single();

    [Fact]
    public void ServiceToken_with_actor_type_service_is_authorized()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("actor_type", "Service"), new Claim("tenant_id", Guid.NewGuid().ToString())],
                "Bearer"
            )
        );

        Assert.True(IsSatisfiedBy(principal));
    }

    [Fact]
    public void NormalUserToken_with_TenantEmployee_role_is_rejected()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Role, "TenantEmployee"), new Claim("tenant_id", Guid.NewGuid().ToString())],
                "Bearer"
            )
        );

        Assert.False(IsSatisfiedBy(principal));
    }

    [Fact]
    public void NormalUserToken_with_TenantAdmin_role_is_still_rejected_without_actor_type()
    {
        // "ServiceOnly" es deliberadamente estricto: ni siquiera un TenantAdmin humano pasa —
        // solo actor_type=Service. Es lo que evita que este endpoint M2M termine reusándose
        // por error como un atajo para humanos.
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "TenantAdmin")], "Bearer"));

        Assert.False(IsSatisfiedBy(principal));
    }

    [Fact]
    public void TryGetTenantId_reads_the_tenant_id_claim_from_a_service_token()
    {
        var tenantId = Guid.NewGuid();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("actor_type", "Service"), new Claim("tenant_id", tenantId.ToString())],
                "Bearer"
            )
        );

        Assert.True(principal.TryGetTenantId(out var resolved));
        Assert.Equal(tenantId, resolved);
    }

    [Fact]
    public void TryGetTenantId_fails_when_the_token_has_no_tenant_id_claim()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("actor_type", "Service")], "Bearer"));

        Assert.False(principal.TryGetTenantId(out _));
    }

    private static bool IsSatisfiedBy(ClaimsPrincipal principal) =>
        principal
            .Claims.Where(c => c.Type == ServiceOnlyRequirement.ClaimType)
            .Any(c =>
                ServiceOnlyRequirement.AllowedValues is null || ServiceOnlyRequirement.AllowedValues.Contains(c.Value)
            );
}
