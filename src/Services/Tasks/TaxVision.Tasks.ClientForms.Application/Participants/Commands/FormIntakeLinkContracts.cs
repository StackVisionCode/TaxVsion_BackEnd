using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public sealed record CreateFormIntakeLinkCommand(
    Guid TenantId,
    Guid ByUserId,
    string Name,
    Guid FormTemplateId,
    Guid? FormTemplateVersionId,
    string EntryNoticeKey,
    DateTime? ExpiresAtUtc,
    int? MaxSubmissions,
    Guid ResponsibleUserId,
    DateTime NowUtc
);

public enum FormIntakeLinkAction
{
    Pause,
    Resume,
    Revoke,
    PinVersion,
    RegenerateToken,
}

public sealed record UpdateFormIntakeLinkCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid LinkId,
    FormIntakeLinkAction Action,
    Guid? FormTemplateVersionId,
    byte[] RowVersion,
    DateTime NowUtc
);

public sealed record GetFormIntakeLinkTokenQuery(Guid TenantId, Guid ByUserId, Guid LinkId);
public sealed record ListFormIntakeLinksQuery(Guid TenantId);
public sealed record GetFormIntakeLinkQuery(Guid TenantId, Guid LinkId);

public sealed record FormIntakeLinkResponse(
    Guid Id,
    string Name,
    Guid FormTemplateId,
    Guid FormTemplateVersionId,
    Guid? EntryNoticeId,
    int? EntryNoticeVersionNumber,
    FormIntakeLinkStatus Status,
    string TokenLast4,
    DateTime? ExpiresAtUtc,
    int? MaxSubmissions,
    int SubmissionCount,
    Guid ResponsibleUserId,
    DateTime CreatedAtUtc,
    string RowVersion
)
{
    public static FormIntakeLinkResponse From(FormIntakeLink link) => new(
        link.Id,
        link.Name,
        link.FormTemplateId,
        link.FormTemplateVersionId,
        link.EntryNoticeId,
        link.EntryNoticeVersionNumber,
        link.Status,
        link.TokenLast4,
        link.ExpiresAtUtc,
        link.MaxSubmissions,
        link.SubmissionCount,
        link.ResponsibleUserId,
        link.CreatedAtUtc,
        Convert.ToBase64String(link.RowVersion)
    );
}

public sealed record FormIntakeLinkTokenResponse(FormIntakeLinkResponse Link, string? Token);
