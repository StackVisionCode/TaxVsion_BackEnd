using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Offboarding;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>
/// Al retirar al responsable de un enlace público: pasa al sucesor si es elegible; si no, el enlace se pausa
/// y queda en la auditoría. Los enlaces de otros y los revocados no se tocan.
/// </summary>
public sealed class FormIntakeLinksUserOffboardedConsumerTests
{
    private static readonly Guid TenantId = FormRequestFixtures.TenantId;
    private static readonly Guid Leaver = Guid.NewGuid();
    private static readonly Guid Successor = Guid.NewGuid();

    [Fact]
    public async Task With_an_eligible_successor_the_link_is_reassigned_and_stays_active()
    {
        var fixture = new Fixture(active: [Successor]);
        var mine = fixture.AddLink(Leaver);
        var other = fixture.AddLink(Guid.NewGuid());

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, Successor));

        Assert.Equal(Successor, mine.ResponsibleUserId);
        Assert.Equal(FormIntakeLinkStatus.Active, mine.Status);
        Assert.NotEqual(Successor, other.ResponsibleUserId);
        Assert.Equal([FormAuditAction.IntakeLinkResponsibleChanged], fixture.Audit.Actions);
        Assert.Equal(1, fixture.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Without_a_successor_the_link_is_paused_and_flagged()
    {
        var fixture = new Fixture(active: []);
        var mine = fixture.AddLink(Leaver);

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, null));

        Assert.Equal(Leaver, mine.ResponsibleUserId);
        Assert.Equal(FormIntakeLinkStatus.Paused, mine.Status);
        Assert.Equal([FormAuditAction.IntakeLinkPausedResponsibleOffboarded], fixture.Audit.Actions);
    }

    [Fact]
    public async Task A_successor_who_cannot_run_the_pinned_actions_does_not_inherit_the_link()
    {
        var fixture = new Fixture(active: [Successor], denied: [Successor]);
        var mine = fixture.AddLink(Leaver);

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, Successor));

        Assert.Equal(Leaver, mine.ResponsibleUserId);
        Assert.Equal(FormIntakeLinkStatus.Paused, mine.Status);
    }

    [Fact]
    public async Task An_inactive_successor_does_not_inherit_the_link()
    {
        var fixture = new Fixture(active: []);
        var mine = fixture.AddLink(Leaver);

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, Successor));

        Assert.Equal(FormIntakeLinkStatus.Paused, mine.Status);
    }

    [Fact]
    public async Task Redelivery_does_not_flag_an_already_paused_link_again()
    {
        var fixture = new Fixture(active: []);
        fixture.AddLink(Leaver);
        var msg = OffboardingFixtures.Offboarded(TenantId, Leaver, null);

        await fixture.RunAsync(msg);
        await fixture.RunAsync(msg);

        Assert.Single(fixture.Audit.Actions);
    }

    [Fact]
    public async Task Revoked_links_are_left_alone()
    {
        var fixture = new Fixture(active: [Successor]);
        var revoked = fixture.AddLink(Leaver);
        revoked.Revoke();

        await fixture.RunAsync(OffboardingFixtures.Offboarded(TenantId, Leaver, Successor));

        Assert.Equal(Leaver, revoked.ResponsibleUserId);
        Assert.Empty(fixture.Audit.Actions);
        Assert.Equal(0, fixture.UnitOfWork.Saves);
    }

    private sealed class Fixture(Guid[] active, Guid[]? denied = null)
    {
        private readonly TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion _version =
            FormRequestFixtures.Version();

        public OffboardingIntakeLinks Links { get; } = new();
        public RecordingFormAuditLog Audit { get; } = new();
        public RecordingUnitOfWork UnitOfWork { get; } = new();

        public FormIntakeLink AddLink(Guid responsible)
        {
            var link = OffboardingFixtures.Link(TenantId, _version.Id, responsible);
            Links.Add(link);
            return link;
        }

        public Task RunAsync(UserOffboardedIntegrationEvent msg) =>
            FormIntakeLinksUserOffboardedConsumer.Handle(
                msg,
                Links,
                new MemoryFormTemplateVersionRepository(_version),
                new OffboardingResponsibleUsers(active),
                new OffboardingAuthorizer(denied),
                Audit,
                UnitOfWork,
                new NoOpCorrelationContext(),
                NullLogger<FormIntakeLink>.Instance,
                CancellationToken.None
            );
    }
}
