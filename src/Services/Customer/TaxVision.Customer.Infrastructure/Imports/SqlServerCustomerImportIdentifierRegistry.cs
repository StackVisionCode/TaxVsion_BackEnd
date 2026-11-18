using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Dueños actuales de los únicos por oficina que trae el bloque. SSN/EIN con lookup dual (clave actual ∪ anterior)
/// y devuelto con la llave actual, que es la que calcula el import. Tenant explícito (B-29).
/// </summary>
public sealed class SqlServerCustomerImportIdentifierRegistry(CustomerDbContext db, ISensitiveDataProtector protector)
    : ICustomerImportIdentifierRegistry
{
    public async Task<ImportTakenIdentifiers> FindTakenAsync(
        Guid tenantId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    )
    {
        if (chunk.Count == 0)
            return ImportTakenIdentifiers.None;

        var taxIdKeys = BlindIndexKeys(chunk.Select(row => row.TaxIdentifier), tenantId);
        var spouseKeys = BlindIndexKeys(chunk.Select(row => row.SpouseTaxIdentifier), tenantId);
        var emails = chunk.Select(row => ImportDuplicateKeys.From(row).Email).OfType<string>().ToHashSet();

        var taxIdOwners = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (taxIdKeys.Count > 0)
        {
            var candidates = taxIdKeys.Keys.ToList();
            var found = await db
                .CustomerFiscalProfiles.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(fp => fp.TenantId == tenantId && candidates.Contains(fp.TaxIdentifierBlindIndex!))
                .Select(fp => new { fp.TaxIdentifierBlindIndex, fp.CustomerId })
                .ToListAsync(ct);
            foreach (var hit in found)
                taxIdOwners[taxIdKeys[hit.TaxIdentifierBlindIndex!]] = hit.CustomerId;
        }

        var spouseTaxIdRelations = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (spouseKeys.Count > 0)
        {
            var candidates = spouseKeys.Keys.ToList();
            var found = await db
                .CustomerRelationFiscalProfiles.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(fp => fp.TenantId == tenantId && candidates.Contains(fp.TaxIdentifierBlindIndex))
                .Select(fp => new { fp.TaxIdentifierBlindIndex, fp.CustomerRelationId })
                .ToListAsync(ct);
            foreach (var hit in found)
                spouseTaxIdRelations[spouseKeys[hit.TaxIdentifierBlindIndex]] = hit.CustomerRelationId;
        }

        var emailOwners = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (emails.Count > 0)
        {
            var found = await db
                .Customers.AsNoTracking()
                .IgnoreQueryFilters()
                .Where(c =>
                    c.TenantId == tenantId
                    && c.Status != CustomerStatus.Archived
                    && emails.Contains(c.PrimaryEmail!.NormalizedValue)
                )
                .Select(c => new { Email = c.PrimaryEmail!.NormalizedValue, c.Id })
                .ToListAsync(ct);
            foreach (var hit in found)
                emailOwners[hit.Email!] = hit.Id;
        }

        return new ImportTakenIdentifiers(taxIdOwners, spouseTaxIdRelations, emailOwners);
    }

    // Blind index guardado (actual o de la clave anterior) → blind index actual.
    private Dictionary<string, string> BlindIndexKeys(IEnumerable<string?> rawValues, Guid tenantId)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in rawValues)
        {
            var digits = IdentifierNormalizer.NormalizeDigits(raw);
            if (digits.Length != 9)
                continue;
            var current = protector.ComputeBlindIndex(digits, tenantId);
            keys[current] = current;
            foreach (var candidate in protector.ComputeBlindIndexCandidates(digits, tenantId))
                keys[candidate] = current;
        }
        return keys;
    }
}
