using System.Text.RegularExpressions;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Consents;

public sealed partial class ConsentNotice : AggregateRoot
{
    private readonly List<ConsentNoticeVersion> _versions = [];

    private ConsentNotice() { }

    public string Key { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public ConsentNoticePurpose Purpose { get; private set; }
    public ConsentNoticeStatus Status { get; private set; }
    public int LatestVersionNumber { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public Guid? ArchivedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public IReadOnlyCollection<ConsentNoticeVersion> Versions => _versions;

    public static Result<ConsentNotice> Create(
        Guid tenantId,
        string key,
        string title,
        ConsentNoticePurpose purpose,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var normalizedKey = key?.Trim().ToLowerInvariant();
        var normalizedTitle = title?.Trim();
        if (
            tenantId == Guid.Empty
            || byUserId == Guid.Empty
            || !Enum.IsDefined(purpose)
            || string.IsNullOrWhiteSpace(normalizedKey)
            || !KeyPattern().IsMatch(normalizedKey)
            || string.IsNullOrWhiteSpace(normalizedTitle)
            || normalizedTitle.Length > 200
        )
            return Result.Failure<ConsentNotice>(ConsentErrors.NoticeInvalid);

        var notice = new ConsentNotice
        {
            Key = normalizedKey,
            Title = normalizedTitle,
            Purpose = purpose,
            Status = ConsentNoticeStatus.Active,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = byUserId,
        };
        notice.SetTenant(tenantId);
        return Result.Success(notice);
    }

    public Result UpdateTitle(string title)
    {
        var normalized = title?.Trim();
        if (Status != ConsentNoticeStatus.Active)
            return Result.Failure(ConsentErrors.NoticeArchived);
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 200)
            return Result.Failure(ConsentErrors.NoticeInvalid);
        Title = normalized;
        return Result.Success();
    }

    public Result<ConsentNoticeVersion> Publish(
        string language,
        string bodyJson,
        string textSnapshot,
        string contentHash,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        if (Status != ConsentNoticeStatus.Active)
            return Result.Failure<ConsentNoticeVersion>(ConsentErrors.NoticeArchived);
        var version = ConsentNoticeVersion.Create(
            TenantId,
            Id,
            LatestVersionNumber + 1,
            language,
            bodyJson,
            textSnapshot,
            contentHash,
            byUserId,
            nowUtc
        );
        if (version.IsFailure)
            return version;
        _versions.Add(version.Value);
        LatestVersionNumber = version.Value.VersionNumber;
        return version;
    }

    public Result Archive(Guid byUserId, DateTime nowUtc)
    {
        if (byUserId == Guid.Empty)
            return Result.Failure(ConsentErrors.NoticeInvalid);
        if (Status == ConsentNoticeStatus.Archived)
            return Result.Success();
        Status = ConsentNoticeStatus.Archived;
        ArchivedAtUtc = nowUtc;
        ArchivedByUserId = byUserId;
        return Result.Success();
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,119}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
