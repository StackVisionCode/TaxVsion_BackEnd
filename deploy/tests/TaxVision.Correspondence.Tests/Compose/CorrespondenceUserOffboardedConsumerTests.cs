using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
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

    private static Task Run(FakeDraftRepository drafts, FakeUnitOfWork unitOfWork, Guid leaver, Guid? successor) =>
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
