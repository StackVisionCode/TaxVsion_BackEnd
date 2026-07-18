using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Consumers.Signature;

/// <summary>
/// Consume el evento genérico <see cref="SignerVerificationChallengeIssuedIntegrationEvent"/>
/// y despacha por el canal apropiado según <c>Method</c>.
///
/// <para>
/// Refactor Notifications Fase 3: las 2 ramas de email (EmailOtp, KbaQuiz) pasan por
/// <see cref="IEmailDispatchGateway"/>. Las ramas SMS/WhatsApp/AppPush siguen con el flujo manual
/// (log + sender + SaveChanges) porque el gateway hoy solo cubre email — los otros canales tienen
/// pendiente su propio abstracto (Fase 9+).
/// </para>
/// </summary>
public static class SignerVerificationChallengeIssuedConsumer
{
    // Fase 8: unificado con SignatureTemplateCatalog.VerificationChallengeKey — el literal previo
    // "signature.verification-challenge" no coincidía con la convención "sig.*.v1" del resto del
    // catálogo (drift detectado al migrar a Scribe); se corrige acá para las 5 ramas de canal.
    private const string TemplateKey = SignatureTemplateCatalog.VerificationChallengeKey;

    public static async Task Handle(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
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
            if (evt.Method is "EmailOtp" or "KbaQuiz")
            {
                await DispatchEmailAsync(evt, gateway, scribeClient, correlationId, logger, ct);
                return;
            }

            // Canales SMS/Push: se maneja con log manual + sender directo hasta que exista abstracto.
            await DispatchNonEmailAsync(
                evt,
                smsSender,
                pushSender,
                pushDeviceTokens,
                logRepository,
                unitOfWork,
                correlationId,
                logger,
                ct
            );
        }
    }

    // ------------------------------------------------------------------
    // Email path — via IEmailDispatchGateway (Fase 3+)
    // ------------------------------------------------------------------

    private static async Task DispatchEmailAsync(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        string correlationId,
        ILogger logger,
        CancellationToken ct
    )
    {
        string subject,
            html;
        string? text;
        IReadOnlyList<EmailInlineAssetReference> inlineAssets;
        if (evt.Method == "EmailOtp")
        {
            // Fase 8: el contenido OTP ya no se arma localmente — se renderiza en Scribe.
            // Hardening Fase 7: un OTP de firma es tiempo-sensible; un render fallido silenciado
            // acá dejaba al firmante sin código y sin ningún rastro del fallo. EnsureRendered lanza
            // para que Wolverine reintente en vez de completar sin haber enviado el OTP.
            var render = (
                await scribeClient.RenderAsync(
                    "sig.verification_challenge_issued.v1",
                    evt.TenantId,
                    new Dictionary<string, object?>
                    {
                        ["full_name"] = evt.SignerFullName,
                        ["code"] = evt.PlaintextAnswer,
                        ["expires_at"] = evt.ExpiresAtUtc.ToString("yyyy-MM-dd HH:mm"),
                        ["language"] = evt.SignerLanguage,
                    },
                    ct
                )
            ).EnsureRendered("sig.verification_challenge_issued.v1");
            (subject, html, text) = (render.Subject, render.Html, render.Text);
            inlineAssets = render.InlineAssets;
        }
        else
        {
            // KbaQuiz: mensaje simple, único, sin catálogo — se queda igual que antes. Nunca pasa por
            // Scribe, así que no hay logo que propagar (Hardening Fase 9).
            (subject, html, text) = BuildKbaEmail(evt);
            inlineAssets = [];
        }

        var result = await gateway.QueueEmailAsync(
            new EmailDispatchRequest(
                TenantId: evt.TenantId,
                To: evt.DeliveryAddress,
                Subject: subject,
                HtmlBody: html,
                TextBody: text ?? string.Empty,
                TemplateKey: TemplateKey,
                RelatedEventId: evt.EventId,
                CorrelationId: correlationId,
                InlineAssets: inlineAssets
            ),
            ct
        );

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "Verification challenge dispatched to signer {SignerId} via {Method}.",
                evt.SignerId,
                evt.Method
            );
        }
        else
        {
            logger.LogWarning(
                "Verification challenge dispatch failed for signer {SignerId} via {Method}: {Error}",
                evt.SignerId,
                evt.Method,
                result.Error
            );
        }
    }

    // ------------------------------------------------------------------
    // Non-email path — SMS / WhatsApp / AppPush con log manual (legacy)
    // ------------------------------------------------------------------

    private static async Task DispatchNonEmailAsync(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        ISmsSender smsSender,
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        INotificationLogRepository logRepository,
        IUnitOfWork unitOfWork,
        string correlationId,
        ILogger logger,
        CancellationToken ct
    )
    {
        var channel = ChannelFor(evt.Method);
        // AppPush no tiene "dirección" de entrega — el destino son los tokens del signer.
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

        var deliver = await DispatchOtherAsync(evt, smsSender, pushSender, pushDeviceTokens, ct);
        ApplyOutcomeToLog(deliver, log, logger, evt);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static Task<Result> DispatchOtherAsync(
        SignerVerificationChallengeIssuedIntegrationEvent evt,
        ISmsSender smsSender,
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        CancellationToken ct
    ) =>
        evt.Method switch
        {
            "SmsOtp" => SendSmsAsync(evt, smsSender, ct),
            "WhatsAppOtp" => SendSmsAsync(evt, smsSender, ct), // fallback provisional hasta ms WhatsApp
            "AppPush" => SendAppPushAsync(evt, pushSender, pushDeviceTokens, ct),
            _ => Task.FromResult(
                Result.Failure(new Error("Notification.UnknownMethod", "Verification method not supported."))
            ),
        };

    // ------------------------------------------------------------------
    // Helpers
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

    private static (string Subject, string Html, string Text) BuildKbaEmail(
        SignerVerificationChallengeIssuedIntegrationEvent evt
    )
    {
        var subject =
            evt.SignerLanguage == "Es"
                ? "TaxVision — Verificación de identidad requerida"
                : "TaxVision — Identity verification required";
        var body =
            evt.SignerLanguage == "Es"
                ? $"<p>Hola {evt.SignerFullName},</p><p>Necesitas completar un cuestionario de verificación de identidad antes de firmar.</p>"
                : $"<p>Hi {evt.SignerFullName},</p><p>You need to complete an identity verification questionnaire before signing.</p>";
        return (subject, body, body);
    }

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
