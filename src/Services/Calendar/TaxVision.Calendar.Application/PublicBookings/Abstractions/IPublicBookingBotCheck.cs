namespace TaxVision.Calendar.Application.PublicBookings.Abstractions;

/// <summary>Anti-bot del formulario público (Turnstile). Sin configurar, la página no ofrece reservas.</summary>
public interface IPublicBookingBotCheck
{
    bool IsReady { get; }

    /// <summary>Clave pública del widget; null si no está listo.</summary>
    string? SiteKey { get; }

    Task<bool> PassesAsync(string? token, string? clientIp, CancellationToken ct = default);
}
