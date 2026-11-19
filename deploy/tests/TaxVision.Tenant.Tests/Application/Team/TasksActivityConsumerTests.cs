using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tenant.Application.Team.Consumers;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class TasksActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    protected override IEnumerable<ActivityCase> Cases()
    {
        var created = Stamp(
            new TaskCreatedIntegrationEvent
            {
                TaskId = TaskId,
                Title = "Review W-2",
                Priority = "High",
                CustomerId = CustomerId,
                CreatedByUserId = Actor,
            }
        );
        yield return new(
            created,
            created with
            {
                CreatedByUserId = null,
            },
            (e, r, u, c) => TasksActivityConsumer.Handle((TaskCreatedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.TaskCreated,
            TaskId,
            CustomerId,
            ActivityVisibility.Team
        );

        var completed = Stamp(
            new TaskCompletedIntegrationEvent
            {
                TaskId = TaskId,
                Title = "Review W-2",
                CompletedByUserId = Actor,
                CompletedAtUtc = DateTime.UtcNow,
                CustomerId = CustomerId,
            }
        );
        yield return new(
            completed,
            completed with
            {
                CompletedByUserId = Guid.Empty,
            },
            (e, r, u, c) => TasksActivityConsumer.Handle((TaskCompletedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.TaskCompleted,
            TaskId,
            CustomerId,
            ActivityVisibility.Team
        );

        var reopened = Stamp(
            new TaskReopenedIntegrationEvent
            {
                TaskId = TaskId,
                ReopenedByUserId = Actor,
                ReopenedAtUtc = DateTime.UtcNow,
            }
        );
        yield return new(
            reopened,
            reopened with
            {
                ReopenedByUserId = Guid.Empty,
            },
            (e, r, u, c) => TasksActivityConsumer.Handle((TaskReopenedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.TaskReopened,
            TaskId,
            null,
            ActivityVisibility.Team
        );

        var cancelled = Stamp(
            new TaskCancelledIntegrationEvent
            {
                TaskId = TaskId,
                Reason = "Duplicate",
                CancelledByUserId = Actor,
            }
        );
        yield return new(
            cancelled,
            cancelled with
            {
                CancelledByUserId = Guid.Empty,
            },
            (e, r, u, c) => TasksActivityConsumer.Handle((TaskCancelledIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.TaskCancelled,
            TaskId,
            null,
            ActivityVisibility.Team
        );

        var requestId = Guid.NewGuid();
        var request = Stamp(
            new ClientRequestCreatedIntegrationEvent
            {
                ClientRequestId = requestId,
                CustomerId = CustomerId,
                Title = "Upload your 1099",
                RequestedByUserId = Actor,
            }
        );
        yield return new(
            request,
            request with
            {
                RequestedByUserId = Guid.Empty,
            },
            (e, r, u, c) => TasksActivityConsumer.Handle((ClientRequestCreatedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.ClientRequestCreated,
            requestId,
            CustomerId,
            ActivityVisibility.Team
        );
    }
}

public sealed class ClientFormsActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static readonly ClientFormParticipant Participant = new() { Kind = "Customer", CustomerId = CustomerId };

    protected override IEnumerable<ActivityCase> Cases()
    {
        var assigned = Stamp(
            new FormRequestAssignedIntegrationEvent
            {
                FormRequestId = RequestId,
                Participant = Participant,
                Title = "Organizer",
                HasMessage = false,
                RequestedByUserId = Actor,
                FormTemplateId = Guid.NewGuid(),
                VersionNumber = 1,
            }
        );
        yield return new(
            assigned,
            assigned with
            {
                RequestedByUserId = Guid.Empty,
            },
            (e, r, u, c) =>
                ClientFormsActivityConsumer.Handle((FormRequestAssignedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.FormRequestAssigned,
            RequestId,
            CustomerId,
            ActivityVisibility.Team
        );

        var completed = Stamp(
            new FormRequestCompletedIntegrationEvent
            {
                FormRequestId = RequestId,
                Participant = Participant,
                Title = "Organizer",
                RevisionNumber = 2,
                CompletedByUserId = Actor,
            }
        );
        yield return new(
            completed,
            completed with
            {
                CompletedByUserId = Guid.Empty,
            },
            (e, r, u, c) =>
                ClientFormsActivityConsumer.Handle((FormRequestCompletedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.FormRequestCompleted,
            RequestId,
            CustomerId,
            ActivityVisibility.Team
        );

        var returned = Stamp(
            new FormRequestReturnedIntegrationEvent
            {
                FormRequestId = RequestId,
                Participant = new ClientFormParticipant { Kind = "Prospect" },
                Title = "Organizer",
                RevisionNumber = 1,
                FieldCommentCount = 3,
                ReturnedByUserId = Actor,
            }
        );
        yield return new(
            returned,
            returned with
            {
                ReturnedByUserId = Guid.Empty,
            },
            (e, r, u, c) =>
                ClientFormsActivityConsumer.Handle((FormRequestReturnedIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.FormRequestReturned,
            RequestId,
            null,
            ActivityVisibility.Team
        );
    }
}
