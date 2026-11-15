using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>Busca la reserva por su secreto (solo el hash toca la base). Un secreto vacío o enorme no consulta.</summary>
internal static class PublicBookingLookup
{
    private const int MaxSecretLength = 100;

    public static Task<PublicBooking?> ByHandleAsync(
        IPublicBookingRepository bookings,
        string? handle,
        CancellationToken ct
    ) =>
        IsPlausible(handle)
            ? bookings.FindByRequestHandleAsync(OpaqueToken.HashOf(handle!), ct)
            : Task.FromResult<PublicBooking?>(null);

    /// <summary>Solo las confirmadas tienen enlace de gestión vigente.</summary>
    public static async Task<PublicBooking?> ByTokenAsync(
        IPublicBookingRepository bookings,
        string? token,
        CancellationToken ct
    )
    {
        if (!IsPlausible(token))
            return null;
        var booking = await bookings.FindByManagementTokenAsync(OpaqueToken.HashOf(token!), ct);
        return booking is { Status: PublicBookingStatus.Confirmed } ? booking : null;
    }

    private static bool IsPlausible(string? secret) =>
        !string.IsNullOrWhiteSpace(secret) && secret.Length <= MaxSecretLength;
}
