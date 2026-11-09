using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using Wolverine.Tracking;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>
/// CUS-10 (D-POR-1). Host real de Auth sobre la base temporal (BB-10): archivar el cliente corta la cuenta de portal y
/// sus sesiones; restaurarlo la devuelve.
/// </summary>
public sealed class CustomerPortalAccessIntegrationTests : IClassFixture<AuthApiFactory>
{
    private const string Password = "Portal-Test-Password-1!";
    private readonly AuthApiFactory _factory;
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly string _email = $"{Guid.NewGuid():N}@example.test";

    public CustomerPortalAccessIntegrationTests(AuthApiFactory factory) => _factory = factory;

    [Fact(Timeout = 120_000)]
    public async Task ArchiveCutsAccess_AndRestoreGivesItBack()
    {
        var (tenant, user) = await SeedPortalUserAsync();
        var first = await LoginAsync(tenant.Id);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var sessionId = await SingleActiveSessionAsync(user.Id);

        await DeliverAsync(
            new CustomerArchivedIntegrationEvent
            {
                TenantId = tenant.Id,
                CustomerId = _customerId,
                ArchivedByUserId = Guid.NewGuid(),
            }
        );

        var afterArchive = await LoginAsync(tenant.Id);
        var denied = await DenylistedAsync(sessionId);
        var stored = await ReadUserAsync(user.Id);
        Assert.Multiple(
            () => Assert.Equal(HttpStatusCode.Forbidden, afterArchive.StatusCode),
            () => Assert.True(denied, "the open session is still accepted after archiving"),
            () => Assert.False(stored.IsActive, "the portal account is still active after archiving")
        );
        Assert.Equal("Auth.UserInactive", await CodeAsync(afterArchive));
        Assert.Empty(await ActiveSessionsAsync(user.Id));

        await DeliverAsync(
            new CustomerReactivatedIntegrationEvent
            {
                TenantId = tenant.Id,
                CustomerId = _customerId,
                ReactivatedByUserId = Guid.NewGuid(),
                ReactivatedAtUtc = DateTime.UtcNow,
            }
        );

        var afterRestore = await LoginAsync(tenant.Id);
        Assert.Equal(HttpStatusCode.OK, afterRestore.StatusCode);
        Assert.True((await ReadUserAsync(user.Id)).IsActive);
    }

    [Fact(Timeout = 120_000)]
    public async Task RestoreDoesNotReactivate_AnAccountTheOfficeDeactivated()
    {
        var (tenant, user) = await SeedPortalUserAsync(deactivatedByOffice: true);

        await DeliverAsync(
            new CustomerArchivedIntegrationEvent
            {
                TenantId = tenant.Id,
                CustomerId = _customerId,
                ArchivedByUserId = Guid.NewGuid(),
            }
        );
        await DeliverAsync(
            new CustomerReactivatedIntegrationEvent
            {
                TenantId = tenant.Id,
                CustomerId = _customerId,
                ReactivatedByUserId = Guid.NewGuid(),
                ReactivatedAtUtc = DateTime.UtcNow,
            }
        );

        Assert.False((await ReadUserAsync(user.Id)).IsActive);
        Assert.Equal(HttpStatusCode.Forbidden, (await LoginAsync(tenant.Id)).StatusCode);
    }

    private async Task<(Tenant Tenant, User User)> SeedPortalUserAsync(bool deactivatedByOffice = false)
    {
        var tenant = Tenant
            .Register(Guid.NewGuid(), "Office", $"t{Guid.NewGuid():N}", TenantKind.Customer, "America/New_York")
            .Value;

        string hash;
        using (var scope = _factory.Services.CreateScope())
            hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(Password);

        var user = User.Register(tenant.Id, "Paula", "Client", _email, hash, UserActorType.CustomerPortal, _customerId)
            .Value;
        if (deactivatedByOffice)
            user.Deactivate(Guid.NewGuid(), DateTime.UtcNow);

        await using var db = NewContext(tenant.Id);
        db.Add(tenant);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (tenant, user);
    }

    private Task<HttpResponseMessage> LoginAsync(Guid tenantId) =>
        _factory
            .CreateClient()
            .PostAsJsonAsync(
                "/auth/login",
                new
                {
                    Email = _email,
                    Password,
                    TenantId = tenantId,
                    AccountKind = "Portal",
                }
            );

    // Como llega por Rabbit: publicado, sin respuesta.
    private Task DeliverAsync(object integrationEvent) =>
        _factory
            .Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(30))
            .ExecuteAndWaitAsync(context => context.PublishAsync(integrationEvent));

    private async Task<bool> DenylistedAsync(Guid sessionId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IAccessTokenDenylist>().IsSessionDeniedAsync(sessionId);
    }

    private async Task<Guid> SingleActiveSessionAsync(Guid userId) => Assert.Single(await ActiveSessionsAsync(userId));

    private async Task<List<Guid>> ActiveSessionsAsync(Guid userId)
    {
        await using var db = NewContext(Guid.Empty);
        return await db
            .UserSessions.IgnoreQueryFilters()
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null)
            .Select(session => session.Id)
            .ToListAsync();
    }

    private async Task<User> ReadUserAsync(Guid userId)
    {
        await using var db = NewContext(Guid.Empty);
        return await db.Users.IgnoreQueryFilters().AsNoTracking().SingleAsync(user => user.Id == userId);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private static AuthDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>().UseSqlServer(AuthIntegrationDatabase.ConnectionString).Options,
            new FixedTenant(tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => tenantId != Guid.Empty;

        public void SetTenant(Guid id) { }
    }
}
