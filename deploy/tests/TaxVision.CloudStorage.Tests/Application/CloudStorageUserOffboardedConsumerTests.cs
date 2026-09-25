using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.CloudStorage.Application.Sharing;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>
/// Al retirar (offboard) a un empleado: se revocan los share links ACTIVOS que él creó; los de otros
/// usuarios no se tocan.
/// </summary>
public sealed class CloudStorageUserOffboardedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ShareLink ActiveLink(Guid createdBy) =>
        ShareLink
            .Create(
                Guid.NewGuid(),
                Tenant,
                Guid.NewGuid(),
                ShareResourceType.File,
                ShareVisibility.Public,
                SharePermission.Download,
                null,
                Now.AddDays(7),
                null,
                createdBy,
                Now
            )
            .Value.Link;

    private static Task Run(
        FakeShareLinkRepository shares,
        FakeStorageAuditRepository audit,
        FakeMessageBus bus,
        FakeUnitOfWork unitOfWork,
        Guid leaver
    ) =>
        CloudStorageUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                OffboardedByUserId = Guid.NewGuid(),
                RemovedAtUtc = Now,
            },
            shares,
            audit,
            new FakeSystemClock(Now),
            bus,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<ShareLink>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Active_links_created_by_the_leaver_are_revoked()
    {
        var leaver = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var shares = new FakeShareLinkRepository();
        var audit = new FakeStorageAuditRepository();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var linkA = ActiveLink(leaver);
        var linkB = ActiveLink(leaver);
        var linkOther = ActiveLink(otherUser);
        shares.Seed(linkA);
        shares.Seed(linkB);
        shares.Seed(linkOther);

        await Run(shares, audit, bus, unitOfWork, leaver);

        Assert.Equal(ShareStatus.Revoked, linkA.Status);
        Assert.Equal(ShareStatus.Revoked, linkB.Status);
        Assert.Equal(ShareStatus.Active, linkOther.Status);
        Assert.Equal(2, bus.Published.OfType<ShareLinkRevokedIntegrationEvent>().Count());
        Assert.Equal(2, audit.Logs.Count);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task No_links_created_by_the_leaver_is_a_noop()
    {
        var shares = new FakeShareLinkRepository();
        shares.Seed(ActiveLink(Guid.NewGuid()));
        var audit = new FakeStorageAuditRepository();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await Run(shares, audit, bus, unitOfWork, Guid.NewGuid());

        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Offboarding_impact_counts_active_links_created_by_the_employee()
    {
        var leaver = Guid.NewGuid();
        var shares = new FakeShareLinkRepository();
        shares.Seed(ActiveLink(leaver));
        shares.Seed(ActiveLink(leaver));
        shares.Seed(ActiveLink(Guid.NewGuid()));

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, leaver),
            shares,
            CancellationToken.None
        );

        Assert.Equal(2, result.ActiveShareLinks);
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new NoOpScope();
        }

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
