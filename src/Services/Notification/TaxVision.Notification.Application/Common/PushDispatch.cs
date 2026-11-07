using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

public sealed record PushDispatchRequest(
    Guid TenantId,
    Guid RecipientUserId,
    string Title,
    string Body,
    NotificationCategory Category,
    string Kind,
    string TemplateKey,
    Guid? RelatedEventId,
    string? CorrelationId,
    IReadOnlyDictionary<string, string>? Data = null,
    string? Topic = null,
    string? Tag = null,
    string? Link = null,
    bool SkipWhenNoDevices = false,
    // Reintento dirigido: solo estos dispositivos (los que fallaron de forma transitoria).
    IReadOnlyList<Guid>? DeviceIds = null
);

/// <param name="Error">Null si salió por al menos un dispositivo o no correspondía enviar.</param>
/// <param name="RetryableDeviceIds">Dispositivos con fallo transitorio: se reintenta solo a esos.</param>
/// <param name="FailureCodes">Un código por dispositivo fallido, para métricas.</param>
public sealed record PushDispatchOutcome(
    Error? Error,
    IReadOnlyList<Guid> RetryableDeviceIds,
    IReadOnlyList<string> FailureCodes
)
{
    public static PushDispatchOutcome Done { get; } = new(null, [], []);
}

