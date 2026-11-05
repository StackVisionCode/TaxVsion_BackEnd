using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Customers.Abstractions;
using TaxVision.Sms.Application.Messages.Abstractions;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.ValueObjects;
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
    private static readonly long DerivedKeyWindowTicks = TimeSpan.FromMinutes(10).Ticks;

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

        // Política de contacto del cliente: se resuelve antes de reservar, así lo filtrado no se cobra.
        var smsDisabled = command.EnforceContactPolicy
            ? (
                await contactPolicies.GetSmsDisabledCustomerIdsAsync(
                    command.TenantId,
                    command.Items.Select(i => i.CustomerId).Distinct().ToList(),
                    ct
                )
            ).ToHashSet()
            : [];
        var chargeableCount = command.Items.Count(i => !smsDisabled.Contains(i.CustomerId));
        var chargeWallet = command.ChargeWallet && chargeableCount > 0;

        // Cobro money-OUT (F6): reservar el saldo ANTES de despachar (solo envío individual; la campaña ya
        // cobró). La referencia es el lote: un reintento con la misma clave reusa la reserva del Wallet.
        if (chargeWallet)
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
                return Result.Failure<SendSmsBatchResponse>(SmsErrors.WalletUnavailable);
            if (!reserve.Authorized)
                return Result.Failure<SendSmsBatchResponse>(
                    SmsErrors.InsufficientFunds(reserve.DeficitMicros, reserve.Currency)
                );
        }

        var results = new List<SmsSendItemResult>(command.Items.Count);

        // Visibilidad por asignación: un preparador solo le escribe a sus clientes. Se resuelve el set
        // una vez para todo el lote — no una consulta por item. Null = sin restricción: servicio M2M,
        // `customers.view_all` o el flag apagado.
        var assignedTo = visibility.Value.Enabled && !command.CanViewAll ? command.ActorUserId : (Guid?)null;
        var allowedCustomers = assignedTo is { } sender
            ? (await assignments.GetAssignedCustomerIdsAsync(command.TenantId, sender, ct)).ToHashSet()
            : null;

        foreach (var item in command.Items)
        {
            if (allowedCustomers is not null && !allowedCustomers.Contains(item.CustomerId))
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
                smsDisabled.Contains(item.CustomerId),
                providers,
                bus,
                nowUtc,
                logger,
                ct
            );
            results.Add(result);
        }

        // Liquidación del cobro (F6, D-SMS-3): consume lo que el proveedor aceptó y libera el resto. Si queda
        // un ítem en curso o desconocido, la reserva sigue abierta. Un fallo del Wallet no tira el envío.
        if (chargeWallet)
            await SettleSmsBatchHandler.SettleAsync(
                command.TenantId,
                batchId,
                command.CallerBearerToken,
                messages,
                wallet,
                logger,
                ct
            );

        logger.LogInformation(
            "SMS batch {BatchId} processed {Count} message(s) for tenant {TenantId} (correlation {CorrelationId}).",
            batchId,
            command.Items.Count,
            command.TenantId,
            correlationId
        );

        return Result.Success(new SendSmsBatchResponse(batchId, correlationId, results));
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
        DateTime nowUtc,
        ILogger logger,
        CancellationToken ct
    )
    {
        var primary = providers[0];

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
        var storedBodyResult = SmsBody.Create(SensitiveNotificationContent.ForStorage(item.TemplateKey, body.Value));
        if (storedBodyResult.IsFailure)
            return Failed(item, storedBodyResult.Error.Code);
        var storedBody = storedBodyResult.Value;

        var mediaPayload = (item.Media ?? [])
            .Select(m => new SmsMediaPayload(m.Url, m.ContentType, m.FileName, m.SizeBytes))
            .ToList();

        var idempotencyKey = string.IsNullOrWhiteSpace(item.IdempotencyKey)
            ? DeriveIdempotencyKey(tenantId, item.CustomerId, phone.Value, body.Value, mediaPayload, nowUtc)
            : item.IdempotencyKey.Trim();

        // 2) Fase 1: reserva confirmada (o supresión por STOP o política). Solo el ganador de la clave envía.
        var reservation = await ReserveAsync(
            bus,
            new ReserveSmsMessageCommand(
                tenantId,
                item.CustomerId,
                phone,
                storedBody,
                idempotencyKey,
                correlationId,
                batchId,
                primary.Code,
                item.SourceContext,
                [.. mediaPayload.Select(m => new SmsMediaInput(m.Url, m.ContentType, m.FileName, m.SizeBytes))],
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

        // 3) Fase 2: envío fuera de la transacción.
        var sendRequest = new SmsSendRequest(
            tenantId,
            item.CustomerId,
            phone.Value,
            body.Value,
            mediaPayload,
            correlationId,
            idempotencyKey,
            item.SourceContext
        );
        var (outcome, provider, providerMessageId, error) = await SendWithFailoverAsync(
            providers,
            sendRequest,
            logger,
            ct
        );

        // 4) Fase 3: resultado y evento en su propia transacción. Se registra aunque la petición se haya cancelado.
        var recorded = await bus.InvokeAsync<Result<SmsMessageSnapshot>>(
            new RecordSmsSendResultCommand(
                tenantId,
                reserved.Message.Id,
                outcome,
                (provider ?? primary).Code,
                providerMessageId,
                error?.Code,
                error?.Message
            ),
            CancellationToken.None
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

    /// <summary>
    /// Failover de PLATAFORMA solo si el proveedor dijo que no salió (rechazo, caído, media no soportada). Un
    /// resultado desconocido o la baja en el carrier lo cortan: otro proveedor podría mandar un segundo SMS.
    /// </summary>
    private static async Task<(
        SmsSendOutcome Outcome,
        ISmsProvider? Provider,
        string? ProviderMessageId,
        Error? Error
    )> SendWithFailoverAsync(
        IReadOnlyList<ISmsProvider> providers,
        SmsSendRequest request,
        ILogger logger,
        CancellationToken ct
    )
    {
        Error? lastError = null;
        foreach (var provider in providers)
        {
            var mediaError = SmsMediaValidator.Validate(provider.Capabilities, request.Media);
            if (mediaError is not null)
            {
                lastError = mediaError;
                logger.LogWarning(
                    "SMS provider {Provider} cannot carry the media for {To} ({Code}); trying next.",
                    provider.Code,
                    request.To,
                    mediaError.Code
                );
                continue;
            }

            Result<SmsSendResult> sendResult;
            try
            {
                sendResult = await provider.SendAsync(request, ct);
            }
            catch (Exception ex)
            {
                // Un adapter que lanza pudo haber enviado: resultado desconocido, sin failover.
                logger.LogWarning(
                    ex,
                    "SMS provider {Provider} threw sending to {To}; outcome unknown.",
                    provider.Code,
                    request.To
                );
                return (SmsSendOutcome.OutcomeUnknown, provider, null, SmsErrors.ProviderOutcomeUnknown);
            }

            if (sendResult.IsSuccess && sendResult.Value.Accepted)
                return (SmsSendOutcome.Accepted, provider, sendResult.Value.ProviderMessageId, null);

            lastError = sendResult.IsFailure
                ? sendResult.Error
                : new Error(
                    sendResult.Value.ErrorCode ?? SmsErrors.ProviderRejected.Code,
                    sendResult.Value.ErrorMessage ?? string.Empty
                );
            if (lastError.Code == SmsSendFailure.OutcomeUnknown)
                return (
                    SmsSendOutcome.OutcomeUnknown,
                    provider,
                    sendResult.IsSuccess ? sendResult.Value.ProviderMessageId : null,
                    lastError
                );
            // El carrier ya bloqueó el número: otro proveedor no debe intentarlo.
            if (lastError.Code == SmsSendFailure.RecipientOptedOut)
                return (SmsSendOutcome.RecipientOptedOut, provider, null, lastError);

            logger.LogWarning(
                "SMS provider {Provider} did not accept {To} ({Code}); trying next if any.",
                provider.Code,
                request.To,
                lastError.Code
            );
        }

        return (SmsSendOutcome.Rejected, null, null, lastError ?? SmsErrors.ProviderRejected);
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

    /// <summary>Red de seguridad para llamadas M2M sin clave. Incluye una ventana de 10 min: un reenvío legítimo
    /// más tarde es otra clave. La media se canonicaliza (orden estable).</summary>
    private static string DeriveIdempotencyKey(
        Guid tenantId,
        Guid customerId,
        string to,
        string body,
        IReadOnlyList<SmsMediaPayload> media,
        DateTime nowUtc
    )
    {
        var mediaCanon = string.Join(
            "|",
            media.Select(m => $"{m.Url}:{m.ContentType}:{m.SizeBytes}").OrderBy(s => s, StringComparer.Ordinal)
        );
        var window = nowUtc.Ticks - nowUtc.Ticks % DerivedKeyWindowTicks;
        var canonical = $"{tenantId:N}|{customerId:N}|{to}|{body}|{mediaCanon}|{window}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "auto-" + Convert.ToHexStringLower(hash);
    }
}
