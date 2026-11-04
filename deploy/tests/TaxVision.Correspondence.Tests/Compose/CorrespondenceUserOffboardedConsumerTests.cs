using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Tests.Messages;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

/// <summary>
/// Al retirar (offboard) a un empleado: sus borradores ABIERTOS se reasignan al sucesor, o se
/// descartan si no hay sucesor. Los de otros autores no se tocan.
/// </summary>
public sealed class CorrespondenceUserOffboardedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static Draft OpenDraft(Guid authorUserId) =>
        Draft.CreateNew(Tenant, Guid.NewGuid(), Guid.NewGuid(), authorUserId).Value;

    private static Task Run(
        FakeDraftRepository drafts,
        FakeUnitOfWork unitOfWork,
        Guid leaver,
        Guid? successor,
        FakeConnectorsClient? connectors = null
    ) =>
        CorrespondenceUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                SuccessorUserId = successor,
                RemovedAtUtc = DateTime.UtcNow,
            },
            drafts,
            connectors ?? new FakeConnectorsClient(),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<Draft>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Open_drafts_are_reassigned_to_an_eligible_successor()
    {
        var leaver = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();
        await drafts.AddAsync(OpenDraft(leaver));
        await drafts.AddAsync(OpenDraft(leaver));

        await Run(drafts, unitOfWork, leaver, successor);

        Assert.All(drafts.All, d => Assert.Equal(successor, d.CreatedByUserId));
        Assert.All(drafts.All, d => Assert.Equal(DraftStatus.Draft, d.Status));
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Open_drafts_are_discarded_when_there_is_no_successor()
    {
        var leaver = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();
        await drafts.AddAsync(OpenDraft(leaver));

        await Run(drafts, unitOfWork, leaver, successor: null);

        Assert.All(drafts.All, d => Assert.Equal(DraftStatus.Discarded, d.Status));
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Another_authors_drafts_are_left_untouched()
    {
        var leaver = Guid.NewGuid();
        var otherAuthor = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();
        await drafts.AddAsync(OpenDraft(otherAuthor));

        await Run(drafts, unitOfWork, leaver, successor: Guid.NewGuid());

        Assert.All(drafts.All, d => Assert.Equal(otherAuthor, d.CreatedByUserId));
        Assert.All(drafts.All, d => Assert.Equal(DraftStatus.Draft, d.Status));
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task No_open_drafts_is_a_noop()
    {
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        await Run(drafts, unitOfWork, Guid.NewGuid(), successor: Guid.NewGuid());

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static FakeConnectorsClient Mailboxes(Guid[] personal, Guid[] office) =>
        new()
        {
            VisibleAccountIdsByIncludeOffice = includeOffice =>
                Result.Success<IReadOnlyCollection<Guid>>(includeOffice ? [.. personal, .. office] : personal),
        };

    [Fact]
    public async Task A_draft_from_the_leavers_personal_mailbox_moves_to_the_default_office_mailbox()
    {
        var leaver = Guid.NewGuid();
        var successor = Guid.NewGuid();
        var personal = Guid.NewGuid();
        var defaultOffice = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var fromPersonal = Draft.CreateNew(Tenant, null, personal, leaver).Value;
        var fromOffice = Draft.CreateNew(Tenant, null, defaultOffice, leaver).Value;
        await drafts.AddAsync(fromPersonal);
        await drafts.AddAsync(fromOffice);

        await Run(
            drafts,
            new FakeUnitOfWork(),
            leaver,
            successor,
            Mailboxes([personal], [defaultOffice, Guid.NewGuid()])
        );

        Assert.Equal(defaultOffice, fromPersonal.AccountId);
        Assert.Equal(defaultOffice, fromOffice.AccountId);
        Assert.All(drafts.All, d => Assert.Equal(successor, d.CreatedByUserId));
        Assert.All(drafts.All, d => Assert.Equal(DraftStatus.Draft, d.Status));
    }

    [Fact]
    public async Task A_draft_from_the_leavers_personal_mailbox_is_discarded_without_an_office_mailbox()
    {
        var leaver = Guid.NewGuid();
        var personal = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var draft = Draft.CreateNew(Tenant, null, personal, leaver).Value;
        await drafts.AddAsync(draft);

        await Run(drafts, new FakeUnitOfWork(), leaver, Guid.NewGuid(), Mailboxes([personal], []));

        Assert.Equal(DraftStatus.Discarded, draft.Status);
        Assert.Equal(personal, draft.AccountId);
    }

    [Fact]
    public async Task Connectors_unavailable_fails_closed_without_touching_the_drafts()
    {
        var leaver = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();
        var draft = OpenDraft(leaver);
        await drafts.AddAsync(draft);
        var connectors = new FakeConnectorsClient
        {
            VisibleAccountIdsResponse = Result.Failure<IReadOnlyCollection<Guid>>(
                new Error("ConnectorsClient.Unavailable", "down")
            ),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Run(drafts, unitOfWork, leaver, Guid.NewGuid(), connectors)
        );

        Assert.Equal(leaver, draft.CreatedByUserId);
        Assert.Equal(DraftStatus.Draft, draft.Status);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Offboarding_impact_counts_the_employees_open_drafts()
    {
        var leaver = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        await drafts.AddAsync(OpenDraft(leaver));
        await drafts.AddAsync(OpenDraft(leaver));
        await drafts.AddAsync(OpenDraft(Guid.NewGuid()));

        var result = await OffboardingImpactHandler.Handle(
            new OffboardingImpactQuery(Tenant, leaver),
            drafts,
            CancellationToken.None
        );

        Assert.Equal(2, result.OpenDrafts);
    }
}
