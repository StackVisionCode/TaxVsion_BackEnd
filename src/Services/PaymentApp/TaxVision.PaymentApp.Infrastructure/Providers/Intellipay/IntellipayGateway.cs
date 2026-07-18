using System.Net.Http.Json;
using BuildingBlocks.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TaxVision.PaymentApp.Infrastructure.Providers.Intellipay;

/// <summary>
/// Wrapper HTTP form-url-encoded contra el endpoint <c>webapi.cfc</c> de Intellipay. Portado
/// del CRM legado (<c>IntellipayService.cs</c>, 734 líneas) con las correcciones del §44.7.2:
/// cero excepción hacia el caller (<see cref="IntellipayResponse"/> siempre se devuelve, el
/// caller decide si <see cref="IntellipayResponse.IsApproved"/>), <see cref="TimeProvider"/>
/// inyectado en vez de <c>DateTime.UtcNow</c>, secretos nunca logueados.
///
/// Intellipay no soporta idempotency-key nativa: <see cref="ChargeCardAsync"/> emula dedupe
/// local vía <see cref="ICacheService"/> (§44.7.1) — un segundo intento con la misma
/// <see cref="IntellipayChargeRequest.IdempotencyKey"/> devuelve la respuesta cacheada sin
/// volver a golpear el provider.
/// </summary>
public sealed class IntellipayGateway(HttpClient http, IOptions<IntellipayOptions> options, ICacheService cache, ILogger<IntellipayGateway> logger)
{
    private IntellipayOptions Options => options.Value;

    public Task<IntellipayResponse> CreateCustomerAsync(IntellipayCreateCustomerRequest request, CancellationToken ct) =>
        PostAsync("create_customer", new Dictionary<string, string>
        {
            ["account"] = request.Account,
            ["email"] = request.Email,
            ["firstname"] = request.FirstName,
        }, ct);

    public async Task<IntellipayResponse> ChargeCardAsync(IntellipayChargeRequest request, CancellationToken ct)
    {
        var cacheKey = $"intellipay:idemp:{request.IdempotencyKey}";

        return await cache.GetOrCreateAsync(
            cacheKey,
            async innerCt =>
            {
                logger.LogInformation("Intellipay charge_card dispatched. IdempotencyKey={IdempotencyKey}", request.IdempotencyKey);
                return await PostAsync("charge_card", new Dictionary<string, string>
                {
                    ["customerid"] = request.CustomerId,
                    ["amount"] = (request.AmountCents / 100m).ToString("F2"),
                    ["description"] = request.Description,
                }, innerCt);
            },
            Options.IdempotencyCacheTtl,
            ct);
    }

    public Task<IntellipayResponse> RefundAsync(IntellipayRefundRequest request, CancellationToken ct) =>
        PostAsync("refund", new Dictionary<string, string>
        {
            ["transactionid"] = request.TransactionId,
            ["amount"] = (request.AmountCents / 100m).ToString("F2"),
            ["reason"] = request.Reason,
        }, ct);

    /// <summary>Consulta el status de una transacción como confirmación out-of-band — usado
    /// por el validador de IPN (§44.9) para rechazar callbacks spoofed, no por
    /// <see cref="IntellipayAdapter"/> directamente en Fase A.</summary>
    public Task<IntellipayResponse> GetTransactionStatusAsync(string transactionId, CancellationToken ct) =>
        PostAsync("get_transaction_status", new Dictionary<string, string> { ["transactionid"] = transactionId }, ct);

    private async Task<IntellipayResponse> PostAsync(string method, Dictionary<string, string> fields, CancellationToken ct)
    {
        fields["method"] = method;
        fields["merchantkey"] = Options.MerchantKey;
        fields["apikey"] = Options.ApiKey;

        using var response = await http.PostAsync(Options.BaseUrl, new FormUrlEncodedContent(fields), ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Intellipay {Method} returned HTTP {StatusCode}", method, (int)response.StatusCode);
            return new IntellipayResponse { Status = "0", Response = "http_error", Message = $"HTTP {(int)response.StatusCode}" };
        }

        var parsed = await response.Content.ReadFromJsonAsync<IntellipayResponse>(ct);
        return parsed ?? new IntellipayResponse { Status = "0", Response = "parse_error", Message = "Empty or invalid Intellipay response." };
    }
}
