namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>Config de <see cref="RawMessageReceivedConsumer"/>, bound desde <c>Correspondence:Ingest</c> en Infrastructure.</summary>
public sealed class CorrespondenceIngestOptions
{
    public const string SectionName = "Correspondence:Ingest";

    /// <summary>
    /// Si <c>true</c>, un <c>From</c> que no matchea ningún customer se guarda en
    /// <c>UnmatchedIncomingEmails</c> (TTL 24h) para debug. <c>false</c> por default — nunca en
    /// prod, per plan §14. No afecta el registro de <c>AuthenticationFailed</c> (ese siempre se
    /// escribe, es un caso de seguridad, no de debug).
    /// </summary>
    public bool EnableUnmatchedDebug { get; set; }

    /// <summary>
    /// Si <c>true</c>, habilita la capa 4 (opcional) de <see cref="ThreadResolver"/>: cuando
    /// nada matcheó por ProviderThreadId/InReplyTo/References, intenta mergear con un thread
    /// reciente del mismo customer cuyo subject normalizado coincida. <c>false</c> por default
    /// per plan §36 Fase 6 — es una heurística explícitamente "opcional" en el plan, sin
    /// cabeceras reales de threading detrás, así que no se enciende sola.
    /// </summary>
    public bool EnableSubjectThreadingFallback { get; set; }
}
