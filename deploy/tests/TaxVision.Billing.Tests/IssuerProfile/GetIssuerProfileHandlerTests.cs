using System.Reflection;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Application.Invoices.IssuerProfile;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using Xunit;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Tests.IssuerProfile;

public sealed class GetIssuerProfileHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Line2_is_exposed()
    {
        var profile = DomainIssuerProfile.Create(Tenant, DateTime.UtcNow);
        profile.ApplyTenantProfile(
            1,
            new TenantOfficeProfile(
                "Office",
                null,
                null,
                new Address("1600 Main St", "Suite 200", "Austin", "TX", "78701", "US"),
                null,
                null,
                null
            ),
            DateTime.UtcNow
        );

        var response = await HandleAsync(profile);

        Assert.Equal("Suite 200", response.Line2);
        Assert.Equal("1600 Main St", response.Line1);
    }

    [Fact]
    public async Task A_row_saved_before_validation_still_loads_as_it_is()
    {
        // Filas viejas: EIN sin guion, correo y estado mal formados. Leer nunca valida.
        var profile = DomainIssuerProfile.Create(Tenant, DateTime.UtcNow);
        Seed(profile, nameof(DomainIssuerProfile.Name), "Legacy office");
        Seed(profile, nameof(DomainIssuerProfile.TaxId), "12 345");
        Seed(profile, nameof(DomainIssuerProfile.Email), "not-an-email");
        Seed(profile, nameof(DomainIssuerProfile.Address), new Address("—", null, "", "Texas", "1", "US"));

        var response = await HandleAsync(profile);

        Assert.Equal("12 345", response.TaxId);
        Assert.Equal("not-an-email", response.Email);
        Assert.Equal("Texas", response.State);
        Assert.Null(response.City);
    }

    [Fact]
    public async Task Without_a_row_an_empty_us_profile_is_returned()
    {
        var response = await HandleAsync(null);

        Assert.Equal(string.Empty, response.Name);
        Assert.Null(response.Line2);
        Assert.Equal("US", response.Country);
    }

    private static void Seed(DomainIssuerProfile profile, string property, object value) =>
        typeof(DomainIssuerProfile)
            .GetProperty(property, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(profile, value);

    private static async Task<IssuerProfileResponse> HandleAsync(DomainIssuerProfile? profile)
    {
        var result = await GetIssuerProfileHandler.Handle(
            new GetIssuerProfileQuery(Tenant),
            new SingleProfile(profile),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class SingleProfile(DomainIssuerProfile? profile) : IIssuerProfileRepository
    {
        public Task<DomainIssuerProfile?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(profile);

        public Task AddAsync(DomainIssuerProfile profile, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<(IReadOnlyList<DomainIssuerProfile> Items, int TotalCount)> ListPageAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
