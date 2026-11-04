using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Providers;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Providers;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Al retirar a un empleado, las conexiones de nube de la oficina ligadas a él se marcan para revisión y se
/// avisa (nunca se revocan); las demás no se tocan.
/// </summary>
public sealed class CloudConnectionsUserOffboardedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private const string LeaverEmail = "leaver@firm.com";

    private static Task Run(
        FakeTenantCloudConnectionRepository connections,
        FakeMessageBus bus,
        FakeUnitOfWork unitOfWork,
        Guid leaver,
        DateTime removedAtUtc
    ) =>
        CloudConnectionsUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = leaver,
                Email = LeaverEmail,
                ActorType = "TenantEmployee",
                OffboardedByUserId = Guid.NewGuid(),
                RemovedAtUtc = removedAtUtc,
            },
            connections,
            new FakeSystemClock(DateTime.UtcNow),
            bus,
            unitOfWork,
            new NoOpCorrelationContext(),
            NullLogger<TenantCloudConnection>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Connections_of_the_leaver_are_flagged_and_admins_are_told_without_revoking()
    {
        var leaver = Guid.NewGuid();
        var byLeaver = TenantCloudConnection.Connect(Tenant, "dropbox", "office@firm.com", "token-a", leaver);
        var leaverAccount = TenantCloudConnection.Connect(Tenant, "google-drive", "LEAVER@firm.com", "token-b");
        var unrelated = TenantCloudConnection.Connect(Tenant, "onedrive", "office@firm.com", "token-c", Guid.NewGuid());
        var connections = new FakeTenantCloudConnectionRepository(byLeaver, leaverAccount, unrelated);
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await Run(connections, bus, unitOfWork, leaver, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(CloudConnectionReviewReasons.ConnectedByOffboardedUser, byLeaver.ReviewReason);
        Assert.Equal(CloudConnectionReviewReasons.AccountOfOffboardedUser, leaverAccount.ReviewReason);
        Assert.False(unrelated.NeedsReview);
        Assert.Equal("token-a", byLeaver.ProtectedRefreshToken);
        var notices = bus.Published.OfType<CloudConnectionReviewRequiredIntegrationEvent>().ToList();
        Assert.Equal(2, notices.Count);
        Assert.All(notices, n => Assert.Equal(CloudStoragePermissions.SettingsManage, n.AudiencePermission));
        Assert.All(notices, n => Assert.Equal(leaver, n.OffboardedUserId));
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Reprocessing_does_not_notify_twice()
    {
        var leaver = Guid.NewGuid();
        var connection = TenantCloudConnection.Connect(Tenant, "dropbox", "office@firm.com", "x", leaver);
        var connections = new FakeTenantCloudConnectionRepository(connection);
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var removedAt = DateTime.UtcNow.AddMinutes(1);

        await Run(connections, bus, unitOfWork, leaver, removedAt);
        await Run(connections, bus, unitOfWork, leaver, removedAt);

        Assert.Single(bus.Published.OfType<CloudConnectionReviewRequiredIntegrationEvent>());
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_connection_reconnected_by_someone_else_after_the_offboarding_is_left_alone()
    {
        var leaver = Guid.NewGuid();
        var connection = TenantCloudConnection.Connect(Tenant, "dropbox", LeaverEmail, "x", Guid.NewGuid());
        var connections = new FakeTenantCloudConnectionRepository(connection);
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await Run(connections, bus, unitOfWork, leaver, DateTime.UtcNow.AddDays(-1));

        Assert.False(connection.NeedsReview);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task A_connection_the_leaver_completed_after_the_offboarding_is_flagged()
    {
        var leaver = Guid.NewGuid();
        var connection = TenantCloudConnection.Connect(Tenant, "dropbox", "office@firm.com", "x", leaver);
        var connections = new FakeTenantCloudConnectionRepository(connection);
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await Run(connections, bus, unitOfWork, leaver, DateTime.UtcNow.AddDays(-1));

        Assert.Equal(CloudConnectionReviewReasons.ConnectedByOffboardedUser, connection.ReviewReason);
        Assert.Single(bus.Published.OfType<CloudConnectionReviewRequiredIntegrationEvent>());
    }

    [Fact]
    public async Task Pending_connect_attempts_of_the_leaver_are_discarded_and_saved_before_notifying()
    {
        var leaver = Guid.NewGuid();
        var other = Guid.NewGuid();
        var connection = TenantCloudConnection.Connect(Tenant, "dropbox", "office@firm.com", "x", leaver);
        var connections = new FakeTenantCloudConnectionRepository(connection);
        connections.States.Add(CloudProviderOAuthState.Create(Tenant, leaver, "google-drive", null));
        connections.States.Add(CloudProviderOAuthState.Create(Tenant, other, "onedrive", null));
        var unitOfWork = new FakeUnitOfWork();
        var savesWhenPublished = new List<int>();
        var bus = new FakeMessageBus { OnPublish = _ => savesWhenPublished.Add(unitOfWork.SaveChangesCallCount) };

        await Run(connections, bus, unitOfWork, leaver, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(other, Assert.Single(connections.States).InitiatedByUserId);
        Assert.Equal([1], savesWhenPublished);
    }

    [Fact]
    public async Task Only_pending_connect_attempts_still_save()
    {
        var leaver = Guid.NewGuid();
        var connections = new FakeTenantCloudConnectionRepository();
        connections.States.Add(CloudProviderOAuthState.Create(Tenant, leaver, "dropbox", null));
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await Run(connections, bus, unitOfWork, leaver, DateTime.UtcNow.AddMinutes(1));

        Assert.Empty(connections.States);
        Assert.Empty(bus.Published);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Offboarding_impact_counts_office_connections_linked_to_the_person()
    {
        var leaver = Guid.NewGuid();
        var connections = new FakeTenantCloudConnectionRepository(
            TenantCloudConnection.Connect(Tenant, "dropbox", "office@firm.com", "x", leaver),
            TenantCloudConnection.Connect(Tenant, "google-drive", LeaverEmail, "y"),
            TenantCloudConnection.Connect(Tenant, "onedrive", "office@firm.com", "z", Guid.NewGuid())
        );

        var withEmail = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, leaver, LeaverEmail),
            new FakeShareLinkRepository(),
            connections,
            CancellationToken.None
        );
        var withoutEmail = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, leaver),
            new FakeShareLinkRepository(),
            connections,
            CancellationToken.None
        );

        Assert.Equal(2, withEmail.OfficeCloudConnections);
        Assert.Equal(1, withoutEmail.OfficeCloudConnections);
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}

/// <summary>Conexiones de nube en memoria, filtradas por tenant como el repo real.</summary>
internal sealed class FakeTenantCloudConnectionRepository(params TenantCloudConnection[] connections)
    : ITenantCloudConnectionRepository
{
    public Task<IReadOnlyList<TenantCloudConnection>> ListByTenantAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<TenantCloudConnection>>(
            connections.Where(connection => connection.TenantId == tenantId).ToList()
        );

    /// <summary>State OAuth sin consumir.</summary>
    public List<CloudProviderOAuthState> States { get; } = [];

    public Task<int> DiscardOAuthStatesInitiatedByAsync(Guid tenantId, Guid userId, CancellationToken ct) =>
        Task.FromResult(States.RemoveAll(state => state.TenantId == tenantId && state.InitiatedByUserId == userId));
}
