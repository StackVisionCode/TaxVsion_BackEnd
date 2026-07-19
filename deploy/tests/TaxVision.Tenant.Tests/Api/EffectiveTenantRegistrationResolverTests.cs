using System.Security.Claims;
using TaxVision.Tenant.Api.Common;

namespace TaxVision.Tenant.Tests.Api;

/// <summary>El claim del ticket firmado por Auth manda siempre que esté presente; el body nunca lo pisa.</summary>
public sealed class EffectiveTenantRegistrationResolverTests
{
    private static ClaimsPrincipal PrincipalWithTicket(string slug, string email) =>
        new(new ClaimsIdentity([new Claim("reg_slug", slug), new Claim("reg_email", email)], "Bearer"));

    private static ClaimsPrincipal PlatformAdminPrincipal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, "PlatformAdmin")], "Bearer"));

    [Fact]
    public void Ticket_claims_win_over_the_request_body()
    {
        var user = PrincipalWithTicket("oficina1", "admin@oficina1.com");
        var request = new CreateTenantRequest("Oficina 1", "otra-cosa", "otro@correo.com", "America/Santo_Domingo");

        var result = EffectiveTenantRegistrationResolver.Resolve(user, request);

        Assert.True(result.IsSuccess);
        Assert.Equal("oficina1", result.Value.Subdomain);
        Assert.Equal("admin@oficina1.com", result.Value.AdminEmail);
    }

    [Fact]
    public void PlatformAdmin_without_a_ticket_falls_back_to_the_request_body()
    {
        var user = PlatformAdminPrincipal();
        var request = new CreateTenantRequest("Oficina 1", "oficina1", "admin@oficina1.com", "America/Santo_Domingo");

        var result = EffectiveTenantRegistrationResolver.Resolve(user, request);

        Assert.True(result.IsSuccess);
        Assert.Equal("oficina1", result.Value.Subdomain);
        Assert.Equal("admin@oficina1.com", result.Value.AdminEmail);
    }

    [Fact]
    public void PlatformAdmin_without_a_ticket_and_missing_body_fields_fails()
    {
        var user = PlatformAdminPrincipal();
        var request = new CreateTenantRequest("Oficina 1", null, null, "America/Santo_Domingo");

        var result = EffectiveTenantRegistrationResolver.Resolve(user, request);

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.RegistrationRequestInvalid", result.Error.Code);
    }
}
