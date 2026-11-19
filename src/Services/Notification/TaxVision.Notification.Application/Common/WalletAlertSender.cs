using System.Globalization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <summary>Un aviso del Wallet: el mismo texto va a la campana y el correo de cada destinatario.</summary>
/// <param name="DeliveryKey">Hecho de negocio que deduplica el correo (la recarga o el evento).</param>
public sealed record WalletAlert(
    Guid TenantId,
    Guid EventId,
    DateTime OccurredOn,
    string SourceEventType,
    string Kind,
    string EmailEventKey,
    string DeliveryKey,
    string Title,
    string Body,
    IReadOnlyDictionary<string, object?> EmailVariables,
    string ResourceId,
    string GroupKey
);

/// <summary>
/// Entrega los avisos del Wallet a quien tiene <c>wallet.manage</c>; si no hay nadie, al owner y los admins.
/// Primero los correos (Scribe es lo único que puede lanzar, así un reintento no repite la campana) y después
/// la campana por destinatario. La clave del correo lleva el hecho y la dirección: uno por persona y evento.
/// </summary>
public sealed class WalletAlertSender(
    NotificationDispatcher dispatcher,
    IRecipientResolver resolver,
    ITenantOwnerProjectionRepository offices,
    IUserEmailResolver emails,
    IEmailDispatchGateway gateway,
    IScribeRenderClient scribe,
    ITenantHostResolver hosts,
    IOptions<PortalOptions> portal,
    ICorrelationContext correlation
)
{
    public const string WalletPath = "/wallet";
    public const string SettingsPath = "/settings/wallet";

    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    /// <summary>"$3.20" en USD; "3.20 EUR" en otra moneda.</summary>
    public static string Money(decimal amount, string? currency) =>
        string.IsNullOrWhiteSpace(currency) || string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)
            ? amount.ToString("C2", English)
            : $"{amount.ToString("N2", English)} {currency.ToUpperInvariant()}";

    public static string FromCents(long cents, string? currency) => Money(cents / 100m, currency);

    public static string FromMicros(long micros, string? currency) => Money(micros / 1_000_000m, currency);

    /// <summary>Base del CRM de la oficina (su subdominio, o el base fijo si no se resolvió).</summary>
    public async Task<string> StaffBaseAsync(Guid tenantId, CancellationToken ct) =>
        TenantEmailLinks.StaffBase(await hosts.ResolveHostAsync(tenantId, ct), portal.Value);

    public async Task<IReadOnlyList<Guid>> RecipientsAsync(Guid tenantId, CancellationToken ct)
    {
        var managers = await resolver.ResolveAsync(
            new ByPermission(tenantId, WalletPermissions.Manage),
            RecipientValidation.None,
            ct
        );
        if (managers.IsSuccess && managers.Value.Count > 0)
            return managers.Value;

        // Nadie con wallet.manage: los administradores de la oficina, como el resto de avisos de facturación.
        var owner = await resolver.ResolveAsync(new TenantOwnerAudience(tenantId), RecipientValidation.None, ct);
        var admins = await offices.GetActiveAdminUserIdsAsync(tenantId, ct);
        return (owner.IsSuccess ? owner.Value : []).Concat(admins).Where(id => id != Guid.Empty).Distinct().ToArray();
    }

    public async Task SendAsync(WalletAlert alert, IReadOnlyList<Guid> recipients, CancellationToken ct)
    {
        await SendEmailsAsync(alert, recipients, ct);
        foreach (var userId in recipients)
            await RecordInAppAsync(alert, userId, ct);
    }

    private async Task SendEmailsAsync(WalletAlert alert, IReadOnlyList<Guid> recipients, CancellationToken ct)
    {
        var addresses = new List<(Guid UserId, string Address)>();
        foreach (var userId in recipients)
        {
            var address = await emails.ResolveAsync(alert.TenantId, userId, ct);
            if (
                !string.IsNullOrWhiteSpace(address)
                && !addresses.Exists(known => string.Equals(known.Address, address, StringComparison.OrdinalIgnoreCase))
            )
                addresses.Add((userId, address));
        }
        if (addresses.Count == 0)
            return;

        var render = (
            await scribe.RenderAsync(alert.EmailEventKey, alert.TenantId, alert.EmailVariables, ct)
        ).EnsureRendered(alert.EmailEventKey);

        foreach (var (userId, address) in addresses)
            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    alert.TenantId,
                    address,
                    alert.EmailEventKey,
                    alert.EventId,
                    correlation.CorrelationId
                ) with
                {
                    RecipientUserId = userId,
                    Category = NotificationCategory.Billing,
                    NotificationKind = alert.Kind,
                    IdempotencyKey = $"{alert.Kind}:{alert.DeliveryKey}:{address.Trim().ToLowerInvariant()}",
                },
                ct
            );
    }

    private Task RecordInAppAsync(WalletAlert alert, Guid userId, CancellationToken ct) =>
        dispatcher.RecordInAppAsync(
            alert.TenantId,
            NotificationRecipient.ForUser(userId),
            alert.Title,
            NotificationCategory.Billing,
            alert.Kind,
            alert.EventId,
            correlation.CorrelationId,
            recipientUserId: userId,
            inApp: new InAppNotificationRequest(
                alert.Kind,
                userId,
                alert.EventId,
                alert.SourceEventType,
                alert.OccurredOn,
                alert.Title,
                FallbackBody: alert.Body,
                ResourceId: alert.ResourceId,
                GroupKey: alert.GroupKey
            ),
            ct: ct
        );
}
