namespace TaxVision.Calendar.Application.Feeds.Abstractions;

/// <summary>
/// La última versión buena del feed de cada token.
///
/// <para>
/// No es una caché de rendimiento: el camino normal siempre va a la base. Existe sólo para que una
/// caída no borre la agenda del calendario del usuario — Google, ante un 500, deja de actualizar, y
/// ante un archivo viejo muestra lo de ayer. Lo segundo es mucho menos malo.
/// </para>
/// </summary>
public interface ICalendarFeedCache
{
    Task<string?> GetAsync(string tokenHashHex, CancellationToken ct = default);

    Task SetAsync(string tokenHashHex, string ics, CancellationToken ct = default);

    /// <summary>Al revocar. Si no, un token muerto seguiría sirviendo desde la caché mientras la base esté caída.</summary>
    Task RemoveAsync(string tokenHashHex, CancellationToken ct = default);
}
