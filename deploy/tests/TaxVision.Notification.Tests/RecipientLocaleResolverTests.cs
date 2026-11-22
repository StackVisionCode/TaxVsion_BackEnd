using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Domain.Recipients;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Idioma y zona de quien recibe: usuario → cliente (cuentas del portal) → inglés.</summary>
public sealed class RecipientLocaleResolverTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    [Fact]
    public async Task Staff_without_language_falls_back_to_english()
    {
        var (resolver, directory, _, _) = Build();
        directory.Seed(TenantId, UserId, "staff@example.com");

        var locale = await resolver.ForUserAsync(TenantId, UserId, default);

        Assert.Equal("en", locale.Language);
        Assert.True(locale.LanguageIsFallback);
    }

    [Fact]
    public async Task User_language_and_zone_win()
    {
        var (resolver, directory, _, _) = Build();
        directory
            .Seed(TenantId, UserId, "staff@example.com")
            .ApplyProfile("es", "America/Santo_Domingo", "Carlos Pérez", DateTime.UtcNow);

        var locale = await resolver.ForUserAsync(TenantId, UserId, default);

        Assert.Equal("es", locale.Language);
        Assert.Equal("America/Santo_Domingo", locale.TimeZoneId);
        Assert.False(locale.LanguageIsFallback);
    }

    [Fact]
    public async Task Portal_account_without_language_uses_the_customer_language()
    {
        var (resolver, directory, portal, policies) = Build();
        directory.Seed(TenantId, UserId, "client@example.com");
        portal.Rows.Add(CustomerPortalAccountProjection.Create(TenantId, CustomerId, UserId));
        policies.Rows.Add(CustomerContactPolicyProjection.Create(TenantId, CustomerId, true, true, "Es"));

        var locale = await resolver.ForUserAsync(TenantId, UserId, default);

        Assert.Equal("es", locale.Language);
    }

    [Fact]
    public async Task Customer_pt_falls_back_to_english()
    {
        var (resolver, _, _, policies) = Build();
        policies.Rows.Add(CustomerContactPolicyProjection.Create(TenantId, CustomerId, true, true, "Pt"));

        var locale = await resolver.ForCustomerAsync(TenantId, CustomerId, default);

        Assert.Equal("en", locale.Language);
    }

    [Fact]
    public async Task Office_email_uses_the_language_of_the_owner_with_that_address()
    {
        var (resolver, directory, _, _) = Build();
        directory
            .Seed(TenantId, UserId, "owner@example.com")
            .ApplyProfile("es", "America/Chicago", "Owner", DateTime.UtcNow);

        var owner = await resolver.ForOfficeEmailAsync(TenantId, " Owner@Example.com ", default);
        var stranger = await resolver.ForOfficeEmailAsync(TenantId, "someone@example.com", default);

        Assert.Equal("es", owner.Language);
        Assert.Equal("America/Chicago", owner.TimeZoneId);
        Assert.Equal("en", stranger.Language);
    }

    [Fact]
    public void TemplateLanguage_is_Es_or_En()
    {
        Assert.Equal("Es", new RecipientLocale("es", null, false).TemplateLanguage);
        Assert.Equal("En", new RecipientLocale("en", null, true).TemplateLanguage);
        Assert.Equal("es", new RecipientLocale("es", "America/Chicago", false).Reader.Locale);
    }

    [Fact]
    public void Event_language_is_normalized()
    {
        var (resolver, _, _, _) = Build();

        Assert.Equal("es", resolver.FromEventLanguage("Es").Language);
        Assert.Equal("en", resolver.FromEventLanguage("fr").Language);
        Assert.Equal("en", resolver.FromEventLanguage(null).Language);
    }

    private static (
        RecipientLocaleResolver Resolver,
        InMemoryUserEmailDirectoryRepository Directory,
        PortalAccounts Portal,
        Policies Policies
    ) Build()
    {
        var directory = new InMemoryUserEmailDirectoryRepository();
        var portal = new PortalAccounts();
        var policies = new Policies();
        return (new RecipientLocaleResolver(directory, portal, policies, Offices), directory, portal, policies);
    }

    private static readonly OwnerOnly Offices = new();

    // El dueño de la oficina es UserId; sin administradores.
    private sealed class OwnerOnly : ITenantOwnerProjectionRepository
    {
        public Task<Guid?> GetActiveOwnerUserIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(UserId);

        public Task<IReadOnlyList<Guid>> GetActiveAdminUserIdsAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<TenantOwnerProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<TenantOwnerProjection?>(null);

        public Task AddAsync(TenantOwnerProjection projection, CancellationToken ct = default) => Task.CompletedTask;

        public Task<TenantOwnerProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantOwnerProjection?>(null);
    }

    private sealed class PortalAccounts : ICustomerPortalAccountProjectionRepository
    {
        public List<CustomerPortalAccountProjection> Rows { get; } = [];

        public Task<Guid?> GetActivePortalUserIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId)?.UserId);

        public Task<CustomerPortalAccountProjection?> GetByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId));

        public Task<CustomerPortalAccountProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.UserId == userId));

        public Task AddAsync(CustomerPortalAccountProjection projection, CancellationToken ct = default)
        {
            Rows.Add(projection);
            return Task.CompletedTask;
        }
    }

    private sealed class Policies : ICustomerContactPolicyRepository
    {
        public List<CustomerContactPolicyProjection> Rows { get; } = [];

        public Task<CustomerContactPolicyProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.CustomerId == customerId));

        public Task AddAsync(CustomerContactPolicyProjection row, CancellationToken ct = default)
        {
            Rows.Add(row);
            return Task.CompletedTask;
        }
    }
}
