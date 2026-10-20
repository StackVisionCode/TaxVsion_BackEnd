namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Reglas que se aplican a cada candidato "al generar" (T4.2): debe estar activo, tener la
/// <paramref name="RequiredCapability"/> (si la hay), el tenant debe tener el
/// <paramref name="RequiredModule"/> habilitado, y nunca se notifica al actor del hecho.
/// La capacidad NUNCA se salta; lo obligatorio solo salta el filtro de suscripción (que es otro permiso).
/// </summary>
public sealed record RecipientValidation(
    string? RequiredCapability = null,
    string? RequiredModule = null,
    Guid? ExcludeActorUserId = null
)
{
    public static readonly RecipientValidation None = new();
}
