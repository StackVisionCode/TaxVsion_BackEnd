using TaxVision.Billing.Application.Invoices.IssuerProfile;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Tests.Fakes;
using Xunit;
using DomainIssuerProfile = TaxVision.Billing.Domain.Invoices.IssuerProfile;

namespace TaxVision.Billing.Tests.IssuerProfile;

public sealed class ListIssuerProfilesHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
    private readonly InMemoryIssuerProfileRepository profiles = new();

    private DomainIssuerProfile Seed(string? name, Address? address = null, string? phone = null)
    {
        var profile = DomainIssuerProfile.Create(Guid.NewGuid(), Now);
        profile.ApplyTenantProfile(
            1,
            new TenantOfficeProfile(name, null, "12-3456789", address, phone, null, null),
            Now
        );
        profiles.Rows.Add(profile);
        return profile;
    }

    [Fact]
    public async Task Pages_carry_the_total_and_every_tenant()
    {
        Seed("A");
        Seed("B");
        Seed("C");

        var first = await ListIssuerProfilesHandler.Handle(new ListIssuerProfilesQuery(1, 2), profiles, default);
        var second = await ListIssuerProfilesHandler.Handle(new ListIssuerProfilesQuery(2, 2), profiles, default);

        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.Size);
        Assert.Equal(
            profiles.Rows.Select(p => p.TenantId).Order(),
            first.Items.Concat(second.Items).Select(i => i.TenantId)
        );
    }

    [Fact]
    public async Task The_address_is_flattened_and_blank_text_becomes_null()
    {
        var profile = Seed("Office", new Address("1600 Main St", "Suite 200", "Austin", "TX", "78701", "US"), " ");

        var item = Assert.Single(
            (await ListIssuerProfilesHandler.Handle(new ListIssuerProfilesQuery(1, 10), profiles, default)).Items
        );

        Assert.Equal(profile.TenantId, item.TenantId);
        Assert.Equal("Office", item.Name);
        Assert.Equal("12-3456789", item.TaxId);
        Assert.Equal("1600 Main St", item.Line1);
        Assert.Equal("Suite 200", item.Line2);
        Assert.Equal("Austin", item.City);
        Assert.Equal("TX", item.State);
        Assert.Equal("78701", item.Zip);
        Assert.Equal("US", item.Country);
        Assert.Null(item.Phone);
        Assert.Equal(Now, item.UpdatedAtUtc);
    }

    [Fact]
    public async Task A_legacy_placeholder_address_or_missing_name_comes_out_as_null()
    {
        Seed(null, new Address("—", null, "", "", "", "US"));

        var item = Assert.Single(
            (await ListIssuerProfilesHandler.Handle(new ListIssuerProfilesQuery(1, 10), profiles, default)).Items
        );

        Assert.Null(item.Name);
        Assert.Null(item.Line1);
        Assert.Null(item.Country);
    }
}
