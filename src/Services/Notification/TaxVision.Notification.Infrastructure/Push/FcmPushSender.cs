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
/// existe, el arranque sigue y cada push devuelve <see cref="PushErrorCodes.NotConfigured"/>.
/// In-app y email siguen despachando; la salud lo marca como Degraded.
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
    // inválida), marcamos el estado permanente del proceso y los push fallan: no
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
                        "FCM disabled at startup: {Reason} Push notifications will fail as not configured.",
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
                _logger.LogError(ex, "FCM initialization failed. Push notifications will fail as not configured.");
                return false;
            }
        }
    }

    /// <summary>Para la salud: intenta inicializar (solo lee el archivo) sin enviar nada.</summary>
    public bool IsAvailable(out string? reason)
    {
        var available = TryEnsureInitialized();
        reason = available ? null : _initializationError ?? "unknown";
        return available;
    }

    public async Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
    {
        if (!TryEnsureInitialized())
        {
            _logger.LogWarning(
                "FCM no inicializado ({Reason}); push a {Platform} no enviado para {Token}.",
                _initializationError ?? "unknown",
                message.Platform,
                Mask(message.Token)
            );
            return Result.Failure(new Error(PushErrorCodes.NotConfigured, "Push provider is not configured."));
        }

        var fcmMessage = BuildMessage(message);

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

    /// <summary>Mensaje FCM sin enviarlo. Sin TTL explícito, Android conserva una hora.</summary>
    public static Message BuildMessage(PushMessage message) =>
        new()
        {
            Token = message.Token,
            Notification = new FcmNotification { Title = message.Title, Body = message.Body },
            Data = message.Data,
            Webpush = BuildWebpush(message),
            Android =
                message.Platform == TaxVision.Notification.Domain.Notifications.PushPlatform.Fcm
                    ? new AndroidConfig
                    {
                        Priority = Priority.High,
                        TimeToLive = message.TimeToLive ?? TimeSpan.FromHours(1),
                        Notification = new AndroidNotification
                        {
                            ChannelId = "taxvision-communication",
                            Tag = message.Tag,
                        },
                    }
                    : null,
        };

    private static WebpushConfig? BuildWebpush(PushMessage message)
    {
        if (message.Platform != TaxVision.Notification.Domain.Notifications.PushPlatform.Web)
            return null;
        var headers = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(message.Topic))
            headers["Topic"] = message.Topic;
        if (message.TimeToLive is { } ttl)
            headers["TTL"] = ((long)ttl.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new WebpushConfig
        {
            Headers = headers.Count == 0 ? null : headers,
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
