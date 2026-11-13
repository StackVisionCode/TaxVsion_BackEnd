using BuildingBlocks.Web.BotProtection;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;

namespace TaxVision.Calendar.Api.PublicBookings;

/// <summary>Turnstile (pieza de BuildingBlocks) para el formulario de la demo. Solo un sí explícito deja pasar.</summary>
public sealed class TurnstilePublicBookingBotCheck(ITurnstileVerifier verifier, ICalendarMetrics metrics)
    : IPublicBookingBotCheck
{
    public bool IsReady => verifier.IsReady;

    public string? SiteKey => verifier.SiteKey;

    public async Task<bool> PassesAsync(string? token, string? clientIp, CancellationToken ct = default)
    {
        var outcome = await verifier.VerifyAsync(token, clientIp, ct);
        metrics.RecordPublicBooking("bot_check", outcome.ToString());
        return outcome == TurnstileOutcome.Passed;
    }
}
