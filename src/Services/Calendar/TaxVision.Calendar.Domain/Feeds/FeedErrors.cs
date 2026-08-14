using BuildingBlocks.Results;

namespace TaxVision.Calendar.Domain.Feeds;

public static class FeedErrors
{
    /// <summary>
    /// El único error que sale del feed público. No distingue token inválido de usuario inexistente
    /// ni de token revocado: cualquiera de los tres respondiendo distinto convierte la URL en un
    /// oráculo de qué usuarios existen.
    /// </summary>
    public static readonly Error NotFound = new("Calendar.Feed.NotFound", "The feed was not found.");

    public static readonly Error AlreadyRevoked = new(
        "Calendar.Feed.AlreadyRevoked",
        "The feed token is already revoked."
    );
}
