using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormActionTestDoubles;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormRequestInboxDecisionTests
{
    [Fact]
    public async Task Link_existing_customer_is_synchronous_and_queues_file_reassignment_after_save()
    {
        var fixture = ExternalWithAttachment();
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await LinkAsync(fixture, unitOfWork, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal(CustomerLinkStatus.Linked, fixture.Request.CustomerLinkStatus);
        Assert.Equal(FormRequestFixtures.CustomerId, fixture.Participant.LinkedCustomerId);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.IsType<ReassignParticipantFilesRequested>(Assert.Single(bus.Published));
    }

    [Fact]
    public async Task Link_existing_customer_returns_conflict_without_mutating_the_request()
    {
        var fixture = ExternalWithAttachment();
        fixture.Requests.OpenConflict = Guid.NewGuid();

        var result = await LinkAsync(fixture, new RecordingUnitOfWork(), new FakeMessageBus());

        Assert.Equal(FormRequestErrors.CustomerHasOpenRequest, result.Error);
        Assert.Equal(CustomerLinkStatus.None, fixture.Request.CustomerLinkStatus);
        Assert.Null(fixture.Participant.LinkedCustomerId);
    }

    [Fact]
    public async Task A_failed_link_save_publishes_nothing()
    {
        var fixture = ExternalWithAttachment();
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<InvalidOperationException>(() => LinkAsync(fixture, new FailingUnitOfWork(), bus));

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Responsible_must_be_active_and_hold_review_permission()
    {
        var request = FormRequestFixtures.CustomerRequest();
        var command = new ReassignFormRequestResponsibleCommand(
            FormRequestFixtures.TenantId,
            FormRequestFixtures.UserId,
            true,
            request.Id,
            Guid.NewGuid(),
            request.RowVersion,
            FormRequestFixtures.Now
        );

        var denied = await ReassignFormRequestResponsibleHandler.Handle(
            command,
            new SingleFormRequestRepository(request),
            new FixedRequestVisibility(true),
            new GrantedResponsibleUsers(),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );
        var allowed = await ReassignFormRequestResponsibleHandler.Handle(
            command,
            new SingleFormRequestRepository(request),
            new FixedRequestVisibility(true),
            new GrantedResponsibleUsers("tasks.form_requests.review"),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            default
        );

        Assert.Equal(FormRequestErrors.ResponsibleNotAuthorized, denied.Error);
        Assert.True(allowed.IsSuccess);
        Assert.Equal(command.UserId, request.ResponsibleUserId);
    }

    [Fact]
    public async Task Archive_uses_its_own_reason_and_never_notifies_the_participant()
    {
        var request = FormRequestFixtures.CustomerRequest();
        var bus = new FakeMessageBus();

        var result = await ArchiveFormRequestHandler.Handle(
            new ArchiveFormRequestCommand(
                FormRequestFixtures.TenantId,
                FormRequestFixtures.UserId,
                true,
                request.Id,
                "Handled elsewhere",
                request.RowVersion,
                FormRequestFixtures.Now
            ),
            new SingleFormRequestRepository(request),
            new FixedRequestVisibility(true),
            null!,
            null!,
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FormCancelReason.Archived, request.CancelReason);
        var integrationEvent =
            Assert.IsType<BuildingBlocks.Messaging.ClientFormsIntegrationEvents.FormRequestCancelledIntegrationEvent>(
                Assert.Single(bus.Published)
            );
        Assert.Equal("archived", integrationEvent.Reason);
        Assert.False(integrationEvent.NotifyClient);
    }

    [Fact]
    public async Task A_failed_archive_save_publishes_nothing()
    {
        var request = FormRequestFixtures.CustomerRequest();
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArchiveFormRequestHandler.Handle(
                new ArchiveFormRequestCommand(
                    FormRequestFixtures.TenantId,
                    FormRequestFixtures.UserId,
                    true,
                    request.Id,
                    null,
                    request.RowVersion,
                    FormRequestFixtures.Now
                ),
                new SingleFormRequestRepository(request),
                new FixedRequestVisibility(true),
                null!,
                null!,
                new RecordingFormAuditLog(),
                new FailingUnitOfWork(),
                bus,
                new TestCorrelationContext(),
                default
            )
        );

        Assert.Empty(bus.Published);
    }

    private static Task<Result<TaxVision.Tasks.ClientForms.Application.Requests.FormRequestResponse>> LinkAsync(
        LinkFixture fixture,
        IUnitOfWork unitOfWork,
        FakeMessageBus bus
    ) =>
        LinkFormRequestCustomerHandler.Handle(
            new LinkFormRequestCustomerCommand(
                TenantId,
                Responsible,
                true,
                fixture.Request.Id,
                FormRequestFixtures.CustomerId,
                fixture.Request.RowVersion,
                Now
            ),
            fixture.Requests,
            new FixedRequestVisibility(true),
            new FixedCustomerReader(),
            new ActionParticipantRepository(fixture.Participant),
            new RecordingFormAuditLog(),
            unitOfWork,
            bus,
            new TestCorrelationContext(),
            default
        );

    private static LinkFixture ExternalWithAttachment()
    {
        var version = Version("[]");
        var participant = Participant();
        var request = ExternalRequest(version, participant);
        AddAttachment(request);
        Submit(request, participant);
        return new(request, participant, new ActionRequestRepository(request));
    }

    private sealed record LinkFixture(
        FormRequest Request,
        TaxVision.Tasks.ClientForms.Domain.Participants.FormParticipant Participant,
        ActionRequestRepository Requests
    );

    private sealed class FailingUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("save failed");
    }
}
