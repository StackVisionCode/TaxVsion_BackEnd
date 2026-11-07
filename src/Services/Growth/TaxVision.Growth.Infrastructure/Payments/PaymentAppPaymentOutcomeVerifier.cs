using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Payments;

/// <summary>
/// GRW-1 paso 4: verifica un commit tardío contra la fila del pago en PaymentApp (M2M de servicio, solo lectura,
/// <c>GET internal/code-reservations/payment-outcome</c>). Sin confirmación no hay canje: el commit queda en revisión.
/// Si no se pudo preguntar, el error es pasajero y el commit se reintenta.
/// </summary>
public sealed class PaymentAppPaymentOutcomeVerifier(
    HttpClient http,
    IServiceTokenAcquirer tokens,
    IOptions<PaymentOutcomeVerifierOptions> options,
    ILogger<PaymentAppPaymentOutcomeVerifier> logger
) : IPaymentOutcomeVerifier
{
    private const string PaymentAppSource = "PaymentApp";
    private const string OnboardingSource = "Onboarding";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record OutcomeDto(bool Found, bool Confirmed, string? Status, string? PromotionSnapshotHash);

    public async Task<Result> VerifySucceededAsync(PaymentOutcomeQuery query, CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
            return Failure(
                PaymentOutcomeVerifierErrors.Disabled,
                "Authoritative payment verification is disabled; late commit was denied."
            );

        var path = BuildPath(query);
        if (path is null)
            return Failure(
                PaymentOutcomeVerifierErrors.UnsupportedSource,
                $"Payment source '{query.Payment.Source}' cannot be verified."
            );

        var token = await tokens.GetTokenAsync(query.TenantId, ct);
        if (string.IsNullOrEmpty(token))
            return Unavailable("no service token");

        OutcomeDto? outcome;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return Unavailable($"HTTP {(int)response.StatusCode}");

            outcome = await response.Content.ReadFromJsonAsync<OutcomeDto>(Json, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(
                ex,
                "PaymentApp payment verification failed for reservation {ReservationId}.",
                query.ReservationId
            );
            return Unavailable(ex.GetType().Name);
        }

        if (outcome is null)
            return Unavailable("empty response");

        if (!outcome.Found || !outcome.Confirmed || !SnapshotMatches(query, outcome))
        {
            logger.LogWarning(
                "Late commit of reservation {ReservationId} is not confirmed by PaymentApp (found {Found}, status {Status}).",
                query.ReservationId,
                outcome.Found,
                outcome.Status
            );
            return Failure(
                PaymentOutcomeVerifierErrors.NotConfirmed,
                "PaymentApp does not confirm the payment of this reservation."
            );
        }

        return Result.Success();
    }

    // El pago de PaymentApp lleva la reserva; el de onboarding se ubica por el OnboardingId del sujeto anónimo.
    private static string? BuildPath(PaymentOutcomeQuery query)
    {
        const string route = "internal/code-reservations/payment-outcome";
        var common = $"paymentId={query.Payment.PaymentId}&codeReservationId={query.ReservationId}";

        if (query.Payment.Source == PaymentAppSource)
            return $"{route}?paymentSource={PaymentAppSource}&{common}";

        if (
            query.Payment.Source == OnboardingSource
            && query.Subject.Type == SubjectType.Anonymous
            && Guid.TryParse(query.Subject.SubjectId, out var onboardingId)
        )
            return $"{route}?paymentSource={OnboardingSource}&{common}&onboardingId={onboardingId}";

        return null;
    }

    // Solo PaymentApp guarda la huella de la promoción; si la tiene, tiene que ser la de la reserva.
    private static bool SnapshotMatches(PaymentOutcomeQuery query, OutcomeDto outcome) =>
        query.Payment.Source != PaymentAppSource
        || string.IsNullOrWhiteSpace(outcome.PromotionSnapshotHash)
        || string.Equals(
            outcome.PromotionSnapshotHash.Trim(),
            query.ExpectedSnapshotHash.Value,
            StringComparison.OrdinalIgnoreCase
        );

    private Result Unavailable(string reason)
    {
        logger.LogWarning("PaymentApp payment verification unavailable: {Reason}.", reason);
        return Failure(PaymentOutcomeVerifierErrors.Unavailable, "Payment verification is temporarily unavailable.");
    }

    private static Result Failure(string code, string message) => Result.Failure(new Error(code, message));
}
