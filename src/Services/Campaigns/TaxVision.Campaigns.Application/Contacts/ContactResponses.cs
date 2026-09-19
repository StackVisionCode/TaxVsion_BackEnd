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

/// <summary>Resultado de un import CSV: cuántos contactos se crearon, se reusaron (dedupe) o se descartaron por inválidos, y cuántas membresías se agregaron.</summary>
public sealed record ImportContactsResponse(int Created, int Reused, int Invalid, int MembersAdded);
