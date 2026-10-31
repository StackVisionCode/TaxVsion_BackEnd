namespace TaxVision.Customer.Domain.Customers;

/// <summary>Resultado de la moderación de contenido (T-20) sobre la foto de perfil subida.</summary>
public enum ProfilePictureModerationState
{
    /// <summary>Espera revisión humana: sigue pendiente y no vence.</summary>
    UnderReview,

    /// <summary>Bloqueada por la política de contenido: se descartó.</summary>
    Blocked,
}
