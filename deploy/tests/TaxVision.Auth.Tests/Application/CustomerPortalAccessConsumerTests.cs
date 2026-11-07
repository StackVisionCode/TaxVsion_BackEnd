using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Customers.IntegrationEvents;
using TaxVision.Auth.Domain.Sessions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.Application;

/// <summary>CUS-10 (D-POR-1): <see cref="CustomerArchivedConsumer"/> y <see cref="CustomerReactivatedConsumer"/>.</summary>
public sealed class CustomerPortalAccessConsumerTests
{
    private static readonly DateTime T0 = new(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemorySessionRepository _sessions = new();
    private readonly RecordingAccessTokenDenylist _denylist = new();
    private readonly RecordingRevocations _revocations = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();
    private readonly User _portal;

    public CustomerPortalAccessConsumerTests()
    {
        _portal = User.Register(
            _tenantId,
            "Paula",
            "Client",
            "paula@example.test",
            "hash",
            UserActorType.CustomerPortal,
            _customerId
        ).Value;
        _users.Add(_portal);
    }

    [Fact]
    public async Task Archived_DeactivatesAndCutsEverySession()
    {
        var session = OpenSession();

        await ArchivedAsync(T0);

        Assert.False(_portal.IsActive);
        Assert.False(session.IsActive);
        Assert.Equal(CustomerArchivedConsumer.CutoffReason, session.RevokedReason);
        Assert.Equal([session.Id], _denylist.Denied);
        Assert.Equal([session.Id], _revocations.Announced);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        var published = Assert.IsType<UserDeactivatedIntegrationEvent>(Assert.Single(_bus.Published));
        Assert.Equal(_portal.Id, published.UserId);
    }

    [Fact]
    public async Task Archived_Twice_PublishesOnce()
    {
        await ArchivedAsync(T0);
        await ArchivedAsync(T0);

        Assert.Single(_bus.Published);
        Assert.False(_portal.IsActive);
    }

    [Fact]
    public async Task Archived_ForAnotherTenant_TouchesNothing()
    {
        var session = OpenSession();

        await CustomerArchivedConsumer.Handle(
            Archived(T0) with { TenantId = Guid.NewGuid() },
            _users,
            Cutoff(),
            new FakeAuthAuditWriter(),
            new FakeCorrelationContext(),
            _unitOfWork,
            _bus,
            CancellationToken.None
        );

        Assert.True(_portal.IsActive);
        Assert.True(session.IsActive);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task Restored_GivesBackWhatTheArchiveCut()
    {
        await ArchivedAsync(T0);

        await RestoredAsync(T0.AddMinutes(1));

        Assert.True(_portal.IsActive);
        Assert.IsType<UserReactivatedIntegrationEvent>(_bus.Published[^1]);
    }

    [Fact]
    public async Task Restored_KeepsAnAccountTheOfficeDeactivated()
    {
        _portal.Deactivate(Guid.NewGuid(), T0);

        await ArchivedAsync(T0.AddMinutes(1));
        await RestoredAsync(T0.AddMinutes(2));

        Assert.False(_portal.IsActive);
        Assert.Empty(_bus.Published);
    }

    [Fact]
    public async Task LateArchive_AfterTheRestore_DoesNotCutAccess()
    {
        var session = OpenSession();

        await RestoredAsync(T0.AddMinutes(1));
        await ArchivedAsync(T0);

        Assert.True(_portal.IsActive);
        Assert.True(session.IsActive);
        Assert.Empty(_denylist.Denied);
        Assert.Empty(_bus.Published);
    }

    private UserSession OpenSession()
    {
        var session = UserSession.Start(_tenantId, _portal.Id, "phone", "xunit", "127.0.0.1");
        _sessions.Sessions.Add(session);
        return session;
    }

    private CustomerArchivedIntegrationEvent Archived(DateTime at) =>
        new()
        {
            TenantId = _tenantId,
            OccurredOn = at,
            CustomerId = _customerId,
            ArchivedByUserId = Guid.NewGuid(),
        };

    private Task ArchivedAsync(DateTime at) =>
        CustomerArchivedConsumer.Handle(
            Archived(at),
            _users,
            Cutoff(),
            new FakeAuthAuditWriter(),
            new FakeCorrelationContext(),
            _unitOfWork,
            _bus,
            CancellationToken.None
        );

    private Task RestoredAsync(DateTime at) =>
        CustomerReactivatedConsumer.Handle(
            new CustomerReactivatedIntegrationEvent
            {
                TenantId = _tenantId,
                OccurredOn = at,
                CustomerId = _customerId,
                ReactivatedByUserId = Guid.NewGuid(),
                ReactivatedAtUtc = at,
            },
            _users,
            new FakeAuthAuditWriter(),
            new FakeCorrelationContext(),
            _unitOfWork,
            _bus,
            CancellationToken.None
        );

    private SessionAccessCutoff Cutoff() => new(_sessions, _denylist, _revocations);

    private sealed class RecordingRevocations : ISessionRevocationPublisher
    {
        public List<Guid> Announced { get; } = [];

        public Task PublishRevokedAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            string reason,
            CancellationToken ct = default
        )
        {
            Announced.Add(sessionId);
            return Task.CompletedTask;
        }
    }
}
