using TaxVision.Sms.Application.Messages.Abstractions;

namespace TaxVision.Sms.Tests.Fakes;

/// <summary>Wallet de prueba: registra reservas y liquidaciones.</summary>
internal sealed class FakeWalletSpendClient : IWalletSpendClient
{
    public WalletReserveResult ReserveResult { get; set; } =
        new(
            Reachable: true,
            Authorized: true,
            CostMicros: 0,
            AvailableMicros: 1_000_000,
            DeficitMicros: 0,
            Currency: "USD"
        );

    public List<ReservationCall> Reservations { get; } = [];
    public List<SettlementCall> Settlements { get; } = [];

    /// <summary>Cada liquidación consume una y la lanza, después de anotarse.</summary>
    public Queue<Exception> SettleFailures { get; } = new();

    public Task<WalletReserveResult> ReserveAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        WalletUnitCounts units,
        string? callerBearerToken,
        CancellationToken ct = default
    )
    {
        Reservations.Add(new ReservationCall(tenantId, referenceType, referenceId, units, callerBearerToken));
        return Task.FromResult(ReserveResult);
    }

    public Task SettleAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        int consumedUnits,
        string? callerBearerToken,
        CancellationToken ct = default
    )
    {
        Settlements.Add(new SettlementCall(tenantId, referenceType, referenceId, consumedUnits, callerBearerToken));
        return SettleFailures.TryDequeue(out var failure) ? Task.FromException(failure) : Task.CompletedTask;
    }

    public sealed record ReservationCall(
        Guid TenantId,
        string ReferenceType,
        Guid ReferenceId,
        WalletUnitCounts Units,
        string? CallerBearerToken
    );

    public sealed record SettlementCall(
        Guid TenantId,
        string ReferenceType,
        Guid ReferenceId,
        int ConsumedUnits,
        string? CallerBearerToken
    );
}
