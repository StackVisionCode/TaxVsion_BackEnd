using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Profiles;
using TaxVision.Tenant.Application.Profiles.Commands;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>PUT tenants/current/profile: guarda y recién después publica el estado completo; sin cambios no publica.</summary>
public sealed class UpdateOfficeProfileHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly InMemoryOfficeProfileRepository _profiles = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly RecordingMessageBus _bus = new();

    [Fact]
    public async Task First_save_creates_the_profile_and_publishes_the_full_public_state()
    {
        var result = await HandleAsync(OfficeProfileDrafts.Full());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Version);
        Assert.False(result.Value.MailingSameAsPhysical);
        Assert.Equal("Llc", result.Value.EntityType);
        Assert.Equal(
            [new BusinessHoursResponse("Monday", "09:00", "18:00"), new("Saturday", "10:00", "14:00")],
            result.Value.BusinessHours
        );
        Assert.Single(_profiles.Profiles);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);

        var evt = Assert.Single(_bus.Published.OfType<TenantProfileChangedIntegrationEvent>());
        Assert.Equal(_tenantId, evt.TenantId);
        Assert.Equal(_tenantId, evt.ProfileTenantId);
        Assert.Equal(1, evt.Version);
        Assert.Equal("Acme Tax Services LLC", evt.LegalName);
        Assert.Equal("Acme Tax", evt.DisplayName);
        Assert.Equal("Llc", evt.EntityType);
        Assert.Equal("12-3456789", evt.Ein);
        Assert.Equal(
            new OfficeAddressData("100 Main St", "Suite 2", "Austin", "TX", "78701-1234", "US"),
            evt.PhysicalAddress
        );
        Assert.Equal(new OfficeAddressData("PO Box 9", null, "Austin", "TX", "78702", "US"), evt.MailingAddress);
        Assert.Equal("es", evt.DefaultLanguage);
        Assert.Equal(new OfficeBusinessHoursData("Monday", "09:00", "18:00"), evt.BusinessHours[0]);
        Assert.Equal("test-correlation-id", evt.CorrelationId);
        Assert.Equal(result.Value.UpdatedAtUtc, evt.UpdatedAtUtc);
    }

    [Fact]
    public async Task Saving_the_same_state_twice_publishes_once()
    {
        await HandleAsync(OfficeProfileDrafts.Full());

        var second = await HandleAsync(OfficeProfileDrafts.Full());

        Assert.True(second.IsSuccess);
        Assert.Equal(1, second.Value.Version);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Single(_bus.Published);
    }

    [Fact]
    public async Task A_real_change_bumps_the_version_in_the_event()
    {
        await HandleAsync(OfficeProfileDrafts.Full());

        await HandleAsync(OfficeProfileDrafts.Full() with { MailingSameAsPhysical = true });

        var events = _bus.Published.OfType<TenantProfileChangedIntegrationEvent>().ToList();
        Assert.Equal([1L, 2L], events.Select(e => e.Version));
        Assert.Null(events[1].MailingAddress);
        Assert.Single(_profiles.Profiles);
    }

    [Fact]
    public async Task An_empty_save_without_a_profile_stores_and_publishes_nothing()
    {
        var result = await HandleAsync(OfficeProfileDrafts.Empty());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Version);
        Assert.Null(result.Value.UpdatedAtUtc);
        Assert.Empty(_profiles.Profiles);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task An_invalid_field_returns_its_code_and_saves_nothing()
    {
        var result = await HandleAsync(OfficeProfileDrafts.Full() with { DisplayName = "IRS Helpers" });

        Assert.Equal("Tenant.Profile.DisplayName", result.Error.Code);
        Assert.Empty(_profiles.Profiles);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_bus.Published);
    }

    private Task<Result<OfficeProfileResponse>> HandleAsync(OfficeProfileDraft draft) =>
        UpdateOfficeProfileHandler.Handle(
            new UpdateOfficeProfileCommand(_tenantId, _actorId, draft),
            _profiles,
            _unitOfWork,
            _bus,
            new NoopCorrelationContext(),
            TimeProvider.System,
            CancellationToken.None
        );
}
