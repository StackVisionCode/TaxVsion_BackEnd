using TaxVision.Campaigns.Domain.Campaigns;

namespace TaxVision.Campaigns.Api.Requests;

// ─────────────────────────── Contacts ───────────────────────────

public sealed record CreateContactRequest(string? Name, string? Email, string? PhoneE164);

public sealed record UpdateContactRequest(string? Name, string? Email, string? PhoneE164);

/// <summary>Cambia el consentimiento por canal. <c>OptedOut=true</c> da de baja; <c>false</c> re-suscribe.</summary>
public sealed record SetContactOptOutRequest(IReadOnlyList<CampaignChannel> Channels, bool OptedOut)
{
    public CampaignChannel ToFlag() =>
        Channels is null ? CampaignChannel.None : Channels.Aggregate(CampaignChannel.None, (acc, c) => acc | c);
}

// ─────────────────────────── Contact lists ───────────────────────────

public sealed record CreateContactListRequest(string Name, string? Description);

public sealed record UpdateContactListRequest(string Name, string? Description);

public sealed record AddListMemberRequest(Guid ContactId);

/// <summary>Agrega clientes seleccionados (ids del directorio de Customer) como miembros de la lista.</summary>
public sealed record AddCustomersToListRequest(IReadOnlyList<Guid>? CustomerIds);

/// <summary>Import CSV: contenido crudo <c>name,email,phone</c> por línea (encabezado opcional).</summary>
public sealed record ImportContactsRequest(string Csv);

// ─────────────────────────── Audience send ───────────────────────────

public sealed record ManualAudienceEntryRequest(string? Email, string? PhoneE164);

/// <summary>Envío inmediato resolviendo audiencia desde listas, clientes (Customer) y/o entradas manuales.</summary>
public sealed record SendToAudienceRequest(
    IReadOnlyList<Guid>? ContactListIds,
    IReadOnlyList<ManualAudienceEntryRequest>? Manual,
    bool IncludeCustomers = false,
    // Clientes seleccionados (fuente "Clients" acotada). No vacío ⇒ solo esos; vacío + IncludeCustomers ⇒ todos.
    IReadOnlyList<Guid>? CustomerIds = null
);
