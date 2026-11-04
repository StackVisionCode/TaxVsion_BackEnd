using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Domain.Audit;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.Tests.ClientForms;

// Cambio manual del responsable del enlace y reanudación: misma regla de elegibilidad que al crearlo.
public sealed class UpdateFormIntakeLinkHandlerTests
{
    private static readonly Guid TenantId = FormRequestFixtures.TenantId;
    private static readonly Guid Admin = FormRequestFixtures.UserId;
    private static readonly Guid Leaver = Guid.NewGuid();
    private static readonly Guid NewResponsible = Guid.NewGuid();

    [Fact]
    public async Task Changing_the_responsible_to_an_eligible_user_reassigns_and_audits()
    {
        var fixture = new Fixture(active: [NewResponsible]);

        var result = await fixture.RunAsync(FormIntakeLinkAction.ChangeResponsible, NewResponsible);

        Assert.True(result.IsSuccess);
        Assert.Equal(NewResponsible, fixture.Link.ResponsibleUserId);
        Assert.Equal(NewResponsible, result.Value.Link.ResponsibleUserId);
        Assert.Equal([FormAuditAction.IntakeLinkResponsibleChanged], fixture.Audit.Actions);
    }

    [Fact]
    public async Task An_inactive_user_cannot_become_responsible()
    {
        var fixture = new Fixture(active: []);

        var result = await fixture.RunAsync(FormIntakeLinkAction.ChangeResponsible, NewResponsible);

        Assert.Equal(FormParticipantErrors.IntakeResponsibleUnavailable, result.Error);
        Assert.Equal(Leaver, fixture.Link.ResponsibleUserId);
    }

    [Fact]
    public async Task A_user_who_cannot_run_the_pinned_actions_cannot_become_responsible()
    {
        var fixture = new Fixture(active: [NewResponsible], denied: [NewResponsible]);

        var result = await fixture.RunAsync(FormIntakeLinkAction.ChangeResponsible, NewResponsible);

        Assert.Equal(FormRequestErrors.ResponsibleNotAuthorized, result.Error);
    }

    [Fact]
    public async Task Changing_the_responsible_requires_a_user()
    {
        var fixture = new Fixture(active: [NewResponsible]);

        var result = await fixture.RunAsync(FormIntakeLinkAction.ChangeResponsible, null);

        Assert.Equal(FormParticipantErrors.IntakeLinkInvalid, result.Error);
    }

    [Fact]
    public async Task A_paused_link_cannot_resume_while_its_responsible_is_inactive()
    {
        var fixture = new Fixture(active: [NewResponsible]);
        fixture.Link.Pause();

        var blocked = await fixture.RunAsync(FormIntakeLinkAction.Resume, null);
        await fixture.RunAsync(FormIntakeLinkAction.ChangeResponsible, NewResponsible);
        var resumed = await fixture.RunAsync(FormIntakeLinkAction.Resume, null);

        Assert.Equal(FormParticipantErrors.IntakeResponsibleUnavailable, blocked.Error);
        Assert.True(resumed.IsSuccess);
        Assert.Equal(FormIntakeLinkStatus.Active, fixture.Link.Status);
    }

    private sealed class Fixture(Guid[] active, Guid[]? denied = null)
    {
        private readonly TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion _version =
            FormRequestFixtures.Version();
        private FormIntakeLink? _link;

        public FormIntakeLink Link => _link ??= OffboardingFixtures.Link(TenantId, _version.Id, Leaver);
        public RecordingFormAuditLog Audit { get; } = new();

        public Task<Result<FormIntakeLinkTokenResponse>> RunAsync(
            FormIntakeLinkAction action,
            Guid? responsibleUserId
        ) =>
            UpdateFormIntakeLinkHandler.Handle(
                new UpdateFormIntakeLinkCommand(
                    TenantId,
                    Admin,
                    Link.Id,
                    action,
                    null,
                    Link.RowVersion,
                    DateTime.UtcNow,
                    responsibleUserId
                ),
                new ExtCapabilities(),
                new OffboardingIntakeLinks(Link),
                new MemoryFormTemplateVersionRepository(_version),
                new OffboardingResponsibleUsers(active),
                new OffboardingAuthorizer(denied),
                new ExtSecurity(),
                Audit,
                new RecordingUnitOfWork(),
                CancellationToken.None
            );
    }
}
