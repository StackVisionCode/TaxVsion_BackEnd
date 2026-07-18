using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Abstractions;

/// <summary>
/// Envía un mensaje ya renderizado a través de un provider resuelto. No conoce templates, tenants
/// ni idempotencia — solo transporte MIME sobre el canal del provider (SMTP en Fase 3).
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Envía <paramref name="message"/> usando <paramref name="provider"/>. Los destinatarios se
    /// leen de <c>message.Recipients</c> — no se duplican como parámetro separado (deviation
    /// justificada respecto al texto literal del plan §Fase 3, que los pasaba dos veces).
    /// <paramref name="inlineAssets"/> ya viene descargado por <see cref="IInlineAssetFetcher"/>
    /// (Fase 3.5) — vacío por default para no romper el flujo de Fase 3 sin logos.
    /// </summary>
    Task<SendResult> SendAsync(
        SentMessage message,
        RenderedContent content,
        ResolvedEmailProvider provider,
        IReadOnlyList<InlineAssetBytes> inlineAssets,
        CancellationToken ct
    );
}
