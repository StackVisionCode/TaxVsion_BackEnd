using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Push;

/// <summary>
/// Emisor cuando <c>Notification:UseFcmPush</c> está apagado: registra el intento sin
/// exponer el token y devuelve <see cref="PushErrorCodes.NotConfigured"/>, porque no se envió.
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
        return Task.FromResult(
            Result.Failure(new Error(PushErrorCodes.NotConfigured, "Push provider is not configured."))
        );
    }

    private static string Mask(string token) => token.Length <= 6 ? "***" : $"{token[..3]}***{token[^3..]}";
}
