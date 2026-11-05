using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.OptOut;
using TaxVision.Sms.Domain.ValueObjects;
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
        await PublishAsync(bus, message);
        return Result.Success(SmsMessageSnapshot.From(message));
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
        switch (message.Status)
        {
            case SmsMessageStatus.Accepted:
                await bus.PublishAsync(
                    new SmsMessageAcceptedIntegrationEvent
                    {
                        TenantId = message.TenantId,
                        CorrelationId = message.CorrelationId,
                        MessageId = message.Id,
                        CustomerId = message.CustomerId,
                        SourceContext = message.SourceContext,
                        ProviderMessageId = message.ProviderMessageId,
                    }
                );
                break;
            case SmsMessageStatus.Suppressed:
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
                break;
            case SmsMessageStatus.Failed:
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
                break;
        }
    }
}
