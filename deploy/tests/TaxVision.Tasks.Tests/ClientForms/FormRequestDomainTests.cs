using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Requests.Events;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormRequestDomainTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid CustomerId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid ParticipantId = Guid.Parse("40000000-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void External_request_requires_responsible_user()
    {
        var result = Create(FormParticipantRef.External(ParticipantId), useDefaultResponsibleUser: false);

        Assert.True(result.IsFailure);
        Assert.Equal("FormRequest.ResponsibleUserRequired", result.Error.Code);
    }

    [Fact]
    public void External_request_rejects_task_and_missing_intake_link()
    {
        var withTask = Create(FormParticipantRef.External(ParticipantId), taskId: Guid.NewGuid());
        var withoutLink = Create(
            FormParticipantRef.External(ParticipantId),
            origin: FormRequestOrigin.IntakeLink,
            intakeLinkId: null
        );

        Assert.Equal("FormRequest.TaskRequiresCustomer", withTask.Error.Code);
        Assert.Equal("FormRequest.IntakeLinkRequired", withoutLink.Error.Code);
    }

    [Fact]
    public void Draft_and_submit_reject_actor_from_another_participant()
    {
        var request = Create(FormParticipantRef.External(ParticipantId)).Value;
        var actor = FormActor.ExternalSession(Guid.NewGuid(), Guid.NewGuid());

        var draft = request.SaveDraft(ProtectedAnswers(), 20, actor, Now.AddMinutes(1));
        var submit = request.Submit(ProtectedAnswers(), [], [], actor, Now.AddMinutes(2));

        Assert.Equal("FormRequest.ActorMismatch", draft.Error.Code);
        Assert.Equal("FormRequest.ActorMismatch", submit.Error.Code);
    }

    [Fact]
    public void Submit_rejects_pending_attachment()
    {
        var request = Create(FormParticipantRef.Customer(CustomerId)).Value;
        var address = new FormAnswerAddress("fd_upload", null);
        var fileId = Guid.NewGuid();
        Assert.True(
            request.RegisterAttachment(address, fileId, "tax.pdf", "application/pdf", 20, 2, 10, null, Now).IsSuccess
        );

        var result = request.Submit(ProtectedAnswers(), [fileId], [], FormActor.PortalUser(UserId), Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("FormRequest.AttachmentsNotReady", result.Error.Code);
    }

    [Fact]
    public void Customer_file_from_another_owner_is_rejected()
    {
        var request = Create(FormParticipantRef.Customer(CustomerId)).Value;
        var fileId = RegisterPending(request);

        var outcome = request.ApplyFileVerdict(
            fileId,
            new FormFileVerdict(FormFileVerdictStatus.Available, "Customer", Guid.NewGuid(), "ClientForms", Now),
            Now
        );

        Assert.Equal(FileVerdictOutcome.Rejected, outcome);
        Assert.Equal("not_owned", request.Submission!.Attachments.Single().RejectionCode);
    }

    [Fact]
    public void Linked_external_accepts_participant_and_customer_file_owners()
    {
        var request = Create(FormParticipantRef.External(ParticipantId)).Value;
        Assert.Equal(CustomerLinkResult.Linked, request.TryAttachCustomer(CustomerId, false, null, Now).Value);
        var participantFile = RegisterPending(request);
        var customerFile = RegisterPending(request);

        var participantOutcome = request.ApplyFileVerdict(
            participantFile,
            new FormFileVerdict(FormFileVerdictStatus.Available, "FormParticipant", ParticipantId, "ClientForms", Now),
            Now
        );
        var customerOutcome = request.ApplyFileVerdict(
            customerFile,
            new FormFileVerdict(FormFileVerdictStatus.Available, "Customer", CustomerId, "ClientForms", Now),
            Now
        );

        Assert.Equal(FileVerdictOutcome.Available, participantOutcome);
        Assert.Equal(FileVerdictOutcome.Available, customerOutcome);
    }

    [Fact]
    public void Late_available_recovers_upload_incomplete()
    {
        var request = Create(FormParticipantRef.Customer(CustomerId)).Value;
        var fileId = RegisterPending(request);
        Assert.Equal(1, request.ExpireStalePendingAttachments(Now.AddMinutes(1), Now.AddHours(1)));

        var outcome = request.ApplyFileVerdict(
            fileId,
            new FormFileVerdict(
                FormFileVerdictStatus.Available,
                "Customer",
                CustomerId,
                "ClientForms",
                Now.AddHours(2)
            ),
            Now.AddHours(2)
        );

        Assert.Equal(FileVerdictOutcome.Available, outcome);
        Assert.Equal(FormAttachmentStatus.Available, request.Submission!.Attachments.Single().Status);
        Assert.Null(request.Submission.Attachments.Single().RejectionCode);
    }

    [Fact]
    public void Available_attachment_stays_available_after_owner_reassignment()
    {
        var request = Create(FormParticipantRef.External(ParticipantId)).Value;
        var fileId = RegisterPending(request);
        Assert.Equal(
            FileVerdictOutcome.Available,
            request.ApplyFileVerdict(
                fileId,
                new FormFileVerdict(
                    FormFileVerdictStatus.Available,
                    "FormParticipant",
                    ParticipantId,
                    "ClientForms",
                    Now
                ),
                Now
            )
        );
        request.TryAttachCustomer(CustomerId, false, null, Now.AddMinutes(1));

        var replay = request.ApplyFileVerdict(
            fileId,
            new FormFileVerdict(
                FormFileVerdictStatus.Available,
                "Customer",
                CustomerId,
                "ClientForms",
                Now.AddMinutes(2)
            ),
            Now.AddMinutes(2)
        );

        Assert.Equal(FileVerdictOutcome.Ignored, replay);
        Assert.Equal(FormAttachmentStatus.Available, request.Submission!.Attachments.Single().Status);
    }

    [Fact]
    public void Capturing_sensitive_answer_supersedes_previous_value()
    {
        var request = Create(FormParticipantRef.Customer(CustomerId)).Value;
        var address = new FormAnswerAddress("fd_ssn", null);
        var actor = FormActor.PortalUser(UserId);
        var first = request
            .CaptureSensitiveAnswer(address, FormTaxIdentifierKind.Ssn, ProtectedTaxId("1234"), actor, Now)
            .Value;
        var second = request
            .CaptureSensitiveAnswer(
                address,
                FormTaxIdentifierKind.Ssn,
                ProtectedTaxId("6789"),
                actor,
                Now.AddMinutes(1)
            )
            .Value;

        Assert.NotNull(first.SupersededAtUtc);
        Assert.Null(second.SupersededAtUtc);
        Assert.Equal("6789", second.Protected.Last4);
    }

    [Fact]
    public void Blocked_customer_link_retries_same_customer()
    {
        var request = Create(FormParticipantRef.External(ParticipantId)).Value;
        var conflictId = Guid.NewGuid();
        Assert.Equal(CustomerLinkResult.Blocked, request.TryAttachCustomer(CustomerId, true, conflictId, Now).Value);

        var result = request.RetryCustomerLink(false, null, Now.AddMinutes(1));

        Assert.Equal(CustomerLinkResult.Linked, result.Value);
        Assert.Equal(CustomerId, request.EffectiveCustomerId);
    }

    [Fact]
    public void Customer_archive_does_not_cancel_unlinked_external_request()
    {
        var request = Create(FormParticipantRef.External(ParticipantId)).Value;

        var result = request.CancelBecauseCustomerArchived(CustomerId, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(FormRequestStatus.Pending, request.Status);
    }

    // §42 y P-19: archivar es Cancel con otro motivo, y Completed es terminal.
    [Fact]
    public void Archive_rejects_a_completed_request_like_cancel_does()
    {
        var request = Create(FormParticipantRef.Customer(CustomerId)).Value;
        request.Submit(ProtectedAnswers(), [], [], FormActor.PortalUser(UserId), Now);
        request.Complete(null, UserId, Now.AddMinutes(1));

        var result = request.Archive("Resolved", UserId, Now.AddMinutes(2));

        Assert.Equal(FormRequestErrors.InvalidTransition, result.Error);
        Assert.Equal(FormRequestStatus.Completed, request.Status);
        Assert.Null(request.CancelReason);
        Assert.Empty(request.DomainEvents.OfType<FormRequestCancelled>());
    }

    [Fact]
    public void Archive_closes_a_submitted_request_with_its_own_reason()
    {
        var request = Create(FormParticipantRef.Customer(CustomerId)).Value;
        request.Submit(ProtectedAnswers(), [], [], FormActor.PortalUser(UserId), Now);

        var result = request.Archive("Spam", UserId, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(FormRequestStatus.Cancelled, request.Status);
        Assert.Equal(FormCancelReason.Archived, request.CancelReason);
    }

    private static BuildingBlocks.Results.Result<FormRequest> Create(
        FormParticipantRef participant,
        Guid? responsibleUserId = null,
        Guid? taskId = null,
        FormRequestOrigin origin = FormRequestOrigin.Directed,
        Guid? intakeLinkId = null,
        bool useDefaultResponsibleUser = true
    ) =>
        FormRequest.Create(
            TenantId,
            participant,
            Version(),
            null,
            null,
            2026,
            taskId,
            Now.AddDays(7),
            null,
            null,
            origin,
            intakeLinkId,
            participant.Kind == FormParticipantKind.External && useDefaultResponsibleUser
                ? responsibleUserId ?? UserId
                : responsibleUserId,
            UserId,
            Now
        );

    private static FormTemplateVersion Version() =>
        FormTemplateVersion.Create(
            TenantId,
            Guid.NewGuid(),
            1,
            "Tax organizer",
            FormPurpose.Questionnaire,
            FormPresentation.Wizard,
            ClientFormsTestDocuments.Definition(
                "{\"schemaVersion\":1,\"meta\":{\"title\":\"Tax organizer\",\"purpose\":\"taxPreparation\"},\"pages\":[]}"
            ),
            ClientFormsTestDocuments.Hash(new string('a', 64)),
            0,
            UserId,
            Now
        );

    private static ProtectedAnswers ProtectedAnswers() => new("cipher", 1);

    private static ProtectedTaxIdentifier ProtectedTaxId(string last4) => new("cipher", 1, last4);

    private static Guid RegisterPending(FormRequest request)
    {
        var fileId = Guid.NewGuid();
        Assert.True(
            request
                .RegisterAttachment(
                    new FormAnswerAddress("fd_upload", null),
                    fileId,
                    "tax.pdf",
                    "application/pdf",
                    20,
                    10,
                    20,
                    null,
                    Now
                )
                .IsSuccess
        );
        return fileId;
    }
}
