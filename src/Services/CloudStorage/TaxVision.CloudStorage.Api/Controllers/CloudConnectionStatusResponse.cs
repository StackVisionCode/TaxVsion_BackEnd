using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// <c>GET /storage/providers/{provider}</c>. <c>needsReview</c> (aditivo) = la conexión quedó ligada a
/// alguien que se retiró: la oficina debe reconectarla con una cuenta propia o desconectarla.
/// </summary>
public sealed record CloudConnectionStatusResponse(
    bool Connected,
    string? Email,
    bool NeedsReview,
    string? ReviewReason,
    DateTime? ReviewRequiredAtUtc
)
{
    public static CloudConnectionStatusResponse From(CloudConnectionStatus status) =>
        new(status.Connected, status.Email, status.NeedsReview, status.ReviewReason, status.ReviewRequiredAtUtc);
}
