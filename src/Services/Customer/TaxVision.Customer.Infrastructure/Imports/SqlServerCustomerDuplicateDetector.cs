using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Detecta duplicados con las reglas del alta (<see cref="CustomerDuplicateRules"/>): Email; Teléfono + Nombre;
/// Nombre + DOB; sin archivados. El import suma antes el identificador fiscal por blind index (el alta no lo pide).
///
/// IgnoreQueryFilters (B-29): el import corre en un scope DI propio sin tenant ambiente y el filtro fail-closed
/// escondía todo; el tenant va explícito en cada consulta.
/// </summary>
public sealed class SqlServerCustomerDuplicateDetector(CustomerDbContext db, ISensitiveDataProtector protector)
    : ICustomerDuplicateDetector
{
    private const string ByTaxIdentifier = "TaxIdentifier";

    public async Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
        Guid tenantId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    )
    {
        if (chunk.Count == 0)
            return [];

        // ---- Llaves por fila: las mismas que arma el alta, más el blind index ----
        var rows = chunk
            .Select(row => new RowKeys(row.RowNumber, ImportDuplicateKeys.From(row), BlindIndexesOf(row, tenantId)))
            .ToList();

        var allBlindIndexes = rows.SelectMany(r => r.BlindIndexes).ToHashSet();
        var allEmails = rows.Select(r => r.Keys.Email).OfType<string>().ToHashSet();
        var allPhones = rows.Where(r => r.Keys.Name is not null)
            .Select(r => r.Keys.PhoneE164)
            .OfType<string>()
            .ToHashSet();
        var allNames = rows.Select(r => r.Keys.Name).OfType<string>().ToHashSet();

        // ---- UNA query por chunk: superconjunto de candidatos, la decisión exacta es en memoria ----
        var candidates = await (
            from c in db
                .Customers.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && c.Status != CustomerStatus.Archived)
            from fp in db
                .CustomerFiscalProfiles.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(fp => fp.CustomerId == c.Id)
                .DefaultIfEmpty()
            where
                (fp != null && allBlindIndexes.Contains(fp.TaxIdentifierBlindIndex))
                || allEmails.Contains(c.PrimaryEmail.NormalizedValue)
                || (
                    c.PrimaryPhone != null
                    && allPhones.Contains(c.PrimaryPhone.E164Value)
                    && allNames.Contains(c.DisplayName.ToLower())
                )
                || (c.DateOfBirth != null && allNames.Contains(c.DisplayName.ToLower()))
            select new
            {
                Customer = new CustomerDuplicateRules.ExistingCustomer(
                    c.Id,
                    c.DisplayName,
                    c.DateOfBirth,
                    c.PrimaryEmail.NormalizedValue,
                    c.PrimaryPhone != null ? c.PrimaryPhone.E164Value : null
                ),
                BlindIndex = fp != null ? fp.TaxIdentifierBlindIndex : null,
            }
        ).ToListAsync(ct);

        if (candidates.Count == 0)
            return [];

        var existing = candidates.Select(c => c.Customer).ToList();
        var matches = new List<DuplicateMatch>(chunk.Count);

        foreach (var row in rows)
        {
            // Prioridad 1 (solo import): blind index contra cualquiera de los candidatos de la fila.
            var byTaxId = candidates.FirstOrDefault(c =>
                c.BlindIndex != null && row.BlindIndexes.Contains(c.BlindIndex)
            );
            if (byTaxId is not null)
            {
                matches.Add(
                    new DuplicateMatch(
                        row.RowNumber,
                        byTaxId.Customer.Id,
                        byTaxId.Customer.DisplayName,
                        ByTaxIdentifier
                    )
                );
                continue;
            }

            if (CustomerDuplicateRules.Match(row.Keys, existing) is { } hit)
                matches.Add(
                    new DuplicateMatch(row.RowNumber, hit.Customer.Id, hit.Customer.DisplayName, hit.MatchedBy)
                );
        }

        return matches;
    }

    /// <summary>Las reglas del alta para un solo candidato; excluye al propio cliente al editar.</summary>
    public async Task<DuplicateMatch?> FindDuplicateAsync(
        Guid tenantId,
        CustomerDuplicateCandidate candidate,
        Guid? excludeCustomerId,
        CancellationToken ct
    )
    {
        var keys = CustomerDuplicateRules.KeysOf(candidate);
        var email = keys.Email;
        var phone = keys.PhoneE164;
        var name = keys.Name;
        var dob = keys.DateOfBirth;

        var existing = await db
            .Customers.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Status != CustomerStatus.Archived)
            .Where(c => excludeCustomerId == null || c.Id != excludeCustomerId)
            .Where(c =>
                (email != null && c.PrimaryEmail.NormalizedValue == email)
                || (
                    phone != null
                    && name != null
                    && c.PrimaryPhone != null
                    && c.PrimaryPhone.E164Value == phone
                    && c.DisplayName.ToLower() == name
                )
                || (name != null && dob != null && c.DateOfBirth == dob && c.DisplayName.ToLower() == name)
            )
            .Select(c => new CustomerDuplicateRules.ExistingCustomer(
                c.Id,
                c.DisplayName,
                c.DateOfBirth,
                c.PrimaryEmail.NormalizedValue,
                c.PrimaryPhone != null ? c.PrimaryPhone.E164Value : null
            ))
            .ToListAsync(ct);

        return CustomerDuplicateRules.Match(keys, existing) is { } hit
            ? new DuplicateMatch(0, hit.Customer.Id, hit.Customer.DisplayName, hit.MatchedBy)
            : null;
    }

    // Lookup dual (B-9/X-9): clave actual ∪ anterior, para casar filas aún no re-cifradas durante la rotación.
    private IReadOnlyCollection<string> BlindIndexesOf(ImportCustomerRow row, Guid tenantId)
    {
        var normalizedTaxId = IdentifierNormalizer.NormalizeDigits(row.TaxIdentifier);
        return normalizedTaxId.Length == 9 ? protector.ComputeBlindIndexCandidates(normalizedTaxId, tenantId) : [];
    }

    private sealed record RowKeys(
        int RowNumber,
        CustomerDuplicateRules.Keys Keys,
        IReadOnlyCollection<string> BlindIndexes
    );
}
