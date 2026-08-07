namespace BuildingBlocks.Sessions;

/// <summary>
/// H-06 — el store de la denylist (Redis) no respondió, así que <b>no se sabe</b> si la sesión está
/// revocada. Antes cada implementación de <see cref="ISessionDenylistReader"/> se tragaba el fallo y
/// devolvía <c>false</c>: fail-open correcto como default, pero invisible y no configurable.
///
/// <para>
/// Señalizarlo como excepción dedicada deja la <i>política</i> (seguir o cortar) en
/// <c>SessionDenylistMiddleware</c>, que es quien tiene la configuración, en vez de quemarla en el
/// adaptador de infraestructura.
/// </para>
/// </summary>
public sealed class SessionDenylistUnavailableException(Guid sessionId, Exception innerException)
    : Exception($"Session denylist check failed for session {sessionId:N}.", innerException)
{
    public Guid SessionId { get; } = sessionId;
}
