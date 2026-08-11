using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Sms.Application.Messages.Commands;

public sealed record SmsMediaDto(string Url, string ContentType, string? FileName, long? SizeBytes);

public sealed record SmsSendItemDto(
    Guid CustomerId,
    string To,
    string Message,
    IReadOnlyList<SmsMediaDto>? Media,
    string? IdempotencyKey,
    string? SourceContext
);

/// <summary>Envío de 1..N mensajes. TenantId y CorrelationId los pone el controller (JWT + header).</summary>
public sealed record SendSmsBatchCommand(Guid TenantId, string CorrelationId, IReadOnlyList<SmsSendItemDto> Items);

public sealed record SmsSendItemResult(
    Guid? MessageId,
    Guid CustomerId,
    string To,
    string Status,
    string? ProviderMessageId,
    string? ErrorCode
);

public sealed record SendSmsBatchResponse(Guid BatchId, string CorrelationId, IReadOnlyList<SmsSendItemResult> Results);

public static class SendSmsBatchHandler
{
    public static async Task<Result<SendSmsBatchResponse>> Handle(
        SendSmsBatchCommand command,
        ISmsMessageRepository messages,
        ISmsOptOutRepository optOuts,
        ISmsAdapterFactory adapters,
        IOptions<SmsOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<SendSmsBatchCommand> logger,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Result.Failure<SendSmsBatchResponse>(SmsErrors.InvalidTenant);
        if (command.Items.Count == 0)
            return Result.Failure<SendSmsBatchResponse>(new Error("sms.emptyBatch", "The batch has no messages."));
        if (command.Items.Count > options.Value.MaxBatchSize)
            return Result.Failure<SendSmsBatchResponse>(
                new Error("sms.batchTooLarge", $"The batch exceeds the maximum of {options.Value.MaxBatchSize}.")
            );

        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : command.CorrelationId;
        var batchId = Guid.NewGuid();
        var provider = adapters.Resolve(options.Value.DefaultProvider);
        var nowUtc = DateTime.UtcNow;

        var results = new List<SmsSendItemResult>(command.Items.Count);

        foreach (var item in command.Items)
        {
            var result = await ProcessItemAsync(
                item, command.TenantId, correlationId, batchId, provider, messages, optOuts, bus, nowUtc, logger, ct
            );
            results.Add(result);
        }

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "SMS batch {BatchId} processed {Count} message(s) for tenant {TenantId} (correlation {CorrelationId}).",
            batchId,
            command.Items.Count,
            command.TenantId,
            correlationId
        );

