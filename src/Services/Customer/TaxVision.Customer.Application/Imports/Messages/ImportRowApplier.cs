using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Domain.Relations;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Imports.Messages;

internal enum ImportRowOutcomeKind
{
    Success,
    Updated,
    Skipped,
    Failed,
}

/// <summary>Resultado de la fila; <c>Customer</c> es el creado o actualizado, para el aviso del bloque.</summary>
internal sealed record ImportRowOutcome(ImportRowOutcomeKind Kind, DomainCustomer? Customer = null);

/// <summary>
/// Aplica una fila del archivo dentro del chunk: la valida, cruza duplicados y crea o actualiza el cliente. El
/// resultado queda en el intento. SSN/EIN siempre cifrado, con blind index por tenant para deduplicar sin texto claro.
/// </summary>
internal sealed class ImportRowApplier(
    CustomerImportAttempt attempt,
    ImportStartInfo info,
    IReadOnlyDictionary<int, DuplicateMatch> matchByRow,
    ImportTakenIdentifiers taken,
    ImportChunkDuplicateTracker trackers,
    ICustomerRepository customerRepo,
    ISensitiveDataProtector protector,
    ICatalogResolver catalogs,
    ILogger logger,
    int defaultTaxYear
)
{
    // Avisos de la fila en curso: el applier procesa una fila a la vez.
    private List<string> _warnings = [];

    /// <summary>P20 (TZ15-H7): año fiscal por defecto de lo importado = año en curso de la oficina.</summary>
    public static int DefaultTaxYear(DateTime nowUtc, ResolvedTimeZone officeZone) =>
        CivilDate.Of(nowUtc, officeZone).Year;

    public async Task<ImportRowOutcome> ApplyAsync(ImportCustomerRow raw, CancellationToken ct)
    {
        var parsed = ImportRowParser.Parse(raw);
        if (parsed.IsFailure)
        {
            attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), parsed.Error.Code, parsed.Error.Message);
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
        }
        var pr = parsed.Value;
        _warnings = [.. pr.Warnings];

        var catalogOutcome = await ResolveCatalogsAsync(pr, ct);

        var businessIdentity = pr.BusinessIdentity;
        if (businessIdentity is not null && catalogOutcome.NaicsId is not null)
        {
            var rebuiltRes = RebuildBusinessIdentity(businessIdentity, catalogOutcome.NaicsId);
            if (rebuiltRes.IsFailure)
            {
                attempt.RecordFailed(
                    raw.RowNumber,
                    BuildDisplayName(raw),
                    rebuiltRes.Error.Code,
                    rebuiltRes.Error.Message
                );
                return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
            }
            businessIdentity = rebuiltRes.Value;
        }

        var blindIndex = ComputeBlindIndexIfPresent(pr);
        var spouseBlindIndex = string.IsNullOrEmpty(pr.Spouse?.NormalizedTaxIdentifier)
            ? null
            : protector.ComputeBlindIndex(pr.Spouse.NormalizedTaxIdentifier, info.TenantId);
        matchByRow.TryGetValue(raw.RowNumber, out var match);
        var rowError =
            CheckTakenIdentifiers(pr, match, blindIndex, spouseBlindIndex)
            ?? CheckIntraChunkDuplicates(pr, blindIndex, WritesSpouse(match) ? spouseBlindIndex : null);
        if (rowError is not null)
        {
            attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), rowError.Value.Code, rowError.Value.Message);
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
        }

        if (match is not null)
            return await HandleMatchedRowAsync(
                raw,
                pr,
                match,
                catalogOutcome.OccupationId,
                businessIdentity,
                blindIndex,
                spouseBlindIndex,
                ct
            );

        return await CreateNewCustomerAsync(raw, pr, catalogOutcome.OccupationId, businessIdentity, blindIndex, ct);
    }

    // Skip no escribe nada; crear, Merge y Overwrite pueden escribir el cónyuge.
    private bool WritesSpouse(DuplicateMatch? match) => match is null || info.Strategy != DuplicateStrategy.Skip;

    private sealed record CatalogOutcome(Guid? OccupationId, Guid? NaicsId);

    // Ocupación y NAICS son opcionales: si no están en el catálogo, se importa sin ellos y con aviso.
    private async Task<CatalogOutcome> ResolveCatalogsAsync(ParsedRow pr, CancellationToken ct)
    {
        Guid? occupationId = null;
        if (!string.IsNullOrWhiteSpace(pr.OccupationName))
        {
            occupationId = await catalogs.ResolveOccupationIdAsync(pr.OccupationName, ct);
            if (occupationId is null)
                _warnings.Add($"Occupation '{pr.OccupationName}' is not in the catalog; imported without it.");
        }

        Guid? naicsId = null;
        if (pr.Kind == CustomerKind.Business && !string.IsNullOrWhiteSpace(pr.PrincipalBusinessActivityCode))
        {
            naicsId = await catalogs.ResolvePrincipalBusinessActivityIdAsync(pr.PrincipalBusinessActivityCode, ct);
            if (naicsId is null)
                _warnings.Add(
                    $"NAICS code '{pr.PrincipalBusinessActivityCode}' is not in the catalog; imported without it."
                );
        }

        return new CatalogOutcome(occupationId, naicsId);
    }

    private static Result<BusinessIdentity> RebuildBusinessIdentity(BusinessIdentity original, Guid? naicsId) =>
        BusinessIdentity.Create(
            legalName: original.LegalName,
            structure: original.Structure,
            dba: original.Dba,
            formationDate: original.FormationDate,
            principalBusinessActivityId: naicsId
        );

    private string? ComputeBlindIndexIfPresent(ParsedRow pr) =>
        string.IsNullOrEmpty(pr.NormalizedTaxIdentifier)
            ? null
            : protector.ComputeBlindIndex(pr.NormalizedTaxIdentifier, info.TenantId);

    private (string Code, string Message)? CheckIntraChunkDuplicates(
        ParsedRow pr,
        string? blindIndex,
        string? spouseBlindIndex
    )
    {
        var reason = trackers.TryAdd(ImportDuplicateKeys.From(pr), blindIndex, spouseBlindIndex);
        return reason is null ? null : ("Import.DuplicateInChunk", reason);
    }

    // Únicos por oficina que el detector no cubre (SSN/EIN de archivados, cónyuge, email al sobrescribir): la fila
    // falla sola en vez de romper el bloque con el índice único. Skip no escribe y no se revisa.
    private (string Code, string Message)? CheckTakenIdentifiers(
        ParsedRow pr,
        DuplicateMatch? match,
        string? blindIndex,
        string? spouseBlindIndex
    )
    {
        if (match is not null && info.Strategy == DuplicateStrategy.Skip)
            return null;

        if (
            blindIndex is not null
            && taken.TaxIdOwners.TryGetValue(blindIndex, out var taxIdOwner)
            && taxIdOwner != match?.ExistingCustomerId
        )
            return (
                "Import.TaxIdentifierTaken",
                "Another client in this office already has this tax identifier (it may be archived)."
            );

        // Al crear; al actualizar se revisa con el cónyuge del cliente ya cargado.
        if (match is null && spouseBlindIndex is not null && taken.SpouseTaxIdRelations.ContainsKey(spouseBlindIndex))
            return SpouseTaxIdentifierTaken;

        if (
            match is not null
            && info.Strategy == DuplicateStrategy.Overwrite
            && EmailOwnedByAnother(pr, match.ExistingCustomerId)
        )
            return ("Import.EmailTaken", "Another client in this office already uses this email.");

        return null;
    }

    private bool EmailOwnedByAnother(ParsedRow pr, Guid customerId) =>
        pr.Email is not null
        && taken.EmailOwners.TryGetValue(pr.Email.NormalizedValue, out var owner)
        && owner != customerId;

    private static readonly (string Code, string Message) SpouseTaxIdentifierTaken = (
        "Import.SpouseTaxIdentifierTaken",
        "This spouse tax identifier is already on file in this office."
    );

    // El SSN del cónyuge solo puede ser del cónyuge que se actualiza. Merge no toca un cónyuge existente.
    private (string Code, string Message)? CheckSpouseOfExisting(CustomerRelation? spouse, string? spouseBlindIndex)
    {
        if (spouseBlindIndex is null || (spouse is not null && info.Strategy == DuplicateStrategy.Merge))
            return null;
        return taken.SpouseTaxIdRelations.TryGetValue(spouseBlindIndex, out var holder) && holder != spouse?.Id
            ? SpouseTaxIdentifierTaken
            : null;
    }

    private async Task<ImportRowOutcome> HandleMatchedRowAsync(
        ImportCustomerRow raw,
        ParsedRow pr,
        DuplicateMatch match,
        Guid? occupationId,
        BusinessIdentity? businessIdentity,
        string? blindIndex,
        string? spouseBlindIndex,
        CancellationToken ct
    )
    {
        if (info.Strategy == DuplicateStrategy.Skip)
        {
            attempt.RecordSkipped(raw.RowNumber, match.ExistingCustomerId, match.ExistingDisplayName, match.MatchedBy);
            return new ImportRowOutcome(ImportRowOutcomeKind.Skipped);
        }

        var existing = await customerRepo.GetByIdAsync(attempt.TenantId, match.ExistingCustomerId, ct);
        if (existing is null)
        {
            attempt.RecordFailed(
                raw.RowNumber,
                BuildDisplayName(raw),
                "Import.MatchedThenMissing",
                "Matched customer disappeared before update."
            );
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
        }

        // Merge completa un email vacío, pero nunca con el de otro cliente: se deja sin email y se avisa.
        if (
            info.Strategy == DuplicateStrategy.Merge
            && existing.PrimaryEmail is null
            && EmailOwnedByAnother(pr, existing.Id)
        )
        {
            _warnings.Add($"Email '{pr.Email!.Value}' already belongs to another client; it was not added.");
            pr = pr with { Email = null };
        }

        var spouse = ActiveSpouse(existing);
        if (pr.Spouse is not null && CheckSpouseOfExisting(spouse, spouseBlindIndex) is { } spouseError)
        {
            attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), spouseError.Code, spouseError.Message);
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
        }

        var applied = ApplyUpdate(
            existing,
            pr,
            occupationId,
            businessIdentity,
            info.CreatedByUserId,
            info.Strategy,
            blindIndex,
            protector,
            _warnings
        );
        var spouseChanged =
            applied.IsSuccess
            && pr.Spouse is not null
            && ApplySpouseToExisting(existing, spouse, pr.Spouse, raw.RowNumber);
        // La importación avisa con un solo CustomersBulkImported al final, no un CustomerUpdated por fila.
        existing.ClearDomainEvents();
        if (applied.IsFailure)
        {
            attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), applied.Error.Code, applied.Error.Message);
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
        }

        // Nada nuevo para el cliente: no se toca ni cuenta como actualizado.
        if (!applied.Value && !spouseChanged)
        {
            attempt.RecordUnchanged(raw.RowNumber, existing.Id, existing.DisplayName, match.MatchedBy, _warnings);
            return new ImportRowOutcome(ImportRowOutcomeKind.Skipped);
        }

        attempt.RecordUpdated(raw.RowNumber, existing.Id, existing.DisplayName, match.MatchedBy, _warnings);
        return new ImportRowOutcome(ImportRowOutcomeKind.Updated, existing);
    }

    private async Task<ImportRowOutcome> CreateNewCustomerAsync(
        ImportCustomerRow raw,
        ParsedRow pr,
        Guid? occupationId,
        BusinessIdentity? businessIdentity,
        string? blindIndex,
        CancellationToken ct
    )
    {
        var registerRes = DomainCustomer.Register(
            tenantId: info.TenantId,
            kind: pr.Kind,
            personalName: pr.PersonalName,
            businessIdentity: businessIdentity,
            primaryEmail: pr.Email,
            primaryPhone: pr.Phone,
            language: pr.Language,
            createdByUserId: info.CreatedByUserId,
            dateOfBirth: pr.DateOfBirth,
            occupationId: occupationId,
            contactPolicy: InitialContactPolicy(pr),
            withTaxIdentifier: blindIndex is not null
        );

        if (registerRes.IsFailure)
        {
            attempt.RecordFailed(
                raw.RowNumber,
                BuildDisplayName(raw),
                registerRes.Error.Code,
                registerRes.Error.Message
            );
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
        }

        var newCustomer = registerRes.Value;

        if (pr.Address is not null)
        {
            var addrRes = newCustomer.AddAddress(AddressKind.Home, pr.Address, isPrimary: true, info.CreatedByUserId);
            if (addrRes.IsFailure)
            {
                attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), addrRes.Error.Code, addrRes.Error.Message);
                return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
            }
        }

        if (blindIndex is not null || HasFiscalData(pr))
        {
            var fpRes = blindIndex is null
                ? newCustomer.SetFiscalProfileWithoutTaxIdentifier(
                    pr.FiscalSubjectKind,
                    pr.FilingStatus,
                    pr.PriorYearAgi,
                    pr.IsReturningCustomer,
                    info.CreatedByUserId,
                    protector.CurrentKeyVersion
                )
                : SetPrimaryFiscalProfile(newCustomer, pr, blindIndex);
            if (fpRes.IsFailure)
            {
                attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), fpRes.Error.Code, fpRes.Error.Message);
                return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
            }
        }

        if (pr.Spouse is not null)
            AttachSpouseRelation(newCustomer, pr.Spouse, raw.RowNumber);

        // La importación avisa con un solo CustomersBulkImported al final, no un CustomerCreated por fila.
        newCustomer.ClearDomainEvents();
        await customerRepo.AddAsync(newCustomer, ct);
        attempt.RecordSuccess(raw.RowNumber, newCustomer.Id, newCustomer.DisplayName, _warnings);
        return new ImportRowOutcome(ImportRowOutcomeKind.Success, newCustomer);
    }

    // D-4: filing status, AGI o "cliente recurrente" sin SSN se guardan con el SSN pendiente.
    internal static bool HasFiscalData(ParsedRow pr) =>
        pr.FilingStatus is not null || pr.PriorYearAgi is not null || pr.IsReturningCustomer;

    // D-5 (TCPA): sin consentimiento en el archivo, SMS apagado; email activado.
    private static ContactPolicy InitialContactPolicy(ParsedRow pr) => new(pr.EmailOptIn ?? true, pr.SmsOptIn ?? false);

    private Result SetPrimaryFiscalProfile(DomainCustomer customer, ParsedRow pr, string blindIndex)
    {
        var cipher = protector.Protect(pr.NormalizedTaxIdentifier);
        var last4 = pr.NormalizedTaxIdentifier[^4..];
        return customer.SetFiscalProfile(
            subjectKind: pr.FiscalSubjectKind,
            taxIdentifierCipher: cipher,
            taxIdentifierBlindIndex: blindIndex,
            taxIdentifierLast4: last4,
            filingStatus: pr.FilingStatus,
            priorYearAgi: pr.PriorYearAgi,
            isReturningCustomer: pr.IsReturningCustomer,
            refundBankAccountCipher: null,
            refundBankRoutingCipher: null,
            byUserId: info.CreatedByUserId,
            taxIdentifierKeyVersion: protector.CurrentKeyVersion
        );
    }

    // Devuelve si el cónyuge quedó agregado.
    private bool AttachSpouseRelation(DomainCustomer customer, ParsedSpouse spouse, int rowNumber)
    {
        var byUserId = info.CreatedByUserId;
        var relRes = customer.AddRelation(
            kind: RelationshipKind.Spouse,
            purposes: RelationPurpose.TaxHouseholdMember,
            name: spouse.Name,
            email: spouse.Email,
            phone: spouse.Phone,
            dateOfBirth: spouse.DateOfBirth,
            address: null,
            byUserId: byUserId
        );

        if (relRes.IsFailure)
        {
            WarnSpouseNotSaved(relRes.Error, rowNumber);
            return false;
        }
        if (string.IsNullOrEmpty(spouse.NormalizedTaxIdentifier))
            return true;

        var spouseCipher = protector.Protect(spouse.NormalizedTaxIdentifier);
        var spouseBlind = protector.ComputeBlindIndex(spouse.NormalizedTaxIdentifier, info.TenantId);
        var spouseLast4 = spouse.NormalizedTaxIdentifier[^4..];

        var spouseFp = relRes.Value.SetFiscalProfile(
            role: TaxRelationshipRole.Spouse,
            taxIdentifierCipher: spouseCipher,
            taxIdentifierBlindIndex: spouseBlind,
            taxIdentifierLast4: spouseLast4,
            taxYear: defaultTaxYear,
            qualifiesAsDependent: false,
            livedWithTaxpayer: true,
            byUserId: byUserId,
            taxIdentifierKeyVersion: protector.CurrentKeyVersion
        );

        if (spouseFp.IsFailure)
            WarnSpouseTaxIdNotSaved(spouseFp.Error, rowNumber);
        return true;
    }

    private void WarnSpouseNotSaved(Error error, int rowNumber)
    {
        logger.LogWarning("[ROW {Row}] spouse could not be saved: {Error}", rowNumber, error.Code);
        _warnings.Add($"Spouse could not be saved ({error.Message}).");
    }

    private void WarnSpouseTaxIdNotSaved(Error error, int rowNumber)
    {
        logger.LogWarning("[ROW {Row}] spouse fiscal profile failed: {Error}", rowNumber, error.Code);
        _warnings.Add($"Spouse tax identifier could not be saved ({error.Message}).");
    }

    private static CustomerRelation? ActiveSpouse(DomainCustomer customer) =>
        customer.Relations.FirstOrDefault(r => r.IsActive && r.RelationshipKind == RelationshipKind.Spouse);

    // Sin cónyuge: se agrega (Merge y Overwrite). Con cónyuge: Overwrite pone lo que trae el archivo; Merge no lo toca.
    // Devuelve si algo cambió.
    private bool ApplySpouseToExisting(
        DomainCustomer existing,
        CustomerRelation? current,
        ParsedSpouse file,
        int rowNumber
    )
    {
        if (current is null)
            return AttachSpouseRelation(existing, file, rowNumber);
        return info.Strategy == DuplicateStrategy.Overwrite && OverwriteSpouse(existing, current, file, rowNumber);
    }

    // Una celda vacía no borra: email, teléfono y fecha se conservan si el archivo no los trae.
    private bool OverwriteSpouse(DomainCustomer existing, CustomerRelation spouse, ParsedSpouse file, int rowNumber)
    {
        var email = file.Email ?? spouse.PrimaryEmail;
        var phone = file.Phone ?? spouse.PrimaryPhone;
        var dateOfBirth = file.DateOfBirth ?? spouse.DateOfBirth;
        var sameData =
            file.Name == spouse.Name
            && email == spouse.PrimaryEmail
            && phone == spouse.PrimaryPhone
            && dateOfBirth == spouse.DateOfBirth;
        var blindIndex = string.IsNullOrEmpty(file.NormalizedTaxIdentifier)
            ? null
            : protector.ComputeBlindIndex(file.NormalizedTaxIdentifier, info.TenantId);
        var sameTaxId = blindIndex is null || spouse.FiscalProfile?.TaxIdentifierBlindIndex == blindIndex;
        if (sameData && sameTaxId)
            return false;

        if (!sameData)
        {
            var updated = existing.UpdateRelation(
                spouse.Id,
                spouse.RelationshipKind,
                spouse.Purposes,
                file.Name,
                email,
                phone,
                dateOfBirth,
                spouse.Address,
                info.CreatedByUserId
            );
            if (updated.IsFailure)
            {
                WarnSpouseNotSaved(updated.Error, rowNumber);
                return false;
            }
        }
        return sameTaxId || SetSpouseTaxIdentifier(existing, spouse, file, blindIndex!, rowNumber);
    }

    private bool SetSpouseTaxIdentifier(
        DomainCustomer existing,
        CustomerRelation spouse,
        ParsedSpouse file,
        string blindIndex,
        int rowNumber
    )
    {
        var profile = spouse.FiscalProfile;
        var fiscal = existing.SetRelationFiscalProfile(
            spouse.Id,
            profile?.Role ?? TaxRelationshipRole.Spouse,
            protector.Protect(file.NormalizedTaxIdentifier),
            blindIndex,
            file.NormalizedTaxIdentifier[^4..],
            profile?.TaxYear ?? defaultTaxYear,
            profile?.QualifiesAsDependent ?? false,
            profile?.LivedWithTaxpayer ?? true,
            info.CreatedByUserId,
            protector.CurrentKeyVersion
        );
        if (fiscal.IsFailure)
            WarnSpouseTaxIdNotSaved(fiscal.Error, rowNumber);
        return true;
    }

    private static string BuildDisplayName(ImportCustomerRow raw)
    {
        if (!string.IsNullOrWhiteSpace(raw.LegalName))
            return raw.LegalName.Trim();
        var name = $"{raw.FirstName} {raw.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "(no name)" : name;
    }

    internal static Result<bool> ApplyUpdate(
        DomainCustomer existing,
        ParsedRow pr,
        Guid? occupationId,
        BusinessIdentity? businessIdentity,
        Guid byUserId,
        DuplicateStrategy strategy,
        string? blindIndex,
        ISensitiveDataProtector protector,
        List<string>? warnings = null
    ) =>
        ImportCustomerUpdater.Apply(
            existing,
            pr,
            occupationId,
            businessIdentity,
            byUserId,
            strategy,
            blindIndex,
            protector,
            warnings
        );
}
