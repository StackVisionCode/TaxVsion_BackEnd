using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume el evento genérico <see cref="SignerVerificationChallengeIssuedIntegrationEvent"/>
/// y despacha por el canal apropiado según <c>Method</c>. Cada método tiene su fase de
/// dispatch en un método privado — sin acumular lógica en un switch gigante.
///
/// <para>
/// Cuando se sume un microservicio dedicado (SmsGateway con Twilio, WhatsApp Business API,
/// KBA provider), este consumer NO cambia: solo se cambia la implementación de
/// <c>ISmsSender</c> por una que hable con el proveedor, o se quita este consumer para
/// SmsOtp/WhatsAppOtp y se deja al nuevo microservicio suscribirse. Ambas opciones son
/// compatibles con el contrato del evento.
/// </para>
/// </summary>
public static class SignerVerificationChallengeIssuedConsumer
{
    private const string TemplateKey = "signature.verification-challenge";

    public static async Task Handle(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        IEmailSender emailSender,
        ISmsSender smsSender,
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        INotificationLogRepository logRepository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SignerVerificationChallengeIssuedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        var correlationId = ResolveCorrelationId(evt);
        using (correlation.Push(correlationId))
        {
            var channel = ChannelFor(evt.Method);
            // AppPush no tiene "direccion" de entrega (email/telefono) — el
            // destino real son los tokens de dispositivo del signer. Usamos su
            // Id como identificador de log, igual que el resto de canales
            // registran el "recipient" real de ese canal.
            var recipient = channel == NotificationChannel.Push ? evt.SignerId.ToString() : evt.DeliveryAddress;
            var logResult = NotificationLog.Create(
                evt.TenantId,
                channel,
                recipient,
                BuildSubject(evt),
                TemplateKey,
                evt.EventId,
                correlationId
            );
            if (logResult.IsFailure)
            {
                logger.LogWarning(
                    "Verification challenge log could not be created for signer {SignerId} method {Method}: {Error}",
                    evt.SignerId,
                    evt.Method,
                    logResult.Error.Message
                );
                return;
            }

            var log = logResult.Value;
            await logRepository.AddAsync(log, ct);

            var deliver = await DispatchAsync(evt, emailSender, smsSender, pushSender, pushDeviceTokens, ct);
            ApplyOutcomeToLog(deliver, log, logger, evt);
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    // ------------------------------------------------------------------
    // Métodos privados: cada uno una única responsabilidad
    // ------------------------------------------------------------------

    private static string ResolveCorrelationId(SignerVerificationChallengeIssuedIntegrationEvent evt) =>
        string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId;

    private static NotificationChannel ChannelFor(string method) =>
        method switch
        {
            "SmsOtp" => NotificationChannel.Sms,
            "WhatsAppOtp" => NotificationChannel.Sms,
            "EmailOtp" => NotificationChannel.Email,
            "KbaQuiz" => NotificationChannel.Email,
            "AppPush" => NotificationChannel.Push,
            _ => NotificationChannel.InApp,
        };

    private static string BuildSubject(SignerVerificationChallengeIssuedIntegrationEvent evt) =>
        evt.SignerLanguage == "Es" ? "TaxVision — Código de verificación" : "TaxVision — Verification code";

    private static Task<Result> DispatchAsync(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        IEmailSender emailSender,
        ISmsSender smsSender,
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        CancellationToken ct
    ) =>
        evt.Method switch
        {
            "SmsOtp" => SendSmsAsync(evt, smsSender, ct),
            "WhatsAppOtp" => SendSmsAsync(evt, smsSender, ct), // fallback provisional hasta ms WhatsApp dedicado
            "EmailOtp" => SendEmailOtpAsync(evt, emailSender, ct),
            "KbaQuiz" => SendKbaLinkAsync(evt, emailSender, ct),
            "AppPush" => SendAppPushAsync(evt, pushSender, pushDeviceTokens, ct),
            _ => Task.FromResult(
                Result.Failure(new Error("Notification.UnknownMethod", "Verification method not supported."))
            ),
        };

    private static Task<Result> SendSmsAsync(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        ISmsSender smsSender,
        CancellationToken ct
    )
    {
        var body =
            evt.SignerLanguage == "Es"
                ? $"Tu codigo TaxVision: {evt.PlaintextAnswer}. Vence a las {evt.ExpiresAtUtc:HH:mm} UTC."
                : $"Your TaxVision code: {evt.PlaintextAnswer}. Expires at {evt.ExpiresAtUtc:HH:mm} UTC.";
        return smsSender.SendAsync(evt.DeliveryAddress, body, ct);
    }

    private static Task<Result> SendEmailOtpAsync(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        IEmailSender emailSender,
        CancellationToken ct
    )
    {
        var subject = BuildSubject(evt);
        var (html, text) = BuildOtpEmail(evt);
        return emailSender.SendAsync(new EmailMessage(evt.DeliveryAddress, subject, html, text), ct);
    }

    private static Task<Result> SendKbaLinkAsync(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        IEmailSender emailSender,
        CancellationToken ct
    )
    {
        // Placeholder: cuando se integre el proveedor KBA, la respuesta será un token de sesión
        // hacia el flujo KBA en el frontend. Por ahora solo notificamos.
        var subject =
            evt.SignerLanguage == "Es"
                ? "TaxVision — Verificación de identidad requerida"
                : "TaxVision — Identity verification required";
        var body =
            evt.SignerLanguage == "Es"
                ? $"<p>Hola {evt.SignerFullName},</p><p>Necesitas completar un cuestionario de verificación de identidad antes de firmar.</p>"
                : $"<p>Hi {evt.SignerFullName},</p><p>You need to complete an identity verification questionnaire before signing.</p>";
        return emailSender.SendAsync(new EmailMessage(evt.DeliveryAddress, subject, body, body), ct);
    }

    /// <summary>
    /// Push al signer via sus tokens de dispositivo registrados — ver
    /// <c>IPushDeviceTokenRepository</c>. IMPORTANTE: hoy NO existe ningun
    /// flujo publico de registro de dispositivo para signers (el link de
    /// firma es web, sin sesion autenticada que permita registrar un token);
    /// este metodo siempre resuelve "sin dispositivos" hasta que ese flujo se
    /// construya. La infraestructura de envio (IPushSender +
    /// IPushDeviceTokenRepository) es real y reusable — lo que falta es
    /// exclusivamente el registro de token del lado del signer.
    /// </summary>
    private static async Task<Result> SendAppPushAsync(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        CancellationToken ct
    )
    {
        var devices = await pushDeviceTokens.ListActiveForUserAsync(evt.TenantId, evt.SignerId, ct);
        if (devices.Count == 0)
            return Result.Failure(
                new Error(
                    "Notification.NoPushDevices",
                    "Signer has no registered push devices — public signer device registration is not implemented yet."
                )
            );

        var title =
            evt.SignerLanguage == "Es" ? "TaxVision — Verificación requerida" : "TaxVision — Verification required";
        var body =
            evt.SignerLanguage == "Es"
                ? "Abre la app para aprobar tu firma."
                : "Open the app to approve your signature.";

        var anySucceeded = false;
        var lastError = new Error("Notification.PushFailed", "All device deliveries failed.");
        foreach (var device in devices)
        {
            var result = await pushSender.SendAsync(new PushMessage(device.Token, device.Platform, title, body), ct);
            if (result.IsSuccess)
                anySucceeded = true;
            else
                lastError = result.Error;
        }
        return anySucceeded ? Result.Success() : Result.Failure(lastError);
    }

    private static (string Html, string Text) BuildOtpEmail(SignerVerificationChallengeIssuedIntegrationEvent evt)
    {
        var isSpanish = evt.SignerLanguage == "Es";
        var greeting = isSpanish ? "Hola" : "Hi";
        var line1 = isSpanish
            ? $"Tu código de verificación TaxVision es <strong>{evt.PlaintextAnswer}</strong>."
            : $"Your TaxVision verification code is <strong>{evt.PlaintextAnswer}</strong>.";
        var line2 = isSpanish
            ? $"El código vence el {evt.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC."
            : $"This code expires on {evt.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC.";
        var line3 = isSpanish
            ? "Si no solicitaste este código, ignora este correo."
            : "If you did not request this code, please ignore this email.";
        var html = $"<p>{greeting} {evt.SignerFullName},</p><p>{line1}</p><p>{line2}</p><p>{line3}</p>";
        var text =
            $"{greeting} {evt.SignerFullName},\n\n{line1.Replace("<strong>", string.Empty).Replace("</strong>", string.Empty)}\n{line2}\n{line3}";
        return (html, text);
    }

    private static void ApplyOutcomeToLog(
        Result outcome,
        NotificationLog log,
        ILogger logger,
        SignerVerificationChallengeIssuedIntegrationEvent evt
    )
    {
        if (outcome.IsSuccess)
        {
            log.MarkSent();
            logger.LogInformation(
                "Verification challenge dispatched to signer {SignerId} via {Method}.",
                evt.SignerId,
                evt.Method
            );
        }
        else
        {
            log.MarkFailed(outcome.Error.Message);
            logger.LogWarning(
                "Verification challenge dispatch failed for signer {SignerId} via {Method}: {Error}",
                evt.SignerId,
                evt.Method,
                outcome.Error.Message
            );
        }
    }
}
