using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Domain.Webhooks;
using Wolverine;

namespace TaxVision.Sms.Application.Webhooks.Commands;

// ───────────────────────── Status / DLR ─────────────────────────

public sealed record ProcessDeliveryReceiptCommand(string ProviderCode, string RawPayload, string SignatureHeader);

public static class ProcessDeliveryReceiptHandler
{
    public static async Task<Result> Handle(
        ProcessDeliveryReceiptCommand command,
        ISmsAdapterFactory adapters,
        ISmsWebhookSecrets secrets,
        IProcessedWebhookRepository processed,
        ISmsMessageRepository messages,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<ProcessDeliveryReceiptCommand> logger,
        CancellationToken ct
    )
    {
        var provider = adapters.Resolve(command.ProviderCode);

        var signature = provider.VerifySignature(command.RawPayload, command.SignatureHeader, secrets.GetSecret(command.ProviderCode) ?? string.Empty);
        if (signature.IsFailure || !signature.Value.IsValid)
            return Result.Failure(new Error("sms.webhook.invalidSignature", "Webhook signature verification failed."));

        var parsed = provider.ParseDeliveryReceipt(command.RawPayload);
        if (parsed.IsFailure)
            return parsed;
        var update = parsed.Value;

        // Dedup anti-replay: reintento idéntico = no-op.
        if (await processed.ExistsAsync(command.ProviderCode, update.ProviderMessageId, update.EventType, ct))
            return Result.Success();

        var message = await messages.GetByProviderMessageIdAsync(command.ProviderCode, update.ProviderMessageId, ct);
        if (message is null)
        {
            logger.LogInformation(
                "SMS DLR for unknown message {ProviderMessageId} ({Provider}); ignored.",
                update.ProviderMessageId,
                command.ProviderCode
            );
            return Result.Success();
        }

        var nowUtc = DateTime.UtcNow;
        var transition = update.Status switch
        {
            SmsCanonicalStatus.Delivered => message.MarkDelivered(nowUtc),
            SmsCanonicalStatus.Failed => message.MarkFailed(nowUtc, update.FailureCode, update.FailureReason),
            SmsCanonicalStatus.Undeliverable => message.MarkUndeliverable(nowUtc, update.FailureCode, update.FailureReason),
            SmsCanonicalStatus.Accepted => message.MarkAccepted(update.ProviderMessageId, nowUtc),
            _ => Result.Success(),
        };
        // Una transición inválida (estado ya avanzado) no rompe el webhook; se registra la dedup igual.

        await processed.AddAsync(new ProcessedWebhook(command.ProviderCode, update.ProviderMessageId, update.EventType, message.TenantId, null, nowUtc), ct);

        bus.TenantId = message.TenantId.ToString();
        if (message.Status == SmsMessageStatus.Delivered)
            await bus.PublishAsync(new SmsMessageDeliveredIntegrationEvent
            {
                TenantId = message.TenantId, CorrelationId = message.CorrelationId, MessageId = message.Id,
                CustomerId = message.CustomerId, SourceContext = message.SourceContext, ProviderMessageId = message.ProviderMessageId,
            });
        else if (message.Status is SmsMessageStatus.Failed or SmsMessageStatus.Undeliverable)
            await bus.PublishAsync(new SmsMessageFailedIntegrationEvent
            {
                TenantId = message.TenantId, CorrelationId = message.CorrelationId, MessageId = message.Id,
                CustomerId = message.CustomerId, SourceContext = message.SourceContext,
                ProviderMessageId = message.ProviderMessageId, FailureCode = message.FailureCode,
            });

        await unitOfWork.SaveChangesAsync(ct);
        return transition.IsSuccess ? Result.Success() : Result.Success();
    }
}

// ───────────────────────── Inbound STOP/START/HELP ─────────────────────────

public sealed record ProcessInboundCommand(string ProviderCode, string RawPayload, string SignatureHeader);

public static class ProcessInboundHandler
{
    private const string InboundEventType = "inbound";

    public static async Task<Result> Handle(
        ProcessInboundCommand command,
        ISmsAdapterFactory adapters,
        ISmsWebhookSecrets secrets,
        IProcessedWebhookRepository processed,
        ISmsMessageRepository messages,
        ISmsOptOutRepository optOuts,
        IUnitOfWork unitOfWork,
        ILogger<ProcessInboundCommand> logger,
        CancellationToken ct
    )
    {
        var provider = adapters.Resolve(command.ProviderCode);

        var signature = provider.VerifySignature(command.RawPayload, command.SignatureHeader, secrets.GetSecret(command.ProviderCode) ?? string.Empty);
        if (signature.IsFailure || !signature.Value.IsValid)
            return Result.Failure(new Error("sms.webhook.invalidSignature", "Webhook signature verification failed."));

        var parsed = provider.ParseInbound(command.RawPayload);
        if (parsed.IsFailure)
            return parsed;
        var inbound = parsed.Value;

        if (await processed.ExistsAsync(command.ProviderCode, inbound.ProviderMessageId, InboundEventType, ct))
            return Result.Success();

        var phoneResult = PhoneE164.Create(inbound.FromPhone);
        if (phoneResult.IsFailure)
            return Result.Success(); // número ilegible: no-op auditable
        var phone = phoneResult.Value;

        // Resolver (tenant, customer): hints del proveedor, o el envío más reciente hacia ese número.
        // Si no se puede resolver con seguridad, NO se inventan ids — se registra y termina no-op.
        Guid tenantId = inbound.TenantIdHint ?? Guid.Empty;
        Guid customerId = inbound.CustomerIdHint ?? Guid.Empty;
        if (tenantId == Guid.Empty || customerId == Guid.Empty)
        {
            var last = await messages.GetLatestByPhoneAsync(phone.Value, ct);
            if (last is null)
            {
                logger.LogWarning(
                    "SMS inbound {Keyword} from {Phone} ({Provider}) could not be resolved to a tenant/customer; ignored (operational).",
                    inbound.Keyword, phone.Value, command.ProviderCode
                );
                await processed.AddAsync(new ProcessedWebhook(command.ProviderCode, inbound.ProviderMessageId, InboundEventType, null, null, DateTime.UtcNow), ct);
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success();
            }
            tenantId = last.TenantId;
            customerId = last.CustomerId;
        }

        var nowUtc = DateTime.UtcNow;
        var optOut = await optOuts.GetByTenantAndPhoneAsync(tenantId, phone.Value, ct);
        if (optOut is null)
        {
            optOut = SmsOptOut.CreateSubscribed(tenantId, customerId, phone, nowUtc);
            await optOuts.AddAsync(optOut, ct);
        }

        switch (inbound.Keyword)
        {
            case SmsInboundKeyword.Stop:
                optOut.OptOut(inbound.RawKeyword, nowUtc);
                break;
            case SmsInboundKeyword.Start:
                optOut.OptIn(inbound.RawKeyword, nowUtc);
                break;
            case SmsInboundKeyword.Help:
            case SmsInboundKeyword.Unknown:
            default:
                // HELP/Unknown: sin cambio de estado (respuesta estándar es config futura).
                break;
        }

        await processed.AddAsync(new ProcessedWebhook(command.ProviderCode, inbound.ProviderMessageId, InboundEventType, tenantId, null, nowUtc), ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
