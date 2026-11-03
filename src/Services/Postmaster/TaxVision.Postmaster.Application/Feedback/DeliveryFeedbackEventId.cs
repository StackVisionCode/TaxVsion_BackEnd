using System.Security.Cryptography;
using System.Text;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Feedback;

/// <summary>
/// Id estable para un aviso cuyo proveedor no trae id de evento: el mismo aviso repetido da el mismo id
/// y el índice único lo descarta.
/// </summary>
public static class DeliveryFeedbackEventId
{
    public static string Derive(string messageReference, string recipientAddress, string kind, DateTime occurredAtUtc)
    {
        var material = string.Join(
            '|',
            messageReference,
            recipientAddress.Trim().ToLowerInvariant(),
            kind,
            occurredAtUtc.ToUniversalTime().ToString("O")
        );
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    /// <summary>
    /// Apertura o clic: el mismo id venga por webhook o por la consulta de respaldo, así se deduplica con el
    /// recibo. Al segundo, porque cada vía puede informar la hora con distinta precisión.
    /// </summary>
    public static string ForEngagement(
        string messageReference,
        string recipientAddress,
        EngagementSignal signal,
        DateTime occurredAtUtc
    ) => Derive(messageReference, recipientAddress, $"engagement:{signal}", WholeSecond(occurredAtUtc));

    /// <summary>
    /// Resultado de entrega (entregado, rebote, rechazo…): mismo criterio que <see cref="ForEngagement"/>, para
    /// que el webhook, la consulta de pendientes y la de actividad reciente den el mismo recibo.
    /// </summary>
    public static string ForOutcome(
        string messageReference,
        string recipientAddress,
        DeliveryOutcome outcome,
        DateTime occurredAtUtc
    ) => Derive(messageReference, recipientAddress, $"outcome:{outcome}", WholeSecond(occurredAtUtc));

    private static DateTime WholeSecond(DateTime value)
    {
        var utc = value.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }
}
