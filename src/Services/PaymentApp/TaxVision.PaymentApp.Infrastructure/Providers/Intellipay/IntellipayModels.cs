using System.Text.Json.Serialization;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Intellipay;

/// <summary>Respuesta cruda de Intellipay. <c>Status == "1"</c> es el único indicador de éxito
/// documentado por el provider — todo lo demás ("0", vacío, error HTTP) se trata como fallo.</summary>
public sealed class IntellipayResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("custid")]
    public string? CustId { get; init; }

    [JsonPropertyName("transactionid")]
    public string? TransactionId { get; init; }

    [JsonPropertyName("response")]
    public string? Response { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    public bool IsApproved => Status == "1";
}

public sealed record IntellipayCreateCustomerRequest(string Account, string Email, string FirstName);

public sealed record IntellipayChargeRequest(string CustomerId, long AmountCents, string Description, string IdempotencyKey);

public sealed record IntellipayRefundRequest(string TransactionId, long AmountCents, string Reason);
