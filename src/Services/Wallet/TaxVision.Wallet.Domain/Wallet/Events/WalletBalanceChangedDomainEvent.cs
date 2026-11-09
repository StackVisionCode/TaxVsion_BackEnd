using BuildingBlocks.Domain;

namespace TaxVision.Wallet.Domain.Wallet.Events;

/// <summary>El saldo del monedero cambió. <see cref="Movement"/> es el primer movimiento de la unidad de trabajo.</summary>
public sealed record WalletBalanceChangedDomainEvent(Guid WalletId, Guid TenantId, MovementType Movement, DateTime OccurredAtUtc)
    : IDomainEvent;
