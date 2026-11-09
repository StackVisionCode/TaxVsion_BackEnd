using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Application.Messages.Abstractions;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Application.Webhooks;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.Messages;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Sms.Application.Messages.Commands;

public sealed record SmsMediaDto(string Url, string ContentType, string? FileName, long? SizeBytes);

public sealed record SmsSendItemDto(
    Guid CustomerId,
    string To,
    string Message,
    IReadOnlyList<SmsMediaDto>? Media,
    string? IdempotencyKey,
    string? SourceContext,
    string? RecipientName = null,
    string? TemplateKey = null
);

/// <summary>
/// Envío de 1..N mensajes. TenantId y CorrelationId los pone el controller (JWT + header).
/// <paramref name="ActorUserId"/> es null cuando quien envía es un servicio (M2M): no hay usuario cuyo
/// alcance medir, y la campaña que dispara el envío ya resolvió su propia audiencia.
/// </summary>
public sealed record SendSmsBatchCommand(
    Guid TenantId,
    string CorrelationId,
    IReadOnlyList<SmsSendItemDto> Items,
    Guid? ActorUserId,
    bool CanViewAll,
    // Cobro money-OUT (F6): SOLO el envío individual (MessagesController) lo prende. El dispatch de campaña
    // deja esto en false — ya lo cobró Campaigns (referenceType="campaign-run"); así no se cobra dos veces.
    bool ChargeWallet = false,
    // Bearer de la sesión para reservar on-behalf-of en el Wallet; null → el cliente usa M2M del tenant.
    string? CallerBearerToken = null,
    // Clave estable de la petición (Idempotency-Key): un reintento reusa el mismo lote y la misma reserva.
    string? BatchKey = null,
    // Respeta la política de contacto del cliente (envío manual y campañas). Los avisos transaccionales
    // (OTP, firma) la dejan en false: Notification ya decide cuáles se filtran.
    bool EnforceContactPolicy = false
);

/// <summary><paramref name="Reused"/> = el resultado viene de una fila que ya existía (no se volvió a enviar).</summary>
public sealed record SmsSendItemResult(
    Guid? MessageId,
    Guid CustomerId,
    string To,
    string Status,
    string? ProviderMessageId,
    string? ErrorCode,
    bool Reused = false
);

public sealed record SendSmsBatchResponse(Guid BatchId, string CorrelationId, IReadOnlyList<SmsSendItemResult> Results);

/// <summary>
/// Orquestador sin transacción (BB-8): por ítem reserva la fila (fase 1, confirma sola), llama al proveedor
/// fuera de la transacción (fase 2) y registra el resultado con su evento (fase 3). El Wallet también queda fuera.
/// </summary>
public static class SendSmsBatchHandler
{
    [NonTransactional]
    public static async Task<Result<SendSmsBatchResponse>> Handle(
        SendSmsBatchCommand command,
        ISmsMessageRepository messages,
        ISmsProviderRouter router,
        IOptions<SmsOptions> options,
        IOptions<SmsVisibilityOptions> visibility,
        ISmsCustomerAssignmentReader assignments,
        ICustomerContactPolicyRepository contactPolicies,
        IWalletSpendClient wallet,
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
        var batchId = string.IsNullOrWhiteSpace(command.BatchKey)
            ? Guid.NewGuid()
            : BatchIdFor(command.TenantId, command.BatchKey);
        var providers = router.ResolveOrder();
        if (providers.Count == 0)
            return Result.Failure<SendSmsBatchResponse>(new Error("sms.noProvider", "No SMS provider is configured."));
        var nowUtc = DateTime.UtcNow;

        var gate = await SmsSendGate.ForContactPolicyAsync(command, contactPolicies, ct);
        var chargeableCount = gate.ChargeableCount(command.Items);
        var chargeWallet = command.ChargeWallet && chargeableCount > 0;

        if (chargeWallet)
        {
            var reserveError = await ReserveWalletAsync(command, batchId, chargeableCount, wallet, ct);
            if (reserveError is not null)
                return Result.Failure<SendSmsBatchResponse>(reserveError);
        }

        await gate.LoadAssignmentsAsync(command, visibility.Value, assignments, ct);

        var results = new List<SmsSendItemResult>(command.Items.Count);
        foreach (var item in command.Items)
        {
            if (!gate.IsAssigned(item.CustomerId))
            {
                results.Add(Failed(item, SmsErrors.CustomerNotAssigned.Code));
                continue;
            }

            var result = await ProcessItemAsync(
                item,
                command.TenantId,
                correlationId,
                batchId,
                chargeWallet,
                gate.BlockedByContactPolicy(item.CustomerId),
                providers,
                bus,
                options.Value.ConflictRetryAttempts,
                nowUtc,
                logger,
                ct
            );
            results.Add(result);
        }

        if (chargeWallet)
            await SettleWalletAsync(command, batchId, messages, wallet, bus, logger, ct);

        logger.LogInformation(
            "SMS batch {BatchId} processed {Count} message(s) for tenant {TenantId} (correlation {CorrelationId}).",
            batchId,
            command.Items.Count,
            command.TenantId,
            correlationId
        );

        return Result.Success(new SendSmsBatchResponse(batchId, correlationId, results));
    }

