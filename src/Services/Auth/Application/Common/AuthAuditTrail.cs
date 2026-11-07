using BuildingBlocks.Common;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Application.Common;

/// <summary>Bitácora del pedido en curso: IP, agente y correlación salen de aquí, no de cada handler.</summary>
public sealed class AuthAuditTrail(IAuthAuditWriter audit, IRequestContext request, ICorrelationContext correlation)
{
    public string? IpAddress => request.IpAddress;
    public string? UserAgent => request.UserAgent;
    public string CorrelationId => correlation.CorrelationId;

    public Task RecordAsync(
        Guid tenantId,
        Guid? userId,
        string action,
        bool success,
        CancellationToken ct,
        string? targetType = null,
        Guid? targetId = null,
        string? detailsJson = null
    ) =>
        audit.AddAsync(
            AuthAuditLog.Record(
                tenantId,
                userId,
                action,
                success,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType,
                targetId,
                detailsJson
            ),
            ct
        );
}
