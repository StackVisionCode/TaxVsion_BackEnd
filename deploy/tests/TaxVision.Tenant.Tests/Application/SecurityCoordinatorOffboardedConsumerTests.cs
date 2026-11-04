using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tenant.Application.Compliance.Consumers;
using TaxVision.Tenant.Domain.Compliance;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>D-3: retirar al responsable de seguridad vacía la designación (no pasa al sucesor) y avisa a los admins.</summary>
public sealed class SecurityCoordinatorOffboardedConsumerTests
{
    private static readonly OfficeComplianceDraft EmptyDraft = new(null, null, null, null, null, null);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _coordinatorId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly InMemoryOfficeComplianceRepository _compliances = new();
    private readonly RecordingMessageBus _bus = new();
    private readonly PublishOrderUnitOfWork _unitOfWork;

    public SecurityCoordinatorOffboardedConsumerTests() => _unitOfWork = new PublishOrderUnitOfWork(_bus);

    [Fact]
    public async Task Offboarding_the_coordinator_vacates_it_saves_and_then_notifies()
    {
        var compliance = SeedCoordinator(_coordinatorId);
        var successor = Guid.NewGuid();

        await HandleAsync(Offboarded(_coordinatorId, successor));

        Assert.Null(compliance.SecurityCoordinatorUserId);
        Assert.Equal(_adminId, compliance.UpdatedByUserId);
        Assert.Equal([0], _unitOfWork.PublishedAtSave);
        var evt = Assert.Single(_bus.Published.OfType<OfficeSecurityCoordinatorVacatedIntegrationEvent>());
        Assert.Equal(_tenantId, evt.TenantId);
        Assert.Equal(_tenantId, evt.OfficeTenantId);
        Assert.Equal(_coordinatorId, evt.FormerCoordinatorUserId);
        Assert.Equal(OfficeSecurityCoordinatorVacancyReasons.Offboarded, evt.Reason);
        Assert.Equal(_adminId, evt.VacatedByUserId);
        Assert.Equal(compliance.UpdatedAtUtc, evt.VacatedAtUtc);
        Assert.NotEqual(successor, compliance.SecurityCoordinatorUserId);
    }

    [Fact]
    public async Task A_redelivered_event_neither_saves_nor_notifies_again()
    {
        SeedCoordinator(_coordinatorId);
        var evt = Offboarded(_coordinatorId, null);

        await HandleAsync(evt);
        await HandleAsync(evt);

        Assert.Single(_unitOfWork.PublishedAtSave);
        Assert.Single(_bus.Published);
    }

    [Fact]
    public async Task Offboarding_someone_else_or_an_office_without_compliance_does_nothing()
    {
        var compliance = SeedCoordinator(_coordinatorId);

        await HandleAsync(Offboarded(Guid.NewGuid(), _coordinatorId));
        await HandleAsync(Offboarded(_coordinatorId, null) with { TenantId = Guid.NewGuid() });

        Assert.Equal(_coordinatorId, compliance.SecurityCoordinatorUserId);
        Assert.Empty(_unitOfWork.PublishedAtSave);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task A_conflicting_save_notifies_nothing()
    {
        SeedCoordinator(_coordinatorId);
        _unitOfWork.ConflictOnSave.Add(1);

        await Assert.ThrowsAsync<BuildingBlocks.Results.ConflictException>(() =>
            HandleAsync(Offboarded(_coordinatorId, null))
        );

        Assert.Empty(_bus.Published);
    }

    private OfficeCompliance SeedCoordinator(Guid coordinatorId)
    {
        var compliance = OfficeCompliance.CreateEmpty(_tenantId);
        compliance.Update(
            EmptyDraft with
            {
                SecurityCoordinatorUserId = coordinatorId,
            },
            EfinChange.Keep,
            Guid.NewGuid(),
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)
        );
        _compliances.Compliances.Add(compliance);
        return compliance;
    }

    private UserOffboardedIntegrationEvent Offboarded(Guid userId, Guid? successorId) =>
        new()
        {
            TenantId = _tenantId,
            UserId = userId,
            Email = "leaver@example.com",
            ActorType = "TenantEmployee",
            OffboardedByUserId = _adminId,
            SuccessorUserId = successorId,
            RemovedAtUtc = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc),
            CorrelationId = "offboard-correlation",
        };

    private Task HandleAsync(UserOffboardedIntegrationEvent evt) =>
        SecurityCoordinatorOffboardedConsumer.Handle(
            evt,
            _compliances,
            _unitOfWork,
            _bus,
            new NoopCorrelationContext(),
            NullLogger<OfficeCompliance>.Instance,
            CancellationToken.None
        );
}
