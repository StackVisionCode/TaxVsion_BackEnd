using BuildingBlocks.Authorization;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

internal static class MailboxAlertRecipients
{
    public static async Task<IReadOnlyList<Guid>> ResolveAsync(
        Guid tenantId,
        Guid createdByUserId,
        bool? isOfficeMailbox,
        Guid? mailboxOwnerUserId,
        IRecipientResolver resolver,
        CancellationToken ct
    )
    {
        var capability =
            isOfficeMailbox == true ? ConnectorsPermissions.AccountsOfficeRead : ConnectorsPermissions.AccountsRead;
        var validation = new RecipientValidation(
            RequiredCapability: capability,
            RequiredModule: PermissionModuleMap.ModuleFor(capability)
        );
        var directUserId = mailboxOwnerUserId ?? createdByUserId;
        var direct =
            directUserId == Guid.Empty
                ? null
                : await resolver.ResolveAsync(new Participants(tenantId, [directUserId]), validation, ct);

        // Política B — buzón de oficina: owner del tenant SIEMPRE se entera de la alerta,
        // además de quien lo conectó y los suscriptores de notifications.mailbox.alerts.
        var subscribers =
            isOfficeMailbox == true
                ? await OfficeAlertRecipients.ResolveOwnerAndSubscribersAsync(
                    tenantId,
                    NotificationPermissions.MailboxAlerts,
                    validation,
                    resolver,
                    ct
                )
                : [];

        return (direct?.IsSuccess == true ? direct.Value : []).Concat(subscribers).Distinct().ToArray();
    }
}
