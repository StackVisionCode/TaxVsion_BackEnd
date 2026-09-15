using System.Diagnostics.Metrics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
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
/// Prueba end-to-end REAL del gate de módulo en modo Fase 1 (log-only) contra Signature.Api con
/// SQL/Redis/RabbitMQ locales — lo que los unit tests no cubren: el hook en <c>PermissionPolicyProvider</c>,
/// la resolución DI de <c>ITenantModuleEntitlementsSource</c> por request, y la lectura EF real de la
/// proyección. Un actor <b>TenantEmployee</b> (no PlatformAdmin, para no bypassear el gate) con el
/// permiso <c>signature.settings.manage</c> sembrado en su proyección:
/// <list type="bullet">
///   <item>si su tenant NO tiene el módulo <c>signatures</c> → el gate registra un <b>deny</b> en la
///   métrica <c>authz.module_decision</c> pero <b>NO</b> bloquea (la respuesta nunca es 403);</item>
///   <item>si SÍ lo tiene → registra <b>allow</b>.</item>
/// </list>
/// La respuesta HTTP es idéntica en ambos casos (ese es el punto de log-only): la diferencia se observa
/// solo en la métrica, capturada con un <see cref="MeterListener"/>.
/// </summary>
public sealed class ModuleGateLogOnlyIntegrationTests : IClassFixture<SignatureApiFactory>
{
    private readonly SignatureApiFactory factory;

    public ModuleGateLogOnlyIntegrationTests(SignatureApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Tenant_without_module_logs_deny_but_does_not_block()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Permiso concedido (pasa Layer 2), pero el plan del tenant NO incluye "signatures".
        SeedPermission(tenantId, userId, permissionsVersion: 1);
        SeedPlanModules(tenantId, planCode: "free", revision: 1, modules: []);

        using var captured = new ModuleDecisionCapture();
        var before = captured.Count(module: "signatures", result: "deny");

        var response = await Get(tenantId, userId, permV: 1, "/signature/settings");

        // Log-only: NUNCA 403 (ni 401) — el permiso pasó y el gate no bloquea.
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        // Pero el gate SÍ evaluó y registró un deny para el módulo faltante.
        Assert.True(
            captured.Count(module: "signatures", result: "deny") > before,
            "Expected a 'deny' authz.module_decision for module 'signatures' to be recorded."
        );
    }

    [Fact]
    public async Task Tenant_with_module_logs_allow()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        SeedPermission(tenantId, userId, permissionsVersion: 1);
        SeedPlanModules(tenantId, planCode: "pro", revision: 1, modules: ["signatures"]);

        using var captured = new ModuleDecisionCapture();
        var before = captured.Count(module: "signatures", result: "allow");

        var response = await Get(tenantId, userId, permV: 1, "/signature/settings");

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(
            captured.Count(module: "signatures", result: "allow") > before,
            "Expected an 'allow' authz.module_decision for module 'signatures' to be recorded."
        );
    }

    // ------------------------------------------------------------------
    // Seeding (proyecciones locales que el host lee en el hot path)
    // ------------------------------------------------------------------

    private void SeedPermission(Guid tenantId, Guid userId, int permissionsVersion)
    {
        using var scope = factory.Services.CreateScope();
        SetTenant(scope, tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
        db.AuthzUserPermissionsProjections.Add(
            AuthzUserPermissionsProjection.Create(
                tenantId,
                userId,
                permissionsVersion,
                [SignaturePermissions.SettingsManage],
                []
            )
        );
        db.SaveChanges();
    }

    private void SeedPlanModules(Guid tenantId, string planCode, long revision, IReadOnlyList<string> modules)
    {
        using var scope = factory.Services.CreateScope();
        SetTenant(scope, tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
        var projection = TenantPlanCodeProjection.Create(tenantId, planCode, revision);
        projection.ApplyIfNewer(planCode, revision, modules);
        db.TenantPlanCodeProjections.Add(projection);
        db.SaveChanges();
    }

    private static void SetTenant(IServiceScope scope, Guid tenantId)
    {
        // El DbContext tiene query filter por tenant; el SaveChanges puede requerir contexto de tenant.
        if (scope.ServiceProvider.GetService<ITenantContext>() is { } tenantContext)
            tenantContext.SetTenant(tenantId);
    }

    // ------------------------------------------------------------------
    // Token TenantEmployee (NO PlatformAdmin — para que el gate no lo bypasee) con perm_v
    // ------------------------------------------------------------------

    private async Task<HttpResponseMessage> Get(Guid tenantId, Guid userId, int permV, string path)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            MintEmployeeToken(tenantId, userId, permV)
        );
        return await client.GetAsync(path);
    }

    private string MintEmployeeToken(Guid tenantId, Guid userId, int permV)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("actor_type", "TenantEmployee"),
            new Claim("perm_v", permV.ToString()),
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

    // ------------------------------------------------------------------
    // Captura de authz.module_decision (independiente del backend de logging)
    // ------------------------------------------------------------------

    private sealed class ModuleDecisionCapture : IDisposable
    {
        private readonly MeterListener listener = new();
        private readonly List<(string module, string result)> measurements = [];
        private readonly object gate = new();

        public ModuleDecisionCapture()
        {
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == AuthorizationMetrics.MeterName && instrument.Name == "authz.module_decision")
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<int>(
                (_, _, tags, _) =>
                {
                    string? module = null;
                    string? result = null;
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "module")
                            module = tag.Value?.ToString();
                        else if (tag.Key == "result")
                            result = tag.Value?.ToString();
                    }

                    if (module is not null && result is not null)
                        lock (gate)
                            measurements.Add((module, result));
                }
            );
            listener.Start();
        }

        public int Count(string module, string result)
        {
            lock (gate)
                return measurements.Count(m => m.module == module && m.result == result);
        }

        public void Dispose() => listener.Dispose();
    }
}
