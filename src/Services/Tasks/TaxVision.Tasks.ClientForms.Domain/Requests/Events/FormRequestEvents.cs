using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Requests.Events;

public sealed record FormRequestAssigned(
    Guid RequestId,
    Guid TenantId,
    FormParticipantKind ParticipantKind,
    Guid? CustomerId,
    Guid? ParticipantId,
    Guid RequestedByUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;

public sealed record FormRequestSubmitted(
    Guid RequestId,
    Guid TenantId,
    int RevisionNumber,
    FormActorKind SubmittedByKind,
    Guid? SubmittedByPortalUserId,
    Guid? SubmittedByParticipantId,
    bool IsResubmission,
    DateTime OccurredAtUtc
) : IDomainEvent;

public sealed record FormRequestReturned(
    Guid RequestId,
    Guid TenantId,
    int RevisionNumber,
    Guid ReviewedByUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;

public sealed record FormRequestCompleted(
    Guid RequestId,
    Guid TenantId,
    int RevisionNumber,
    Guid CompletedByUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;

public sealed record FormRequestCancelled(
    Guid RequestId,
    Guid TenantId,
    FormCancelReason Reason,
    bool NotifyClient,
    DateTime OccurredAtUtc
) : IDomainEvent;

public sealed record FormRequestDueDateChanged(
    Guid RequestId,
    Guid TenantId,
    DateTime? DueAtUtc,
    Guid ChangedByUserId,
    DateTime OccurredAtUtc
) : IDomainEvent;

public sealed record FormAttachmentRejected(
    Guid RequestId,
    Guid TenantId,
    Guid AttachmentId,
    Guid FileId,
    string RejectionCode,
    DateTime OccurredAtUtc
) : IDomainEvent;
