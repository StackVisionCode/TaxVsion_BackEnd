using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Application.Contacts;

/// <summary>DTO de salida de un contacto — nunca se devuelve el aggregate al Api.</summary>
public sealed record ContactResponse(
    Guid Id,
    Guid TenantId,
    string? Name,
    string? Email,
    string? PhoneE164,
    string Source,
    Guid? CustomerRef,
    IReadOnlyList<string> OptedOutChannels,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
)
{
    public static ContactResponse From(Contact c) =>
        new(
            c.Id,
            c.TenantId,
            c.Name,
            c.Email,
            c.PhoneE164,
            c.Source.ToString(),
            c.CustomerRef,
            SplitChannels(c.OptedOutChannels),
            c.CreatedAtUtc,
            c.UpdatedAtUtc
        );

    private static IReadOnlyList<string> SplitChannels(CampaignChannel channels) =>
        Enum.GetValues<CampaignChannel>()
            .Where(ch => ch != CampaignChannel.None && channels.HasFlag(ch))
            .Select(ch => ch.ToString())
            .ToList();
}

/// <summary>DTO de salida de una lista de contactos (con conteo de miembros).</summary>
public sealed record ContactListResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    int MemberCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc
)
{
    public static ContactListResponse From(ContactList l) =>
        new(l.Id, l.TenantId, l.Name, l.Description, l.Members.Count, l.CreatedAtUtc, l.UpdatedAtUtc);
}

/// <summary>
/// Resultado de un import CSV. Parte "contactos": cuántos se crearon, se reusaron (dedupe) o se
/// descartaron por inválidos, y cuántas membresías se agregaron a la lista. Parte "clientes" (Etapa C):
/// cada fila con email se provisiona además como cliente en el servicio Customer (on-behalf-of, con el
/// token del usuario que importa) — <see cref="CustomersCreated"/>/<see cref="CustomersExisting"/> y, si
/// algo no pudo, <see cref="CustomersSkippedNoEmail"/>/<see cref="CustomersFailed"/>.
/// <see cref="CustomerPermissionDenied"/> en <c>true</c> = el usuario no tiene permiso para crear clientes
/// (los contactos locales igual se importaron).
/// </summary>
public sealed record ImportContactsResponse(
    int Created,
    int Reused,
    int Invalid,
    int MembersAdded,
    int CustomersCreated = 0,
    int CustomersExisting = 0,
    int CustomersSkippedNoEmail = 0,
    int CustomersFailed = 0,
    bool CustomerPermissionDenied = false
);
