namespace BuildingBlocks.Web.BotProtection;

/// <summary>Verifica en el servidor el token de Cloudflare Turnstile que manda un formulario público.</summary>
public interface ITurnstileVerifier
{
    /// <summary>Hay claves y acción configuradas. Si no, el formulario no debe ofrecerse.</summary>
    bool IsReady { get; }

    /// <summary>La clave pública que necesita el widget; null si no está lista.</summary>
    string? SiteKey { get; }

    Task<TurnstileOutcome> VerifyAsync(string? token, string? clientIp, CancellationToken ct = default);
}
