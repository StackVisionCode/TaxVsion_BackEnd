using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Authorization;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Infrastructure.Onboarding.Security;

namespace TaxVision.Auth.Infrastructure.Onboarding.HttpClients;

/// <summary>
/// M2M Auth→Growth para aplicar códigos y calificar referidos en el onboarding (pre-tenant). Token
/// acuñado en proceso vía <see cref="OnboardingServiceTokenCache"/> (Auth es el emisor), con audience
/// <c>taxvision-growth</c> y el scope puntual por operación. Contrato pre-tenant: dueño =
/// <c>PlatformTenant.Id</c>, sujeto = <c>Anonymous(OnboardingId)</c>, referencia de pago =
/// <c>("Onboarding", OnboardingId)</c>. Los enums viajan como string (Growth usa JsonStringEnumConverter).
/// </summary>
public sealed class GrowthOnboardingClient(
    HttpClient httpClient,
    OnboardingServiceTokenCache tokenCache,
    HttpResiliencePipelineRegistry resilience,
    ILogger<GrowthOnboardingClient> logger
) : IGrowthOnboardingClient
{
    private const string ClientId = "auth-onboarding-growth";
    private const string Audience = "taxvision-growth";
    private const string PaymentSourceOnboarding = "Onboarding";
    private const string SubjectTypeAnonymous = "Anonymous";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<GrowthQuoteResult>> QuoteAsync(GrowthQuoteRequest request, CancellationToken ct = default)
    {
        var body = new
        {
            codeToken = request.CodeToken,
            subjectType = SubjectTypeAnonymous,
            subjectId = request.OnboardingId.ToString("N"),
            offerOwner = "Subscription",
            offerId = request.PlanId.ToString(),
            offerVersion = request.PlanVersion,
            grossAmountCents = request.GrossAmountCents,
            currency = request.Currency,
            snapshotHash = request.SnapshotHash,
            ttlSeconds = request.TtlSeconds,
            scopeTargets = (object?)null,
        };

        var response = await SendAsync(
            GrowthServiceScopes.CodesQuote,
            HttpMethod.Post,
            "internal/codes/quotes",
            body,
            idempotencyKey: $"onb-quote:{request.OnboardingId:N}:{request.SnapshotHash}",
            request.OnboardingId,
            ct
        );
        if (response.IsFailure)
            return Result.Failure<GrowthQuoteResult>(response.Error);

        using var message = response.Value;
        if (!message.IsSuccessStatusCode)
            return Result.Failure<GrowthQuoteResult>(await MapErrorAsync(message, "Growth.Quote", ct));

        var dto = await message.Content.ReadFromJsonAsync<QuoteDto>(Json, ct);
        if (dto is null)
            return Result.Failure<GrowthQuoteResult>(new Error("Growth.Quote.Empty", "Growth returned an empty quote."));

        return Result.Success(
            new GrowthQuoteResult(
                dto.QuoteId,
                dto.GrossAmountCents,
                dto.DiscountAmountCents,
                dto.NetAmountCents,
                dto.Currency,
                dto.ExpiresAtUtc
            )
        );
    }

    public async Task<Result<GrowthReserveResult>> ReserveAsync(
        Guid quoteId,
        Guid paymentReferenceId,
        int ttlSeconds,
        string idempotencyKey,
        CancellationToken ct = default
    )
    {
        var body = new
        {
            quoteId,
            paymentSource = PaymentSourceOnboarding,
            // Stacking: cada reserva del mismo onboarding necesita un PaymentId ÚNICO — Growth tiene
            // UX_CodeReservations_Payment (unique sobre (Source, PaymentId)). El caller deriva un GUID
            // determinístico por orden de código (OnboardingPaymentReference.For) para reserve+commit.
            paymentId = paymentReferenceId,
            ttlSeconds,
        };

        var response = await SendAsync(
            GrowthServiceScopes.CodesReserve,
            HttpMethod.Post,
            "internal/codes/reservations",
            body,
            idempotencyKey,
            paymentReferenceId,
            ct
        );
        if (response.IsFailure)
            return Result.Failure<GrowthReserveResult>(response.Error);

        using var message = response.Value;
        if (!message.IsSuccessStatusCode)
            return Result.Failure<GrowthReserveResult>(await MapErrorAsync(message, "Growth.Reserve", ct));

        var dto = await message.Content.ReadFromJsonAsync<ReserveDto>(Json, ct);
        if (dto is null)
            return Result.Failure<GrowthReserveResult>(
                new Error("Growth.Reserve.Empty", "Growth returned an empty reservation.")
            );

        return Result.Success(
            new GrowthReserveResult(dto.ReservationId, dto.DiscountAmountCents, dto.NetAmountCents, dto.ExpiresAtUtc)
        );
    }

    public async Task<Result> CommitAsync(
        Guid reservationId,
        Guid paymentReferenceId,
        string snapshotHash,
        Guid sourceEventId,
        string idempotencyKey,
        CancellationToken ct = default
    )
    {
        var body = new
        {
            paymentSource = PaymentSourceOnboarding,
            // Debe COINCIDIR con el PaymentId usado en ReserveAsync (mismo OnboardingPaymentReference.For
            // por orden de código) — Growth valida que el commit refiera la misma reserva/pago.
            paymentId = paymentReferenceId,
            snapshotHash,
            sourceEventId,
        };

        var response = await SendAsync(
            GrowthServiceScopes.CodesCommit,
            HttpMethod.Post,
            $"internal/codes/reservations/{reservationId}/commit",
            body,
            idempotencyKey,
            paymentReferenceId,
            ct
        );
        if (response.IsFailure)
            return Result.Failure(response.Error);

        using var message = response.Value;
        return message.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(await MapErrorAsync(message, "Growth.Commit", ct));
    }

    public async Task<Result> CancelAsync(
        Guid reservationId,
        Guid onboardingId,
        string reason,
        string idempotencyKey,
        CancellationToken ct = default
    )
    {
        var body = new
        {
            paymentSource = PaymentSourceOnboarding,
            paymentId = onboardingId,
            reason,
        };

        var response = await SendAsync(
            GrowthServiceScopes.CodesCancel,
            HttpMethod.Post,
            $"internal/codes/reservations/{reservationId}/cancel",
            body,
            idempotencyKey,
            onboardingId,
            ct
        );
        if (response.IsFailure)
            return Result.Failure(response.Error);

        using var message = response.Value;
        return message.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(await MapErrorAsync(message, "Growth.Cancel", ct));
    }

    public async Task<Result> QualifyReferralAsync(GrowthQualifyRequest request, CancellationToken ct = default)
    {
        var body = new
        {
            attributionId = request.AttributionId,
            qualifyingEventId = request.QualifyingEventId,
            paymentId = request.PaymentId,
            paymentSource = "PaymentApp",
            paymentAmountCents = request.PaymentAmountCents,
            paymentCurrency = request.PaymentCurrency,
            isFirstSuccessfulPayment = request.IsFirstSuccessfulPayment,
            paymentSucceededAtUtc = request.PaymentSucceededAtUtc,
        };

        var response = await SendAsync(
            GrowthServiceScopes.ReferralsQualify,
            HttpMethod.Post,
            "internal/referrals/qualifications",
            body,
            idempotencyKey: $"onb-qualify:{request.AttributionId:N}:{request.PaymentId:N}",
            correlationScope: request.AttributionId,
            ct
        );
        if (response.IsFailure)
            return Result.Failure(response.Error);

        using var message = response.Value;
        return message.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(await MapErrorAsync(message, "Growth.Qualify", ct));
    }

    private async Task<Result<HttpResponseMessage>> SendAsync(
        string scope,
        HttpMethod method,
        string path,
        object body,
        string idempotencyKey,
        Guid correlationScope,
        CancellationToken ct
    )
    {
        var token = await tokenCache.GetOrCreateAsync(
            PlatformTenant.Id,
            ClientId,
            permissions: [],
            scopes: [scope],
            audience: Audience,
            lifetimeMinutes: 5,
            ct
        );

        try
        {
            using var httpRequest = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            httpRequest.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

            var breaker = resilience.GetOrCreate(nameof(GrowthOnboardingClient));
            var response = await breaker.ExecuteAsync(inner => httpClient.SendAsync(httpRequest, inner), ct);
            return Result.Success(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or BrokenCircuitException)
        {
            logger.LogWarning(ex, "Growth call {Path} failed for scope {Scope} ({Corr}).", path, scope, correlationScope);
            return Result.Failure<HttpResponseMessage>(new Error("Growth.Unreachable", "Could not reach Growth."));
        }
    }

    private async Task<Error> MapErrorAsync(HttpResponseMessage message, string prefix, CancellationToken ct)
    {
        ErrorDto? error = null;
        try
        {
            error = await message.Content.ReadFromJsonAsync<ErrorDto>(Json, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { }

        // Código de negocio "no encontrado/no aplica" → error específico para reportarlo al usuario.
        if (message.StatusCode == HttpStatusCode.NotFound || (error?.Code?.Contains("NotFound") ?? false))
            return new Error($"{prefix}.CodeNotFound", error?.Message ?? "The code was not found or does not apply.");

        logger.LogWarning("Growth {Prefix} returned {Status}: {Code}", prefix, (int)message.StatusCode, error?.Code);
        return new Error($"{prefix}.Failed", error?.Message ?? $"Growth returned {(int)message.StatusCode}.");
    }

    private sealed record QuoteDto(
        Guid QuoteId,
        long GrossAmountCents,
        long DiscountAmountCents,
        long NetAmountCents,
        string Currency,
        DateTime ExpiresAtUtc
    );

    private sealed record ReserveDto(
        Guid ReservationId,
        long DiscountAmountCents,
        long NetAmountCents,
        DateTime ExpiresAtUtc
    );

    private sealed record ErrorDto(string? Code, string? Message);
}
