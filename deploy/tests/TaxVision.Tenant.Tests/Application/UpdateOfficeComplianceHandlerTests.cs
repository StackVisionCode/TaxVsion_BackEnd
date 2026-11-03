using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Compliance;
using TaxVision.Tenant.Application.Compliance.Commands;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Infrastructure.Security;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>
/// PUT tenants/current/compliance: EFIN cifrado y enmascarado, responsable del mismo tenant; solo sale el
/// cambio de años de retención, después de guardar.
/// </summary>
public sealed class UpdateOfficeComplianceHandlerTests
{
    private static readonly OfficeComplianceDraft EmptyDraft = new(null, null, null, null, null, null);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly InMemoryOfficeComplianceRepository _compliances = new();
    private readonly InMemoryUserPermissionsProjectionRepository _users = new();
    private readonly EfinProtector _protector = TestEfinProtectors.Create(out _);
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly RecordingMessageBus _bus = new();

    [Fact]
    public async Task A_new_efin_is_stored_encrypted_and_only_its_mask_is_returned()
    {
        var result = await HandleAsync("123456", EmptyDraft);

        Assert.True(result.Value.HasEfin);
        Assert.Equal("••••56", result.Value.EfinMasked);
        var stored = Assert.Single(_compliances.Compliances).Efin!;
        Assert.NotEqual("123456"u8.ToArray(), stored.Ciphertext);
        Assert.Equal("123456", _protector.Reveal(stored));
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Sending_the_same_efin_again_is_not_a_change()
    {
        await HandleAsync("123456", EmptyDraft);
        var firstCipher = _compliances.Compliances[0].Efin!.Ciphertext;

        var result = await HandleAsync("123456", EmptyDraft);

        Assert.True(result.IsSuccess);
        Assert.Same(firstCipher, _compliances.Compliances[0].Efin!.Ciphertext);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Null_keeps_the_efin_and_empty_clears_it()
    {
        await HandleAsync("123456", EmptyDraft);

        var kept = await HandleAsync(null, EmptyDraft with { RetentionYears = 5 });
        Assert.True(kept.Value.HasEfin);
        Assert.Equal(5, kept.Value.RetentionYears);

        var cleared = await HandleAsync("", EmptyDraft with { RetentionYears = 5 });
        Assert.False(cleared.Value.HasEfin);
        Assert.Null(cleared.Value.EfinMasked);
        Assert.Equal(3, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_malformed_efin_is_rejected_before_anything_changes()
    {
        var result = await HandleAsync("12345", EmptyDraft with { RetentionYears = 5 });

        Assert.Equal("Tenant.Compliance.Efin", result.Error.Code);
        Assert.Empty(_compliances.Compliances);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task The_security_coordinator_must_be_an_active_user_of_the_same_office()
    {
        var otherOfficeUser = Guid.NewGuid();
        _users.Seed(Guid.NewGuid(), otherOfficeUser);
        var ownUser = Guid.NewGuid();
        _users.Seed(_tenantId, ownUser);

        var foreign = await HandleAsync(null, EmptyDraft with { SecurityCoordinatorUserId = otherOfficeUser });
        var own = await HandleAsync(null, EmptyDraft with { SecurityCoordinatorUserId = ownUser });

        Assert.Equal(OfficeComplianceErrors.SecurityCoordinator, foreign.Error);
        Assert.Equal(ownUser, own.Value.SecurityCoordinatorUserId);
    }

    [Fact]
    public async Task An_unchanged_empty_save_stores_nothing()
    {
        var result = await HandleAsync(null, EmptyDraft);

        Assert.False(result.Value.HasEfin);
        Assert.Null(result.Value.UpdatedAtUtc);
        Assert.Empty(_compliances.Compliances);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Changing_the_retention_publishes_it_after_saving()
    {
        var bus = new RecordingMessageBus();
        var unitOfWork = new PublishOrderUnitOfWork(bus);

        var result = await UpdateOfficeComplianceHandler.Handle(
            new UpdateOfficeComplianceCommand(_tenantId, _actorId, null, EmptyDraft with { RetentionYears = 15 }),
            _compliances,
            _users,
            _protector,
            unitOfWork,
            bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal([0], unitOfWork.PublishedAtSave);
        var evt = Assert.Single(bus.Published.OfType<TenantRecordRetentionChangedIntegrationEvent>());
        Assert.Equal(_tenantId, evt.OfficeTenantId);
        Assert.Equal(_tenantId, evt.TenantId);
        Assert.Equal(15, evt.RetentionYears);
        Assert.Equal(result.Value.UpdatedAtUtc, evt.UpdatedAtUtc);
        Assert.Equal("test-correlation-id", evt.CorrelationId);
    }

    [Fact]
    public async Task Clearing_the_retention_publishes_null()
    {
        await HandleAsync(null, EmptyDraft with { RetentionYears = 7 });

        await HandleAsync(null, EmptyDraft);

        var events = _bus.Published.OfType<TenantRecordRetentionChangedIntegrationEvent>().ToList();
        Assert.Equal([7, null], events.Select(e => e.RetentionYears));
    }

    [Fact]
    public async Task Other_changes_or_the_same_retention_publish_nothing()
    {
        await HandleAsync(null, EmptyDraft with { RetentionYears = 7 });
        _bus.Published.Clear();

        await HandleAsync("123456", EmptyDraft with { RetentionYears = 7, WispAdoptedOn = "2025-01-15" });
        await HandleAsync(null, EmptyDraft with { RetentionYears = 7, WispAdoptedOn = "2025-01-15" });

        Assert.Equal(2, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_first_save_without_retention_publishes_nothing()
    {
        await HandleAsync("123456", EmptyDraft);

        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_rejected_save_publishes_nothing()
    {
        var result = await HandleAsync(null, EmptyDraft with { RetentionYears = 21 });

        Assert.Equal("Tenant.Compliance.RetentionYears", result.Error.Code);
        Assert.Empty(_bus.Published);
    }

    private Task<Result<OfficeComplianceResponse>> HandleAsync(string? efin, OfficeComplianceDraft draft) =>
        UpdateOfficeComplianceHandler.Handle(
            new UpdateOfficeComplianceCommand(_tenantId, _actorId, efin, draft),
            _compliances,
            _users,
            _protector,
            _unitOfWork,
            _bus,
            new NoopCorrelationContext(),
            CancellationToken.None
        );
}
