using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Profiles.Commands;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>Republicar perfiles: un evento por perfil guardado (con su versión), nunca por uno vacío.</summary>
public sealed class RepublishOfficeProfilesHandlerTests
{
    private readonly InMemoryOfficeProfileRepository _profiles = new();
    private readonly RecordingMessageBus _bus = new();

    [Fact]
    public async Task Publishes_one_event_per_saved_profile_with_its_version()
    {
        var saved = Saved("Acme Tax");
        _profiles.Profiles.Add(saved);
        _profiles.Profiles.Add(OfficeProfile.CreateEmpty(Guid.NewGuid()));

        var result = await HandleAsync();

        Assert.Equal(1, result.Value.Published);
        var evt = Assert.Single(_bus.Published.OfType<TenantProfileChangedIntegrationEvent>());
        Assert.Equal(saved.TenantId, evt.ProfileTenantId);
        Assert.Equal(saved.Version, evt.Version);
        Assert.Equal("Acme Tax", evt.DisplayName);
    }

    [Fact]
    public async Task Walks_every_page()
    {
        for (var i = 0; i < RepublishOfficeProfilesHandler.PageSize + 3; i++)
            _profiles.Profiles.Add(Saved($"Office {i}"));

        var result = await HandleAsync();

        Assert.Equal(RepublishOfficeProfilesHandler.PageSize + 3, result.Value.Published);
        Assert.Equal(
            RepublishOfficeProfilesHandler.PageSize + 3,
            _bus.Published.OfType<TenantProfileChangedIntegrationEvent>()
                .Select(e => e.ProfileTenantId)
                .Distinct()
                .Count()
        );
    }

    private static OfficeProfile Saved(string displayName)
    {
        var profile = OfficeProfile.CreateEmpty(Guid.NewGuid());
        profile.Update(OfficeProfileDrafts.Empty() with { DisplayName = displayName }, Guid.NewGuid(), DateTime.UtcNow);
        return profile;
    }

    private Task<BuildingBlocks.Results.Result<RepublishOfficeProfilesResult>> HandleAsync() =>
        RepublishOfficeProfilesHandler.Handle(
            new RepublishOfficeProfilesCommand(Guid.NewGuid()),
            _profiles,
            _bus,
            new NoopCorrelationContext(),
            NullLogger<RepublishOfficeProfilesCommand>.Instance,
            CancellationToken.None
        );
}
