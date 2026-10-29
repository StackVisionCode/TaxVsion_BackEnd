using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Actions.Commands;
using TaxVision.Tasks.ClientForms.Application.Actions.Consumers;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using TaxVision.Tasks.Tests.Dependencies;
using static TaxVision.Tasks.Tests.ClientForms.FormActionTestDoubles;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormActionHandlerTests
{
    private static readonly Guid CustomerId = Guid.Parse("30000000-0000-0000-0000-0000000000b7");

    [Fact]
    // T-8: el run se guarda primero y su comando y el hecho del envío se publican después del SaveChanges
    // (quedan en el outbox de Wolverine, que los confirma al cerrar el handler).
    public async Task Submit_saves_the_run_before_publishing_its_command()
    {
        var fixture = Fixture(submitted: false);
        var (bus, outbox) = OutboxMessageBus.Create();
        var metrics = new RecordingClientFormsMetrics();

        var result = await SubmitAsync(fixture, bus, new OutboxUnitOfWork(outbox), metrics);

        Assert.True(result.Value.Submitted);
        Assert.Single(fixture.Request.ActionRuns);
        Assert.Empty(outbox.Flushed);
        Assert.Contains(outbox.Pending, message => message is CustomerCreationFromFormRequestedIntegrationEvent);
        Assert.Contains(outbox.Pending, message => message is FormRequestSubmittedIntegrationEvent);
        Assert.Equal(["submitted:False:True", "action:CreateCustomer:Requested"], metrics.Recorded);
    }

    [Fact]
    public async Task A_failed_submit_commit_emits_no_command()
    {
        var fixture = Fixture(submitted: false);
        var (bus, outbox) = OutboxMessageBus.Create();
        var metrics = new RecordingClientFormsMetrics();

        await Assert.ThrowsAsync<ConflictException>(() =>
            SubmitAsync(
                fixture,
                bus,
                new OutboxUnitOfWork(outbox, new ConflictException("Persistence.UniqueConstraint", "x")),
                metrics
            )
        );

        Assert.Empty(outbox.Flushed);
        Assert.Empty(metrics.Recorded);
    }

    // T-5: reintento manual después de Completed.
    [Fact]
    public async Task Retry_after_completed_is_409_and_emits_nothing()
    {
        var fixture = Fixture();
        var run = Planned(fixture);
        fixture.Request.CompleteAction(run.CommandId!.Value, "Customer", CustomerId, null, Now);
        var bus = new FakeMessageBus();

        var result = await RetryAsync(fixture, run.Id, bus);

        Assert.Equal("FormActionRun.NotRetryable", result.Error.Code);
        Assert.Equal(409, BuildingBlocks.Web.Results.ErrorHttpMapping.ToHttpStatusCode(result.Error));
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Retry_while_requested_is_409()
    {
        var fixture = Fixture();
        var run = Planned(fixture);
        var bus = new FakeMessageBus();

        var result = await RetryAsync(fixture, run.Id, bus);

        Assert.Equal("FormActionRun.NotRetryable", result.Error.Code);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Retry_after_a_correctable_rejection_emits_a_new_command_for_the_same_operation()
    {
        var fixture = Fixture();
        var run = Planned(fixture);
        fixture.Request.RejectAction(run.CommandId!.Value, "Customer.CreatorNotEligible", null, null, Now);
        var bus = new FakeMessageBus();

        var result = await RetryAsync(fixture, run.Id, bus);

        var command = Assert.IsType<CustomerCreationFromFormRequestedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(2, result.Value.Attempt);
        Assert.NotEqual(run.CommandId, command.RequestId);
        Assert.Equal(run.IdempotencyKey, command.IdempotencyKey);
        Assert.Equal(fixture.Request.Id, command.SourceReferenceId);
    }

    // T-8: la segunda llamada concurrente ve el intento ya reemplazado; en SQL la frena el índice único.
    [Fact]
    public async Task Two_retries_of_the_same_run_emit_once()
    {
        var fixture = Fixture();
        var run = Planned(fixture);
        fixture.Request.RejectAction(run.CommandId!.Value, "Customer.CreatorNotInDirectory", null, null, Now);
        var bus = new FakeMessageBus();

        var first = await RetryAsync(fixture, run.Id, bus);
        var second = await RetryAsync(fixture, run.Id, bus);

        Assert.True(first.IsSuccess);
        Assert.Equal("FormActionRun.NotRetryable", second.Error.Code);
        Assert.Single(bus.Published);
    }

    [Fact]
    public async Task Manual_start_requests_creation_when_the_action_is_manual()
    {
        var fixture = Fixture(
            actions: """[{"key":"create_client","type":"createCustomer","trigger":"manual","config":{"kind":{"constant":"Individual"},"language":{"mapped":true}}}]"""
        );
        var bus = new FakeMessageBus();

        var result = await StartFormCustomerCreationHandler.Handle(
            new StartFormCustomerCreationCommand(TenantId, Responsible, true, fixture.Request.Id, Now),
            fixture.Requests,
            new FixedRequestVisibility(true),
            fixture.Planner,
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            bus,
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.Equal(FormActionRunStatus.Requested, result.Value.Status);
        Assert.IsType<CustomerCreationFromFormRequestedIntegrationEvent>(Assert.Single(bus.Published));
    }

    [Fact]
    public async Task Completed_links_the_request_and_participant_and_queues_the_file_reassignment()
    {
        var fixture = Fixture(withAttachment: true);
        var run = Planned(fixture);
        var bus = new FakeMessageBus();
        var metrics = new RecordingClientFormsMetrics();

        await CompletedAsync(fixture, run, bus, metrics: metrics);

        Assert.Equal(["action:CreateCustomer:Completed"], metrics.Recorded);
        Assert.Equal(FormActionRunStatus.Completed, run.Status);
        Assert.Equal(CustomerId, run.ResultRefId);
        Assert.Equal(CustomerLinkStatus.Linked, fixture.Request.CustomerLinkStatus);
        Assert.Equal(CustomerId, fixture.Request.EffectiveCustomerId);
        Assert.Equal(CustomerId, fixture.Participant.LinkedCustomerId);
        var reassign = Assert.IsType<ReassignParticipantFilesRequested>(Assert.Single(bus.Published));
        Assert.Equal(fixture.Request.Id, reassign.FormRequestId);
    }

    [Fact]
    public async Task A_repeated_completed_changes_nothing()
    {
        var fixture = Fixture(withAttachment: true);
        var run = Planned(fixture);
        await CompletedAsync(fixture, run, new FakeMessageBus());
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork();
        var metrics = new RecordingClientFormsMetrics();

        await CompletedAsync(fixture, run, bus, unitOfWork, metrics);

        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(metrics.Recorded);
    }

    // T-11: cliente creado con la vinculación en conflicto; después se libera y se reintenta el vínculo.
    [Fact]
    public async Task A_link_conflict_blocks_without_losing_the_customer_and_retry_link_resolves_it()
    {
        var fixture = Fixture(withAttachment: true);
        var run = Planned(fixture);
        var conflicting = Guid.NewGuid();
        fixture.Requests.OpenConflict = conflicting;
        var bus = new FakeMessageBus();

        await CompletedAsync(fixture, run, bus);

        Assert.Equal(FormActionRunStatus.Completed, run.Status);
        Assert.Equal(CustomerId, fixture.Participant.LinkedCustomerId);
        Assert.Equal(CustomerLinkStatus.Blocked, fixture.Request.CustomerLinkStatus);
        Assert.Equal(conflicting, fixture.Request.LinkConflictingRequestId);
        var warning = Assert.IsType<FormActionFailedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(FormActionNames.LinkBlockedCode, warning.ErrorCode);
        Assert.DoesNotContain(bus.Published, message => message is CustomerCreationFromFormRequestedIntegrationEvent);

        var replay = new FakeMessageBus();
        await CompletedAsync(fixture, run, replay);
        Assert.Empty(replay.Published);
        Assert.Equal(CustomerLinkStatus.Blocked, fixture.Request.CustomerLinkStatus);

        var stillBlocked = await RetryLinkAsync(fixture, new FakeMessageBus());
        Assert.Equal("FormRequest.CustomerHasOpenRequest", stillBlocked.Error.Code);
        Assert.Equal(409, BuildingBlocks.Web.Results.ErrorHttpMapping.ToHttpStatusCode(stillBlocked.Error));

        fixture.Requests.OpenConflict = null;
        var retryBus = new FakeMessageBus();
        var linked = await RetryLinkAsync(fixture, retryBus);
        Assert.Equal(CustomerLinkStatus.Linked, linked.Value.CustomerLinkStatus);
        Assert.Equal(CustomerId, fixture.Request.CustomerId);
        Assert.IsType<ReassignParticipantFilesRequested>(Assert.Single(retryBus.Published));

        var again = new FakeMessageBus();
        Assert.True((await RetryLinkAsync(fixture, again)).IsSuccess);
        Assert.Empty(again.Published);
    }

    [Fact]
    public async Task A_duplicate_rejection_keeps_the_existing_customer_and_warns_the_responsible()
    {
        var fixture = Fixture();
        var run = Planned(fixture);
        var existing = Guid.NewGuid();
        var bus = new FakeMessageBus();

        await RejectedAsync(fixture, run, bus, FormActionRunErrors.DuplicateFoundCode, existing, "Email");

        Assert.Equal(FormActionRunStatus.Rejected, run.Status);
        Assert.Equal(existing, run.ExistingCustomerId);
        Assert.Equal("Email", run.ErrorDetail);
        Assert.False(run.IsRetryable);
        Assert.Equal(CustomerLinkStatus.None, fixture.Request.CustomerLinkStatus);
        var warning = Assert.IsType<FormActionFailedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(FormActionRunErrors.DuplicateFoundCode, warning.ErrorCode);
    }

    [Fact]
    public async Task A_rejection_after_completion_is_ignored()
    {
        var fixture = Fixture();
        var run = Planned(fixture);
        await CompletedAsync(fixture, run, new FakeMessageBus());
        var bus = new FakeMessageBus();

        await RejectedAsync(fixture, run, bus, FormActionRunErrors.DuplicateFoundCode, CustomerId, "Email");

        Assert.Equal(FormActionRunStatus.Completed, run.Status);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task A_result_from_another_tenant_is_ignored()
    {
        var fixture = Fixture();
        var run = Planned(fixture);
        var bus = new FakeMessageBus();

        await CustomerCreationFromFormCompletedConsumer.Handle(
            Completed(run, fixture.Request) with
            {
                TenantId = Guid.NewGuid(),
            },
            fixture.Requests,
            new ActionParticipantRepository(fixture.Participant),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            NullLogger<CustomerCreationFromFormCompletedIntegrationEvent>.Instance,
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.Equal(FormActionRunStatus.Requested, run.Status);
    }

    [Fact]
    public async Task File_reassignment_records_the_outcome_per_file()
    {
        var fixture = Fixture(withAttachment: true);
        await CompletedAsync(fixture, Planned(fixture), new FakeMessageBus());
        var client = new RecordingReassignmentClient();
        var bus = new FakeMessageBus();

        await ReassignAsync(fixture, client, bus);

        var call = Assert.Single(client.Calls);
        Assert.Equal(fixture.Participant.Id, call.ParticipantId);
        Assert.Equal(CustomerId, call.CustomerId);
        Assert.Equal(
            FormFileReassignmentOutcomes.Reassigned,
            fixture.Request.Submission!.Attachments.Single().OwnerReassignment
        );
        Assert.Empty(fixture.Request.FilesPendingOwnerReassignment());
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task An_owner_mismatch_is_recorded_and_warned_without_retrying()
    {
        var fixture = Fixture(withAttachment: true);
        await CompletedAsync(fixture, Planned(fixture), new FakeMessageBus());
        var bus = new FakeMessageBus();

        await ReassignAsync(
            fixture,
            new RecordingReassignmentClient(_ => FormFileReassignmentOutcomes.OwnerMismatch),
            bus
        );

        var warning = Assert.IsType<FormActionFailedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(FormActionNames.FilesNotReassignedCode, warning.ErrorCode);
        Assert.Equal(
            FormFileReassignmentOutcomes.OwnerMismatch,
            fixture.Request.Submission!.Attachments.Single().OwnerReassignment
        );
    }

    [Fact]
    public async Task An_unavailable_storage_throws_so_the_bus_retries()
    {
        var fixture = Fixture(withAttachment: true);
        await CompletedAsync(fixture, Planned(fixture), new FakeMessageBus());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReassignAsync(fixture, new RecordingReassignmentClient { Unavailable = true }, new FakeMessageBus())
        );
    }

    [Fact]
    public async Task The_responsible_must_hold_the_permissions_of_each_action()
    {
        var authorizer = new FormActionAuthorizer(
            FormTemplateHandlerTestDoubles.Engine(),
            FormTemplateHandlerTestDoubles.TargetCatalog(),
            new GrantedResponsibleUsers("tasks.form_requests.review")
        );
        var allowed = new FormActionAuthorizer(
            FormTemplateHandlerTestDoubles.Engine(),
            FormTemplateHandlerTestDoubles.TargetCatalog(),
            new GrantedResponsibleUsers("customers.manage")
        );

        var denied = await authorizer.EnsureResponsibleCanRunAsync(TenantId, Responsible, Version());

        Assert.Equal("FormRequest.ResponsibleNotAuthorized", denied.Error.Code);
        Assert.Equal(422, BuildingBlocks.Web.Results.ErrorHttpMapping.ToHttpStatusCode(denied.Error));
        Assert.True((await allowed.EnsureResponsibleCanRunAsync(TenantId, Responsible, Version())).IsSuccess);
        Assert.True((await authorizer.EnsureResponsibleCanRunAsync(TenantId, Responsible, Version("[]"))).IsSuccess);
    }

    private static Task<Result<FormSubmitOutcome>> SubmitAsync(
        ActionFixture fixture,
        Wolverine.IMessageBus bus,
        BuildingBlocks.Persistence.IUnitOfWork unitOfWork,
        RecordingClientFormsMetrics? metrics = null
    ) =>
        SubmitPortalFormRequestHandler.Handle(
            new SubmitPortalFormRequestCommand(
                ParticipantContext.External(TenantId, fixture.Participant.Id, fixture.Request.Id, Guid.NewGuid()),
                fixture.Request.Id,
                fixture.Request.RowVersion,
                Now
            ),
            fixture.Requests,
            fixture.Versions,
            FormTemplateHandlerTestDoubles.Engine(),
            new PlainFormAnswersProtector(),
            null!,
            null!,
            new FormConsentGate(new MemoryConsentNoticeRepository(), new MemoryFormConsentRecordRepository()),
            new MemoryFormConsentRecordRepository(),
            fixture.Planner,
            Options.Create(new ClientFormsOptions()),
            new RecordingFormAuditLog(),
            unitOfWork,
            bus,
            new TestCorrelationContext(),
            metrics ?? new RecordingClientFormsMetrics(),
            default
        );

    private static Task<Result<FormActionRunResponse>> RetryAsync(
        ActionFixture fixture,
        Guid runId,
        FakeMessageBus bus
    ) =>
        RetryFormActionRunHandler.Handle(
            new RetryFormActionRunCommand(TenantId, Responsible, true, fixture.Request.Id, runId, Now),
            fixture.Requests,
            new FixedRequestVisibility(true),
            fixture.Planner,
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            bus,
            new RecordingClientFormsMetrics(),
            default
        );

    private static Task<Result<FormRequestResponse>> RetryLinkAsync(ActionFixture fixture, FakeMessageBus bus) =>
        RetryFormCustomerLinkHandler.Handle(
            new RetryFormCustomerLinkCommand(TenantId, Responsible, true, fixture.Request.Id, Now),
            fixture.Requests,
            new FixedRequestVisibility(true),
            new ActionParticipantRepository(fixture.Participant),
            new RecordingFormAuditLog(),
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            NullLogger<RetryFormCustomerLinkCommand>.Instance,
            default
        );

    private static Task CompletedAsync(
        ActionFixture fixture,
        FormActionRun run,
        FakeMessageBus bus,
        RecordingUnitOfWork? unitOfWork = null,
        RecordingClientFormsMetrics? metrics = null
    ) =>
        CustomerCreationFromFormCompletedConsumer.Handle(
            Completed(run, fixture.Request),
            fixture.Requests,
            new ActionParticipantRepository(fixture.Participant),
            new RecordingFormAuditLog(),
            unitOfWork ?? new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            NullLogger<CustomerCreationFromFormCompletedIntegrationEvent>.Instance,
            metrics ?? new RecordingClientFormsMetrics(),
            default
        );

    private static Task RejectedAsync(
        ActionFixture fixture,
        FormActionRun run,
        FakeMessageBus bus,
        string code,
        Guid? existing,
        string? matchedBy
    ) =>
        CustomerCreationFromFormRejectedConsumer.Handle(
            new CustomerCreationFromFormRejectedIntegrationEvent
            {
                TenantId = TenantId,
                RequestId = run.CommandId!.Value,
                SourceReferenceId = fixture.Request.Id,
                ErrorCode = code,
                ExistingCustomerId = existing,
                MatchedBy = matchedBy,
            },
            fixture.Requests,
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            NullLogger<CustomerCreationFromFormRejectedIntegrationEvent>.Instance,
            new RecordingClientFormsMetrics(),
            default
        );

    private static Task ReassignAsync(ActionFixture fixture, RecordingReassignmentClient client, FakeMessageBus bus) =>
        ReassignParticipantFilesHandler.Handle(
            new ReassignParticipantFilesRequested(TenantId, fixture.Request.Id, "tests"),
            fixture.Requests,
            client,
            new RecordingUnitOfWork(),
            bus,
            new TestCorrelationContext(),
            NullLogger<ReassignParticipantFilesRequested>.Instance,
            default
        );

    private static CustomerCreationFromFormCompletedIntegrationEvent Completed(
        FormActionRun run,
        FormRequest request
    ) =>
        new()
        {
            TenantId = TenantId,
            RequestId = run.CommandId!.Value,
            SourceReferenceId = request.Id,
            CustomerId = CustomerId,
            Replayed = false,
        };

    // Planifica como lo haría Submit y devuelve el run emitido.
    private static FormActionRun Planned(ActionFixture fixture)
    {
        fixture
            .Planner.PlanAsync(
                fixture.Request,
                FormActionTrigger.OnSubmit,
                FormTemplateHandlerTestDoubles.Engine().ParseAnswers(Answers).Value,
                new DeferredMessages(),
                Now,
                default
            )
            .GetAwaiter()
            .GetResult();
        return fixture.Request.ActionRuns.Single();
    }

    private static ActionFixture Fixture(bool submitted = true, bool withAttachment = false, string? actions = null)
    {
        var version = Version(actions);
        var participant = Participant();
        var request = ExternalRequest(version, participant);
        if (withAttachment)
            AddAttachment(request);
        if (submitted)
            Submit(request, participant);
        else
            request.SaveDraft(new ProtectedAnswers(Answers, 1), 100, Actor(participant), Now);
        var versions = new MemoryFormTemplateVersionRepository(version);
        return new ActionFixture(
            request,
            participant,
            version,
            new ActionRequestRepository(request),
            versions,
            Planner(versions)
        );
    }

    private sealed record ActionFixture(
        FormRequest Request,
        FormParticipant Participant,
        FormTemplateVersion Version,
        ActionRequestRepository Requests,
        MemoryFormTemplateVersionRepository Versions,
        FormActionPlanner Planner
    );
}
