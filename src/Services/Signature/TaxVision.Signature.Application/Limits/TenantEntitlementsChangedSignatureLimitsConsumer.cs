using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Limits;

/// <summary>
/// Proyecta las claves <c>signature.*</c> del snapshot de entitlements (Subscription
/// <c>SignatureEntitlementCatalog</c>) en <see cref="TenantSignatureLimits"/>. Una clave ausente toma el
/// valor vigente antes de los entitlements. Idempotente por revisión.
/// </summary>
public static class TenantEntitlementsChangedSignatureLimitsConsumer
{
    public static async Task Handle(
        TenantEntitlementsChangedIntegrationEvent message,
        ITenantSignatureLimitsRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(message.CorrelationId) ? message.EventId.ToString("N") : message.CorrelationId
        );

        var values = message.EntitlementValues;
        var limits = await repository.GetByTenantIdAsync(message.TenantId, ct);
        if (limits is null)
        {
            limits = TenantSignatureLimits.Create(message.TenantId);
            repository.Add(limits);
        }

        limits.ApplyIfNewer(
            message.PlanCode,
            message.RevisionNumber,
            Int(values, "signature.max_documents_per_request", TenantSignatureLimits.FallbackMaxDocumentsPerRequest),
            Int(values, "signature.max_signers_per_request", TenantSignatureLimits.FallbackMaxSignersPerRequest),
            Long(values, "signature.max_pdf_bytes", TenantSignatureLimits.FallbackMaxPdfBytes),
            Int(values, "signature.max_pages_per_document", TenantSignatureLimits.FallbackMaxPagesPerDocument),
            Int(values, "signature.max_token_hours", TenantSignatureLimits.FallbackMaxTokenHours),
            Int(values, "signature.min_retention_years", TenantSignatureLimits.FallbackMinRetentionYears),
            values.TryGetValue("signature.purge_allowed", out var purge) && bool.TryParse(purge, out var allowed)
                ? allowed
                : TenantSignatureLimits.FallbackPurgeAllowed,
            Channels(values),
            DateTime.UtcNow
        );

        await unitOfWork.SaveChangesAsync(ct);
    }

    private static int Int(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
        values.TryGetValue(key, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static long Long(IReadOnlyDictionary<string, string> values, string key, long fallback) =>
        values.TryGetValue(key, out var raw)
        && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    // "Email,Sms": nombres del enum separados por coma (Enum.TryParse admite flags así).
    private static VerificationChannel Channels(IReadOnlyDictionary<string, string> values) =>
        values.TryGetValue("signature.channels", out var raw)
        && Enum.TryParse<VerificationChannel>(raw, ignoreCase: true, out var channels)
        && channels != VerificationChannel.None
            ? channels
            : TenantSignatureLimits.FallbackChannels;
}
