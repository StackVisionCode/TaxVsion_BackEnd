namespace TaxVision.Postmaster.Application.Abstractions;

/// <summary>
/// Hermano de <see cref="ResolvedEmailProvider"/>, deliberadamente separado — ese record está
/// modelado 1:1 sobre SMTP (Host/Port/Password); forzar una cuenta OAuth ahí adentro (sin host/puerto
/// reales, con un <see cref="AccountId"/> en vez de credenciales) sería el mismo tipo de "generic
/// aggregate" que las guardrails del repo ya prohíben para dominio, aplicado acá a una abstracción de
/// Application (D3 §4.2).
/// </summary>
public sealed record ResolvedOAuthProvider(
    Guid AccountId,
    string ProviderCode,
    string FromAddress,
    string? FromDisplayName
);
