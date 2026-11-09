using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
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

internal sealed record ImportRowOutcome(ImportRowOutcomeKind Kind, Guid? CustomerId = null);

/// <summary>
/// Aplica una fila del archivo dentro del chunk: la valida, cruza duplicados y crea o actualiza el cliente. El
/// resultado queda en el intento. SSN/EIN siempre cifrado, con blind index por tenant para deduplicar sin texto claro.
/// </summary>
internal sealed class ImportRowApplier(
    CustomerImportAttempt attempt,
    ImportStartInfo info,
    IReadOnlyDictionary<int, DuplicateMatch> matchByRow,
    ImportChunkDuplicateTracker trackers,
    ICustomerRepository customerRepo,
    ISensitiveDataProtector protector,
    ICatalogResolver catalogs,
    ILogger logger
)
{
    public async Task<ImportRowOutcome> ApplyAsync(ImportCustomerRow raw, CancellationToken ct)
    {
        var parsed = ImportRowParser.Parse(raw);
        if (parsed.IsFailure)
        {
            attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), parsed.Error.Code, parsed.Error.Message);
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
        }
        var pr = parsed.Value;

        var catalogOutcome = await ResolveCatalogsAsync(raw, pr, ct);
        if (catalogOutcome.Failed)
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);

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
        var dedupError = CheckIntraChunkDuplicates(pr, blindIndex);
        if (dedupError is not null)
        {
            attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), dedupError.Value.Code, dedupError.Value.Message);
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
        }

        if (matchByRow.TryGetValue(raw.RowNumber, out var match))
            return await HandleMatchedRowAsync(
                raw,
                pr,
                match,
                catalogOutcome.OccupationId,
                businessIdentity,
                blindIndex,
                ct
            );

        return await CreateNewCustomerAsync(raw, pr, catalogOutcome.OccupationId, businessIdentity, blindIndex, ct);
    }

    private sealed record CatalogOutcome(Guid? OccupationId, Guid? NaicsId, bool Failed);

    private async Task<CatalogOutcome> ResolveCatalogsAsync(ImportCustomerRow raw, ParsedRow pr, CancellationToken ct)
    {
        Guid? occupationId = null;
        if (!string.IsNullOrWhiteSpace(pr.OccupationName))
        {
            occupationId = await catalogs.ResolveOccupationIdAsync(pr.OccupationName, ct);
            if (occupationId is null)
            {
                attempt.RecordFailed(
                    raw.RowNumber,
                    BuildDisplayName(raw),
                    "Catalog.UnknownOccupation",
                    $"Occupation '{pr.OccupationName}' is not in the catalog."
                );
                return new CatalogOutcome(null, null, true);
            }
        }

        Guid? naicsId = null;
        if (pr.Kind == CustomerKind.Business && !string.IsNullOrWhiteSpace(pr.PrincipalBusinessActivityCode))
        {
            naicsId = await catalogs.ResolvePrincipalBusinessActivityIdAsync(pr.PrincipalBusinessActivityCode, ct);
            if (naicsId is null)
            {
                attempt.RecordFailed(
                    raw.RowNumber,
                    BuildDisplayName(raw),
                    "Catalog.UnknownNaics",
                    $"NAICS code '{pr.PrincipalBusinessActivityCode}' is not in the catalog."
                );
                return new CatalogOutcome(occupationId, null, true);
            }
        }

        return new CatalogOutcome(occupationId, naicsId, false);
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

    private (string Code, string Message)? CheckIntraChunkDuplicates(ParsedRow pr, string? blindIndex)
    {
        var reason = trackers.TryAdd(ImportDuplicateKeys.From(pr), blindIndex);
        return reason is null ? null : ("Import.DuplicateInChunk", reason);
    }

    private async Task<ImportRowOutcome> HandleMatchedRowAsync(
        ImportCustomerRow raw,
        ParsedRow pr,
        DuplicateMatch match,
        Guid? occupationId,
        BusinessIdentity? businessIdentity,
        string? blindIndex,
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

        var applied = ApplyUpdate(
            existing,
            pr,
            occupationId,
            businessIdentity,
            info.CreatedByUserId,
            info.Strategy,
            blindIndex,
            protector
        );
        // La importación avisa con un solo CustomersBulkImported al final, no un CustomerUpdated por fila.
        existing.ClearDomainEvents();
        if (applied.IsFailure)
        {
            attempt.RecordFailed(raw.RowNumber, BuildDisplayName(raw), applied.Error.Code, applied.Error.Message);
            return new ImportRowOutcome(ImportRowOutcomeKind.Failed);
        }

        attempt.RecordUpdated(raw.RowNumber, existing.Id, existing.DisplayName, match.MatchedBy);
        return new ImportRowOutcome(ImportRowOutcomeKind.Updated, existing.Id);
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
            occupationId: occupationId
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

        if (blindIndex is not null)
        {
            var fpRes = SetPrimaryFiscalProfile(newCustomer, pr, blindIndex);
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
        attempt.RecordSuccess(raw.RowNumber, newCustomer.Id, newCustomer.DisplayName);
        return new ImportRowOutcome(ImportRowOutcomeKind.Success, newCustomer.Id);
    }

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

    private void AttachSpouseRelation(DomainCustomer customer, ParsedSpouse spouse, int rowNumber)
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

        if (relRes.IsFailure || string.IsNullOrEmpty(spouse.NormalizedTaxIdentifier))
            return;

        var spouseCipher = protector.Protect(spouse.NormalizedTaxIdentifier);
        var spouseBlind = protector.ComputeBlindIndex(spouse.NormalizedTaxIdentifier, info.TenantId);
        var spouseLast4 = spouse.NormalizedTaxIdentifier[^4..];

        var spouseFp = relRes.Value.SetFiscalProfile(
            role: TaxRelationshipRole.Spouse,
            taxIdentifierCipher: spouseCipher,
            taxIdentifierBlindIndex: spouseBlind,
            taxIdentifierLast4: spouseLast4,
            taxYear: DateTime.UtcNow.Year,
            qualifiesAsDependent: false,
            livedWithTaxpayer: true,
            byUserId: byUserId,
            taxIdentifierKeyVersion: protector.CurrentKeyVersion
        );

        if (spouseFp.IsFailure)
            logger.LogWarning("[ROW {Row}] spouse fiscal profile failed: {Error}", rowNumber, spouseFp.Error.Message);
    }

    private static string BuildDisplayName(ImportCustomerRow raw)
    {
        if (!string.IsNullOrWhiteSpace(raw.LegalName))
            return raw.LegalName.Trim();
        var name = $"{raw.FirstName} {raw.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "(no name)" : name;
    }

    internal static Result ApplyUpdate(
        DomainCustomer existing,
        ParsedRow pr,
        Guid? occupationId,
        BusinessIdentity? businessIdentity,
        Guid byUserId,
        DuplicateStrategy strategy,
        string? blindIndex,
        ISensitiveDataProtector protector
    )
    {
        var results = new List<Result>();

        // Merge: solo cambia campos no nulos del archivo SI el existente no los tiene
        // Overwrite: pisa todo
        if (strategy == DuplicateStrategy.Overwrite)
        {
            results.Add(existing.ChangeLanguage(pr.Language, byUserId));
            results.Add(existing.ChangeOccupation(occupationId, byUserId));
            results.Add(existing.ChangePrimaryEmail(pr.Email, byUserId));
            if (pr.Phone is not null)
                results.Add(existing.ChangePrimaryPhone(pr.Phone, byUserId));
        }
        else // Merge
        {
            if (existing.OccupationId is null && occupationId is not null)
                results.Add(existing.ChangeOccupation(occupationId, byUserId));
            if (existing.PrimaryPhone is null && pr.Phone is not null)
                results.Add(existing.ChangePrimaryPhone(pr.Phone, byUserId));
            // Email y preferencias no se pisan en Merge (son fuente de verdad del existente)
        }

        if (blindIndex is not null)
        {
            var cipher = protector.Protect(pr.NormalizedTaxIdentifier);
            var last4 = pr.NormalizedTaxIdentifier[^4..];
            results.Add(
                existing.SetFiscalProfile(
                    subjectKind: pr.FiscalSubjectKind,
                    taxIdentifierCipher: cipher,
                    taxIdentifierBlindIndex: blindIndex,
                    taxIdentifierLast4: last4,
                    filingStatus: pr.FilingStatus,
                    priorYearAgi: pr.PriorYearAgi,
                    isReturningCustomer: pr.IsReturningCustomer,
                    refundBankAccountCipher: null,
                    refundBankRoutingCipher: null,
                    byUserId: byUserId,
                    taxIdentifierKeyVersion: protector.CurrentKeyVersion
                )
            );
        }

        return results.Find(result => result.IsFailure) ?? Result.Success();
    }
}
