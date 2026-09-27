using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Authorization;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Signature.Domain.Permissions;
using TaxVision.Signature.Domain.RateLimiting;
using TaxVision.Signature.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// A6 — el gate de módulo **aplicando**, contra el host real de Signature.Api con SQL/Redis/RabbitMQ
/// locales. El plan lo pedía explícitamente ("tests con Enforce=true; hoy no existe ninguna") y es la
/// contraparte de <see cref="ModuleGateLogOnlyIntegrationTests"/>: mismo escenario, mismo seeding, y la
/// única diferencia es el escalón — así queda demostrado que el flag es lo que cambia la respuesta y no
/// otra cosa del entorno.
///
/// Cubre además el contrato del 403 que consumen los frontends (B7/C5): `code`, `reason` y `module`, que
/// es lo que les permite distinguir "no tienes permiso" de "tu plan no lo incluye" sin mantener su
/// propia copia del mapa permiso → módulo.
/// </summary>
public sealed class ModuleGateEnforceIntegrationTests : IClassFixture<SignatureApiEnforcingModuleGateFactory>
{
    private readonly SignatureApiEnforcingModuleGateFactory factory;

    public ModuleGateEnforceIntegrationTests(SignatureApiEnforcingModuleGateFactory factory) => this.factory = factory;

    [Fact]
    public async Task Tenant_without_the_module_gets_403_with_the_module_in_the_problem_details()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Permiso concedido (la capa 2 pasa); el plan del tenant NO incluye "signatures".
        SeedPermission(tenantId, userId);
        SeedPlanModules(tenantId, planCode: "free", modules: []);

        var response = await Get(tenantId, userId, "/signature/settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Authz.ModuleUnavailable", problem.GetProperty("code").GetString());
        // La constante, no el literal: el contrato lo define el backend y el frontend lo lee de ahí.
        Assert.Equal(AuthorizationDenialReasons.Module, problem.GetProperty("reason").GetString());
        Assert.Equal("signatures", problem.GetProperty("module").GetString());
    }

    [Fact]
    public async Task Tenant_with_the_module_is_not_blocked_by_the_same_gate()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        SeedPermission(tenantId, userId);
        SeedPlanModules(tenantId, planCode: "pro", modules: ["signatures"]);

        var response = await Get(tenantId, userId, "/signature/settings");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_without_projection_is_not_blocked_even_while_enforcing()
    {
        // Consistencia eventual: sin fila de proyección no se sabe qué tiene el tenant, y denegar acá
        // convertiría un evento que todavía no llegó en 403 para alguien que sí paga. Es el caso que
        // hace seguro encender el escalón: un tenant nuevo no se corta mientras su snapshot viaja.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        SeedPermission(tenantId, userId);
        // Deliberadamente SIN SeedPlanModules.

        var response = await Get(tenantId, userId, "/signature/settings");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private void SeedPermission(Guid tenantId, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        SetTenant(scope, tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
        db.AuthzUserPermissionsProjections.Add(
            AuthzUserPermissionsProjection.Create(tenantId, userId, 1, [SignaturePermissions.SettingsManage], [])
        );
        db.SaveChanges();
    }

    private void SeedPlanModules(Guid tenantId, string planCode, IReadOnlyList<string> modules)
    {
        using var scope = factory.Services.CreateScope();
        SetTenant(scope, tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
        var projection = TenantPlanCodeProjection.Create(tenantId, planCode, 1);
        projection.ApplyIfNewer(planCode, 1, modules);
        db.TenantPlanCodeProjections.Add(projection);
        db.SaveChanges();
    }

    private static void SetTenant(IServiceScope scope, Guid tenantId)
    {
        if (scope.ServiceProvider.GetService<ITenantContext>() is { } tenantContext)
            tenantContext.SetTenant(tenantId);
    }

    private async Task<HttpResponseMessage> Get(Guid tenantId, Guid userId, string path)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            MintEmployeeToken(tenantId, userId)
        );
        return await client.GetAsync(path);
    }

    /// <summary>TenantEmployee a propósito: un PlatformAdmin bypasea el gate y no probaría nada.</summary>
    private string MintEmployeeToken(Guid tenantId, Guid userId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("actor_type", "TenantEmployee"),
            new Claim("perm_v", "1"),
        };

        var token = new JwtSecurityToken(
            issuer: factory.JwtIssuer,
            audience: factory.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
