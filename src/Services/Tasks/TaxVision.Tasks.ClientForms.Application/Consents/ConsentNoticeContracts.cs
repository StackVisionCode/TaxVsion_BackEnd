using TaxVision.DataCollection.Engine.Text;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public sealed record CreateConsentNoticeCommand(
    Guid TenantId,
    Guid ByUserId,
    string Key,
    string Title,
    ConsentNoticePurpose Purpose,
    DateTime NowUtc
);

public sealed record UpdateConsentNoticeCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid NoticeId,
    string Title,
    byte[] RowVersion
);

public sealed record PublishConsentNoticeVersionCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid NoticeId,
    string Language,
    RichTextDocument Body,
    byte[] RowVersion,
    DateTime NowUtc
);

public sealed record ArchiveConsentNoticeCommand(
    Guid TenantId,
    Guid ByUserId,
    Guid NoticeId,
    byte[] RowVersion,
    DateTime NowUtc
);

public sealed record GetConsentNoticeQuery(Guid TenantId, Guid NoticeId);
public sealed record ListConsentNoticesQuery(Guid TenantId);

public sealed record ConsentNoticeVersionResponse(
    int VersionNumber,
    string Language,
    string BodyJson,
    string TextSnapshot,
    string ContentHash,
    DateTime PublishedAtUtc,
    Guid PublishedByUserId
)
{
    public static ConsentNoticeVersionResponse From(ConsentNoticeVersion version) => new(
        version.VersionNumber,
        version.Language,
        version.BodyJson,
        version.TextSnapshot,
        version.ContentHash,
        version.PublishedAtUtc,
        version.PublishedByUserId
    );
}

public sealed record ConsentNoticeResponse(
    Guid Id,
    string Key,
    string Title,
    ConsentNoticePurpose Purpose,
    ConsentNoticeStatus Status,
    int LatestVersionNumber,
    IReadOnlyList<ConsentNoticeVersionResponse> Versions,
    DateTime CreatedAtUtc,
    string RowVersion
)
{
    public static ConsentNoticeResponse From(ConsentNotice notice) => new(
        notice.Id,
        notice.Key,
        notice.Title,
        notice.Purpose,
        notice.Status,
        notice.LatestVersionNumber,
        notice.Versions.OrderByDescending(item => item.VersionNumber)
            .Select(ConsentNoticeVersionResponse.From)
            .ToArray(),
        notice.CreatedAtUtc,
        Convert.ToBase64String(notice.RowVersion)
    );
}
