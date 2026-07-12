namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// Consulta pública del firmante (autorizada por token firmado). Registra la primera
/// apertura del enlace en el aggregate como side-effect audit-trail, por eso se modela
/// como Command en lugar de Query pura.
/// </summary>
public sealed record ViewPublicSignerCommand(string Token, string? ClientIp, string? UserAgent);
