using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>
/// Resultado de <see cref="ThreadResolver.ResolveAsync"/>: o bien un <see cref="EmailThread"/>
/// existente que matcheó alguna de las 4 capas (<see cref="MatchedLayer"/> dice cuál), o ningún
/// match (<see cref="NoMatch"/>). <see cref="ThreadResolver"/> solo busca — el caller
/// (<see cref="RawMessageReceivedConsumer"/>) decide entre <c>EmailThread.AppendMessage</c> y
/// <c>EmailThread.NewFromMessage</c> según este resultado.
/// </summary>
public sealed record EmailThreadResolution(EmailThread? MatchedThread, ThreadMatchLayer? MatchedLayer)
{
    public static EmailThreadResolution NoMatch { get; } = new(null, null);
}
