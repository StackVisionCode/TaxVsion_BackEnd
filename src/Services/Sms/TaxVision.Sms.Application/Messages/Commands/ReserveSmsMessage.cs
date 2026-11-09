using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Sms.Application.Messages.Commands;

/// <summary>Fase 1 del envío (BB-8): guarda la fila con su clave antes de llamar al proveedor y confirma sola.</summary>
public sealed record ReserveSmsMessageCommand(
    Guid TenantId,
    Guid CustomerId,
    PhoneE164 To,
    SmsBody StoredBody,
    string IdempotencyKey,
    string CorrelationId,
    Guid BatchId,
    string ProviderCode,
    string? SourceContext,
    IReadOnlyList<SmsMediaInput> Media,
    string? RecipientName,
    bool WalletCharged,
    // El cliente apagó los SMS en su política de contacto: la fila nace Suppressed y no se envía.
    bool BlockedByContactPolicy = false
);

public enum SmsReservationOutcome
{
    /// <summary>Fila nueva en Pending: este intento envía.</summary>
    Reserved,

    /// <summary>Número dado de baja o política de contacto apagada: fila Suppressed, no se envía.</summary>
    Suppressed,

    /// <summary>La clave ya existía (reintento o carrera perdida): no se envía.</summary>
    Existing,
}

/// <summary>Lo que el orquestador necesita de la fila, sin compartir la entidad rastreada de otro scope.</summary>
public sealed record SmsMessageSnapshot(
    Guid Id,
    Guid BatchId,
    string To,
    SmsMessageStatus Status,
    string? ProviderMessageId,
    string? FailureCode
)
{
    public static SmsMessageSnapshot From(SmsMessage message) =>
        new(message.Id, message.BatchId, message.To, message.Status, message.ProviderMessageId, message.FailureCode);
}

public sealed record ReserveSmsMessageResponse(SmsReservationOutcome Outcome, SmsMessageSnapshot Message);

public static class ReserveSmsMessageHandler
{
    public static async Task<Result<ReserveSmsMessageResponse>> Handle(
        ReserveSmsMessageCommand command,
        ISmsMessageRepository messages,
        ISmsOptOutRepository optOuts,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var existing = await messages.GetByIdempotencyKeyAsync(command.TenantId, command.IdempotencyKey, ct);
        if (existing is not null)
            return Respond(SmsReservationOutcome.Existing, existing);

        var nowUtc = DateTime.UtcNow;
        var created = SmsMessage.Create(
            command.TenantId,
            command.CustomerId,
            command.To,
            command.StoredBody,
            command.IdempotencyKey,
            command.CorrelationId,
            command.BatchId,
            command.ProviderCode,
            command.SourceContext,
            command.Media,
            nowUtc,
            command.RecipientName,
            command.WalletCharged
        );
        if (created.IsFailure)
            return Result.Failure<ReserveSmsMessageResponse>(created.Error);
        var message = created.Value;

        await SmsSendGate.ApplySuppressionAsync(message, command.BlockedByContactPolicy, optOuts, nowUtc, ct);

        if (message.Status == SmsMessageStatus.Suppressed)
        {
            await messages.AddAsync(message, ct);
            await unitOfWork.SaveChangesAsync(ct);
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
            return Respond(SmsReservationOutcome.Suppressed, message);
        }

        // El índice único (tenant, clave) decide al ganador; el perdedor recibe ConflictException.
        message.MarkSendAttempted(nowUtc);
        await messages.AddAsync(message, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Respond(SmsReservationOutcome.Reserved, message);
    }

    private static Result<ReserveSmsMessageResponse> Respond(SmsReservationOutcome outcome, SmsMessage message) =>
        Result.Success(new ReserveSmsMessageResponse(outcome, SmsMessageSnapshot.From(message)));
}
