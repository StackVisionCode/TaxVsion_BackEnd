using System.Text.Json;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Audit;

namespace TaxVision.PaymentClient.Application.Common;

public static class AuditEntryFactory
{
    public static async Task AppendAsync<TBefore, TAfter>(
        IPaymentAuditLogWriter writer,
        Guid tenantId,
        string aggregateType,
        Guid aggregateId,
        PaymentAuditAction action,
        Guid actorUserId,
        string? correlationId,
        TBefore? before,
        TAfter? after,
        string? reason,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actorType = actorUserId == Guid.Empty ? "System" : "User";

        var entryResult = PaymentAuditEntry.Create(
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
            reason);

        if (entryResult.IsSuccess)
            await writer.AppendAsync(entryResult.Value, ct);
    }
}
