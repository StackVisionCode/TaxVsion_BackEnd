namespace TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;

public sealed record FormFileReassignmentRequest(
    Guid TenantId,
    Guid FormRequestId,
    Guid ParticipantId,
    Guid CustomerId,
    IReadOnlyList<Guid> FileIds
);
