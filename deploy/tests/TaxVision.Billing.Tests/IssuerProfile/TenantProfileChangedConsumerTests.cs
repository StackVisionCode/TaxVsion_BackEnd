using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Billing.Application.Invoices.IssuerProfile;
using TaxVision.Billing.Tests.Fakes;
using Xunit;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Tests.IssuerProfile;

public sealed class TenantProfileChangedConsumerTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private readonly InMemoryIssuerProfileRepository profiles = new();
    private readonly CountingUnitOfWork unitOfWork = new();

    private static TenantProfileChangedIntegrationEvent Event(
        Guid tenantId,
        long version,
        string? legalName = "Gutierrez Tax LLC"
    ) =>
        new()
        {
            TenantId = tenantId,
            ProfileTenantId = tenantId,
            Version = version,
            UpdatedAtUtc = Now,
            LegalName = legalName,
            DisplayName = "Gutierrez Tax",
            Ein = "12-3456789",
            Phone = "(512) 555-0112",
            Email = "office@gutierreztax.com",
            Website = "https://gutierreztax.com",
            PhysicalAddress = new OfficeAddressData("1600 Main St", "Suite 200", "Austin", "TX", "78701", "US"),
            MailingAddress = new OfficeAddressData("PO Box 1", null, "Austin", "TX", "78702", "US"),
        };

    private Task HandleAsync(TenantProfileChangedIntegrationEvent evt) =>
        TenantProfileChangedConsumer.Handle(
            evt,
            profiles,
            unitOfWork,
            new NoopCorrelationContext(),
            new FixedClock(Now),
            NullLogger<DomainIssuerProfile>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task The_first_event_creates_the_profile_with_the_physical_address()
    {
        var tenantId = Guid.NewGuid();

        await HandleAsync(Event(tenantId, 1));

        var profile = Assert.Single(profiles.Rows);
        Assert.Equal(tenantId, profile.TenantId);
        Assert.Equal("Gutierrez Tax LLC", profile.Name);
        Assert.Equal("12-3456789", profile.TaxId);
        Assert.Equal("1600 Main St", profile.Address!.Line1);
        Assert.Equal("Suite 200", profile.Address.Line2);
        Assert.Equal("78701", profile.Address.Zip);
        Assert.Equal(1, profile.SourceVersion);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task A_newer_event_updates_the_same_row()
    {
        var tenantId = Guid.NewGuid();
        await HandleAsync(Event(tenantId, 1));

        await HandleAsync(Event(tenantId, 2, legalName: "Renamed LLC") with { PhysicalAddress = null });

        var profile = Assert.Single(profiles.Rows);
        Assert.Equal("Renamed LLC", profile.Name);
        Assert.Null(profile.Address);
        Assert.Equal(2, profile.SourceVersion);
    }

    [Fact]
    public async Task An_older_or_repeated_event_is_ignored_without_saving()
    {
        var tenantId = Guid.NewGuid();
        await HandleAsync(Event(tenantId, 5));

        await HandleAsync(Event(tenantId, 5, legalName: "Duplicate"));
        await HandleAsync(Event(tenantId, 4, legalName: "Older"));

        Assert.Equal("Gutierrez Tax LLC", Assert.Single(profiles.Rows).Name);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task An_event_without_a_tenant_is_ignored()
    {
        await HandleAsync(Event(Guid.Empty, 1));

        Assert.Empty(profiles.Rows);
        Assert.Equal(0, unitOfWork.Saves);
    }
}
