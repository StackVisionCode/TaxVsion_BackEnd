using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Resultado de <see cref="ThreadResolver.ResolveAsync"/>: o bien un <see cref="EmailThread"/>
/// existente que matcheó alguna de las 4 capas (<see cref="MatchedLayer"/> dice cuál), o ningún
/// match (<see cref="NoMatch"/>). <see cref="ThreadResolver"/> solo busca — el caller
/// (<see cref="RawMessageReceivedConsumer"/>) decide entre <c>EmailThread.AppendMessage</c> y
/// <c>EmailThread.NewFromMessage</c> según este resultado.
/// </summary>
/// <param name="UnthreadedSentDraft">
/// COR-16: la respuesta cita un correo nuevo enviado desde el CRM que todavía no tiene hilo; el caller le crea el
/// suyo y lo enlaza.
/// </param>
public sealed record EmailThreadResolution(
    EmailThread? MatchedThread,
    ThreadMatchLayer? MatchedLayer,
    Draft? UnthreadedSentDraft = null
)
{
    public static EmailThreadResolution NoMatch { get; } = new(null, null);
}
