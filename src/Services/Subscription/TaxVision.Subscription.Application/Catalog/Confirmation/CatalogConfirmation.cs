using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Catalog;

namespace TaxVision.Subscription.Application.Catalog.Confirmation;

/// <summary>
/// Piezas comunes de las operaciones peligrosas del catálogo (D13): motivo obligatorio y token de la vista previa.
/// El token es un hash del estado mostrado: no es un secreto, es la prueba de que se confirma lo que se vio.
/// </summary>
public static class CatalogConfirmation
{
    public static readonly Error Stale = new(
        "Catalog.ConfirmationStale",
        "Something changed since the preview. Preview it again and confirm."
    );

    public static string Token(params object?[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts))));

    public static bool Matches(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(provided.Trim())
        );

    /// <summary>Motivo y token presentes; se valida antes de leer nada.</summary>
    public static Result ValidateRequest(string? confirmationToken, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Catalog.ReasonRequired", "A reason is required for this operation."));

        if (reason.Trim().Length > CatalogAuditEntry.MaxReasonLength)
            return Result.Failure(
                new Error(
                    "Catalog.ReasonTooLong",
                    $"The reason must be {CatalogAuditEntry.MaxReasonLength} characters or fewer."
                )
            );

        if (string.IsNullOrWhiteSpace(confirmationToken))
            return Result.Failure(
                new Error(
                    "Catalog.ConfirmationRequired",
                    "Preview the operation first and send its confirmation token."
                )
            );

        return Result.Success();
    }
}
