using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

public interface IRecipientResolver
{
    Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default);

    /// <summary>
    /// Resuelve cualquier audiencia a sus destinatarios válidos contra proyecciones locales (sin HTTP) y
    /// aplica la validación "al generar" (T4.2): activo + capacidad + módulo del tenant + excluir al actor.
    /// Devuelve <see cref="Result{T}"/>: "sin destinatario válido" por regla de negocio es un fallo
    /// explícito (G7), no una lista vacía ambigua. Una lista vacía legítima (nadie suscrito) es éxito.
    /// </summary>
    Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
        NotificationAudience audience,
        RecipientValidation validation,
        CancellationToken ct = default
    );
}
