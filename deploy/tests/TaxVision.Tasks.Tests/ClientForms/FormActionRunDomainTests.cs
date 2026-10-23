using TaxVision.Tasks.ClientForms.Domain.Requests;
using static TaxVision.Tasks.Tests.ClientForms.FormActionTestDoubles;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormActionRunDomainTests
{
    private const string Key = "create_client";
    private const string CreateCustomer = "CreateCustomer";

    [Fact]
    public void A_requested_run_carries_the_logical_idempotency_key_and_first_attempt()
    {
        var request = Submitted();
        var commandId = Guid.NewGuid();

        var run = request.RequestAction(Key, CreateCustomer, commandId, Now).Value;

        Assert.Equal($"{request.Id:N}:r1:{Key}", run.IdempotencyKey);
        Assert.Equal(1, run.Attempt);
        Assert.Equal(commandId, run.CommandId);
        Assert.Equal(FormActionRunStatus.Requested, run.Status);
    }

    [Fact]
    public void An_action_cannot_be_planned_before_the_first_revision()
    {
        var participant = Participant();
        var request = ExternalRequest(Version(), participant);

        Assert.Equal(
            FormActionRunErrors.Invalid,
            request.RequestAction(Key, CreateCustomer, Guid.NewGuid(), Now).Error
        );
    }

    [Theory]
    [InlineData(FormActionRunStatus.Requested)]
    [InlineData(FormActionRunStatus.Completed)]
    public void Retry_is_forbidden_while_the_result_is_unknown_or_already_applied(FormActionRunStatus status)
    {
        var request = Submitted();
        var commandId = Guid.NewGuid();
        var run = request.RequestAction(Key, CreateCustomer, commandId, Now).Value;
        if (status == FormActionRunStatus.Completed)
            request.CompleteAction(commandId, "Customer", Guid.NewGuid(), null, Now);

        var retried = request.RetryAction(run.Id, Guid.NewGuid(), null, Now);

        Assert.Equal(FormActionRunErrors.NotRetryable, retried.Error);
        Assert.Single(request.ActionRuns);
    }

    [Fact]
    public void Retry_is_forbidden_after_a_duplicate_rejection()
    {
        var request = Submitted();
        var commandId = Guid.NewGuid();
        var run = request.RequestAction(Key, CreateCustomer, commandId, Now).Value;
        request.RejectAction(commandId, FormActionRunErrors.DuplicateFoundCode, Guid.NewGuid(), "Email", Now);

        Assert.False(run.IsRetryable);
        Assert.Equal(FormActionRunErrors.NotRetryable, request.RetryAction(run.Id, Guid.NewGuid(), null, Now).Error);
    }

    [Theory]
    [InlineData("Customer.CreatorNotEligible")]
    [InlineData("Customer.CreatorNotInDirectory")]
    [InlineData("Customer.FormValueInvalid")]
    public void A_correctable_rejection_retries_with_a_new_command_and_the_same_key(string code)
    {
        var request = Submitted();
        var firstCommand = Guid.NewGuid();
        var first = request.RequestAction(Key, CreateCustomer, firstCommand, Now).Value;
        request.RejectAction(firstCommand, code, null, null, Now);
        var secondCommand = Guid.NewGuid();

        var second = request.RetryAction(first.Id, secondCommand, null, Now.AddMinutes(1)).Value;

        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(2, second.Attempt);
        Assert.Equal(secondCommand, second.CommandId);
        Assert.False(first.IsLatestAttempt);
        Assert.Equal(FormActionRunStatus.Requested, second.Status);
    }

    // T-8 a nivel de dominio: el intento ya reintentado no admite otro reintento sobre sí mismo.
    [Fact]
    public void A_superseded_attempt_cannot_be_retried_twice()
    {
        var request = Submitted();
        var run = request
            .CloseActionWithoutCommand(
                Key,
                CreateCustomer,
                FormActionRunStatus.Failed,
                "FormAction.PayloadUnavailable",
                Now
            )
            .Value;
        request.RetryAction(run.Id, Guid.NewGuid(), null, Now);

        Assert.Equal(FormActionRunErrors.NotRetryable, request.RetryAction(run.Id, Guid.NewGuid(), null, Now).Error);
        Assert.Equal(2, request.ActionRuns.Count);
    }

    [Fact]
    public void Completed_wins_over_a_duplicate_rejection_of_the_same_command_in_any_order()
    {
        var customerId = Guid.NewGuid();
        var rejectedFirst = Submitted();
        var first = Guid.NewGuid();
        rejectedFirst.RequestAction(Key, CreateCustomer, first, Now);
        rejectedFirst.RejectAction(first, FormActionRunErrors.DuplicateFoundCode, customerId, "Email", Now);
        var completedFirst = Submitted();
        var second = Guid.NewGuid();
        completedFirst.RequestAction(Key, CreateCustomer, second, Now);
        completedFirst.CompleteAction(second, "Customer", customerId, null, Now);

        Assert.Equal(
            FormActionRunTransition.Applied,
            rejectedFirst.CompleteAction(first, "Customer", customerId, null, Now).Value
        );
        Assert.Equal(
            FormActionRunTransition.Ignored,
            completedFirst.RejectAction(second, FormActionRunErrors.DuplicateFoundCode, customerId, "Email", Now).Value
        );
        Assert.All(
            [rejectedFirst.FindActionByCommand(first)!, completedFirst.FindActionByCommand(second)!],
            run =>
            {
                Assert.Equal(FormActionRunStatus.Completed, run.Status);
                Assert.Equal(customerId, run.ResultRefId);
            }
        );
    }

    [Fact]
    public void A_repeated_completion_is_ignored()
    {
        var request = Submitted();
        var commandId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        request.RequestAction(Key, CreateCustomer, commandId, Now);
        request.CompleteAction(commandId, "Customer", customerId, null, Now);

        Assert.Equal(
            FormActionRunTransition.Ignored,
            request.CompleteAction(commandId, "Customer", customerId, null, Now).Value
        );
    }

    [Fact]
    public void A_result_for_an_unknown_command_is_not_applied()
    {
        var request = Submitted();

        Assert.Equal(
            FormActionRunErrors.NotFound,
            request.CompleteAction(Guid.NewGuid(), "Customer", Guid.NewGuid(), null, Now).Error
        );
    }

    [Fact]
    public void A_skipped_run_does_not_block_a_later_plan_of_the_same_key()
    {
        var request = Submitted();
        request.CloseActionWithoutCommand(
            Key,
            CreateCustomer,
            FormActionRunStatus.Skipped,
            "FormAction.ConditionNotMet",
            Now
        );

        var requested = request.RequestAction(Key, CreateCustomer, Guid.NewGuid(), Now).Value;

        Assert.False(request.ActionRuns.First().IsRetryable);
        Assert.Equal(2, requested.Attempt);
        Assert.True(request.HasActiveAction(CreateCustomer));
    }

    private static FormRequest Submitted()
    {
        var participant = Participant();
        var request = ExternalRequest(Version(), participant);
        Submit(request, participant);
        return request;
    }
}
