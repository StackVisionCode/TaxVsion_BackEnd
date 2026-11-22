using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Directory.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Resuelve <see cref="RecipientLocale"/> con las proyecciones locales, sin llamar a nadie. Solo lectura.
/// El idioma de la oficina aún no entra en la cadena (D-N2: segundo paso).
/// </summary>
public sealed class RecipientLocaleResolver(
    IUserEmailDirectoryRepository directory,
    ICustomerPortalAccountProjectionRepository portalAccounts,
    ICustomerContactPolicyRepository policies,
    ITenantOwnerProjectionRepository offices
) : IRecipientLocale
{
    public async Task<RecipientLocale> ForUserAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var entry = await directory.FindAsync(tenantId, userId, ct);
        var zone = entry?.TimeZoneId;
        if (Normalize(entry?.Language) is { } own)
            return new RecipientLocale(own, zone, false);

        // Cuenta del portal sin idioma propio: el que la oficina tiene puesto al cliente.
        if (await portalAccounts.GetByUserAsync(tenantId, userId, ct) is { } account)
        {
            var customer = await ForCustomerAsync(tenantId, account.CustomerId, ct);
            return customer with { TimeZoneId = zone };
        }
        return RecipientLocale.English with { TimeZoneId = zone };
    }

    public async Task<RecipientLocale> ForCustomerAsync(Guid tenantId, Guid customerId, CancellationToken ct)
    {
        var policy = await policies.GetByCustomerIdAsync(tenantId, customerId, ct);
        return Normalize(policy?.Language) is { } language
            ? new RecipientLocale(language, null, false)
            : RecipientLocale.English;
    }

    public async Task<RecipientLocale> ForOfficeEmailAsync(Guid tenantId, string email, CancellationToken ct)
    {
        var address = email.Trim();
        var owner = await offices.GetActiveOwnerUserIdAsync(tenantId, ct);
        var admins = await offices.GetActiveAdminUserIdsAsync(tenantId, ct);
        foreach (var userId in admins.Prepend(owner ?? Guid.Empty).Where(id => id != Guid.Empty).Distinct())
        {
            var entry = await directory.FindAsync(tenantId, userId, ct);
            if (entry is not null && string.Equals(entry.Email.Trim(), address, StringComparison.OrdinalIgnoreCase))
                return await ForUserAsync(tenantId, userId, ct);
        }
        return RecipientLocale.English;
    }

    public RecipientLocale FromEventLanguage(string? language) =>
        Normalize(language) is { } value ? new RecipientLocale(value, null, false) : RecipientLocale.English;

    // Solo en y es (D-N39); pt, fr y lo desconocido caen al respaldo.
    private static string? Normalize(string? language) =>
        language?.Trim().ToLowerInvariant() switch
        {
            "en" => "en",
            "es" => "es",
            _ => null,
        };
}
