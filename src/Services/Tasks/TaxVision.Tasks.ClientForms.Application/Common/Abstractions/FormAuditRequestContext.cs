namespace TaxVision.Tasks.ClientForms.Application.Common.Abstractions;

// Origen HTTP de una acción auditada que el comando trae explícito, sin depender del contexto ambiente.
public sealed record FormAuditRequestContext(string? IpAddress, string? UserAgent, string? CorrelationId);
