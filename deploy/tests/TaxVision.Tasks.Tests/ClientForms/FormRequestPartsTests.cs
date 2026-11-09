using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Tests.ClientForms;

/// <summary>
/// TSK-9: caracteriza lo que se mueve fuera de <see cref="FormRequest"/>: acciones, resumen y firma, y actualización
/// de perfil. Lo que importa: la numeración de intentos, la idempotencia y que cada cambio toque la raíz, que es lo
/// que hace chocar a dos consumidores por la RowVersion (TSK-4, TSK-11).
/// </summary>
public sealed class FormRequestPartsTests
{
    private static readonly DateTime Now = new(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddHours(1);

    [Fact]
    public void An_action_needs_a_submitted_revision()
    {
        var pending = FormRequestPartsFixture.Pending(Now);

        var result = pending.Actions.Request("key", "CreateCustomer", Guid.NewGuid(), Now);

        Assert.Equal(FormActionRunErrors.Invalid, result.Error);
    }

    [Fact]
    public void An_action_needs_a_command()
    {
        var request = FormRequestPartsFixture.Submitted(Now);

        var result = request.Actions.Request("key", "CreateCustomer", Guid.Empty, Now);

        Assert.Equal(FormActionRunErrors.Invalid, result.Error);
    }

    [Fact]
    public void Attempts_of_the_same_action_share_the_key_and_count_up()
    {
        var request = FormRequestPartsFixture.Submitted(Now);
        var first = request.Actions.Request("key", "CreateCustomer", Guid.NewGuid(), Now).Value;
        var commandId = first.CommandId!.Value;
        request.Actions.Reject(commandId, "Rejected", null, "detail", Now);

        var retried = request.Actions.Retry(first.Id, Guid.NewGuid(), null, Later);

        Assert.True(retried.IsSuccess);
        Assert.Equal(first.IdempotencyKey, retried.Value.IdempotencyKey);
        Assert.Equal(2, retried.Value.Attempt);
        Assert.Equal(2, request.ActionRuns.Count);
        Assert.Same(retried.Value, request.Actions.Latest("CreateCustomer"));
    }

    [Fact]
    public void A_retry_takes_a_command_or_a_failure_but_not_both()
    {
        var request = FormRequestPartsFixture.Submitted(Now);
        var first = request.Actions.Request("key", "CreateCustomer", Guid.NewGuid(), Now).Value;
        request.Actions.Reject(first.CommandId!.Value, "Rejected", null, null, Now);

        Assert.Equal(FormActionRunErrors.Invalid, request.Actions.Retry(first.Id, null, null, Later).Error);
        Assert.Equal(FormActionRunErrors.Invalid, request.Actions.Retry(first.Id, Guid.NewGuid(), "Failed", Later).Error);
        Assert.Equal(FormActionRunErrors.Invalid, request.Actions.Retry(first.Id, Guid.Empty, null, Later).Error);
    }

    [Fact]
    public void A_retry_without_a_command_is_born_failed()
    {
        var request = FormRequestPartsFixture.Submitted(Now);
        var first = request.Actions.Request("key", "CreateCustomer", Guid.NewGuid(), Now).Value;
        request.Actions.Reject(first.CommandId!.Value, "Rejected", null, null, Now);

        var retried = request.Actions.Retry(first.Id, null, "NoData", Later);

        Assert.Equal(FormActionRunStatus.Failed, retried.Value.Status);
        Assert.Equal("NoData", retried.Value.ErrorCode);
    }

    [Fact]
    public void Only_a_failed_latest_attempt_can_be_retried()
    {
        var request = FormRequestPartsFixture.Submitted(Now);
        var first = request.Actions.Request("key", "CreateCustomer", Guid.NewGuid(), Now).Value;

        Assert.Equal(FormActionRunErrors.NotRetryable, request.Actions.GetRetryable(first.Id).Error);
        Assert.Equal(FormActionRunErrors.NotFound, request.Actions.GetRetryable(Guid.NewGuid()).Error);
    }

    [Fact]
    public void Closing_without_a_command_only_skips_or_fails_and_needs_a_reason()
    {
        var request = FormRequestPartsFixture.Submitted(Now);

        Assert.Equal(
            FormActionRunErrors.Invalid,
            request.Actions.CloseWithoutCommand("key", "Sign", FormActionRunStatus.Rejected, "why", Now).Error
        );
        Assert.Equal(
            FormActionRunErrors.Invalid,
            request.Actions.CloseWithoutCommand("key", "Sign", FormActionRunStatus.Skipped, " ", Now).Error
        );

        var closed = request.Actions.CloseWithoutCommand("key", "Sign", FormActionRunStatus.Skipped, "why", Now);

        Assert.Equal(FormActionRunStatus.Skipped, closed.Value.Status);
        Assert.True(request.Actions.HasClosed("key", 1, "why"));
        Assert.False(request.Actions.HasClosed("key", 1, "other"));
        Assert.False(request.Actions.HasActive("Sign"));
        Assert.Null(request.Actions.Latest("Sign"));
    }

    [Fact]
    public void Completing_and_rejecting_find_the_run_by_its_command()
    {
        var request = FormRequestPartsFixture.Submitted(Now);
        var run = request.Actions.Request("key", "CreateCustomer", Guid.NewGuid(), Now).Value;
        var commandId = run.CommandId!.Value;

        Assert.Null(request.Actions.FindByCommand(Guid.Empty));
        Assert.Same(run, request.Actions.FindByCommand(commandId));
        Assert.Equal(FormActionRunErrors.NotFound, request.Actions.Complete(Guid.NewGuid(), "Customer", Guid.NewGuid(), null, Now).Error);
        Assert.Equal(FormActionRunErrors.Invalid, request.Actions.Complete(commandId, " ", Guid.NewGuid(), null, Now).Error);
        Assert.Equal(FormActionRunErrors.Invalid, request.Actions.Complete(commandId, "Customer", Guid.Empty, null, Now).Error);
        Assert.Equal(FormActionRunErrors.NotFound, request.Actions.Reject(Guid.NewGuid(), "Code", null, null, Now).Error);
        Assert.Equal(FormActionRunErrors.Invalid, request.Actions.Reject(commandId, " ", null, null, Now).Error);

        var completed = request.Actions.Complete(commandId, "Customer", Guid.NewGuid(), null, Now);

        Assert.True(completed.IsSuccess);
        Assert.Equal(FormActionRunStatus.Completed, run.Status);
    }

    [Fact]
    public void The_summary_correlates_by_generation_and_touches_the_root()
    {
        var request = FormRequestPartsFixture.Submitted(Now);
        var generation = Guid.NewGuid();
        var before = request.LastActivityAtUtc;

        Assert.True(request.Summary.MarkPdfRequested(1, generation).IsSuccess);

        Assert.NotEqual(before, request.LastActivityAtUtc);
        Assert.Equal(1, request.Summary.RevisionNumberForGeneration(generation));
        Assert.Null(request.Summary.RevisionNumberForGeneration(Guid.NewGuid()));
        Assert.True(request.Summary.LinkPdf(generation, Guid.NewGuid()).IsSuccess);
        Assert.True(request.Summary.MarkPdfFailed(generation).IsSuccess);
        Assert.Equal(FormRequestErrors.NotFound, request.Summary.MarkPdfRequested(9, generation).Error);
        Assert.Equal(FormRequestErrors.NotFound, request.Summary.LinkPdf(Guid.NewGuid(), Guid.NewGuid()).Error);
        Assert.Equal(FormRequestErrors.NotFound, request.Summary.MarkPdfFailed(Guid.NewGuid()).Error);
    }

    [Fact]
    public void A_signature_walks_its_states_and_only_touches_the_root_when_it_moves()
    {
        var request = FormRequestPartsFixture.Submitted(Now);
        var signatureId = Guid.NewGuid();

        Assert.True(request.Summary.RevisionAwaitsSignature(1));
        Assert.False(request.Summary.RevisionHasPendingSignature(1));
        Assert.True(request.Summary.MarkSignatureRequesting(1, Guid.NewGuid(), "hash").IsSuccess);
        Assert.True(request.Summary.RevisionHasPendingSignature(1));
        Assert.Equal(FormRevisionSignatureStatus.Requesting, request.Summary.RevisionSignatureStatus(1));

        Assert.True(request.Summary.LinkSignatureRequest(1, signatureId).Value);
        Assert.Equal(signatureId, request.Summary.SignatureRequestIdFor(1));
        Assert.True(request.Summary.MarkSignatureSigned(1).Value);
        Assert.True(request.Summary.MarkSignatureCompleted(1).Value);
        Assert.True(request.Summary.MarkSignatureSealed(1, Guid.NewGuid(), "post", null).Value);
        Assert.Equal(FormRevisionSignatureStatus.Sealed, request.Summary.RevisionSignatureStatus(1));

        var touched = request.LastActivityAtUtc;
        Assert.False(request.Summary.MarkSignatureDeclined(1).Value);
        Assert.False(request.Summary.MarkSignatureCanceled(1).Value);
        Assert.False(request.Summary.MarkSignatureExpired(1).Value);
        Assert.Equal(touched, request.LastActivityAtUtc);
    }

    [Fact]
    public void A_signature_failure_is_reported_once()
    {
        var request = FormRequestPartsFixture.Submitted(Now);
        request.Summary.MarkSignatureRequesting(1, Guid.NewGuid(), "hash");

        Assert.True(request.Summary.MarkSignatureFailed(1).IsSuccess);
        Assert.Equal(FormRequestErrors.SignatureNotPending, request.Summary.MarkSignatureFailed(1).Error);
        Assert.Equal(FormRequestErrors.NotFound, request.Summary.MarkSignatureFailed(9).Error);
        Assert.Equal(FormRequestErrors.NotFound, request.Summary.MarkSignatureSigned(9).Error);
        Assert.False(request.Summary.RevisionHasPendingSignature(9));
        Assert.Equal(FormRevisionSignatureStatus.None, request.Summary.RevisionSignatureStatus(9));
    }

    [Fact]
    public void A_profile_update_needs_a_completed_customer_request()
    {
        var pending = FormRequestPartsFixture.Pending(Now);
        var completed = FormRequestPartsFixture.Completed(Now);

        Assert.Equal(FormRequestErrors.ProfileUpdateNotAllowed, pending.ProfileUpdate.Request(Guid.NewGuid(), ["a"], Now).Error);
        Assert.Equal(FormRequestErrors.ProfileUpdateNoTargets, completed.ProfileUpdate.Request(Guid.NewGuid(), [], Now).Error);
        Assert.Equal(FormRequestErrors.ProfileUpdateNoTargets, completed.ProfileUpdate.Request(Guid.Empty, ["a"], Now).Error);

        Assert.True(completed.ProfileUpdate.Request(Guid.NewGuid(), ["a", "b"], Now).IsSuccess);

        Assert.Equal(FormProfileUpdateStatus.Processing, completed.ProfileUpdateStatus);
        Assert.Equal(2, completed.ProfileUpdateOutcomes.Count);
        Assert.Equal(FormRequestErrors.ProfileUpdateInProgress, completed.ProfileUpdate.Request(Guid.NewGuid(), ["a"], Now).Error);
    }

    [Fact]
    public void Applied_with_failures_is_partial_and_a_second_answer_changes_nothing()
    {
        var request = FormRequestPartsFixture.Completed(Now);
        var command = Guid.NewGuid();
        request.ProfileUpdate.Request(command, ["a", "b"], Now);

        Assert.Equal(
            FormRequestErrors.ProfileUpdateCommandMismatch,
            request.ProfileUpdate.MarkApplied(Guid.NewGuid(), [], Later).Error
        );
        var applied = request.ProfileUpdate.MarkApplied(
            command,
            [new FormProfileUpdateOutcome("a", true, null), new FormProfileUpdateOutcome("b", false, "bad")],
            Later
        );

        Assert.True(applied.IsSuccess);
        Assert.Equal(FormProfileUpdateStatus.PartiallyApplied, request.ProfileUpdateStatus);
        Assert.Equal(Later, request.ProfileUpdateResolvedAtUtc);
        Assert.True(request.ProfileUpdate.MarkRejected(command, [], Later.AddHours(1)).IsSuccess);
        Assert.Equal(FormProfileUpdateStatus.PartiallyApplied, request.ProfileUpdateStatus);
        Assert.Equal(Later, request.ProfileUpdateResolvedAtUtc);
    }

    [Fact]
    public void Rejected_marks_every_failure_as_not_applied()
    {
        var request = FormRequestPartsFixture.Completed(Now);
        var command = Guid.NewGuid();
        request.ProfileUpdate.Request(command, ["a"], Now);

        request.ProfileUpdate.MarkRejected(command, [new FormProfileUpdateOutcome("a", true, "bad")], Later);

        Assert.Equal(FormProfileUpdateStatus.Rejected, request.ProfileUpdateStatus);
        Assert.False(Assert.Single(request.ProfileUpdateOutcomes).Applied);
    }

    private static class FormRequestPartsFixture
    {
        private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        private static readonly Guid CustomerId = Guid.Parse("30000000-0000-0000-0000-000000000001");

        public static FormRequest Pending(DateTime now) =>
            FormRequest
                .Create(
                    TenantId,
                    FormParticipantRef.Customer(CustomerId),
                    Version(now),
                    null,
                    null,
                    2026,
                    null,
                    now.AddDays(7),
                    null,
                    null,
                    FormRequestOrigin.Directed,
                    null,
                    null,
                    UserId,
                    now
                )
                .Value;

        public static FormRequest Submitted(DateTime now)
        {
            var request = Pending(now);
            Assert.True(
                request.Submit(new ProtectedAnswers("cipher", 1), [], [], FormActor.PortalUser(UserId), now).IsSuccess
            );
            return request;
        }

        public static FormRequest Completed(DateTime now)
        {
            var request = Submitted(now);
            Assert.True(request.Complete(null, UserId, now).IsSuccess);
            return request;
        }

        private static FormTemplateVersion Version(DateTime now) =>
            FormTemplateVersion.Create(
                TenantId,
                Guid.NewGuid(),
                1,
                "Tax organizer",
                FormPurpose.Questionnaire,
                FormPresentation.Wizard,
                ClientFormsTestDocuments.Definition(
                    "{\"schemaVersion\":1,\"meta\":{\"title\":\"Tax organizer\",\"purpose\":\"taxPreparation\"},\"sections\":[]}"
                ),
                ClientFormsTestDocuments.Hash(new string('a', 64)),
                0,
                UserId,
                now
            );
    }
}