/// <summary>Canal push: fan-out por dispositivo, un log por envío y reintento dirigido.</summary>
public sealed class PushDispatch(
    IPushSender pushSender,
    IPushDeviceTokenRepository pushDeviceTokens,
    INotificationLogRepository logs,
    DispatchChannelGate gate,
    IUnitOfWork unitOfWork,
    INotificationDispatchMetrics metrics,
    IPushAttemptRecorder pushAttempts,
    ILogger<PushDispatch> logger
)
{
    public const string NoDeviceReason = "no_device";

    /// <summary>
    /// Envía a TODOS los dispositivos activos del usuario (fan-out best-effort:
    /// un solo dispositivo entregado ya cuenta como Sent — mismo criterio que
    /// el multicast nativo de FCM/APNs, para no perder el aviso por un
    /// dispositivo viejo/desinstalado). Falla con `Notification.NoPushDevices`
    /// si el usuario no tiene ningún token registrado (el log queda Skipped, no Failed).
    /// </summary>
    public async Task<Result> SendAsync(PushDispatchRequest request, CancellationToken ct = default)
    {
        var outcome = await DispatchAsync(request, ct);
        return outcome.Error is null ? Result.Success() : Result.Failure(outcome.Error);
    }

    /// <summary>
    /// Igual que <see cref="SendAsync"/>, pero informa qué dispositivos fallaron de forma transitoria para
    /// reintentar solo esos. Un error del proveedor no lanza. Con <see cref="PushDispatchRequest.DeviceIds"/>
    /// envía solo a esos dispositivos, cada uno con su log: el log del evento puede estar Sent por otro
    /// dispositivo y no debe frenar el reintento.
    /// </summary>
    public async Task<PushDispatchOutcome> DispatchAsync(PushDispatchRequest request, CancellationToken ct = default)
    {
        if (!await AllowsAsync(request.TenantId, request.RecipientUserId, request.Category, request.Kind, ct))
            return PushDispatchOutcome.Done;

        var devices = await pushDeviceTokens.ListActiveForUserAsync(request.TenantId, request.RecipientUserId, ct);
        if (request.DeviceIds is { Count: > 0 } targets)
            return await RetryDevicesAsync(request, devices.Where(device => targets.Contains(device.Id)), ct);
        if (devices.Count == 0 && request.SkipWhenNoDevices)
        {
            metrics.RecordNoRecipient(request.Kind, "Push");
            return PushDispatchOutcome.Done;
        }
        return await SendToDevicesAsync(
            request,
            devices,
            NotificationRecipient.ForPushTarget(request.RecipientUserId),
            ct
        );
    }

    /// <summary>
    /// Preferencia y dispositivos, sin escribir log: permite omitir el render del push cuando no saldría.
    /// <see cref="SendAsync"/> repite estos gates al enviar.
    /// </summary>
    public async Task<bool> CanSendAsync(
        Guid tenantId,
        Guid recipientUserId,
        NotificationCategory category,
        string kind,
        CancellationToken ct = default
    )
    {
        if (!await AllowsAsync(tenantId, recipientUserId, category, kind, ct))
            return false;
        var devices = await pushDeviceTokens.ListActiveForUserAsync(tenantId, recipientUserId, ct);
        if (devices.Count > 0)
            return true;
        metrics.RecordNoRecipient(kind, "Push");
        return false;
    }

    private Task<bool> AllowsAsync(
        Guid tenantId,
        Guid recipientUserId,
        NotificationCategory category,
        string kind,
        CancellationToken ct
    ) => gate.AllowsAsync(tenantId, recipientUserId, recipientUserId, category, NotificationChannel.Push, kind, ct);

    // Un dispositivo revocado o dado de baja entre intentos simplemente ya no está en la lista.
    private async Task<PushDispatchOutcome> RetryDevicesAsync(
        PushDispatchRequest request,
        IEnumerable<PushDeviceToken> devices,
        CancellationToken ct
    )
    {
        var retryable = new List<Guid>();
        var codes = new List<string>();
        Error? error = null;
        foreach (var device in devices)
        {
            var recipient = NotificationRecipient.ForDevice(request.RecipientUserId, device.Id);
            var outcome = await SendToDevicesAsync(request, [device], recipient, ct);
            retryable.AddRange(outcome.RetryableDeviceIds);
            codes.AddRange(outcome.FailureCodes);
            error ??= outcome.Error;
        }
        return new(error, retryable, codes);
    }

    private async Task<PushDispatchOutcome> SendToDevicesAsync(
        PushDispatchRequest request,
        IReadOnlyList<PushDeviceToken> devices,
        NotificationRecipient recipient,
        CancellationToken ct
    )
    {
        if (request.RelatedEventId is { } eventId && await WasSentAsync(request, eventId, recipient, ct))
            return PushDispatchOutcome.Done;

        var logResult = NotificationLog.Create(
            request.TenantId,
            NotificationChannel.Push,
            recipient,
            request.Title,
            request.TemplateKey,
            request.RelatedEventId,
            request.CorrelationId,
            request.Kind
        );
        if (logResult.IsFailure)
        {
            logger.LogError(
                "Invalid notification log for template {TemplateKey}: {Error}",
                request.TemplateKey,
                logResult.Error.Message
            );
            return new(logResult.Error, [], []);
        }

        var log = logResult.Value;

        // Sin dispositivo no hay entrega posible: Skipped, no Failed. El error sigue saliendo para Campaigns.
        if (devices.Count == 0)
        {
            var error = new Error("Notification.NoPushDevices", "Recipient has no registered push devices.");
            await logs.AddAsync(log, ct);
            log.MarkSkipped(NoDeviceReason);
            metrics.RecordNoRecipient(request.Kind, "Push");
            await unitOfWork.SaveChangesAsync(ct);
            return new(error, [], []);
        }

        await pushAttempts.RecordAttemptAsync(log, ct);

        var anySucceeded = false;
        var failures = new List<(Guid DeviceId, Error Error)>();
        foreach (var device in devices)
        {
            var sendResult = await SendToDeviceAsync(request, device, ct);
            if (sendResult.IsSuccess)
            {
                anySucceeded = true;
                continue;
            }
            failures.Add((device.Id, sendResult.Error));
            logger.LogWarning(
                "Push to device {DeviceId} for user {UserId} failed: {Error}",
                device.Id,
                request.RecipientUserId,
                sendResult.Error.Message
            );

            // Fase 7 — el proveedor (FcmPushSender) confirma que el token ya no es
            // entregable (app desinstalada/token expirado). Se revoca acá, no en el sender,
            // porque solo el dispatcher tiene el device.Id resuelto de la proyección local.
            if (sendResult.Error.Code == PushErrorCodes.TokenInvalid)
                await pushDeviceTokens.RevokeAsync(request.TenantId, device.Id, ct);
        }

        var retryable = failures
            .Where(failure => PushErrorCodes.IsTransient(failure.Error.Code))
            .Select(failure => failure.DeviceId)
            .ToList();
        var codes = failures.Select(failure => failure.Error.Code).ToList();
        if (anySucceeded)
        {
            await pushAttempts.RecordSentAsync(request.TenantId, log.Id, CancellationToken.None);
            metrics.RecordSent(request.Kind, "Push");
            await unitOfWork.SaveChangesAsync(ct);
            return new(null, retryable, codes);
        }

        // Si algún dispositivo falló de forma transitoria, su código manda: el envío se puede reintentar.
        var failed =
            failures.FirstOrDefault(failure => PushErrorCodes.IsTransient(failure.Error.Code)).Error
            ?? failures[^1].Error;
        await pushAttempts.RecordFailedAsync(request.TenantId, log.Id, failed.Message, CancellationToken.None);
        metrics.RecordFailed(request.Kind, "Push");
        await unitOfWork.SaveChangesAsync(ct);
        return new(failed, retryable, codes);
    }

    // Una excepción del proveedor (red, timeout) cuenta como no disponible para ese dispositivo.
    private async Task<Result> SendToDeviceAsync(
        PushDispatchRequest request,
        PushDeviceToken device,
        CancellationToken ct
    )
    {
        try
        {
            return await pushSender.SendAsync(
                new PushMessage(
                    device.Token,
                    device.Platform,
                    request.Title,
                    request.Body,
                    request.Data,
                    request.Topic,
                    request.Tag,
                    request.Link
                ),
                ct
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Result.Failure(new Error(PushErrorCodes.Unavailable, ex.Message));
        }
    }

    private async Task<bool> WasSentAsync(
        PushDispatchRequest request,
        Guid eventId,
        NotificationRecipient recipient,
        CancellationToken ct
    )
    {
        var existing = await logs.GetByRelatedEventAsync(
            request.TenantId,
            eventId,
            request.Kind,
            NotificationChannel.Push,
            recipient.Value,
            ct
        );
        // Pending = se intentó y el resultado no quedó registrado: pudo salir, así que no se manda de nuevo.
        return existing?.Status
            is NotificationStatus.Sent
                or NotificationStatus.Delivered
                or NotificationStatus.Pending;
    }
}
