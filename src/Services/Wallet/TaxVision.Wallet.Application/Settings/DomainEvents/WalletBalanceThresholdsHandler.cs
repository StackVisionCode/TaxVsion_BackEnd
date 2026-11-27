using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.TimeZones;
using TaxVision.Wallet.Application.Settings.Abstractions;
using TaxVision.Wallet.Application.Wallet.Abstractions;
using TaxVision.Wallet.Domain.Settings;
using TaxVision.Wallet.Domain.Wallet;
using TaxVision.Wallet.Domain.Wallet.Events;
using Wolverine;

namespace TaxVision.Wallet.Application.Settings.DomainEvents;

/// <summary>
/// Tras cada cambio de saldo (después del commit, cola domain-events): aviso de saldo bajo una vez por cruce y, si
/// la recarga automática está activa y bajo su umbral, crea la orden automática. Guarda primero y devuelve los
/// avisos como mensajes en cascada (outbox): una unidad por transacción.
/// </summary>
public static class WalletBalanceThresholdsHandler
{
    private const string WalletModule = "wallet";

    public static async Task<OutgoingMessages> Handle(
        WalletBalanceChangedDomainEvent changed,
        IWalletRepository wallets,
        IWalletSettingsRepository settingsRepository,
        IOfficeTimeZone officeTimeZone,
        ITenantEntitlementModulesReader modules,
        IUnitOfWork unitOfWork,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var messages = new OutgoingMessages();
        // Oficina sin el módulo: sin recarga ni avisos, y sin consumir el cruce del umbral (se evalúa al reactivar).
        if (!await modules.HasModuleAsync(changed.TenantId, WalletModule, ct))
            return messages;

        var wallet = await wallets.GetByTenantAsync(changed.TenantId, Domain.Wallet.Wallet.DefaultCurrency, ct);
        if (wallet is null)
            return messages;

        var stored = await settingsRepository.GetAsync(changed.TenantId, ct);
        var settings = stored ?? WalletSettings.CreateDefault(changed.TenantId);
        var available = wallet.AvailableMicros;
        var pending =
            settings.NeedsAutoRecharge(available) && await wallets.HasPendingAutomaticTopUpAsync(changed.TenantId, ct);
        var zone = await officeTimeZone.ResolveAsync(changed.TenantId, ct);
        var check = settings.CheckBalance(available, CivilDate.Of(DateTime.UtcNow, zone), pending);
        if (!check.Changed)
            return messages;

        var notices = new BalanceNotices(wallet, settings, envelope.CorrelationId ?? string.Empty);
        if (check.AutoRecharge == AutoRechargeDecision.Recharge)
            messages.Add(await notices.CreateAutomaticTopUpAsync(wallets, ct));
        if (stored is null)
            await settingsRepository.AddAsync(settings, ct);
        await unitOfWork.SaveChangesAsync(ct);

        notices.AddTo(messages, check);
        return messages;
    }
}

/// <summary>Arma los avisos del cambio de saldo con los datos ya guardados.</summary>
internal sealed class BalanceNotices(Domain.Wallet.Wallet wallet, WalletSettings settings, string correlationId)
{
    public async Task<WalletTopUpDueIntegrationEvent> CreateAutomaticTopUpAsync(
        IWalletRepository wallets,
        CancellationToken ct
    )
    {
        // Active implica consentimiento y método guardados (EnableAutoRecharge los exige).
        var topUp = WalletTopUp
            .CreateAutomatic(
                wallet.TenantId,
                settings.AutoRechargeAmountCents,
                wallet.Currency,
                settings.ConsentAcceptedByUserId!.Value,
                settings.AuthorizedMethod!
            )
            .Value;
        await wallets.AddTopUpAsync(topUp, ct);
        return new WalletTopUpDueIntegrationEvent
        {
            TenantId = wallet.TenantId,
            CorrelationId = correlationId,
            TopUpId = topUp.Id,
            AmountCents = topUp.AmountCents,
            Currency = topUp.Currency,
            IdempotencyKey = topUp.IdempotencyKey,
            RequestedByUserId = topUp.RequestedByUserId,
            Automatic = true,
            Provider = topUp.Provider,
            PaymentMethodReference = topUp.PaymentMethodReference,
        };
    }

    public void AddTo(OutgoingMessages messages, BalanceCheck check)
    {
        if (check.LowBalanceAlert)
            messages.Add(
                new WalletLowBalanceIntegrationEvent
                {
                    TenantId = wallet.TenantId,
                    CorrelationId = correlationId,
                    AvailableMicros = wallet.AvailableMicros,
                    ThresholdMicros = settings.LowBalanceThresholdMicros,
                    Currency = wallet.Currency,
                }
            );
        if (check.AutoRecharge == AutoRechargeDecision.DailyLimitReached)
            messages.Add(
                new WalletAutoRechargeDailyLimitReachedIntegrationEvent
                {
                    TenantId = wallet.TenantId,
                    CorrelationId = correlationId,
                    DailyLimit = WalletSettings.DailyRechargeLimit,
                    AmountCents = settings.AutoRechargeAmountCents,
                    Currency = wallet.Currency,
                }
            );
    }
}
