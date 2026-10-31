namespace TaxVision.Auth.Domain.Users;

/// <summary>Resultado de la moderación de contenido (T-20) sobre una foto de perfil subida.</summary>
public enum AvatarModerationState
{
    /// <summary>Espera revisión humana: sigue pendiente y no vence.</summary>
    UnderReview,

    /// <summary>Bloqueada por la política de contenido: se descartó.</summary>
    Blocked,
}
