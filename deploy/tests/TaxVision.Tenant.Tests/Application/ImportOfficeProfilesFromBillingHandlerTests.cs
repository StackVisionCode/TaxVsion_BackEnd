using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Application.Profiles.Commands;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>
/// Importación única desde Billing (cubierta con un Billing de mentira: el endpoint interno real lo
/// construye otra fase). Solo llena oficinas sin perfil, tolera datos viejos y se puede repetir.
/// </summary>
public sealed class ImportOfficeProfilesFromBillingHandlerTests
{
    private readonly FakeBillingIssuerProfileClient _billing = new();
    private readonly InMemoryOfficeProfileRepository _profiles = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly RecordingMessageBus _bus = new();
    private readonly ListLogger _logger = new();

    public ImportOfficeProfilesFromBillingHandlerTests() =>
        _bus.InvokeHandler = async message =>
            await ImportOfficeProfilesPageHandler.Handle(
                (ImportOfficeProfilesPageCommand)message,
                _profiles,
                _unitOfWork,
                _bus,
                new NoopCorrelationContext(),
                _logger,
                TimeProvider.System,
                CancellationToken.None
            );

    [Fact]
    public async Task Imports_offices_without_a_profile_maps_the_fields_and_publishes_each_one()
    {
        var tenantId = SeedTenant();
        _billing.Items.Add(Issuer(tenantId) with { Name = "Acme Tax LLC", TaxId = "123456789", Zip = "78701" });

        var result = await HandleAsync();

        Assert.Equal(new ImportOfficeProfilesResponse(1, 0, 0, 0, 0), result.Value);
        var profile = Assert.Single(_profiles.Profiles);
        Assert.Equal("Acme Tax LLC", profile.LegalName);
        Assert.Null(profile.DisplayName);
        Assert.Equal("12-3456789", profile.Ein);
        Assert.Equal("78701", profile.PhysicalAddress!.PostalCode);
        Assert.Null(profile.MailingAddress);
        Assert.Equal(1, profile.Version);

        var evt = Assert.Single(_bus.Published.OfType<TenantProfileChangedIntegrationEvent>());
        Assert.Equal(tenantId, evt.ProfileTenantId);
        Assert.Equal("Acme Tax LLC", evt.LegalName);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Never_overwrites_an_existing_profile_and_skips_unknown_tenants()
    {
        var withProfile = SeedTenant();
        var existing = OfficeProfile.CreateEmpty(withProfile);
        existing.Update(
            OfficeProfileDrafts.Empty() with
            {
                LegalName = "Typed by the office",
            },
            Guid.NewGuid(),
            DateTime.UtcNow
        );
        _profiles.Profiles.Add(existing);
        _billing.Items.Add(Issuer(withProfile) with { Name = "Old Billing Name" });
        _billing.Items.Add(Issuer(Guid.NewGuid()));

        var result = await HandleAsync();

        // Uno ya tenía perfil y el otro no es una oficina: cada motivo se cuenta aparte.
        Assert.Equal(new ImportOfficeProfilesResponse(0, 2, 1, 1, 0), result.Value);
        Assert.Equal("Typed by the office", existing.LegalName);
        Assert.Empty(_bus.Published);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Malformed_legacy_values_are_dropped_one_by_one_and_logged()
    {
        var tenantId = SeedTenant();
        _billing.Items.Add(
            Issuer(tenantId) with
            {
                Name = "Legacy Co",
                TaxId = "N/A",
                Line1 = "—",
                City = "Austin",
                State = "Texas",
                Phone = "call us",
                Website = "acme",
            }
        );

        var result = await HandleAsync();

        Assert.Equal(1, result.Value.Imported);
        var profile = Assert.Single(_profiles.Profiles);
        Assert.Equal("Legacy Co", profile.LegalName);
        Assert.Null(profile.Ein);
        Assert.Null(profile.Phone);
        Assert.Null(profile.Website);
        Assert.Null(profile.PhysicalAddress!.Line1);
        Assert.Null(profile.PhysicalAddress.State);
        Assert.Equal("Austin", profile.PhysicalAddress.City);

        var dropped = _logger.Messages.Where(m => m.Contains("dropped", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, dropped.Count);
        Assert.Contains(dropped, m => m.Contains("Tenant.Profile.Ein", StringComparison.Ordinal));
        Assert.Contains(dropped, m => m.Contains("Tenant.Profile.State", StringComparison.Ordinal));
        Assert.DoesNotContain(dropped, m => m.Contains("call us", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pages_through_billing_and_a_second_run_imports_nothing()
    {
        for (var i = 0; i < ImportOfficeProfilesFromBillingHandler.PageSize + 5; i++)
            _billing.Items.Add(Issuer(SeedTenant()) with { Name = $"Office {i}" });

        var first = await HandleAsync();
        var publishedAfterFirst = _bus.Published.Count;
        var second = await HandleAsync();

        Assert.Equal(new ImportOfficeProfilesResponse(105, 0, 0, 0, 0), first.Value);
        Assert.Equal([(1, 100), (2, 100), (1, 100), (2, 100)], _billing.Requests);
        Assert.Equal(105, publishedAfterFirst);
        Assert.Equal(new ImportOfficeProfilesResponse(0, 105, 0, 105, 0), second.Value);
        Assert.Equal(105, _bus.Published.Count);
    }

    [Fact]
    public async Task A_billing_failure_keeps_earlier_pages_and_reports_unavailable()
    {
        for (var i = 0; i < ImportOfficeProfilesFromBillingHandler.PageSize + 1; i++)
            _billing.Items.Add(Issuer(SeedTenant()) with { Name = $"Office {i}" });
        _billing.FailOnPage = 2;

        var result = await HandleAsync();

        Assert.Equal(BillingIssuerProfileErrors.Unavailable, result.Error);
        Assert.Equal(100, _profiles.Profiles.Count);
        Assert.Equal(100, _bus.Published.Count);
    }

    [Fact]
    public async Task A_tenant_repeated_in_billing_is_imported_once()
    {
        var tenantId = SeedTenant();
        _billing.Items.Add(Issuer(tenantId) with { Name = "First" });
        _billing.Items.Add(Issuer(tenantId) with { Name = "Second" });

        var result = await HandleAsync();

        Assert.Equal(new ImportOfficeProfilesResponse(1, 1, 0, 1, 0), result.Value);
        Assert.Equal("First", Assert.Single(_profiles.Profiles).LegalName);
    }

    private Guid SeedTenant()
    {
        var id = Guid.NewGuid();
        _profiles.ExistingTenants.Add(id);
        return id;
    }

    private static BillingIssuerProfile Issuer(Guid tenantId) =>
        new(tenantId, "Office", null, null, null, null, null, null, "US", null, null, null, DateTime.UtcNow);

    private Task<Result<ImportOfficeProfilesResponse>> HandleAsync() =>
        ImportOfficeProfilesFromBillingHandler.Handle(
            new ImportOfficeProfilesFromBillingCommand(Guid.NewGuid()),
            _billing,
            _bus,
            _logger,
            CancellationToken.None
        );

    private sealed class ListLogger
        : ILogger<ImportOfficeProfilesFromBillingCommand>,
            ILogger<ImportOfficeProfilesPageCommand>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }
}
