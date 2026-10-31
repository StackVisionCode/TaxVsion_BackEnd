using System.Globalization;
using Microsoft.Extensions.Logging;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Moderation;

namespace TaxVision.CloudStorage.Application.Moderation;

/// <summary>
/// <see cref="IContentScanner"/> real (T-20): lee la política de BD, pide la puntuación al scanner y
/// decide. Falla cerrada si así lo dice la política: scanner caído o sin configurar → en revisión.
/// </summary>
public sealed class ContentModerationScanner(
    IContentModerationPolicyRepository policies,
    ContentModerationPolicyCache cache,
    IContentClassifier classifier,
    ISystemClock clock,
    ILogger<ContentModerationScanner> logger
) : IContentScanner
{
    public async Task<ContentScanResult> ScanAsync(Stream content, ContentScanContext context, CancellationToken ct)
    {
        var settings = await LoadAsync(ct);
        if (settings is null || !settings.AppliesTo(context.FolderType, context.DetectedContentType))
            return ContentScanResult.Clean();

        if (!classifier.IsConfigured)
            return Unavailable(settings, context, "Content scanner is not configured.");
        if (content.CanSeek && content.Length > settings.MaxScanBytes)
            return Unavailable(settings, context, "File is larger than the content scan limit.");

        ContentClassification classification;
        try
        {
            classification = await classifier.ClassifyAsync(
                content,
                context.DetectedContentType,
                new ContentSampling(settings.FrameIntervalSeconds, settings.MaxFrames),
                ct
            );
        }
        catch (ContentClassifierUnavailableException ex)
        {
            logger.LogWarning(ex, "Content scanner unavailable for file {FileId}", context.FileId);
            return Unavailable(settings, context, "Content scanner unavailable.");
        }
        catch (ContentNotDecodableException)
        {
            // El inspector dijo imagen/video pero no se puede decodificar: que lo mire una persona.
            return ContentScanResult.Uncertain("Content could not be decoded for moderation.");
        }

        var score = classification.SexualScore.ToString("0.000", CultureInfo.InvariantCulture);
        return settings.Decide(classification.SexualScore) switch
        {
            ModerationDecision.Block => ContentScanResult.PolicyViolation($"Sexual content detected (score {score})."),
            ModerationDecision.Review => ContentScanResult.Uncertain($"Possible sexual content (score {score})."),
            _ => ContentScanResult.Clean(),
        };
    }

    private ContentScanResult Unavailable(ContentModerationSettings settings, ContentScanContext context, string reason)
    {
        if (settings.FailClosed)
            return ContentScanResult.Uncertain(reason);
        logger.LogWarning("Content moderation skipped for file {FileId}: {Reason}", context.FileId, reason);
        return ContentScanResult.Clean();
    }

    private async Task<ContentModerationSettings?> LoadAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (cache.TryGet(now, out var cached))
            return cached;
        var policy = await policies.GetAsync(ct);
        if (policy is null)
            logger.LogError("Content moderation policy row is missing; uploads are not being moderated.");
        var settings = policy?.ToSettings();
        cache.Set(settings, now);
        return settings;
    }
}
