using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Moderation;

namespace TaxVision.CloudStorage.Application.Moderation;

public sealed record ContentModerationPolicyView(
    bool Enabled,
    double BlockThreshold,
    double ReviewThreshold,
    bool ScanVideos,
    int FrameIntervalSeconds,
    int MaxFrames,
    long MaxScanBytes,
    IReadOnlyList<string> ExcludedFolders,
    bool FailClosed,
    Guid UpdatedByUserId,
    DateTime UpdatedAtUtc
)
{
    public static ContentModerationPolicyView From(ContentModerationPolicy policy)
    {
        var settings = policy.ToSettings();
        return new(
            settings.Enabled,
            settings.BlockThreshold,
            settings.ReviewThreshold,
            settings.ScanVideos,
            settings.FrameIntervalSeconds,
            settings.MaxFrames,
            settings.MaxScanBytes,
            settings.ExcludedFolders.Select(f => f.ToString()).ToList(),
            settings.FailClosed,
            policy.UpdatedByUserId,
            policy.UpdatedAtUtc
        );
    }
}

public sealed record GetContentModerationPolicyQuery;

public static class GetContentModerationPolicyHandler
{
    public static async Task<Result<ContentModerationPolicyView>> Handle(
        GetContentModerationPolicyQuery query,
        IContentModerationPolicyRepository policies,
        CancellationToken ct
    )
    {
        var policy = await policies.GetAsync(ct);
        return policy is null
            ? Result.Failure<ContentModerationPolicyView>(ContentModerationErrors.PolicyMissing)
            : Result.Success(ContentModerationPolicyView.From(policy));
    }
}

/// <summary>Reemplaza la política completa (PUT): los umbrales se validan juntos en el dominio.</summary>
public sealed record UpdateContentModerationPolicyCommand(ContentModerationSettings Settings, Guid ActorUserId);

public static class UpdateContentModerationPolicyHandler
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static async Task<Result<ContentModerationPolicyView>> Handle(
        UpdateContentModerationPolicyCommand command,
        IContentModerationPolicyRepository policies,
        IContentModerationPolicyAuditRepository audit,
        ContentModerationPolicyCache cache,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var policy = await policies.GetAsync(ct);
        if (policy is null)
            return Result.Failure<ContentModerationPolicyView>(ContentModerationErrors.PolicyMissing);

        var before = JsonSerializer.Serialize(policy.ToSettings(), Json);
        var now = clock.UtcNow;
        var updated = policy.Update(command.Settings, command.ActorUserId, now);
        if (updated.IsFailure)
            return Result.Failure<ContentModerationPolicyView>(updated.Error);

        audit.Add(
            ContentModerationPolicyAudit.Record(
                before,
                JsonSerializer.Serialize(policy.ToSettings(), Json),
                command.ActorUserId,
                now
            )
        );
        await unitOfWork.SaveChangesAsync(ct);
        // Tras guardar: una lectura concurrente no debe repoblar la caché con el valor viejo.
        cache.Invalidate();
        return Result.Success(ContentModerationPolicyView.From(policy));
    }
}

public sealed record ListContentModerationPolicyAuditQuery(int Skip, int Take);

public sealed record ContentModerationPolicyAuditView(
    Guid Id,
    string OldSettingsJson,
    string NewSettingsJson,
    Guid ActorUserId,
    DateTime OccurredAtUtc
);

public static class ListContentModerationPolicyAuditHandler
{
    public static async Task<IReadOnlyList<ContentModerationPolicyAuditView>> Handle(
        ListContentModerationPolicyAuditQuery query,
        IContentModerationPolicyAuditRepository audit,
        CancellationToken ct
    )
    {
        var entries = await audit.ListAsync(Math.Max(0, query.Skip), Math.Clamp(query.Take, 1, 200), ct);
        return entries
            .Select(e => new ContentModerationPolicyAuditView(
                e.Id,
                e.OldSettingsJson,
                e.NewSettingsJson,
                e.ActorUserId,
                e.OccurredAtUtc
            ))
            .ToList();
    }
}

/// <summary>Parseo de nombres de carpeta del request: solo nombres del enum, nunca su valor numérico.</summary>
public static class ModerationFolderNames
{
    public static bool TryParse(IEnumerable<string>? names, out IReadOnlyCollection<FolderType> folders)
    {
        var parsed = new List<FolderType>();
        foreach (var raw in names ?? [])
        {
            if (
                !Enum.TryParse<FolderType>(raw, ignoreCase: true, out var folder)
                || long.TryParse(raw, out _)
                || !Enum.IsDefined(folder)
            )
            {
                folders = [];
                return false;
            }
            parsed.Add(folder);
        }
        folders = parsed;
        return true;
    }
}