        return Result.Success(new SendSmsBatchResponse(batchId, correlationId, results));
    }

    private static async Task<SmsSendItemResult> ProcessItemAsync(
        SmsSendItemDto item,
        Guid tenantId,
        string correlationId,
        Guid batchId,
        ISmsProvider provider,
        ISmsMessageRepository messages,
        ISmsOptOutRepository optOuts,
        IMessageBus bus,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct
    )
    {
        // 1) Validaciones de entrada — un item inválido NO aborta el lote.
        if (item.CustomerId == Guid.Empty)
            return Failed(item, SmsErrors.InvalidCustomer.Code);

        var phoneResult = PhoneE164.Create(item.To);
        if (phoneResult.IsFailure)
            return Failed(item, SmsErrors.InvalidDestination.Code);
        var phone = phoneResult.Value;

        var bodyResult = SmsBody.Create(item.Message);
        if (bodyResult.IsFailure)
            return Failed(item, SmsErrors.InvalidBody.Code);
        var body = bodyResult.Value;

        var mediaPayload = (item.Media ?? [])
            .Select(m => new SmsMediaPayload(m.Url, m.ContentType, m.FileName, m.SizeBytes))
            .ToList();

        // 2) Gate opt-out: si el número hizo STOP, se persiste como Suppressed (auditable), no se envía.
        var optOut = await optOuts.GetAsync(tenantId, item.CustomerId, phone.Value, ct);
        if (optOut is { IsOptedOut: true })
        {
            var suppressed = BuildMessage(tenantId, item, phone, body, correlationId, batchId, provider.Code, mediaPayload, nowUtc);
            if (suppressed.IsFailure)
                return Failed(item, suppressed.Error.Code);
            suppressed.Value.MarkSuppressed("Recipient opted out (STOP).", nowUtc);
            await messages.AddAsync(suppressed.Value, ct);
            await bus.PublishAsync(new SmsMessageSuppressedIntegrationEvent
            {
                TenantId = tenantId, CorrelationId = correlationId, MessageId = suppressed.Value.Id,
                CustomerId = item.CustomerId, SourceContext = item.SourceContext,
            });
            return ItemResult(suppressed.Value, item, null);
        }

        // 3) Idempotencia: clave explícita o derivada; si ya existe, devuelve el existente sin reenviar.
        var idempotencyKey = string.IsNullOrWhiteSpace(item.IdempotencyKey)
            ? DeriveIdempotencyKey(tenantId, item.CustomerId, phone.Value, body.Value, mediaPayload)
            : item.IdempotencyKey.Trim();

        var existing = await messages.GetByIdempotencyKeyAsync(tenantId, idempotencyKey, ct);
        if (existing is not null)
            return ItemResult(existing, item, null);

        // 4) Validación de media contra capabilities — media no soportada FALLA explícitamente.
        var mediaError = SmsMediaValidator.Validate(provider.Capabilities, mediaPayload);

        var createResult = SmsMessage.Create(
            tenantId, item.CustomerId, phone, body, idempotencyKey, correlationId, batchId,
            provider.Code, item.SourceContext, mediaPayload
                .Select(m => new SmsMediaInput(m.Url, m.ContentType, m.FileName, m.SizeBytes)).ToList(),
            nowUtc
        );
        if (createResult.IsFailure)
            return Failed(item, createResult.Error.Code);
        var message = createResult.Value;

        if (mediaError is not null)
        {
            message.MarkFailed(nowUtc, mediaError.Code, mediaError.Message);
            await messages.AddAsync(message, ct);
            await PublishFailed(bus, message, item, correlationId, mediaError.Code, ct);
            return ItemResult(message, item, null);
        }

        // 5) Transformar + enviar al proveedor.
        var sendRequest = new SmsSendRequest(
            tenantId, item.CustomerId, phone.Value, body.Value, mediaPayload, correlationId, idempotencyKey, item.SourceContext
        );
        var sendResult = await provider.SendAsync(sendRequest, ct);

        if (sendResult.IsFailure || !sendResult.Value.Accepted)
        {
            var code = sendResult.IsFailure ? sendResult.Error.Code : sendResult.Value.ErrorCode ?? SmsErrors.ProviderRejected.Code;
            var reason = sendResult.IsFailure ? sendResult.Error.Message : sendResult.Value.ErrorMessage;
            message.MarkFailed(nowUtc, code, reason);
            await messages.AddAsync(message, ct);
            await PublishFailed(bus, message, item, correlationId, code, ct);
            return ItemResult(message, item, null);
        }

        message.MarkAccepted(sendResult.Value.ProviderMessageId ?? string.Empty, nowUtc);
        await messages.AddAsync(message, ct);
        await bus.PublishAsync(new SmsMessageAcceptedIntegrationEvent
        {
            TenantId = tenantId, CorrelationId = correlationId, MessageId = message.Id,
            CustomerId = item.CustomerId, SourceContext = item.SourceContext,
            ProviderMessageId = message.ProviderMessageId,
        });
        return ItemResult(message, item, message.ProviderMessageId);
    }

    private static Result<SmsMessage> BuildMessage(
        Guid tenantId, SmsSendItemDto item, PhoneE164 phone, SmsBody body, string correlationId,
        Guid batchId, string providerCode, IReadOnlyList<SmsMediaPayload> media, DateTime nowUtc
    ) =>
        SmsMessage.Create(
            tenantId, item.CustomerId, phone, body,
            string.IsNullOrWhiteSpace(item.IdempotencyKey)
                ? DeriveIdempotencyKey(tenantId, item.CustomerId, phone.Value, body.Value, media)
                : item.IdempotencyKey.Trim(),
            correlationId, batchId, providerCode, item.SourceContext,
            media.Select(m => new SmsMediaInput(m.Url, m.ContentType, m.FileName, m.SizeBytes)).ToList(),
            nowUtc
        );

    private static async Task PublishFailed(
        IMessageBus bus, SmsMessage message, SmsSendItemDto item, string correlationId, string? code, CancellationToken ct
    ) =>
        await bus.PublishAsync(new SmsMessageFailedIntegrationEvent
        {
            TenantId = message.TenantId, CorrelationId = correlationId, MessageId = message.Id,
            CustomerId = item.CustomerId, SourceContext = item.SourceContext,
            ProviderMessageId = message.ProviderMessageId, FailureCode = code,
        });

    private static SmsSendItemResult ItemResult(SmsMessage message, SmsSendItemDto item, string? providerMessageId) =>
        new(message.Id, item.CustomerId, message.To, message.Status.ToString(), providerMessageId ?? message.ProviderMessageId, message.FailureCode);

    private static SmsSendItemResult Failed(SmsSendItemDto item, string errorCode) =>
        new(null, item.CustomerId, item.To, "Failed", null, errorCode);

    /// <summary>Clave idempotente determinística cuando el caller no la manda. La media se canonicaliza
    /// (orden estable) para que el mismo contenido lógico produzca la misma clave.</summary>
    private static string DeriveIdempotencyKey(
        Guid tenantId, Guid customerId, string to, string body, IReadOnlyList<SmsMediaPayload> media
    )
    {
        var mediaCanon = string.Join("|", media.Select(m => $"{m.Url}:{m.ContentType}:{m.SizeBytes}").OrderBy(s => s, StringComparer.Ordinal));
        var canonical = $"{tenantId:N}|{customerId:N}|{to}|{body}|{mediaCanon}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "auto-" + Convert.ToHexStringLower(hash);
    }
}
