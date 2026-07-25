using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Abstractions;

public interface IBusinessIdempotencyExecutor
{
    /// <summary>
    /// Executes a mutating operation under an insert-first business-idempotency record.
    /// The implementation must use a unique (operation, scope, key) constraint, compare
    /// fingerprints on collision, replay the stored response for equal fingerprints,
    /// reject different fingerprints, and atomically persist the response with the
    /// business changes. A failed Result must roll back the transaction and discard
    /// all tracked aggregate/counter mutations before returning.
    /// </summary>
    /// <remarks>
    /// <paramref name="tenantId"/> es explícito porque este servicio puede correr dentro de un
    /// handler de Wolverine (bus.InvokeAsync) donde el ITenantContext ambiental (poblado por
    /// JwtTenantContextMiddleware en el scope de la request HTTP) no está disponible en el nuevo
    /// scope de DI. El comando/query que llama ya trae el tenantId validado desde el JWT.
    /// </remarks>
    Task<Result<TResponse>> ExecuteAsync<TResponse>(
        Guid tenantId,
        string operation,
        Guid scopeId,
        IdempotencyKey idempotencyKey,
        PayloadFingerprint payloadFingerprint,
        Func<CancellationToken, Task<Result<TResponse>>> operationBody,
        CancellationToken ct = default
    );
}
