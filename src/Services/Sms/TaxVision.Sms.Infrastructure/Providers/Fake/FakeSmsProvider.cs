using System.Text.Json;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Providers;

namespace TaxVision.Sms.Infrastructure.Providers.Fake;

/// <summary>
/// Proveedor de desarrollo/E2E. No hace HTTP: acepta el envío con un `providerMessageId` sintético y
/// parsea webhooks en un formato canónico simple (para probar el loop completo sin proveedor real).
/// Escenarios de error por convención en el body: `[REJECT]` → rechazado. Capabilities configurables por
/// `Sms:Providers:fake:Capabilities` (para probar media-no-soportada, etc.); si no hay config, defaults amplios.
/// </summary>
[SmsProvider("fake")]
public sealed class FakeSmsProvider(IOptions<SmsProvidersOptions> options, ILogger<FakeSmsProvider> logger) : ISmsProvider
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public string Code => "fake";

    public SmsProviderCapabilities Capabilities
    {
        get
        {
            if (options.Value.Providers.TryGetValue(Code, out var cfg))
                return new SmsProviderCapabilities
                {
                    SupportsDeliveryReceipts = cfg.Capabilities.SupportsDeliveryReceipts,
                    SupportsInbound = cfg.Capabilities.SupportsInbound,
                    SupportsBulkSend = cfg.Capabilities.SupportsBulkSend,
                    MaxBatchSize = cfg.Capabilities.MaxBatchSize,
                    SupportsMedia = cfg.Capabilities.SupportsMedia,
                    SupportsMultipleMedia = cfg.Capabilities.SupportsMultipleMedia,
                    MaxMediaItems = cfg.Capabilities.MaxMediaItems,
                    MaxMediaSizeBytes = cfg.Capabilities.MaxMediaSizeBytes,
                    AllowedMediaTypes = cfg.Capabilities.AllowedMediaTypes.ToHashSet(StringComparer.OrdinalIgnoreCase),
                };

            return new SmsProviderCapabilities
            {
                SupportsDeliveryReceipts = true,
                SupportsInbound = true,
                SupportsBulkSend = false,
                MaxBatchSize = 1,
                SupportsMedia = true,
                SupportsMultipleMedia = true,
                MaxMediaItems = 10,
                MaxMediaSizeBytes = 5 * 1024 * 1024,
                AllowedMediaTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            };
        }
    }

    public Task<Result<SmsSendResult>> SendAsync(SmsSendRequest request, CancellationToken ct = default)
    {
        if (request.Body.Contains("[REJECT]", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(Result.Success(new SmsSendResult(false, null, "providerRejected", "Simulated rejection.")));

        var providerMessageId = "fake-" + Guid.NewGuid().ToString("N");
        logger.LogInformation("FakeSmsProvider accepted message to {To} as {ProviderMessageId}.", request.To, providerMessageId);
        return Task.FromResult(Result.Success(new SmsSendResult(true, providerMessageId, null, null)));
    }

    public async Task<Result<IReadOnlyList<SmsSendResult>>> SendBatchAsync(IReadOnlyList<SmsSendRequest> requests, CancellationToken ct = default)
    {
        var results = new List<SmsSendResult>(requests.Count);
        foreach (var r in requests)
            results.Add((await SendAsync(r, ct)).Value);
        return Result.Success<IReadOnlyList<SmsSendResult>>(results);
    }

    // Dev: la firma siempre es válida (no hay proveedor real firmando).
    public Result<SmsSignatureCheck> VerifySignature(string rawPayload, string signatureHeader, string secret) =>
        Result.Success(new SmsSignatureCheck(true, null));

    public Result<SmsDeliveryUpdate> ParseDeliveryReceipt(string rawPayload)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<FakeDlr>(rawPayload, Json);
            if (dto is null || string.IsNullOrWhiteSpace(dto.ProviderMessageId))
                return Result.Failure<SmsDeliveryUpdate>(new Error("sms.webhook.malformed", "Malformed DLR payload."));

            var status = dto.Status?.ToLowerInvariant() switch
            {
                "delivered" => SmsCanonicalStatus.Delivered,
                "failed" => SmsCanonicalStatus.Failed,
                "undeliverable" => SmsCanonicalStatus.Undeliverable,
                _ => SmsCanonicalStatus.Accepted,
            };
            return Result.Success(new SmsDeliveryUpdate(
                dto.ProviderMessageId, dto.EventType ?? dto.Status ?? "status", status, dto.FailureCode, dto.FailureReason));
        }
        catch (JsonException)
        {
            return Result.Failure<SmsDeliveryUpdate>(new Error("sms.webhook.malformed", "Malformed DLR payload."));
        }
    }

    public Result<SmsInboundMessage> ParseInbound(string rawPayload)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<FakeInbound>(rawPayload, Json);
            if (dto is null || string.IsNullOrWhiteSpace(dto.From))
                return Result.Failure<SmsInboundMessage>(new Error("sms.webhook.malformed", "Malformed inbound payload."));

            var raw = (dto.Keyword ?? dto.Text ?? string.Empty).Trim();
            var keyword = raw.ToUpperInvariant() switch
            {
                "STOP" => SmsInboundKeyword.Stop,
                "START" => SmsInboundKeyword.Start,
                "HELP" => SmsInboundKeyword.Help,
                _ => SmsInboundKeyword.Unknown,
            };
            return Result.Success(new SmsInboundMessage(
                dto.From, keyword, raw, "inbound",
                dto.ProviderMessageId ?? Guid.NewGuid().ToString("N"),
                dto.TenantId, dto.CustomerId));
        }
        catch (JsonException)
        {
            return Result.Failure<SmsInboundMessage>(new Error("sms.webhook.malformed", "Malformed inbound payload."));
        }
    }

    private sealed record FakeDlr(string? ProviderMessageId, string? Status, string? EventType, string? FailureCode, string? FailureReason);

    private sealed record FakeInbound(string? From, string? Keyword, string? Text, string? ProviderMessageId, Guid? TenantId, Guid? CustomerId);
}
