namespace BuildingBlocks.Authorization.SlotGating;

public interface ISeatSlotGate
{
    ValueTask<SeatSlotDecision> CheckAsync(Guid tenantId, Guid userId, CancellationToken ct);
}

public sealed record SeatSlotDecision(bool Allowed, string? Reason, int? SlotIndex, int? EffectiveCap);
