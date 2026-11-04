using BuildingBlocks.Authorization;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Accounts.Consumers;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Accounts;

/// <summary>
/// Al retirar a un empleado, los buzones de OFICINA ligados a él se marcan para revisión y se avisa; nunca
/// se desconectan. Los personales (de él o de otros) y los de oficina ajenos no se tocan.
/// </summary>
public sealed class OfficeMailboxesUserOffboardedConsumerTests
{
    private const string LeaverEmail = "leaver@firm.com";
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly FakeTenantEmailAccountRepository _accounts = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeMessageBus _bus = new();

    private Task Run(Guid leaver, DateTime removedAtUtc) =>
        OfficeMailboxesUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = TenantId,
                UserId = leaver,
                Email = LeaverEmail,
                ActorType = "TenantEmployee",
                RemovedAtUtc = removedAtUtc,
            },
            _accounts,
            _unitOfWork,
            _bus,
            new FakeCorrelationContext(),
            NullLogger<TenantEmailAccount>.Instance,
            CancellationToken.None
        );

    private TenantEmailAccount Seed(string email, Guid createdBy, Guid? owner = null)
    {
        var connectedAt = DateTime.UtcNow.AddDays(-10);
        var account = TenantEmailAccount
            .Create(TenantId, email, ProviderCode.Gmail, createdBy, connectedAt, ownerUserId: owner)
            .Value;
        account.MarkConnected(connectedAt);
        account.Activate(connectedAt);
        _accounts.Accounts.Add(account);
        return account;
    }

    [Fact]
    public async Task Office_mailboxes_linked_to_the_leaver_are_flagged_and_admins_told_without_disconnecting()
    {
        var leaver = Guid.NewGuid();
        var connectedByLeaver = Seed("office@firm.com", createdBy: leaver);
        var leaverAddress = Seed("LEAVER@firm.com", createdBy: Guid.NewGuid());
        var unrelatedOffice = Seed("billing@firm.com", createdBy: Guid.NewGuid());
        var personal = Seed("leaver@gmail.com", createdBy: leaver, owner: leaver);

        await Run(leaver, DateTime.UtcNow);

        Assert.Equal(OfficeMailboxReviewReasons.ConnectedByOffboardedUser, connectedByLeaver.ReviewReason);
        Assert.Equal(OfficeMailboxReviewReasons.AddressOfOffboardedUser, leaverAddress.ReviewReason);
        Assert.False(unrelatedOffice.NeedsReview);
        Assert.False(personal.NeedsReview);
        Assert.Equal(TenantEmailAccountStatus.Active, connectedByLeaver.Status);
        var notices = _bus.Published.OfType<ConnectorsOfficeMailboxReviewRequiredIntegrationEvent>().ToList();
        Assert.Equal(2, notices.Count);
        Assert.All(notices, n => Assert.Equal(ConnectorsPermissions.AccountsWrite, n.AudiencePermission));
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task The_review_is_saved_before_the_notice_is_published()
    {
        // Si el guardado fallara y Wolverine reintentara, publicar antes dejaría salir el aviso dos veces.
        var leaver = Guid.NewGuid();
        Seed("office@firm.com", createdBy: leaver);
        Seed(LeaverEmail, createdBy: Guid.NewGuid());
        var savesSeenAtPublish = new List<int>();
        _bus.OnPublish = _ => savesSeenAtPublish.Add(_unitOfWork.SaveChangesCallCount);

        await Run(leaver, DateTime.UtcNow);

        Assert.Equal([1, 1], savesSeenAtPublish);
    }

    [Fact]
    public async Task Reprocessing_does_not_notify_twice()
    {
        var leaver = Guid.NewGuid();
        Seed("office@firm.com", createdBy: leaver);

        await Run(leaver, DateTime.UtcNow);
        await Run(leaver, DateTime.UtcNow);

        Assert.Single(_bus.Published);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task An_office_mailbox_reconnected_after_the_offboarding_is_left_alone()
    {
        var leaver = Guid.NewGuid();
        var office = Seed("office@firm.com", createdBy: leaver);

        await Run(leaver, DateTime.UtcNow.AddDays(-30));

        Assert.False(office.NeedsReview);
        Assert.Empty(_bus.Published);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Offboarding_impact_counts_office_mailboxes_linked_to_the_person()
    {
        var leaver = Guid.NewGuid();
        Seed("office@firm.com", createdBy: leaver);
        Seed(LeaverEmail, createdBy: Guid.NewGuid());
        Seed("billing@firm.com", createdBy: Guid.NewGuid());
        Seed("leaver@gmail.com", createdBy: leaver, owner: leaver);

        var withEmail = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(TenantId, leaver, LeaverEmail),
            _accounts,
            CancellationToken.None
        );
        var withoutEmail = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(TenantId, leaver),
            _accounts,
            CancellationToken.None
        );

        Assert.Equal(2, withEmail.OfficeMailboxes);
        Assert.Equal(1, withoutEmail.OfficeMailboxes);
    }
}
