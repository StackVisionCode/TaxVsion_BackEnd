using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Correo obligatorio de un aviso a la oficina: uno por dirección, un render por idioma y lector, todos antes de
/// encolar. Va antes de la campana: si Scribe falla, el reintento no repite avisos. La clave de plantilla no es el
/// kind (el in-app ya usa el kind al buscar duplicados).
/// </summary>
public static class OfficeAlertEmails
{
    /// <summary>El archivo en Documents del CRM.</summary>
    public static async Task<string> FileUrlAsync(PushLinks links, Guid tenantId, Guid fileId, CancellationToken ct) =>
        $"{await links.StaffBaseAsync(tenantId, ct)}/documents?fileId={fileId}";

    /// <summary>Settings → Online payments del CRM.</summary>
    public static async Task<string> BillingUrlAsync(PushLinks links, Guid tenantId, CancellationToken ct) =>
        $"{await links.StaffBaseAsync(tenantId, ct)}/settings/billing";

    /// <param name="intent">Kind, oficina y evento de origen; el destinatario y el correo los pone este método.</param>
    public static async Task SendAsync(
        NotificationIntent intent,
        string eventKey,
        IReadOnlyDictionary<string, object?> variables,
        IReadOnlyList<Guid> recipients,
        NotificationDispatcher dispatcher,
        IUserEmailResolver emails,
        IScribeRenderClient scribe,
        IRecipientLocale locales,
        CancellationToken ct
    )
    {
        var addresses = new List<(Guid UserId, string Address)>();
        foreach (var userId in recipients)
        {
            var address = await emails.ResolveAsync(intent.TenantId, userId, ct);
            if (
                !string.IsNullOrWhiteSpace(address)
                && !addresses.Exists(known => string.Equals(known.Address, address, StringComparison.OrdinalIgnoreCase))
            )
                addresses.Add((userId, address));
        }
        if (addresses.Count == 0)
            return;

        var renders = new RecipientRenders(scribe, eventKey, intent.TenantId, ScribeRenderAudience.Staff, variables);
        var languages = new RecipientLocaleCache(locales, intent.TenantId);
        var targets = new List<NotificationTarget>(addresses.Count);
        foreach (var (userId, address) in addresses)
        {
            var locale = await languages.ForUserAsync(userId, ct);
            await renders.ForAsync(locale, ct);
            targets.Add(new NotificationTarget(userId, address, Locale: locale));
        }

        foreach (var target in targets)
            await dispatcher.DispatchAsync(
                intent with
                {
                    Target = target,
                    Email = new EmailContent(eventKey, variables, Render: renders.ForAsync),
                },
                ct
            );
    }
}
