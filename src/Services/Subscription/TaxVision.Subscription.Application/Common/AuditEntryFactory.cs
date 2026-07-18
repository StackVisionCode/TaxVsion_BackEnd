using System.Text.Json;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Audit;

namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// Serializa el estado antes/después de un aggregate y lo anexa como una entrada de
/// auditoría inmutable. Vive en Application (no en Domain) porque la serialización JSON es
/// una preocupación de infraestructura de logging, no del propio aggregate.
/// </summary>
public static class AuditEntryFactory
{
    public static async Task AppendAsync<TBefore, TAfter>(
        ISubscriptionAuditLogWriter writer,
        Guid tenantId,
        string aggregateType,
        Guid aggregateId,
        string action,
        Guid actorUserId,
        string? correlationId,
        TBefore? before,
        TAfter? after,
        string? reason,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var actorType = actorUserId == Guid.Empty ? "System" : "User";

        var entryResult = SubscriptionAuditLog.Create(
            tenantId,
            aggregateType,
            aggregateId,
            action,
            actorUserId,
            actorType,
            nowUtc,
            correlationId,
            causationId: null,
            beforePayload: before is null ? null : JsonSerializer.Serialize(before),
            afterPayload: after is null ? null : JsonSerializer.Serialize(after),
            reason
        );

        if (entryResult.IsSuccess)
            await writer.AppendAsync(entryResult.Value, ct);
    }
}
