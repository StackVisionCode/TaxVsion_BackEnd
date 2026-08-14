using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Feeds.Abstractions;
using TaxVision.Calendar.Application.Observability;
using TaxVision.Calendar.Domain.Feeds;

namespace TaxVision.Calendar.Application.Feeds.Queries;

public sealed record GetCalendarFeedQuery(Guid UserId, string Token);

/// <summary>
/// El camino en vivo del feed.
///
/// <para>
/// Si la base no responde, esto <b>lanza</b>: el respaldo con la última copia buena vive en el
/// controller, no acá. La transacción de Wolverine se abre <b>antes</b> del cuerpo del handler, así
/// que un <c>catch</c> en este método nunca vería un fallo de conexión — medido con la base puesta
/// offline y el servicio corriendo.
/// </para>
/// </summary>
public static class GetCalendarFeedHandler
{
    /// <summary>
    /// Sin ventana, el feed de alguien con años de historia es un timeout. Hacia atrás alcanza un mes:
    /// nadie suscribe un calendario para consultar el año pasado.
    /// </summary>
    private static readonly TimeSpan Past = TimeSpan.FromDays(30);
    private static readonly TimeSpan Future = TimeSpan.FromDays(365);

    public static async Task<Result<string>> Handle(
        GetCalendarFeedQuery query,
        ICalendarFeedTokenRepository tokens,
        IAppointmentRepository appointments,
        ICalendarFeedCache cache,
        IUnitOfWork unitOfWork,
        ICalendarMetrics metrics,
        CancellationToken ct
    )
    {
        var hash = FeedToken.HashOf(query.Token);
        var token = await tokens.FindByHashAsync(hash, ct);

        // Los tres motivos devuelven lo mismo: token que no existe, token revocado y token de otro
        // usuario. Responder distinto convierte la URL en un oráculo de qué usuarios hay.
        if (token is null || !token.IsActive || token.UserId != query.UserId)
        {
            metrics.RecordIcsFeedRequest(found: false);
            return Result.Failure<string>(FeedErrors.NotFound);
        }

        var nowUtc = DateTime.UtcNow;
        var mine = await appointments.ListForUserRangeAsync(
            token.TenantId,
            token.UserId,
            nowUtc - Past,
            nowUtc + Future,
            ct
        );

        token.RegisterAccess(nowUtc);
        await unitOfWork.SaveChangesAsync(ct);

        var ics = IcsWriter.Write(mine);
        await cache.SetAsync(CacheKey.For(query.Token), ics, ct);

        metrics.RecordIcsFeedRequest(found: true);
        return Result.Success(ics);
    }
}

/// <summary>La clave de la copia sale del token y de nada más: se calcula sin tocar la base, que es el punto.</summary>
public static class CacheKey
{
    public static string For(string token) => Convert.ToHexString(FeedToken.HashOf(token));
}
