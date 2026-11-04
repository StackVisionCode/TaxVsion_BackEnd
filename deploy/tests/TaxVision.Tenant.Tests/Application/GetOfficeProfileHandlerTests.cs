using TaxVision.Tenant.Application.Profiles.Queries;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

public sealed class GetOfficeProfileHandlerTests
{
    [Fact]
    public async Task Without_a_saved_profile_returns_the_empty_contract_shape()
    {
        var response = await GetOfficeProfileHandler.Handle(
            new GetOfficeProfileQuery(Guid.NewGuid()),
            new InMemoryOfficeProfileRepository(),
            CancellationToken.None
        );

        Assert.Null(response.LegalName);
        Assert.Equal(new(null, null, null, null, null, "US"), response.PhysicalAddress);
        Assert.True(response.MailingSameAsPhysical);
        Assert.Null(response.MailingAddress);
        Assert.Equal("en", response.DefaultLanguage);
        Assert.Empty(response.BusinessHours);
        Assert.Equal(0, response.Version);
        Assert.Null(response.UpdatedAtUtc);
    }

    [Fact]
    public async Task Returns_the_saved_profile_of_the_requested_tenant()
    {
        var tenantId = Guid.NewGuid();
        var repository = new InMemoryOfficeProfileRepository();
        var profile = OfficeProfile.CreateEmpty(tenantId);
        profile.Update(OfficeProfileDrafts.Full(), Guid.NewGuid(), DateTime.UtcNow);
        await repository.AddAsync(profile);

        var response = await GetOfficeProfileHandler.Handle(
            new GetOfficeProfileQuery(tenantId),
            repository,
            CancellationToken.None
        );

        Assert.Equal("Acme Tax Services LLC", response.LegalName);
        Assert.Equal("Austin", response.PhysicalAddress.City);
        Assert.Equal(1, response.Version);
    }
}
