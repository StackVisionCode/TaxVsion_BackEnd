using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Application.Webhooks;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;
using TaxVision.Sms.Domain.Webhooks;
using Wolverine;

namespace TaxVision.Sms.Application.Messages.Commands;

public enum SmsSendOutcome
{
    Accepted,

    /// <summary>El proveedor dijo que no salió (rechazo, caído, media no soportada).</summary>
    Rejected,

    /// <summary>Timeout después de pedirlo: no se sabe si salió.</summary>
    OutcomeUnknown,

    /// <summary>El carrier ya tiene el número dado de baja.</summary>
    RecipientOptedOut,
}

/// <summary>Fase 3 del envío (BB-8): registra lo que dijo el proveedor y publica el evento en la misma transacción.</summary>
public sealed record RecordSmsSendResultCommand(
    Guid TenantId,
    Guid MessageId,
    SmsSendOutcome Outcome,
    string ProviderCode,
    string? ProviderMessageId,
    string? ErrorCode,
    string? ErrorMessage
);

public static class RecordSmsSendResultHandler
{
    private const string CarrierOptOutKeyword = "carrier";

    public static async Task<Result<SmsMessageSnapshot>> Handle(
        RecordSmsSendResultCommand command,
        ISmsMessageRepository messages,
        ISmsOptOutRepository optOuts,
        IPendingDeliveryReceiptRepository pending,
        IProcessedWebhookRepository processed,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var message = await messages.GetByIdAsync(command.TenantId, command.MessageId, ct);
        if (message is null)
            return Result.Failure<SmsMessageSnapshot>(SmsErrors.MessageNotFound);

        // Reintento de un resultado ya registrado: no se vuelve a publicar.
        if (message.Status != SmsMessageStatus.Pending)
            return Result.Success(SmsMessageSnapshot.From(message));

        var nowUtc = DateTime.UtcNow;
        switch (command.Outcome)
        {
            case SmsSendOutcome.Accepted:
                message.MarkAccepted(command.ProviderMessageId ?? string.Empty, nowUtc, command.ProviderCode);
                await ApplyEarlyReceiptsAsync(message, pending, processed, nowUtc, ct);
                break;
            case SmsSendOutcome.OutcomeUnknown:
                message.MarkOutcomeUnknown(
                    nowUtc,
                    command.ProviderMessageId,
                    command.ErrorCode ?? SmsSendFailure.OutcomeUnknown,
                    command.ErrorMessage,
                    command.ProviderCode
                );
                break;
            case SmsSendOutcome.RecipientOptedOut:
                message.MarkSuppressed("Recipient opted out at the carrier.", nowUtc);
                await RecordCarrierOptOutAsync(optOuts, message, nowUtc, ct);
                break;
            default:
                message.MarkFailed(nowUtc, command.ErrorCode ?? SmsErrors.ProviderRejected.Code, command.ErrorMessage);
                break;
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Un DLR adelantado deja el mensaje más allá de Accepted: se avisan los dos pasos, en orden.
        if (command.Outcome == SmsSendOutcome.Accepted && message.Status != SmsMessageStatus.Accepted)
            await SmsDeliveryOutcome.PublishAcceptedAsync(bus, message);
        await PublishAsync(bus, message);
        return Result.Success(SmsMessageSnapshot.From(message));
    }

    // Los DLR que llegaron antes que este commit.
    private static async Task ApplyEarlyReceiptsAsync(
        SmsMessage message,
        IPendingDeliveryReceiptRepository pending,
        IProcessedWebhookRepository processed,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(message.ProviderMessageId))
            return;

        foreach (var receipt in await pending.ListAsync(message.ProviderCode, message.ProviderMessageId, ct))
        {
            SmsDeliveryOutcome.Apply(message, SmsDeliveryOutcome.ToUpdate(receipt), nowUtc);
            await processed.AddAsync(
                new ProcessedWebhook(
                    receipt.ProviderCode,
                    receipt.ProviderMessageId,
                    receipt.EventType,
                    message.TenantId,
                    null,
                    nowUtc
                ),
                ct
            );
            pending.Remove(receipt);
        }
    }

    // Baja del número en el tenant con motivo "carrier": todas sus filas, o una nueva con el cliente del envío.
    private static async Task RecordCarrierOptOutAsync(
        ISmsOptOutRepository optOuts,
        SmsMessage message,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var rows = (await optOuts.ListByTenantAndPhoneAsync(message.TenantId, message.To, ct)).ToList();
        if (rows.Count == 0)
        {
            var phone = PhoneE164.Create(message.To).Value;
            var created = SmsOptOut.CreateSubscribed(message.TenantId, message.CustomerId, phone, nowUtc);
            await optOuts.AddAsync(created, ct);
            rows.Add(created);
        }
        foreach (var row in rows)
            row.OptOut(CarrierOptOutKeyword, nowUtc);
    }

    // OutcomeUnknown no publica: todavía no hay resultado.
    private static async Task PublishAsync(IMessageBus bus, SmsMessage message)
    {
        if (message.Status == SmsMessageStatus.Suppressed)
            await bus.PublishAsync(
                new SmsMessageSuppressedIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = message.CorrelationId,
                    MessageId = message.Id,
                    CustomerId = message.CustomerId,
                    SourceContext = message.SourceContext,
                }
            );
        else
            await SmsDeliveryOutcome.PublishAsync(bus, message);
    }
}
