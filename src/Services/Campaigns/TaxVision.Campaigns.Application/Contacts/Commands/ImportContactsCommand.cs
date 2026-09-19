using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Domain;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Application.Contacts.Commands;

/// <summary>
/// Importa contactos (CSV) a una lista existente (<c>Domain_Design.md §6</c>). Formato por línea:
/// <c>name,email,phone</c> (email y/o teléfono; el nombre es opcional). Dedupe por email/teléfono
/// normalizados: si el contacto ya existe (en la BD o antes en el mismo lote) se reusa; si no, se crea
/// con source <see cref="ContactSource.Import"/>. Cada contacto se agrega como miembro de la lista.
/// Parser CSV mínimo (sin comas embebidas ni comillas complejas) — suficiente para el slice.
/// </summary>
public sealed record ImportContactsCommand(Guid TenantId, Guid ContactListId, string CsvContent);

public static class ImportContactsHandler
{
    public static async Task<Result<ImportContactsResponse>> Handle(
        ImportContactsCommand command,
        IContactListRepository lists,
        IContactRepository contacts,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var list = await lists.GetByIdAsync(command.TenantId, command.ContactListId, ct);
        if (list is null)
            return Result.Failure<ImportContactsResponse>(ContactListErrors.NotFound);

        int created = 0,
            reused = 0,
            invalid = 0,
            membersAdded = 0;

        // Dedupe dentro del lote: destino normalizado → Contact ya materializado en esta importación
        // (FindByDestination no ve las inserciones aún no guardadas).
        var batch = new Dictionary<string, Contact>(StringComparer.Ordinal);

        foreach (var (name, email, phone) in ParseRows(command.CsvContent))
        {
            // Import tolerante: un teléfono no-E.164 se descarta (se conserva el email) en vez de tumbar la fila.
            var e164 = PhoneNumbers.ToE164(phone);
            var draft = Contact.Create(command.TenantId, name, email, e164, ContactSource.Import);
            if (draft.IsFailure)
            {
                invalid++;
                continue;
            }
            var normalized = draft.Value; // usa el email/teléfono ya normalizados por el dominio

            var contact = FindInBatch(batch, normalized);
            if (contact is null)
            {
                contact = await contacts.FindByDestinationAsync(
                    command.TenantId,
                    normalized.Email,
                    normalized.PhoneE164,
                    ct
                );
                if (contact is null)
                {
                    contact = normalized;
                    await contacts.AddAsync(contact, ct);
                    created++;
                }
                else
                {
                    reused++;
                }
                IndexInBatch(batch, contact);
            }
            else
            {
                reused++;
            }

            var before = list.Members.Count;
            list.AddMember(contact.Id);
            if (list.Members.Count > before)
                membersAdded++;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ImportContactsResponse(created, reused, invalid, membersAdded));
    }

    private static Contact? FindInBatch(Dictionary<string, Contact> batch, Contact c)
    {
        if (c.Email is not null && batch.TryGetValue("e:" + c.Email, out var byEmail))
            return byEmail;
        if (c.PhoneE164 is not null && batch.TryGetValue("p:" + c.PhoneE164, out var byPhone))
            return byPhone;
        return null;
    }

    private static void IndexInBatch(Dictionary<string, Contact> batch, Contact c)
    {
        if (c.Email is not null)
            batch["e:" + c.Email] = c;
        if (c.PhoneE164 is not null)
            batch["p:" + c.PhoneE164] = c;
    }

    private static IEnumerable<(string? Name, string? Email, string? Phone)> ParseRows(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            yield break;

        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = true;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var cols = line.Split(',');
            var name = Field(cols, 0);
            var email = Field(cols, 1);
            var phone = Field(cols, 2);

            // Salta una fila de encabezado ("name,email,phone" en cualquier orden/caso).
            if (first)
            {
                first = false;
                if (LooksLikeHeader(name, email, phone))
                    continue;
            }

            yield return (name, email, phone);
        }
    }

    private static bool LooksLikeHeader(string? a, string? b, string? c)
    {
        var joined = string.Join(",", a, b, c).ToLowerInvariant();
        return joined.Contains("email")
            || joined.Contains("name")
            || joined.Contains("phone")
            || joined.Contains("tel");
    }

    private static string? Field(string[] cols, int i)
    {
        if (i >= cols.Length)
            return null;
        var v = cols[i].Trim().Trim('"').Trim();
        return v.Length == 0 ? null : v;
    }
}
