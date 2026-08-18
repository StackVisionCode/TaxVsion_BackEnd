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

    /// <summary>
    /// El mismo criterio y el mismo orden de prioridad que el import, para un solo candidato.
    ///
    /// <para>
    /// Es una consulta y no reusa la del chunk porque acá no hay identificador fiscal —el alta no lo
    /// pide— y porque hay que poder excluirse a uno mismo al editar.
    /// </para>
    /// </summary>
    public async Task<DuplicateMatch?> FindDuplicateAsync(
        Guid tenantId,
        CustomerDuplicateCandidate candidate,
        Guid? excludeCustomerId,
        CancellationToken ct
    )
    {
        var email = candidate.Email.Trim().ToLowerInvariant();
        var phone = string.IsNullOrWhiteSpace(candidate.PhoneE164) ? null : candidate.PhoneE164.Trim();
        var name = string.IsNullOrWhiteSpace(candidate.DisplayName)
            ? null
            : candidate.DisplayName.Trim().ToLowerInvariant();
        var dob = candidate.DateOfBirth;

        var candidates = await db
            .Customers.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Status != CustomerStatus.Archived)
            .Where(c => excludeCustomerId == null || c.Id != excludeCustomerId)
            .Where(c =>
                c.PrimaryEmail.NormalizedValue == email
                || (
                    phone != null
                    && name != null
                    && c.PrimaryPhone != null
                    && c.PrimaryPhone.E164Value == phone
                    && c.DisplayName.ToLower() == name
                )
                || (name != null && dob != null && c.DateOfBirth == dob && c.DisplayName.ToLower() == name)
            )
            .Select(c => new
            {
                c.Id,
                c.DisplayName,
                c.DateOfBirth,
                EmailNormalized = c.PrimaryEmail.NormalizedValue,
                PhoneE164 = c.PrimaryPhone != null ? c.PrimaryPhone.E164Value : null,
            })
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return null;

        // El correo manda: es la llave del portal y la que la base exige unica por tenant.
        var byEmail = candidates.FirstOrDefault(c => c.EmailNormalized == email);
        if (byEmail is not null)
            return new DuplicateMatch(0, byEmail.Id, byEmail.DisplayName, "Email");

        // El telefono NO alcanza solo, y esto se aprendio dandole de alta a alguien de verdad: una
        // familia comparte el numero de la casa, asi que el telefono a secas bloquea al conyuge y al
        // hijo. Y con Overwrite encima, sobreescribiria a la persona equivocada. Tiene que coincidir
        // tambien el nombre — que es lo que pedia el CRM viejo.
        if (phone is not null && name is not null)
        {
            var byPhone = candidates.FirstOrDefault(c =>
                c.PhoneE164 == phone && c.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)
            );
            if (byPhone is not null)
                return new DuplicateMatch(0, byPhone.Id, byPhone.DisplayName, "Phone+Name");
        }

        if (name is not null && dob is not null)
        {
            var byName = candidates.FirstOrDefault(c =>
                c.DateOfBirth == dob && c.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)
            );
            if (byName is not null)
                return new DuplicateMatch(0, byName.Id, byName.DisplayName, "Name+DOB");
        }

        return null;
    }
}
