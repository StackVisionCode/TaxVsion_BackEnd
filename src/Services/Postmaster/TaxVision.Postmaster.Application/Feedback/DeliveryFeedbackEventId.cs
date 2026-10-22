using System.Security.Cryptography;
using System.Text;

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
}
