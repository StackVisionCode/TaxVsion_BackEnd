using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public sealed record ListActiveConsentNoticesQuery(Guid TenantId, ConsentNoticePurpose? Purpose);

// Vista acotada para quien solo elige un aviso: activo y publicado, con su última versión y sin datos de gestión.
public sealed record ActiveConsentNoticeResponse(
    Guid Id,
    string Key,
    string Title,
    ConsentNoticePurpose Purpose,
    int VersionNumber,
    string Language,
    string TextSnapshot,
    string ContentHash,
    DateTime PublishedAtUtc
);

public static class ListActiveConsentNoticesHandler
{
    public static async Task<IReadOnlyList<ActiveConsentNoticeResponse>> Handle(
        ListActiveConsentNoticesQuery query,
        IConsentNoticeRepository notices,
        CancellationToken ct
    ) =>
        (await notices.ListAsync(query.TenantId, ct))
            .Where(notice =>
                notice.Status == ConsentNoticeStatus.Active
                && notice.LatestVersionNumber > 0
                && (query.Purpose is null || notice.Purpose == query.Purpose)
            )
            .Select(notice =>
                (
                    Notice: notice,
                    Latest: notice.Versions.SingleOrDefault(version =>
                        version.VersionNumber == notice.LatestVersionNumber
                    )
                )
            )
            .Where(item => item.Latest is not null)
            .Select(item => new ActiveConsentNoticeResponse(
                item.Notice.Id,
                item.Notice.Key,
                item.Notice.Title,
                item.Notice.Purpose,
                item.Latest!.VersionNumber,
                item.Latest.Language,
                item.Latest.TextSnapshot,
                item.Latest.ContentHash,
                item.Latest.PublishedAtUtc
            ))
            .ToArray();
}
