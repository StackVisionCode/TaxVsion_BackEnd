namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>Capa que resolvió un <see cref="EmailThreadResolution.MatchedThread"/>, en orden de prioridad decreciente.</summary>
public enum ThreadMatchLayer
{
    ProviderThreadId,
    InReplyTo,
    References,
    SubjectFallback,
}
