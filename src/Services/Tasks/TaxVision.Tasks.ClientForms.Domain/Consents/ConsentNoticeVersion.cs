using System.Text.RegularExpressions;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Consents;

public sealed partial class ConsentNoticeVersion : TenantEntity
{
    private ConsentNoticeVersion() { }

    public Guid ConsentNoticeId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Language { get; private set; } = string.Empty;
    public string BodyJson { get; private set; } = string.Empty;
    public string TextSnapshot { get; private set; } = string.Empty;
    public string ContentHash { get; private set; } = string.Empty;
    public DateTime PublishedAtUtc { get; private set; }
    public Guid PublishedByUserId { get; private set; }

    internal static Result<ConsentNoticeVersion> Create(
        Guid tenantId,
        Guid noticeId,
        int versionNumber,
        string language,
        string bodyJson,
        string textSnapshot,
        string contentHash,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var normalizedLanguage = language?.Trim();
        var normalizedText = textSnapshot?.Trim();
        if (
            tenantId == Guid.Empty
            || noticeId == Guid.Empty
            || versionNumber < 1
            || byUserId == Guid.Empty
            || string.IsNullOrWhiteSpace(normalizedLanguage)
            || !LanguagePattern().IsMatch(normalizedLanguage)
            || string.IsNullOrWhiteSpace(bodyJson)
            || bodyJson.Length > 100_000
            || string.IsNullOrWhiteSpace(normalizedText)
            || normalizedText.Length > 20_000
            || string.IsNullOrWhiteSpace(contentHash)
            || !HashPattern().IsMatch(contentHash)
        )
            return Result.Failure<ConsentNoticeVersion>(ConsentErrors.NoticeInvalid);

        var version = new ConsentNoticeVersion
        {
            ConsentNoticeId = noticeId,
            VersionNumber = versionNumber,
            Language = normalizedLanguage,
            BodyJson = bodyJson,
            TextSnapshot = normalizedText,
            ContentHash = contentHash.ToLowerInvariant(),
            PublishedAtUtc = nowUtc,
            PublishedByUserId = byUserId,
        };
        version.SetTenant(tenantId);
        return Result.Success(version);
    }

    [GeneratedRegex("^[a-z]{2}(-[A-Z]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
