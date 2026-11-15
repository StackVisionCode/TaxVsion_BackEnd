namespace BuildingBlocks.Web.BotProtection;

/// <summary>Resultado de verificar un token. Solo <see cref="Passed"/> deja seguir.</summary>
public enum TurnstileOutcome
{
    Passed,

    /// <summary>Token ausente, inválido, de otra acción o de otro host.</summary>
    Rejected,

    /// <summary>Falta configuración: el servicio no debe ofrecer el formulario.</summary>
    NotConfigured,

    /// <summary>Cloudflare no respondió bien (error, caída o tiempo agotado).</summary>
    Unavailable,
}
