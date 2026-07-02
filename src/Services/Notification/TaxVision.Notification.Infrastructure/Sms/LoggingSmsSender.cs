using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Sms;

/// <summary>
/// Implementación provisional hasta integrar un proveedor (Twilio/AWS SNS).
/// Registra el envío sin exponer el contenido (que incluye códigos OTP);
/// el texto completo solo a nivel Debug para desarrollo.
/// </summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task<Result> SendAsync(string phoneNumber, string text, CancellationToken ct = default)
    {
        logger.LogWarning(
            "Proveedor SMS no configurado. SMS a {Phone} NO fue enviado.",
            Mask(phoneNumber));
        logger.LogDebug("[DEV] Contenido del SMS para {Phone}: {Text}", Mask(phoneNumber), text);
        return Task.FromResult(Result.Success());
    }

    private static string Mask(string phone) =>
        phone.Length <= 4 ? "***" : $"***{phone[^4..]}";
}
