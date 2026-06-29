using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Detecta duplicados batch usando:
///   - Blind index HMAC por tenant (SSN/EIN) via CustomerFiscalProfile.TaxIdentifierBlindIndex
///   - Email normalizado via Customer.PrimaryEmailNormalized (owned VO)
///   - Phone E164 via Customer.PrimaryPhone.E164Value
///   - (NombreNormalizado + DOB) via DisplayName + DateOfBirth
///
/// Prioridad descendente: el primer match HARD gana; si solo hay match por nombre+DOB,
/// se reporta. Las filas sin match no aparecen en el resultado.
/// UNA sola query por chunk.
/// </summary>
internal sealed class SqlServerCustomerDuplicateDetector(CustomerDbContext db, ISensitiveDataProtector protector)
    : ICustomerDuplicateDetector
{
    public async Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
        Guid tenantId,
        IReadOnlyList<ImportCustomerRow> chunk,
        CancellationToken ct
    )
    {
        if (chunk.Count == 0)
            return [];

        // ---- Preparar criterios de busqueda ----
        var blindIndexByRow = new Dictionary<int, string>();
        var emailByRow = new Dictionary<int, string>();
        var phoneByRow = new Dictionary<int, string>();
        var nameDobByRow = new Dictionary<int, (string Name, DateOnly Dob)>();

        var allBlindIndexes = new HashSet<string>();
        var allEmails = new HashSet<string>();
        var allPhones = new HashSet<string>();
        var allNameDobs = new HashSet<(string Name, DateOnly Dob)>();

        foreach (var row in chunk)
        {
            var normalizedTaxId = IdentifierNormalizer.NormalizeDigits(row.TaxIdentifier);
            if (normalizedTaxId.Length == 9)
            {
                var bi = protector.ComputeBlindIndex(normalizedTaxId, tenantId);
                blindIndexByRow[row.RowNumber] = bi;
                allBlindIndexes.Add(bi);
            }

            if (!string.IsNullOrWhiteSpace(row.Email))
            {
                var normalizedEmail = row.Email.Trim().ToLowerInvariant();
                emailByRow[row.RowNumber] = normalizedEmail;
                allEmails.Add(normalizedEmail);
            }

            if (!string.IsNullOrWhiteSpace(row.Phone))
            {
                var digits = IdentifierNormalizer.NormalizeDigits(row.Phone);
                if (digits.Length >= 10)
                {
                    var e164 = digits.StartsWith("1") ? $"+{digits}" : $"+1{digits}";
                    phoneByRow[row.RowNumber] = e164;
                    allPhones.Add(e164);
                }
            }

            if (
                !string.IsNullOrWhiteSpace(row.FirstName)
                && !string.IsNullOrWhiteSpace(row.LastName)
                && !string.IsNullOrWhiteSpace(row.DateOfBirth)
                && DateOnly.TryParse(row.DateOfBirth, out var dob)
            )
            {
                var name = $"{row.FirstName.Trim()} {row.LastName.Trim()}".ToLowerInvariant();
                nameDobByRow[row.RowNumber] = (name, dob);
                allNameDobs.Add((name, dob));
            }
        }

        // ---- Buscar candidatos en BD: UNA query con OR de todos los criterios ----
        var candidates = await (
            from c in db
                .Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.Status != CustomerStatus.Archived)
            from fp in db.CustomerFiscalProfiles.AsNoTracking().Where(fp => fp.CustomerId == c.Id).DefaultIfEmpty()
            where
                (fp != null && allBlindIndexes.Contains(fp.TaxIdentifierBlindIndex))
                || (allEmails.Contains(c.PrimaryEmail.NormalizedValue))
                || (c.PrimaryPhone != null && allPhones.Contains(c.PrimaryPhone.E164Value))
                || (c.DateOfBirth != null && allNameDobs.Select(nd => nd.Name).Contains(c.DisplayName.ToLower()))
            select new
            {
                c.Id,
                c.DisplayName,
                c.DateOfBirth,
                EmailNormalized = c.PrimaryEmail.NormalizedValue,
                PhoneE164 = c.PrimaryPhone != null ? c.PrimaryPhone.E164Value : null,
                BlindIndex = fp != null ? fp.TaxIdentifierBlindIndex : null,
            }
        ).ToListAsync(ct);

        if (candidates.Count == 0)
            return [];

        // ---- Matchear filas con candidatos por prioridad descendente ----
        var matches = new List<DuplicateMatch>(chunk.Count);

        foreach (var row in chunk)
        {
            // Prioridad 1: SSN/EIN blind index
            if (blindIndexByRow.TryGetValue(row.RowNumber, out var bi))
            {
                var hit = candidates.FirstOrDefault(c => c.BlindIndex == bi);
                if (hit is not null)
                {
                    matches.Add(new DuplicateMatch(row.RowNumber, hit.Id, hit.DisplayName, "TaxIdentifier"));
                    continue;
                }
            }

            // Prioridad 2: Email normalizado
            if (emailByRow.TryGetValue(row.RowNumber, out var email))
            {
                var hit = candidates.FirstOrDefault(c => c.EmailNormalized == email);
                if (hit is not null)
                {
                    matches.Add(new DuplicateMatch(row.RowNumber, hit.Id, hit.DisplayName, "Email"));
                    continue;
                }
            }

            // Prioridad 3: Phone E164
            if (phoneByRow.TryGetValue(row.RowNumber, out var phone))
            {
                var hit = candidates.FirstOrDefault(c => c.PhoneE164 == phone);
                if (hit is not null)
                {
                    matches.Add(new DuplicateMatch(row.RowNumber, hit.Id, hit.DisplayName, "Phone"));
                    continue;
                }
            }

            // Prioridad 4: Nombre + DOB
            if (nameDobByRow.TryGetValue(row.RowNumber, out var nd))
            {
                var hit = candidates.FirstOrDefault(c =>
                    c.DisplayName.Equals(nd.Name, StringComparison.OrdinalIgnoreCase) && c.DateOfBirth == nd.Dob
                );
                if (hit is not null)
                {
                    matches.Add(new DuplicateMatch(row.RowNumber, hit.Id, hit.DisplayName, "Name+DOB"));
                    continue;
                }
            }
        }

        return matches;
    }
}
