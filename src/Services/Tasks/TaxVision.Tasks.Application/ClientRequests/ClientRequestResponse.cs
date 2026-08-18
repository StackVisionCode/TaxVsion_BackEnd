using TaxVision.Tasks.Domain.ClientRequests;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.ClientRequests;

public sealed record ClientRequestDocumentResponse(
    Guid Id,
    Guid FileId,
    string DisplayName,
    string? ContentType,
    long SizeBytes,
    AttachmentStatus Status,
    DateTime UploadedAtUtc
);

/// <summary>
/// Lo que ve el staff. El portal usa <see cref="PortalClientRequestResponse"/>, que deliberadamente
/// no lleva ni quién lo pidió ni el motivo técnico de un rechazo.
/// </summary>
public sealed record ClientRequestResponse(
    Guid Id,
    Guid CustomerId,
    Guid? TaskId,
    string Title,
    string? Details,
    ClientRequestStatus Status,
    DateTime? DueAtUtc,
    Guid RequestedByUserId,
    DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime? ResolvedAtUtc,
    string? ResolutionNote,
    IReadOnlyList<ClientRequestDocumentResponse> Documents
)
{
    public static ClientRequestResponse From(ClientRequest request) =>
        new(
            request.Id,
            request.CustomerId,
            request.TaskId,
            request.Title,
            request.Details,
            request.Status,
            request.DueAtUtc,
            request.RequestedByUserId,
            request.CreatedAtUtc,
            request.SubmittedAtUtc,
            request.ResolvedAtUtc,
            request.ResolutionNote,
            [.. request.Documents.Select(Map)]
        );

    internal static ClientRequestDocumentResponse Map(ClientRequestDocument d) =>
        new(d.Id, d.FileId, d.DisplayName, d.ContentType, d.SizeBytes, d.Status, d.UploadedAtUtc);
}

/// <summary>
/// La misma información, recortada a lo que le sirve al cliente. Sin <c>RequestedByUserId</c> —el id
/// de un empleado de la firma no es asunto suyo— y sin el motivo técnico de un rechazo.
/// </summary>
public sealed record PortalClientRequestResponse(
    Guid Id,
    string Title,
    string? Details,
    ClientRequestStatus Status,
    DateTime? DueAtUtc,
    DateTime CreatedAtUtc,
    string? ResolutionNote,
    IReadOnlyList<ClientRequestDocumentResponse> Documents
)
{
    public static PortalClientRequestResponse From(ClientRequest request) =>
        new(
            request.Id,
            request.Title,
            request.Details,
            request.Status,
            request.DueAtUtc,
            request.CreatedAtUtc,
            request.ResolutionNote,
            [.. request.Documents.Where(d => d.IsActive).Select(ClientRequestResponse.Map)]
        );
}
