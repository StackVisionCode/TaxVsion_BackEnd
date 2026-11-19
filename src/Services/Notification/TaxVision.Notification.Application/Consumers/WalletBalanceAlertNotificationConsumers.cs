using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>El saldo bajó del umbral: correo y campana a quien maneja el Wallet. Una vez por cruce (lo asegura el Wallet).</summary>
public static class WalletLowBalanceNotificationConsumer
{
    public const string EmailEventKey = "wallet.low_balance.v1";

    public static async Task Handle(
        WalletLowBalanceIntegrationEvent evt,
        WalletAlertSender sender,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var available = WalletAlertSender.FromMicros(evt.AvailableMicros, evt.Currency);
            var threshold = WalletAlertSender.FromMicros(evt.ThresholdMicros, evt.Currency);
            var crm = await sender.StaffBaseAsync(evt.TenantId, ct);
            var alert = new WalletAlert(
                evt.TenantId,
                evt.EventId,
                evt.OccurredOn,
                "wallet.low-balance.v1",
                NotificationKinds.WalletLowBalance,
                EmailEventKey,
                evt.EventId.ToString("N"),
                "Your wallet balance is low",
                $"Your Wallet has {available} left, below your {threshold} alert threshold. Add funds to keep messages sending.",
                new Dictionary<string, object?>
                {
                    ["available_label"] = available,
                    ["threshold_label"] = threshold,
                    ["wallet_url"] = crm + WalletAlertSender.WalletPath,
                    ["settings_url"] = crm + WalletAlertSender.SettingsPath,
                },
                evt.TenantId.ToString(),
                $"wallet:low-balance:{evt.TenantId:N}"
            );
            await sender.SendAsync(alert, await sender.RecipientsAsync(evt.TenantId, ct), ct);
        }
    }
}

/// <summary>Recibo de una recarga automática: monto, método, saldo después y hora en la zona de la oficina.</summary>
public static class WalletAutoRechargeSucceededNotificationConsumer
{
    public const string EmailEventKey = "wallet.auto_recharge_succeeded.v1";

    public static async Task Handle(
        WalletAutoRechargeSucceededIntegrationEvent evt,
        WalletAlertSender sender,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var amount = WalletAlertSender.FromCents(evt.AmountCents, evt.Currency);
            var after = WalletAlertSender.FromMicros(evt.AvailableAfterMicros, evt.Currency);
            var crm = await sender.StaffBaseAsync(evt.TenantId, ct);
            var alert = new WalletAlert(
                evt.TenantId,
                evt.EventId,
                evt.OccurredOn,
                "wallet.auto-recharge.succeeded.v1",
                NotificationKinds.WalletAutoRechargeSucceeded,
                EmailEventKey,
                evt.TopUpId.ToString("N"),
                "Automatic recharge completed",
                $"We added {amount} to your Wallet using {evt.MethodLabel}. New balance: {after}.",
                new Dictionary<string, object?>
                {
                    ["amount_label"] = amount,
                    ["method_label"] = evt.MethodLabel,
                    ["balance_after_label"] = after,
                    ["charged_at"] = ScribeDates.Instant(evt.OccurredOn),
                    ["wallet_url"] = crm + WalletAlertSender.WalletPath,
                    ["settings_url"] = crm + WalletAlertSender.SettingsPath,
                },
                evt.TopUpId.ToString(),
                $"wallet:auto-recharge:{evt.TopUpId:N}:succeeded"
            );
            await sender.SendAsync(alert, await sender.RecipientsAsync(evt.TenantId, ct), ct);
        }
    }
}

/// <summary>La recarga automática quedó en pausa: por qué, que no reintenta sola y dónde revisarla.</summary>
public static class WalletAutoRechargePausedNotificationConsumer
{
    public const string EmailEventKey = "wallet.auto_recharge_paused.v1";

    public static async Task Handle(
        WalletAutoRechargePausedIntegrationEvent evt,
        WalletAlertSender sender,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var amount = WalletAlertSender.FromCents(evt.AmountCents, evt.Currency);
            var reason = Reason(evt.Reason, evt.MethodLabel, amount);
            var delivery = evt.TopUpId ?? evt.EventId;
            var alert = new WalletAlert(
                evt.TenantId,
                evt.EventId,
                evt.OccurredOn,
                "wallet.auto-recharge.paused.v1",
                NotificationKinds.WalletAutoRechargePaused,
                EmailEventKey,
                delivery.ToString("N"),
                "Automatic recharge is paused",
                $"{reason} It won't retry on its own. Review it in Settings → Wallet.",
                new Dictionary<string, object?>
                {
                    ["reason_text"] = reason,
                    ["amount_label"] = amount,
                    ["method_label"] = evt.MethodLabel,
                    ["settings_url"] = await sender.StaffBaseAsync(evt.TenantId, ct) + WalletAlertSender.SettingsPath,
                },
                (evt.TopUpId ?? evt.TenantId).ToString(),
                $"wallet:auto-recharge:paused:{delivery:N}"
            );
            await sender.SendAsync(alert, await sender.RecipientsAsync(evt.TenantId, ct), ct);
        }
    }

    internal static string Reason(string reason, string method, string amount) =>
        reason switch
        {
            "PaymentFailed" => $"We couldn't charge {method} for your {amount} automatic recharge.",
            "MethodUnavailable" =>
                $"Your saved payment method {method} is no longer available, so we couldn't run your {amount} automatic recharge.",
            _ => $"We couldn't run your {amount} automatic recharge with {method}.",
        };
}

/// <summary>Se llegó al tope diario de recargas automáticas: no pausa, sigue mañana.</summary>
public static class WalletAutoRechargeDailyLimitReachedNotificationConsumer
{
    public const string EmailEventKey = "wallet.auto_recharge_daily_limit.v1";

    public static async Task Handle(
        WalletAutoRechargeDailyLimitReachedIntegrationEvent evt,
        WalletAlertSender sender,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var amount = WalletAlertSender.FromCents(evt.AmountCents, evt.Currency);
            var limit = evt.DailyLimit.ToString(CultureInfo.InvariantCulture);
            var alert = new WalletAlert(
                evt.TenantId,
                evt.EventId,
                evt.OccurredOn,
                "wallet.auto-recharge.daily-limit.v1",
                NotificationKinds.WalletAutoRechargeDailyLimitReached,
                EmailEventKey,
                evt.EventId.ToString("N"),
                "Daily automatic recharge limit reached",
                $"Your Wallet reached its limit of {limit} automatic recharges today ({amount} each). It continues tomorrow.",
                new Dictionary<string, object?>
                {
                    ["daily_limit"] = limit,
                    ["amount_label"] = amount,
                    ["settings_url"] = await sender.StaffBaseAsync(evt.TenantId, ct) + WalletAlertSender.SettingsPath,
                },
                evt.TenantId.ToString(),
                $"wallet:auto-recharge:daily-limit:{evt.EventId:N}"
            );
            await sender.SendAsync(alert, await sender.RecipientsAsync(evt.TenantId, ct), ct);
        }
    }
}
