using TaxVision.Correspondence.Domain.Audit;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Solo escritura — Fase 14 no necesita consultar el rastro de auditoría, solo dejarlo asentado
/// (ver comentario de clase de <see cref="CorrespondenceAuditLog"/>). Un lado de lectura/listado
/// se agrega en una fase futura si un caso de uso real lo pide, no antes.
/// </summary>
public interface ICorrespondenceAuditLogRepository
{
    Task AddAsync(CorrespondenceAuditLog entity, CancellationToken ct = default);
}