    // Cobro money-OUT (F6): se reserva el saldo ANTES de despachar (solo envío individual; la campaña ya cobró). La
    // referencia es el lote: un reintento con la misma clave reusa la reserva del Wallet.
    private static async Task<Error?> ReserveWalletAsync(
        SendSmsBatchCommand command,
        Guid batchId,
        int chargeableCount,
        IWalletSpendClient wallet,
        CancellationToken ct
    )
    {
        var reserve = await wallet.ReserveAsync(
            command.TenantId,
            WalletReferenceTypes.IndividualSms,
            batchId,
            new WalletUnitCounts(0, chargeableCount, 0, 0),
            command.CallerBearerToken,
            ct
        );
        if (!reserve.Reachable)
            return SmsErrors.WalletUnavailable;

        return reserve.Authorized ? null : SmsErrors.InsufficientFunds(reserve.DeficitMicros, reserve.Currency);
    }

    // Liquidación del cobro (F6, D-SMS-3): consume lo que el proveedor aceptó y libera el resto. Si queda un ítem en
    // curso o desconocido, la reserva sigue abierta. Un fallo del Wallet no tira el envío: pasa a la cola durable.
    private static async Task SettleWalletAsync(
        SendSmsBatchCommand command,
        Guid batchId,
        ISmsMessageRepository messages,
        IWalletSpendClient wallet,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        try
        {
            await SettleSmsBatchHandler.SettleAsync(
                command.TenantId,
                batchId,
                command.CallerBearerToken,
                messages,
                wallet,
                logger,
                ct
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SMS batch {BatchId}: wallet settle failed; queued for retry.", batchId);
            await bus.PublishAsync(new SettleSmsBatchCommand(command.TenantId, batchId));
        }
    }

    /// <summary>Id de lote determinístico a partir de (tenant, clave de la petición).</summary>
    public static Guid BatchIdFor(Guid tenantId, string batchKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{tenantId:N}|{batchKey.Trim()}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static async Task<SmsSendItemResult> ProcessItemAsync(
        SmsSendItemDto item,
        Guid tenantId,
        string correlationId,
        Guid batchId,
        bool walletCharged,
        bool blockedByContactPolicy,
        IReadOnlyList<ISmsProvider> providers,
        IMessageBus bus,
        int conflictRetryAttempts,
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct
    )
    {
        var primary = providers[0];

        var prepared = SmsOutgoingItem.Prepare(item, tenantId, nowUtc);
        if (prepared.IsFailure)
            return Failed(item, prepared.Error.Code);
        var outgoing = prepared.Value;

        // Fase 1: reserva confirmada (o supresión por STOP o política). Solo el ganador de la clave envía.
        var reservation = await ReserveAsync(
            bus,
            new ReserveSmsMessageCommand(
                tenantId,
                item.CustomerId,
                outgoing.Phone,
                outgoing.StoredBody,
                outgoing.IdempotencyKey,
                correlationId,
                batchId,
                primary.Code,
                item.SourceContext,
                [.. outgoing.Media.Select(m => new SmsMediaInput(m.Url, m.ContentType, m.FileName, m.SizeBytes))],
                item.RecipientName,
                walletCharged,
                blockedByContactPolicy
            ),
            ct
        );
        if (reservation.IsFailure)
            return Failed(item, reservation.Error.Code);
        var reserved = reservation.Value;
        if (reserved.Outcome != SmsReservationOutcome.Reserved)
            return ItemResult(reserved.Message, item, reused: reserved.Outcome == SmsReservationOutcome.Existing);

        // Fase 2: envío fuera de la transacción.
        var attempt = await SmsFailoverSender.SendAsync(
            providers,
            new SmsSendRequest(
                tenantId,
                item.CustomerId,
                outgoing.Phone.Value,
                outgoing.Body.Value,
                outgoing.Media,
                correlationId,
                outgoing.IdempotencyKey,
                item.SourceContext
            ),
            logger,
            ct
        );

        // Fase 3: resultado y evento en su propia transacción. Se registra aunque la petición se haya cancelado.
        // El SMS ya salió: un choque con la conciliación no puede cortar el lote.
        var recorded = await SmsConflictRetry.InvokeAsync(
            () =>
                bus.InvokeAsync<Result<SmsMessageSnapshot>>(
                    new RecordSmsSendResultCommand(
                        tenantId,
                        reserved.Message.Id,
                        attempt.Outcome,
                        (attempt.Provider ?? primary).Code,
                        attempt.ProviderMessageId,
                        attempt.Error?.Code,
                        attempt.Error?.Message
                    ),
                    CancellationToken.None
                ),
            conflictRetryAttempts
        );
        return recorded.IsSuccess ? ItemResult(recorded.Value, item, reused: false) : Failed(item, recorded.Error.Code);
    }

    private static async Task<Result<ReserveSmsMessageResponse>> ReserveAsync(
        IMessageBus bus,
        ReserveSmsMessageCommand command,
        CancellationToken ct
    )
    {
        try
        {
            return await bus.InvokeAsync<Result<ReserveSmsMessageResponse>>(command, ct);
        }
        catch (ConflictException)
        {
            // Otro intento con la misma clave ganó la carrera: se relee su fila y no se envía.
            return await bus.InvokeAsync<Result<ReserveSmsMessageResponse>>(command, ct);
        }
    }

    private static SmsSendItemResult ItemResult(SmsMessageSnapshot message, SmsSendItemDto item, bool reused) =>
        new(
            message.Id,
            item.CustomerId,
            message.To,
            message.Status.ToString(),
            message.ProviderMessageId,
            message.FailureCode,
            reused
        );

    private static SmsSendItemResult Failed(SmsSendItemDto item, string errorCode) =>
        new(null, item.CustomerId, item.To, "Failed", null, errorCode);
}
