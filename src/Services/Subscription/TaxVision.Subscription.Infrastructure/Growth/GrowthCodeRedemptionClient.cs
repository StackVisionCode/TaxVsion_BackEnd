using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Infrastructure.Growth;

/// <summary>
/// M2M contra <c>internal/codes</c> de Growth con el cliente <c>subscription-growth</c> y el token de la oficina (así
/// Growth cuenta el límite por oficina).
/// La referencia de pago es <c>("SubscriptionDiscount", RedemptionId)</c>. Los errores <c>Codes.*</c> de Growth se
/// devuelven tal cual para que el Account los explique.
/// </summary>
internal sealed class GrowthCodeRedemptionClient(
    HttpClient httpClient,
    IGrowthCodesTokenAcquirer tokenAcquirer,
    ILogger<GrowthCodeRedemptionClient> logger
) : IGrowthCodeRedemptionClient
{
    private const string PaymentSource = "SubscriptionDiscount";
    private const int TtlSeconds = 10 * 60;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Error Unavailable = new(
        "Subscription.DiscountCode.Unavailable",
        "We couldn't check the code right now. Try again in a few minutes."
    );

    // El plan siempre; con ciclo, también la oferta plan + ciclo (así se puede limitar un código a anual).
    private static object[] ScopeTargets(GrowthCodeQuoteRequest request)
    {
        var plan = new { type = "Plan", scopeId = request.PlanId.ToString() };
        return string.IsNullOrWhiteSpace(request.BillingCycle)
            ? [plan]
            : [plan, new { type = "Offer", scopeId = $"Subscription/{request.PlanId}/{request.BillingCycle}" }];
    }

    public async Task<Result<GrowthCodeQuote>> QuoteAsync(GrowthCodeQuoteRequest request, CancellationToken ct)
    {
        var body = new
        {
            codeToken = request.Code,
            subjectType = "Tenant",
            subjectId = request.TenantId.ToString("D"),
            offerOwner = "Subscription",
            offerId = request.PlanId.ToString(),
            offerVersion = "1",
            grossAmountCents = request.GrossAmountCents,
            currency = request.Currency,
            snapshotHash = request.SnapshotHash,
            ttlSeconds = TtlSeconds,
            scopeTargets = ScopeTargets(request),
            context = "ExistingSubscription",
            buyerTenantId = request.TenantId,
        };

        var sent = await PostAsync<QuoteDto>(
            request.TenantId,
            "internal/codes/quotes",
            body,
            $"sub-redeem-quote:{request.RedemptionId:N}",
            ct
        );
        if (sent.IsFailure)
            return Result.Failure<GrowthCodeQuote>(sent.Error);

        var dto = sent.Value;
        if (dto.CodeDefinitionId is not { } codeId || string.IsNullOrWhiteSpace(dto.Duration))
            return Result.Failure<GrowthCodeQuote>(Unavailable);

        return Result.Success(
            new GrowthCodeQuote(
                dto.QuoteId,
                codeId,
                dto.PercentageBasisPoints,
                dto.FixedAmountCents,
                dto.Currency,
                dto.Duration,
                dto.DurationMonths,
                dto.CodeDisplay
            )
        );
    }

    public async Task<Result<Guid>> ReserveAsync(Guid tenantId, Guid quoteId, Guid redemptionId, CancellationToken ct)
    {
        var body = new
        {
            quoteId,
            paymentSource = PaymentSource,
            paymentId = redemptionId,
            ttlSeconds = TtlSeconds,
        };
        var sent = await PostAsync<ReserveDto>(
            tenantId,
            "internal/codes/reservations",
            body,
            $"sub-redeem-reserve:{redemptionId:N}",
            ct
        );
        return sent.IsSuccess ? Result.Success(sent.Value.ReservationId) : Result.Failure<Guid>(sent.Error);
    }

    public async Task<Result> CommitAsync(
        Guid tenantId,
        Guid reservationId,
        Guid redemptionId,
        string snapshotHash,
        CancellationToken ct
    )
    {
        var body = new
        {
            paymentSource = PaymentSource,
            paymentId = redemptionId,
            snapshotHash,
            sourceEventId = redemptionId,
        };
        var sent = await PostAsync<JsonElement>(
            tenantId,
            $"internal/codes/reservations/{reservationId:D}/commit",
            body,
            $"sub-redeem-commit:{redemptionId:N}",
            ct
        );
        return sent.IsSuccess ? Result.Success() : Result.Failure(sent.Error);
    }

    public async Task<Result> CancelAsync(
        Guid tenantId,
        Guid reservationId,
        Guid redemptionId,
        string reason,
        CancellationToken ct
    )
    {
        var body = new
        {
            paymentSource = PaymentSource,
            paymentId = redemptionId,
            reason,
        };
        var sent = await PostAsync<JsonElement>(
            tenantId,
            $"internal/codes/reservations/{reservationId:D}/cancel",
            body,
            $"sub-redeem-cancel:{redemptionId:N}",
            ct
        );
        if (sent.IsFailure)
            logger.LogWarning(
                "Growth did not cancel reservation {ReservationId}: {Code}; it expires on its own.",
                reservationId,
                sent.Error.Code
            );
        return sent.IsSuccess ? Result.Success() : Result.Failure(sent.Error);
    }

    private async Task<Result<T>> PostAsync<T>(
        Guid tenantId,
        string path,
        object body,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        var token = await tokenAcquirer.GetTokenAsync(tenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Result.Failure<T>(Unavailable);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, options: Json),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Idempotency-Key", idempotencyKey);

            using var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return Result.Failure<T>(await MapErrorAsync(response, path, ct));

            var payload = await response.Content.ReadFromJsonAsync<T>(Json, ct);
            return payload is null ? Result.Failure<T>(Unavailable) : Result.Success(payload);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Growth call {Path} failed.", path);
            return Result.Failure<T>(Unavailable);
        }
    }

    // Los errores de negocio de Growth (Codes.*) pasan tal cual; el resto es "no disponible".
    private async Task<Error> MapErrorAsync(HttpResponseMessage response, string path, CancellationToken ct)
    {
        ErrorDto? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ErrorDto>(Json, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { }

        if (error?.Code is { } code && code.StartsWith("Codes.", StringComparison.Ordinal))
            return new Error(code, error.Message ?? "This code can't be used.");

        logger.LogWarning("Growth {Path} returned {Status}: {Code}", path, (int)response.StatusCode, error?.Code);
        return Unavailable;
    }

    private sealed record QuoteDto(
        Guid QuoteId,
        string Currency,
        Guid? CodeDefinitionId,
        int? PercentageBasisPoints,
        long? FixedAmountCents,
        string? Duration,
        int? DurationMonths,
        string? CodeDisplay = null
    );

    private sealed record ReserveDto(Guid ReservationId);

    private sealed record ErrorDto(string? Code, string? Message);
}
