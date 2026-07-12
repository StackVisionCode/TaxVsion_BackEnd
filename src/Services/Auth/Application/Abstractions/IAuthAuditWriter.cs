using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Application.Abstractions;

/// <summary>
/// Escritura de auditoría de seguridad. El registro se agrega al mismo DbContext,
/// por lo que se persiste atómicamente con la operación del handler (IUnitOfWork).
/// </summary>
public interface IAuthAuditWriter
{
    Task AddAsync(AuthAuditLog log, CancellationToken ct = default);
}

/// <summary>Lectura paginada de auditoría para el endpoint de consulta.</summary>
public interface IAuthAuditReader
{
    Task<(IReadOnlyList<AuthAuditLog> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        Guid? userId,
        string? action,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int size,
        CancellationToken ct = default
    );
}
