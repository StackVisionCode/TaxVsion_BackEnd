using TaxVision.CloudStorage.Domain.Providers;

namespace TaxVision.CloudStorage.Infrastructure.Storage;

/// <summary>Estado de la conexión de un proveedor de nube de la oficina, tal cual lo lee el CRM.</summary>
public sealed record CloudConnectionStatus(
    bool Connected,
    string? Email,
    bool NeedsReview,
    string? ReviewReason,
    DateTime? ReviewRequiredAtUtc
)
{
    public static CloudConnectionStatus From(TenantCloudConnection? connection) =>
        connection is null
            ? new(false, null, false, null, null)
            : new(
                true,
                connection.AccountEmail,
                connection.NeedsReview,
                connection.ReviewReason,
                connection.ReviewRequiredAtUtc
            );
}
