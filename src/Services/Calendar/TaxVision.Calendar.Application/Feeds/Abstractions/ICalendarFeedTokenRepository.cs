using TaxVision.Calendar.Domain.Feeds;

namespace TaxVision.Calendar.Application.Feeds.Abstractions;

public interface ICalendarFeedTokenRepository
{
    Task<CalendarFeedToken?> FindActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Todos los activos: el índice único deja uno, pero los datos viejos podrían tener más.</summary>
    Task<IReadOnlyList<CalendarFeedToken>> ListActiveForUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    );

    /// <summary>
    /// La búsqueda del feed público. No lleva tenant porque la URL no lo trae: el token <b>es</b> lo
    /// que resuelve el tenant, así que la consulta va por hash sobre todos y el usuario de la ruta se
    /// compara después.
    /// </summary>
    Task<CalendarFeedToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct = default);

    void Add(CalendarFeedToken token);
}
