using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Sms;

/// <summary>
/// Stub de desarrollo: registra que un SMS no se envió sin filtrar el contenido
/// (puede incluir códigos OTP). Siempre devuelve éxito para que el pipeline
/// continúe sin bloquear al tenant que todavía no configuró un proveedor.
/// </summary>
public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger) : ISmsSender
{
    public Task<Result> SendAsync(
        string phoneNumber,
        string text,
        CancellationToken ct,
        string? templateKey,
        string? idempotencyToken
    )
    {
        logger.LogWarning("Proveedor SMS no configurado. SMS a {Phone} NO fue enviado.", Mask(phoneNumber));
        return Task.FromResult(Result.Success());
    }

    private static string Mask(string phone) => phone.Length <= 4 ? "***" : $"***{phone[^4..]}";
}
