using System.Text.Json;
using TaxVision.Tenant.Application.Profiles;
using TaxVision.Tenant.Application.Profiles.Queries;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Tests.TestSupport;
using TenantEntity = TaxVision.Tenant.Domain.Tenant;

namespace TaxVision.Tenant.Tests.Application;

public sealed class GetClientOfficeProfileHandlerTests
{
    private readonly TenantEntity _tenant = TenantEntity.Create("Acme Office", "acme-office", "America/Chicago").Value;
    private readonly InMemoryOfficeProfileRepository _profiles = new();

    [Fact]
    public async Task Doing_business_as_name_wins_over_the_office_name()
    {
        await SaveProfileAsync(OfficeProfileDrafts.Full());

        var response = await HandleAsync();

        Assert.Equal("Acme Tax", response.Name);
        Assert.Equal("America/Chicago", response.TimeZoneId);
        Assert.Equal("(555) 123-4567", response.Phone);
        Assert.Equal("office@acmetax.com", response.Email);
        Assert.Equal("https://acmetax.com", response.Website);
        Assert.Equal("https://calendly.com/acmetax", response.BookingUrl);
        Assert.Equal("https://g.page/r/acmetax/review", response.ReviewUrl);
        Assert.Equal(["Monday", "Saturday"], response.BusinessHours.Select(h => h.Day));
        Assert.Equal(new BusinessHoursResponse("Monday", "09:00", "18:00"), response.BusinessHours[0]);
    }

    [Fact]
    public async Task Without_a_saved_profile_returns_only_the_office_name_and_time_zone()
    {
        var response = await HandleAsync();

        Assert.Equal("Acme Office", response.Name);
        Assert.Equal("America/Chicago", response.TimeZoneId);
        Assert.Null(response.Phone);
        Assert.Null(response.Email);
        Assert.Null(response.Website);
        Assert.Null(response.Address);
        Assert.Empty(response.BusinessHours);
        Assert.Null(response.BookingUrl);
        Assert.Null(response.ReviewUrl);
    }

    [Fact]
    public async Task Without_a_doing_business_as_name_uses_the_office_name()
    {
        await SaveProfileAsync(OfficeProfileDrafts.Full() with { DisplayName = null });

        var response = await HandleAsync();

        Assert.Equal("Acme Office", response.Name);
    }

    [Fact]
    public async Task Mailing_address_wins_over_the_physical_one()
    {
        await SaveProfileAsync(OfficeProfileDrafts.Full());

        var response = await HandleAsync();

        Assert.Equal(new OfficeAddressResponse("PO Box 9", null, "Austin", "TX", "78702", "US"), response.Address);
    }

    [Fact]
    public async Task Same_mailing_address_falls_back_to_the_physical_one()
    {
        await SaveProfileAsync(OfficeProfileDrafts.Full() with { MailingSameAsPhysical = true, MailingAddress = null });

        var response = await HandleAsync();

        Assert.Equal(
            new OfficeAddressResponse("100 Main St", "Suite 2", "Austin", "TX", "78701-1234", "US"),
            response.Address
        );
    }

    [Fact]
    public async Task An_address_without_street_or_city_is_not_shown()
    {
        await SaveProfileAsync(
            OfficeProfileDrafts.Empty() with
            {
                Phone = "5551234567",
                PhysicalAddress = new OfficeAddressDraft(null, null, "Austin", "TX", null, null),
            }
        );

        var response = await HandleAsync();

        Assert.Null(response.Address);
        Assert.Equal("(555) 123-4567", response.Phone);
    }

    [Fact]
    public async Task Never_exposes_legal_name_ein_or_entity_type()
    {
        await SaveProfileAsync(OfficeProfileDrafts.Full());

        var json = JsonSerializer.Serialize(await HandleAsync(), JsonSerializerOptions.Web);

        Assert.DoesNotContain("Acme Tax Services LLC", json);
        Assert.DoesNotContain("12-3456789", json);
        using var document = JsonDocument.Parse(json);
        var properties = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
        Assert.Equal(
            ["name", "phone", "email", "website", "address", "timeZoneId", "businessHours", "bookingUrl", "reviewUrl"],
            properties
        );
    }

    [Fact]
    public async Task Unknown_tenant_is_not_found()
    {
        var result = await GetClientOfficeProfileHandler.Handle(
            new GetClientOfficeProfileQuery(Guid.NewGuid()),
            new SingleTenantRepository(_tenant),
            _profiles,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.NotFound", result.Error.Code);
    }

    private async Task SaveProfileAsync(OfficeProfileDraft draft)
    {
        var profile = OfficeProfile.CreateEmpty(_tenant.Id);
        Assert.True(profile.Update(draft, Guid.NewGuid(), DateTime.UtcNow).IsSuccess);
        await _profiles.AddAsync(profile);
    }

    private async Task<ClientOfficeProfileResponse> HandleAsync()
    {
        var result = await GetClientOfficeProfileHandler.Handle(
            new GetClientOfficeProfileQuery(_tenant.Id),
            new SingleTenantRepository(_tenant),
            _profiles,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class SingleTenantRepository(TenantEntity existing) : ITenantRepository
    {
        public Task<TenantEntity?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(existing.Id == id ? existing : null);

        public Task AddAsync(TenantEntity entity, CancellationToken ct = default) => throw new NotSupportedException();

        public void Remove(TenantEntity entity) => throw new NotSupportedException();

        public Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Guid?> GetIdBySubDomainAsync(string subdomain, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantEntity?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
