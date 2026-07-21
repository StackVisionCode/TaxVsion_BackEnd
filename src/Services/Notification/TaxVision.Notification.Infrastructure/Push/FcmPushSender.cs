using BuildingBlocks.Results;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using FcmNotification = FirebaseAdmin.Messaging.Notification;

namespace TaxVision.Notification.Infrastructure.Push;

/// <summary>
/// Fase 7 del plan de notificaciones dinámicas — reemplaza <see cref="LoggingPushSender"/> con
/// un envío real vía Firebase Admin SDK (FCM HTTP v1 API). Cubre Android/iOS/Web con un único
/// pipeline: iOS se registra hoy con el SDK de Firebase (no APNs directo), así que
/// <see cref="TaxVision.Notification.Domain.Notifications.PushPlatform.Apns"/> también trae un
/// token FCM válido para <see cref="FirebaseMessaging.SendAsync(Message, System.Threading.CancellationToken)"/>
/// — no hace falta una rama separada por plataforma.
/// </summary>
public sealed class FcmPushSender : IPushSender
{
    private static readonly object InitLock = new();
    private readonly ILogger<FcmPushSender> _logger;

    public FcmPushSender(IOptions<FcmOptions> options, ILogger<FcmPushSender> logger)
    {
        _logger = logger;
        EnsureInitialized(options.Value);
    }

    // FirebaseApp.Create solo puede llamarse una vez por proceso para la app default — esta
    // clase se registra AddScoped (una instancia por request), así que la inicialización real
    // tiene que ser idempotente y compartida entre instancias.
    private static void EnsureInitialized(FcmOptions options)
    {
        if (FirebaseApp.DefaultInstance is not null)
            return;

        lock (InitLock)
        {
            if (FirebaseApp.DefaultInstance is not null)
                return;

            FirebaseApp.Create(
                new AppOptions { Credential = GoogleCredential.FromFile(options.ServiceAccountJsonPath) }
            );
        }
    }

    public async Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
    {
        var fcmMessage = new Message
        {
            Token = message.Token,
            Notification = new FcmNotification { Title = message.Title, Body = message.Body },
            Data = message.Data,
        };

        try
        {
            await FirebaseMessaging.DefaultInstance.SendAsync(fcmMessage, ct);
            return Result.Success();
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
        {
            // Token muerto (app desinstalada / token expirado) — el caller (NotificationDispatcher,
            // SignerVerificationChallengeIssuedConsumer) revoca el PushDeviceToken correspondiente
            // al ver este código, para no seguir reintentando contra un dispositivo fantasma.
            _logger.LogInformation("FCM token ya no registrado: {Token}", Mask(message.Token));
            return Result.Failure(new Error(PushErrorCodes.TokenInvalid, "FCM token is no longer registered."));
        }
        catch (FirebaseMessagingException ex)
        {
            _logger.LogWarning(
                ex,
                "FCM send falló para {Platform} token {Token}: {ErrorCode}",
                message.Platform,
                Mask(message.Token),
                ex.MessagingErrorCode
            );
            return Result.Failure(new Error("Notification.PushFailed", ex.Message));
        }
    }

    private static string Mask(string token) => token.Length <= 6 ? "***" : $"{token[..3]}***{token[^3..]}";
}
