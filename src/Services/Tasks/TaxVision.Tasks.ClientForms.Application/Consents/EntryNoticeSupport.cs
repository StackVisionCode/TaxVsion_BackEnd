using System.Text.Json;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Consents;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public sealed record PublicEntryNoticeResponse(string Title, string Language, JsonElement Body, string ContentHash);

internal sealed record EntryNoticeSelection(ConsentNotice Notice, ConsentNoticeVersion Version);

internal static class EntryNoticeSupport
{
    internal static async Task<EntryNoticeSelection?> ResolveForCreationAsync(
        Guid tenantId,
        string key,
        IConsentNoticeRepository notices,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var notice = await notices.GetByKeyAsync(tenantId, key, ct);
        if (
            notice is null
            || notice.Status != ConsentNoticeStatus.Active
            || notice.Purpose != ConsentNoticePurpose.IdentityVerification
        )
            return null;
        var version = notice.Versions.SingleOrDefault(item => item.VersionNumber == notice.LatestVersionNumber);
        return version is null ? null : new EntryNoticeSelection(notice, version);
    }

    internal static async Task<EntryNoticeSelection?> ResolvePinnedAsync(
        Guid tenantId,
        Guid? noticeId,
        int? versionNumber,
        IConsentNoticeRepository notices,
        CancellationToken ct
    )
    {
        if (!noticeId.HasValue || !versionNumber.HasValue)
            return null;
        var notice = await notices.GetAsync(tenantId, noticeId.Value, ct);
        var version = notice?.Versions.SingleOrDefault(item => item.VersionNumber == versionNumber.Value);
        return notice is null || notice.Purpose != ConsentNoticePurpose.IdentityVerification || version is null
            ? null
            : new EntryNoticeSelection(notice, version);
    }

    internal static PublicEntryNoticeResponse ToResponse(EntryNoticeSelection selection) =>
        new(
            selection.Notice.Title,
            selection.Version.Language,
            JsonDocument.Parse(selection.Version.BodyJson).RootElement.Clone(),
            selection.Version.ContentHash
        );
}
