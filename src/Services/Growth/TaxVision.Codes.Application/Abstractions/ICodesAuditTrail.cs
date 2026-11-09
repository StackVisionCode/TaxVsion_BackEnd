namespace TaxVision.Codes.Application.Abstractions;

/// <summary>Auditoría append-only de Codes. Se guarda con la unidad de trabajo del handler.</summary>
public interface ICodesAuditTrail
{
    void Append(CodesAuditRecord record);
}

public sealed record CodesAuditRecord(
    Guid TenantId,
    string AggregateType,
    Guid AggregateId,
    string Action,
    Guid ActorUserId,
    string ActorType,
    object? Before,
    object? After,
    string? Reason,
    DateTime OccurredAtUtc
);
