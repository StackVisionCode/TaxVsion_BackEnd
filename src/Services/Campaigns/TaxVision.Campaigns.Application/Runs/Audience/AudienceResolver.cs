using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Domain;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Contacts;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Audience;

/// <summary>Una entrada de audiencia manual (dirección/número suelto, no un contacto guardado).</summary>
public sealed record ManualAudienceEntry(string? Email, string? PhoneE164);

/// <summary>
/// Resuelve una <c>AudienceSpec</c> (ContactLists + Manual — Customer llega en una fase posterior) a
/// unidades <see cref="RunRecipientDraft"/> (una por contacto/canal) para un run
/// (<c>Domain_Design.md §6/§7.5</c>). NO materializa audiencia dentro de la campaña (el snapshot stale
/// es anti-patrón legado): resuelve por ejecución. Reglas de este slice:
/// <list type="bullet">
///   <item>Solo los canales activos de la campaña.</item>
///   <item><b>Opt-out</b> por canal: un contacto dado de baja de un canal se EXCLUYE de ese canal
///   (la visibilidad como <c>Skipped(opt_out)</c> es refinamiento posterior).</item>
///   <item>Sin destino para el canal → se excluye esa unidad.</item>
///   <item><b>Dedupe</b> por <c>(canal, destino normalizado)</c> — la supresión prevalece (#14).</item>
/// </list>
/// SIN dinero.
/// </summary>
public static class AudienceResolver
{
    public static async Task<IReadOnlyList<RunRecipientDraft>> ResolveAsync(
        Guid tenantId,
        CampaignChannel channels,
        IReadOnlyCollection<Guid> contactListIds,
        IReadOnlyCollection<ManualAudienceEntry> manual,
        IContactRepository contacts,
        IContactListRepository lists,
        bool includeCustomers = false,
        ICustomerAudienceClient? customerClient = null,
        CancellationToken ct = default
    )
    {
        var activeChannels = Enum.GetValues<CampaignChannel>()
            .Where(c => c != CampaignChannel.None && channels.HasFlag(c))
            .ToList();

        var units = new List<RunRecipientDraft>();
        var seen = new HashSet<string>(StringComparer.Ordinal); // "canal|destino" → dedupe global

        // 1) Contactos de las listas (unión; un contacto puede estar en varias listas → set de ids).
        if (contactListIds.Count > 0)
        {
            var memberIds = await lists.GetMemberContactIdsAsync(tenantId, contactListIds, ct);
            if (memberIds.Count > 0)
            {
                var members = await contacts.GetManyByIdsAsync(tenantId, memberIds, ct);
                foreach (var contact in members)
                foreach (var channel in activeChannels)
                {
                    if (contact.IsOptedOut(channel))
                        continue;
                    var destination = contact.DestinationFor(channel);
                    if (string.IsNullOrWhiteSpace(destination))
                        continue;
                    if (!seen.Add($"{channel}|{destination}"))
                        continue;
                    units.Add(
                        new RunRecipientDraft(contact.Id.ToString("N"), channel, contact.Email, contact.PhoneE164)
                    );
                }
            }
        }

        // 2) Clientes (directorio de Customer, M2M). Fuente AudienceSpec.Clients. Sin opt-out por-canal
        // (no son contactos guardados de Campaigns); la supresión sigue aplicando por dedupe de destino.
        if (includeCustomers && customerClient is not null)
        {
            var customers = await customerClient.GetActiveCustomersAsync(tenantId, ct);
            foreach (var customer in customers)
            foreach (var channel in activeChannels)
            {
                var destination = channel switch
                {
                    CampaignChannel.Email => customer.Email,
                    CampaignChannel.Sms or CampaignChannel.WhatsApp => customer.PhoneE164,
                    _ => null,
                };
                if (string.IsNullOrWhiteSpace(destination))
                    continue;
                if (!seen.Add($"{channel}|{destination}"))
                    continue;
                units.Add(
                    new RunRecipientDraft(
                        customer.CustomerId.ToString("N"),
                        channel,
                        customer.Email,
                        customer.PhoneE164
                    )
                );
            }
        }

        // 3) Audiencia manual (no son contactos guardados → id generado por entrada, nunca "manual").
        foreach (var entry in manual)
        {
            var email = string.IsNullOrWhiteSpace(entry.Email) ? null : entry.Email.Trim().ToLowerInvariant();
            var phone = PhoneNumbers.ToE164(entry.PhoneE164); // null si no es E.164 → no genera unidad SMS
            if (email is null && phone is null)
                continue;

            var contactRef = Guid.NewGuid().ToString("N");
            foreach (var channel in activeChannels)
            {
                var destination = channel switch
                {
                    CampaignChannel.Email => email,
                    CampaignChannel.Sms or CampaignChannel.WhatsApp => phone,
                    _ => null,
                };
                if (string.IsNullOrWhiteSpace(destination))
                    continue;
                if (!seen.Add($"{channel}|{destination}"))
                    continue;
                units.Add(new RunRecipientDraft(contactRef, channel, email, phone));
            }
        }

        return units;
    }
}
