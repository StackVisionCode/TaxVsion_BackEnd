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
/// Envía push via Firebase Admin SDK (FCM HTTP v1). Cubre Android/iOS/Web con un único
/// pipeline — iOS usa token FCM (no APNs directo).
/// </summary>
/// <remarks>
/// La inicialización de Firebase es perezosa y tolerante: si la credencial falla o no
/// existe, el sender degrada a un no-op con logging para no tumbar el dispatcher al
/// arrancar. Un arranque con <c>UseFcmPush=true</c> y <c>FIREBASE_SERVICE_ACCOUNT_JSON_B64</c>
/// vacío debe seguir despachando notificaciones in-app y por email aunque las push
/// terminen en el log.
/// </remarks>
public sealed class FcmPushSender : IPushSender
{
    private static readonly object InitLock = new();
    private static bool _initializationFailed;
    private static string? _initializationError;
    private readonly FcmOptions _options;
    private readonly ILogger<FcmPushSender> _logger;

    public FcmPushSender(IOptions<FcmOptions> options, ILogger<FcmPushSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    // FirebaseApp.Create corre una vez por proceso. Si falla (credencial faltante o
    // inválida), marcamos el estado permanente del proceso y caemos a un no-op: no
    // intentamos de nuevo porque el error es de configuración, no transitorio.
    private bool TryEnsureInitialized()
    {
        if (_initializationFailed)
            return false;
        if (FirebaseApp.DefaultInstance is not null)
            return true;

        lock (InitLock)
        {
            if (_initializationFailed)
                return false;
            if (FirebaseApp.DefaultInstance is not null)
                return true;

            try
            {
                if (
                    string.IsNullOrWhiteSpace(_options.ServiceAccountJsonPath)
                    || !File.Exists(_options.ServiceAccountJsonPath)
                )
                {
                    _initializationFailed = true;
                    _initializationError =
                        $"Firebase service account JSON not found at '{_options.ServiceAccountJsonPath}'.";
                    _logger.LogError(
                        "FCM disabled at startup: {Reason} Push notifications will be logged, not delivered.",
                        _initializationError
                    );
                    return false;
                }

                FirebaseApp.Create(
                    new AppOptions { Credential = GoogleCredential.FromFile(_options.ServiceAccountJsonPath) }
                );
                return true;
            }
            catch (Exception ex)
            {
                _initializationFailed = true;
                _initializationError = ex.Message;
                _logger.LogError(ex, "FCM initialization failed. Push notifications will be logged, not delivered.");
                return false;
            }
        }
    }

    public async Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
    {
        if (!TryEnsureInitialized())
        {
            _logger.LogInformation(
                "FCM no inicializado ({Reason}); push a {Platform} descartado para {Token}.",
                _initializationError ?? "unknown",
                message.Platform,
                Mask(message.Token)
            );
            return Result.Success();
        }

        var fcmMessage = new Message
        {
            Token = message.Token,
            Notification = new FcmNotification { Title = message.Title, Body = message.Body },
            Data = message.Data,
            Webpush = BuildWebpush(message),
        };

        try
        {
            await FirebaseMessaging.DefaultInstance.SendAsync(fcmMessage, ct);
            return Result.Success();
        }
        catch (FirebaseMessagingException ex) when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
        {
            // Token muerto: el caller revoca el PushDeviceToken para no reintentar sobre un dispositivo fantasma.
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
            return Result.Failure(new Error(MapError(ex.MessagingErrorCode), ex.Message));
        }
        catch (Exception ex)
            when (ex is FirebaseException or HttpRequestException
                || (ex is OperationCanceledException && !ct.IsCancellationRequested)
            )
        {
            // Red, timeout o error del SDK sin código de mensajería: transitorio, se reintenta.
            _logger.LogWarning(
                ex,
                "FCM send no disponible para {Platform} token {Token}.",
                message.Platform,
                Mask(message.Token)
            );
            return Result.Failure(new Error(PushErrorCodes.Unavailable, ex.Message));
        }
    }

    private static WebpushConfig? BuildWebpush(PushMessage message)
    {
        if (message.Platform != TaxVision.Notification.Domain.Notifications.PushPlatform.Web)
            return null;
        var headers = string.IsNullOrWhiteSpace(message.Topic)
            ? null
            : new Dictionary<string, string> { ["Topic"] = message.Topic };
        return new WebpushConfig
        {
            Headers = headers,
            Notification = string.IsNullOrWhiteSpace(message.Tag)
                ? null
                : new WebpushNotification { Tag = message.Tag },
            FcmOptions = string.IsNullOrWhiteSpace(message.Link) ? null : new WebpushFcmOptions { Link = message.Link },
        };
    }

    private static string MapError(MessagingErrorCode? code) =>
        code switch
        {
            MessagingErrorCode.InvalidArgument => PushErrorCodes.InvalidArgument,
            MessagingErrorCode.SenderIdMismatch => PushErrorCodes.SenderIdMismatch,
            MessagingErrorCode.ThirdPartyAuthError => PushErrorCodes.ThirdPartyAuthError,
            MessagingErrorCode.Unavailable => PushErrorCodes.Unavailable,
            MessagingErrorCode.Internal => PushErrorCodes.Internal,
            MessagingErrorCode.QuotaExceeded => PushErrorCodes.QuotaExceeded,
            _ => "Notification.PushFailed",
        };

    private static string Mask(string token) => token.Length <= 6 ? "***" : $"{token[..3]}***{token[^3..]}";
}
