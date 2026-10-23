using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record RegisterPortalFormAttachmentCommand(
    ParticipantContext Context,
    Guid RequestId,
    string ComponentId,
    string? RowId,
    Guid FileId,
    string DisplayName,
    string ContentType,
    long SizeBytes,
    DateTime NowUtc
);

public sealed record FormAttachmentResponse(
    Guid Id,
    Guid FileId,
    string ComponentId,
    string? RowId,
    string DisplayName,
    FormAttachmentStatus Status,
    string? RejectionCode,
    string RowVersion
)
{
    public static FormAttachmentResponse From(FormAttachment value, FormRequest request) =>
        new(
            value.Id,
            value.FileId,
            value.ComponentId,
            value.RowId,
            value.DisplayName,
            value.Status,
            value.RejectionCode,
            Convert.ToBase64String(request.RowVersion)
        );
}

public sealed record DetachPortalFormAttachmentCommand(
    ParticipantContext Context,
    Guid RequestId,
    Guid AttachmentId,
    DateTime NowUtc
);
