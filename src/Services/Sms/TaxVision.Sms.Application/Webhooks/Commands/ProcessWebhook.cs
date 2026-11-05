using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Domain.Webhooks;
using Wolverine;

namespace TaxVision.Sms.Application.Webhooks.Commands;

public static class SmsWebhookErrors
{
    public static readonly Error UnknownProvider = new("sms.webhook.unknownProvider", "Unknown SMS provider.");
}

// ───────────────────────── Status / DLR ─────────────────────────

public sealed record ProcessDeliveryReceiptCommand(
    string ProviderCode,
    string RawPayload,
    string SignatureHeader,
    string RequestUrl = ""
);

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
        if (!adapters.TryResolve(command.ProviderCode, out var provider))
            return Result.Failure(SmsWebhookErrors.UnknownProvider);

        var signature = provider.VerifySignature(
            command.RawPayload,
            command.SignatureHeader,
            secrets.GetSecret(command.ProviderCode) ?? string.Empty,
            command.RequestUrl
        );
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
            SmsCanonicalStatus.Undeliverable => message.MarkUndeliverable(
                nowUtc,
                update.FailureCode,
                update.FailureReason
            ),
            SmsCanonicalStatus.Accepted => message.MarkAccepted(update.ProviderMessageId, nowUtc),
            _ => Result.Success(),
        };
        // Una transición inválida (estado ya avanzado) no rompe el webhook; se registra la dedup igual.

        await processed.AddAsync(
            new ProcessedWebhook(
                command.ProviderCode,
                update.ProviderMessageId,
                update.EventType,
                message.TenantId,
                null,
                nowUtc
            ),
            ct
        );

        bus.TenantId = message.TenantId.ToString();
        if (message.Status == SmsMessageStatus.Delivered)
            await bus.PublishAsync(
                new SmsMessageDeliveredIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = message.CorrelationId,
                    MessageId = message.Id,
                    CustomerId = message.CustomerId,
                    SourceContext = message.SourceContext,
                    ProviderMessageId = message.ProviderMessageId,
                }
            );
        else if (message.Status is SmsMessageStatus.Failed or SmsMessageStatus.Undeliverable)
            await bus.PublishAsync(
                new SmsMessageFailedIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = message.CorrelationId,
                    MessageId = message.Id,
                    CustomerId = message.CustomerId,
                    SourceContext = message.SourceContext,
                    ProviderMessageId = message.ProviderMessageId,
                    FailureCode = message.FailureCode,
                }
            );

        await unitOfWork.SaveChangesAsync(ct);
        return transition.IsSuccess ? Result.Success() : Result.Success();
    }
}

// ───────────────────────── Inbound STOP/START/HELP ─────────────────────────

public sealed record ProcessInboundCommand(
    string ProviderCode,
    string RawPayload,
    string SignatureHeader,
    string RequestUrl = ""
);

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
        if (!adapters.TryResolve(command.ProviderCode, out var provider))
            return Result.Failure(SmsWebhookErrors.UnknownProvider);

        var signature = provider.VerifySignature(
            command.RawPayload,
            command.SignatureHeader,
            secrets.GetSecret(command.ProviderCode) ?? string.Empty,
            command.RequestUrl
        );
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

        // Resolver los tenants: con hint, solo ese tenant (número propio de la oficina). Sin hint, el
        // remitente es compartido y el STOP aplica a cada tenant que le escribió al número (D-SMS-1).
        // Si no se puede resolver con seguridad, NO se inventan ids — se registra y termina no-op.
        var history = await messages.ListTenantsByPhoneAsync(phone.Value, ct);
        var targets = new List<SmsPhoneTenant>();
        if (inbound.TenantIdHint is { } hintTenant && hintTenant != Guid.Empty)
        {
            var customer =
                inbound.CustomerIdHint is { } hintCustomer && hintCustomer != Guid.Empty
                    ? hintCustomer
                    : history.FirstOrDefault(t => t.TenantId == hintTenant)?.CustomerId ?? Guid.Empty;
            targets.Add(new SmsPhoneTenant(hintTenant, customer));
        }
        else
        {
            targets.AddRange(history);
        }

        var nowUtc = DateTime.UtcNow;
        if (targets.Count == 0)
        {
            logger.LogWarning(
                "SMS inbound {Keyword} from {Phone} ({Provider}) could not be resolved to a tenant/customer; ignored (operational).",
                inbound.Keyword,
                phone.Value,
                command.ProviderCode
            );
            await processed.AddAsync(
                new ProcessedWebhook(
                    command.ProviderCode,
                    inbound.ProviderMessageId,
                    InboundEventType,
                    null,
                    null,
                    nowUtc
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success();
        }

        if (inbound.Keyword is SmsInboundKeyword.Stop or SmsInboundKeyword.Start)
        {
            foreach (var target in targets)
                await ApplyToNumberAsync(optOuts, target, phone, inbound, nowUtc, ct);
            if (targets.Count > 1)
                logger.LogInformation(
                    "SMS inbound {Keyword} applied to {TenantCount} tenants (shared sender).",
                    inbound.Keyword,
                    targets.Count
                );
        }
        // HELP/Unknown: sin cambio de estado (respuesta estándar es config futura).

        await processed.AddAsync(
            new ProcessedWebhook(
                command.ProviderCode,
                inbound.ProviderMessageId,
                InboundEventType,
                targets.Count == 1 ? targets[0].TenantId : null,
                null,
                nowUtc
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    // STOP/START cambia todas las filas del número en el tenant; si no hay ninguna, crea una con el cliente.
    private static async Task ApplyToNumberAsync(
        ISmsOptOutRepository optOuts,
        SmsPhoneTenant target,
        PhoneE164 phone,
        SmsInboundMessage inbound,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var rows = (await optOuts.ListByTenantAndPhoneAsync(target.TenantId, phone.Value, ct)).ToList();
        if (rows.Count == 0)
        {
            if (target.CustomerId == Guid.Empty)
                return;
            var created = SmsOptOut.CreateSubscribed(target.TenantId, target.CustomerId, phone, nowUtc);
            await optOuts.AddAsync(created, ct);
            rows.Add(created);
        }

        foreach (var row in rows)
        {
            if (inbound.Keyword == SmsInboundKeyword.Stop)
                row.OptOut(inbound.RawKeyword, nowUtc);
            else
                row.OptIn(inbound.RawKeyword, nowUtc);
        }
    }
}
