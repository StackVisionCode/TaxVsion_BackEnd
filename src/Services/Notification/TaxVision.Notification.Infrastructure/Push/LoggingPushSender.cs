using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Push;

/// <summary>
/// Implementación provisional hasta integrar Firebase Admin SDK (FCM) / APNs
/// HTTP2 — mismo criterio que <c>LoggingSmsSender</c>. Registra el intento sin
/// exponer el token completo.
/// </summary>
public sealed class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
    {
        logger.LogWarning(
            "Proveedor push no configurado. {Platform} push a {Token} NO fue enviado.",
            message.Platform,
            Mask(message.Token)
        );
        logger.LogDebug("[DEV] Push para {Token}: {Title} — {Body}", Mask(message.Token), message.Title, message.Body);
        return Task.FromResult(Result.Success());
    }

    private static string Mask(string token) => token.Length <= 6 ? "***" : $"{token[..3]}***{token[^3..]}";
}
