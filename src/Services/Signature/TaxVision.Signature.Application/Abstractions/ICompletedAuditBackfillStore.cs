using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Results;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>Solicitud completada sin línea <c>RequestCompleted</c> en la cadena (SIG-17).</summary>
public sealed record CompletedAuditGap(
    Guid TenantId,
    Guid SignatureRequestId,
    DateTime CompletedAtUtc,
    bool GenerateCertificate,
    IReadOnlyList<DocumentHashDescriptor> Documents,
    int ExistingLines
);

/// <summary>Lectura y escritura del backfill SIG-17. Cada append es una unidad con su transacción.</summary>
public interface ICompletedAuditBackfillStore
{
    Task<IReadOnlyList<CompletedAuditGap>> ListAsync(Guid? tenantId, CancellationToken ct = default);

    /// <summary>
    /// Agrega la línea al final, en su propia transacción y con el candado de la cadena. Vuelve a mirar dentro del
    /// candado: <c>false</c> si la línea ya estaba.
    /// </summary>
    Task<Result<bool>> AppendIfMissingAsync(CompletedAuditGap gap, object payload, CancellationToken ct = default);
}
