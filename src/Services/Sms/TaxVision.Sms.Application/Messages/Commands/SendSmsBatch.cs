using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Messages.Abstractions;
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
    string? SourceContext,
    string? RecipientName = null
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
    string? CallerBearerToken = null
);

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
        ISmsProviderRouter router,
        IOptions<SmsOptions> options,
        IOptions<SmsVisibilityOptions> visibility,
        ISmsCustomerAssignmentReader assignments,
        IWalletSpendClient wallet,
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
        var providers = router.ResolveOrder();
        if (providers.Count == 0)
            return Result.Failure<SendSmsBatchResponse>(new Error("sms.noProvider", "No SMS provider is configured."));
        var nowUtc = DateTime.UtcNow;

        // Cobro money-OUT (F6): reservar el saldo ANTES de despachar (solo envío individual; la campaña ya
        // cobró). Se reserva por la cantidad de mensajes del lote; al cerrar se consume lo realmente aceptado
        // y se libera el resto. Si no alcanza, se rechaza el lote entero sin enviar nada.
        if (command.ChargeWallet)
        {
            var reserve = await wallet.ReserveAsync(
                command.TenantId,
                WalletReferenceTypes.IndividualSms,
                batchId,
                new WalletUnitCounts(0, command.Items.Count, 0, 0),
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
                providers,
                messages,
                optOuts,
                bus,
                nowUtc,
                logger,
                ct
            );
            results.Add(result);
        }

        await unitOfWork.SaveChangesAsync(ct);

        // Liquidación del cobro (F6): consume lo realmente aceptado por el proveedor; el resto (suppressed,
        // inválidos, rechazos) se libera. No tira el envío si el settle falla — los mensajes ya salieron; la
        // reserva queda viva para reconciliar. (El envío de campaña no entra acá: ChargeWallet=false.)
        if (command.ChargeWallet)
        {
            var consumed = results.Count(r => r.Status == SmsMessageStatus.Accepted.ToString());
            try
            {
                await wallet.SettleAsync(
                    command.TenantId,
                    WalletReferenceTypes.IndividualSms,
                    batchId,
                    consumed,
                    command.CallerBearerToken,
                    ct
                );
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "SMS batch {BatchId}: wallet settle failed ({Consumed}/{Total}); reservation left open for reconcile.",
                    batchId,
                    consumed,
                    command.Items.Count
                );
            }
        }

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
        IReadOnlyList<ISmsProvider> providers,
        ISmsMessageRepository messages,
        ISmsOptOutRepository optOuts,
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

        var mediaPayload = (item.Media ?? [])
            .Select(m => new SmsMediaPayload(m.Url, m.ContentType, m.FileName, m.SizeBytes))
            .ToList();

        // 2) Gate opt-out: si el número hizo STOP, se persiste como Suppressed (auditable), no se envía.
        var optOut = await optOuts.GetAsync(tenantId, item.CustomerId, phone.Value, ct);
        if (optOut is { IsOptedOut: true })
        {
            var suppressed = BuildMessage(
                tenantId,
                item,
                phone,
                body,
                correlationId,
                batchId,
                primary.Code,
                mediaPayload,
                nowUtc
            );
            if (suppressed.IsFailure)
                return Failed(item, suppressed.Error.Code);
            suppressed.Value.MarkSuppressed("Recipient opted out (STOP).", nowUtc);
            await messages.AddAsync(suppressed.Value, ct);
            await bus.PublishAsync(
                new SmsMessageSuppressedIntegrationEvent
                {
                    TenantId = tenantId,
                    CorrelationId = correlationId,
                    MessageId = suppressed.Value.Id,
                    CustomerId = item.CustomerId,
                    SourceContext = item.SourceContext,
                }
            );
            return ItemResult(suppressed.Value, item, null);
        }

        // 3) Idempotencia: clave explícita o derivada; si ya existe, devuelve el existente sin reenviar.
        var idempotencyKey = string.IsNullOrWhiteSpace(item.IdempotencyKey)
            ? DeriveIdempotencyKey(tenantId, item.CustomerId, phone.Value, body.Value, mediaPayload)
            : item.IdempotencyKey.Trim();

        var existing = await messages.GetByIdempotencyKeyAsync(tenantId, idempotencyKey, ct);
        if (existing is not null)
            return ItemResult(existing, item, null);

        var mediaInputs = mediaPayload
            .Select(m => new SmsMediaInput(m.Url, m.ContentType, m.FileName, m.SizeBytes))
            .ToList();

        // 4) Failover de PLATAFORMA: intenta cada proveedor en orden. Uno que no soporta la media, o
        // que rechaza / está caído, deja paso al siguiente; se recuerda el último error para reportarlo.
        SmsSendResult? accepted = null;
        ISmsProvider? usedProvider = null;
        Error? lastError = null;
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

        foreach (var provider in providers)
        {
            var mediaError = SmsMediaValidator.Validate(provider.Capabilities, mediaPayload);
            if (mediaError is not null)
            {
                lastError = mediaError;
                logger.LogWarning(
                    "SMS provider {Provider} cannot carry the media for {To} ({Code}); trying next.",
                    provider.Code,
                    phone.Value,
                    mediaError.Code
                );
                continue;
            }

            // Un proveedor que LANZA (p. ej. fallo de transporte, bug del adapter) NO debe abortar el lote:
            // se trata como un fallo de ese proveedor y se pasa al siguiente (failover). Abortar aquí haría
            // que Wolverine reintente todo el comando y, con el cobro F6, dejaría una reserva huérfana.
            try
            {
                var sendResult = await provider.SendAsync(sendRequest, ct);
                if (sendResult.IsSuccess && sendResult.Value.Accepted)
                {
                    accepted = sendResult.Value;
                    usedProvider = provider;
                    break;
                }

                lastError = sendResult.IsFailure
                    ? sendResult.Error
                    : new Error(
                        sendResult.Value.ErrorCode ?? SmsErrors.ProviderRejected.Code,
                        sendResult.Value.ErrorMessage ?? string.Empty
                    );
            }
            catch (Exception ex)
            {
                lastError = new Error(SmsErrors.ProviderRejected.Code, ex.Message);
                logger.LogWarning(
                    ex,
                    "SMS provider {Provider} threw sending to {To}; trying next if any.",
                    provider.Code,
                    phone.Value
                );
                continue;
            }

            logger.LogWarning(
                "SMS provider {Provider} did not accept {To} ({Code}); trying next if any.",
                provider.Code,
                phone.Value,
                lastError.Code
            );
        }

        // 5) Persistir con el proveedor que envió (o el primario si todos fallaron) + publicar evento.
        var finalProviderCode = (usedProvider ?? primary).Code;
        var createResult = SmsMessage.Create(
            tenantId,
            item.CustomerId,
            phone,
            body,
            idempotencyKey,
            correlationId,
            batchId,
            finalProviderCode,
            item.SourceContext,
            mediaInputs,
            nowUtc,
            item.RecipientName
        );
        if (createResult.IsFailure)
            return Failed(item, createResult.Error.Code);
        var message = createResult.Value;

        if (accepted is not null)
        {
            message.MarkAccepted(accepted.ProviderMessageId ?? string.Empty, nowUtc);
            await messages.AddAsync(message, ct);
            await bus.PublishAsync(
                new SmsMessageAcceptedIntegrationEvent
                {
                    TenantId = tenantId,
                    CorrelationId = correlationId,
                    MessageId = message.Id,
                    CustomerId = item.CustomerId,
                    SourceContext = item.SourceContext,
                    ProviderMessageId = message.ProviderMessageId,
                }
            );
            return ItemResult(message, item, message.ProviderMessageId);
        }

        var errorCode = lastError?.Code ?? SmsErrors.ProviderRejected.Code;
        message.MarkFailed(nowUtc, errorCode, lastError?.Message);
        await messages.AddAsync(message, ct);
        await PublishFailed(bus, message, item, correlationId, errorCode, ct);
        return ItemResult(message, item, null);
    }

    private static Result<SmsMessage> BuildMessage(
        Guid tenantId,
        SmsSendItemDto item,
        PhoneE164 phone,
        SmsBody body,
        string correlationId,
        Guid batchId,
        string providerCode,
        IReadOnlyList<SmsMediaPayload> media,
        DateTime nowUtc
    ) =>
        SmsMessage.Create(
            tenantId,
            item.CustomerId,
            phone,
            body,
            string.IsNullOrWhiteSpace(item.IdempotencyKey)
                ? DeriveIdempotencyKey(tenantId, item.CustomerId, phone.Value, body.Value, media)
                : item.IdempotencyKey.Trim(),
            correlationId,
            batchId,
            providerCode,
            item.SourceContext,
            media.Select(m => new SmsMediaInput(m.Url, m.ContentType, m.FileName, m.SizeBytes)).ToList(),
            nowUtc,
            item.RecipientName
        );

    private static async Task PublishFailed(
        IMessageBus bus,
        SmsMessage message,
        SmsSendItemDto item,
        string correlationId,
        string? code,
        CancellationToken ct
    ) =>
        await bus.PublishAsync(
            new SmsMessageFailedIntegrationEvent
            {
                TenantId = message.TenantId,
                CorrelationId = correlationId,
                MessageId = message.Id,
                CustomerId = item.CustomerId,
                SourceContext = item.SourceContext,
                ProviderMessageId = message.ProviderMessageId,
                FailureCode = code,
            }
        );

    private static SmsSendItemResult ItemResult(SmsMessage message, SmsSendItemDto item, string? providerMessageId) =>
        new(
            message.Id,
            item.CustomerId,
            message.To,
            message.Status.ToString(),
            providerMessageId ?? message.ProviderMessageId,
            message.FailureCode
        );

    private static SmsSendItemResult Failed(SmsSendItemDto item, string errorCode) =>
        new(null, item.CustomerId, item.To, "Failed", null, errorCode);

    /// <summary>Clave idempotente determinística cuando el caller no la manda. La media se canonicaliza
    /// (orden estable) para que el mismo contenido lógico produzca la misma clave.</summary>
    private static string DeriveIdempotencyKey(
        Guid tenantId,
        Guid customerId,
        string to,
        string body,
        IReadOnlyList<SmsMediaPayload> media
    )
    {
        var mediaCanon = string.Join(
            "|",
            media.Select(m => $"{m.Url}:{m.ContentType}:{m.SizeBytes}").OrderBy(s => s, StringComparer.Ordinal)
        );
        var canonical = $"{tenantId:N}|{customerId:N}|{to}|{body}|{mediaCanon}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return "auto-" + Convert.ToHexStringLower(hash);
    }
}
