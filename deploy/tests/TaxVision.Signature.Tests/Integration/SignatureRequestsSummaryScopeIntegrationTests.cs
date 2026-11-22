using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BuildingBlocks.Authorization;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Signature.Application.Requests.Queries.Summary;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Tests.Application.Offboarding;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// Alcance "Office" del resumen contra el host real, llamando al endpoint directo: sin users.work.view es 403 aunque
/// el CRM lo pida, y con él la oficina sigue recortada a los clientes del empleado si no tiene customers.view_all.
/// </summary>
public sealed class SignatureRequestsSummaryScopeIntegrationTests
    : IClassFixture<SignatureSummaryScopeApiFactory>,
        IAsyncLifetime
{
    private readonly SignatureSummaryScopeApiFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employee = Guid.NewGuid();
    private readonly Guid _colleague = Guid.NewGuid();

    public SignatureRequestsSummaryScopeIntegrationTests(SignatureSummaryScopeApiFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        // Un borrador propio (visible) y uno de un colega (fuera del recorte del empleado).
        await using var db = NewContext();
        db.SignatureRequests.AddRange(
            PreparerRequests.Draft(_tenantId, _employee),
            PreparerRequests.Draft(_tenantId, _colleague)
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.SignatureRequests.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Set<CustomerAssignmentProjection>()
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Office_without_users_work_view_is_forbidden()
    {
        var client = Client(SignaturePermissions.RequestRead);

        var response = await client.GetAsync("/signature/requests/summary?scope=office");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_scope_is_a_bad_request()
    {
        var client = Client(SignaturePermissions.RequestRead, UserManagementPermissions.UsersWorkView);

        var response = await client.GetAsync("/signature/requests/summary?scope=everyone");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Office_keeps_the_employee_cut_without_view_all()
    {
        var restricted = Client(SignaturePermissions.RequestRead, UserManagementPermissions.UsersWorkView);
        var viewAll = Client(
            SignaturePermissions.RequestRead,
            UserManagementPermissions.UsersWorkView,
            CustomersPermissions.ViewAll
        );

        var mine = await ReadAsync(restricted, "office");
        var office = await ReadAsync(viewAll, "office");

        Assert.Equal(1, mine.Draft);
        Assert.Equal(2, office.Draft);
    }

    [Fact]
    public async Task Me_counts_only_what_the_employee_created()
    {
        var client = Client(SignaturePermissions.RequestRead, CustomersPermissions.ViewAll);

        var me = await ReadAsync(client, "me");

        Assert.Equal(1, me.Draft);
    }

    private static async Task<SignatureRequestsSummaryResponse> ReadAsync(HttpClient client, string scope)
    {
        var response = await client.GetAsync($"/signature/requests/summary?scope={scope}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SignatureRequestsSummaryResponse>())!;
    }

    private HttpClient Client(params string[] permissions)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            _factory.Mint(_tenantId, _employee, permissions)
        );
        return client;
    }

    private SignatureDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>()
                .UseSqlServer(SignatureIntegrationDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}

/// <summary>
/// El host real con un empleado (no PlatformAdmin) cuyos permisos vienen del token de prueba, y el recorte por
/// asignación encendido: la prueba fija el comportamiento y no lo hereda del entorno.
/// </summary>
public sealed class SignatureSummaryScopeApiFactory : SignatureApiFactory
{
    private const string PermissionClaim = "test_perm";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration(
            (_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["Signature:AssignmentVisibility:Enabled"] = "true" }
                )
        );
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUserPermissionsSource>();
            services.AddScoped<IUserPermissionsSource, TokenPermissions>();
        });
    }

    public string Mint(Guid tenantId, Guid userId, IEnumerable<string> permissions)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        Claim[] claims =
        [
            new("sub", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("actor_type", "TenantEmployee"),
            .. permissions.Select(permission => new Claim(PermissionClaim, permission)),
        ];
        var token = new JwtSecurityToken(
            JwtIssuer,
            JwtAudience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(30),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class TokenPermissions : IUserPermissionsSource
    {
        public Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permission, CancellationToken ct = default) =>
            Task.FromResult(user.FindAll(PermissionClaim).Any(claim => claim.Value == permission));
    }
}
