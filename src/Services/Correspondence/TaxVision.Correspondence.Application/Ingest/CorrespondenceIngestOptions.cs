namespace TaxVision.Correspondence.Application.Ingest;

/// <summary>Config de <see cref="RawMessageReceivedConsumer"/>, bound desde <c>Correspondence:Ingest</c> en Infrastructure.</summary>
public sealed class CorrespondenceIngestOptions
{
    public const string SectionName = "Correspondence:Ingest";

    /// <summary>
    /// Inicio del buzón de los sistemas de correo. Sin ser clientes no entran a "Not a client": sus avisos de no
    /// entrega ya los procesa Connectors. Misma lista que <c>DeliveryReportCandidate</c>.
    /// </summary>
    public string[] SystemSenderPrefixes { get; set; } =
    ["mailer-daemon", "postmaster", "microsoftexchange", "mail-daemon"];

    public bool IsSystemSender(string address) =>
        SystemSenderPrefixes.Any(prefix => address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Si <c>true</c>, habilita la capa 4 (opcional) de <see cref="ThreadResolver"/>: cuando
    /// nada matcheó por ProviderThreadId/InReplyTo/References, intenta mergear con un thread
    /// reciente del mismo customer cuyo subject normalizado coincida. <c>false</c> por default
    /// per plan §36 Fase 6 — es una heurística explícitamente "opcional" en el plan, sin
    /// cabeceras reales de threading detrás, así que no se enciende sola.
    /// </summary>
    public bool EnableSubjectThreadingFallback { get; set; }
}
