using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Imports;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Imports.Messages;

/// <summary>
/// Aplica una fila del archivo a un cliente que ya existe. Merge solo completa lo vacío; Overwrite pone lo que trae el
/// archivo y una celda vacía nunca borra. Solo llama al agregado cuando el valor cambia: si nada cambia, el cliente
/// queda intacto y la fila no cuenta como actualizada.
/// </summary>
internal static class ImportCustomerUpdater
{
    public static Result<bool> Apply(
        DomainCustomer existing,
        ParsedRow pr,
        Guid? occupationId,
        BusinessIdentity? businessIdentity,
        Guid byUserId,
        DuplicateStrategy strategy,
        string? blindIndex,
        ISensitiveDataProtector protector,
        List<string>? warnings
    )
    {
        var changes = new ImportChanges();
        var overwrite = strategy == DuplicateStrategy.Overwrite;

        if (overwrite)
            OverwriteContact(existing, pr, occupationId, byUserId, changes);
        else
            MergeContact(existing, pr, occupationId, byUserId, changes);

        ApplyIdentity(existing, pr, businessIdentity, byUserId, overwrite, changes, warnings);
        ApplyAddress(existing, pr.Address, byUserId, overwrite, changes);

        if (blindIndex is not null)
            ApplyFiscalUpdate(existing, pr, byUserId, overwrite, blindIndex, protector, changes);
        else if (ImportRowApplier.HasFiscalData(pr))
            ApplyFiscalDataWithoutTaxId(existing, pr, byUserId, overwrite, protector, changes);

        return changes.ToResult();
    }

    private static void OverwriteContact(
        DomainCustomer existing,
        ParsedRow pr,
        Guid? occupationId,
        Guid byUserId,
        ImportChanges changes
    )
    {
        if (pr.LanguageProvided && pr.Language != existing.Language)
            changes.Apply(existing.ChangeLanguage(pr.Language, byUserId));
        if (occupationId is not null && occupationId != existing.OccupationId)
            changes.Apply(existing.ChangeOccupation(occupationId, byUserId));
        if (pr.Email is not null && pr.Email != existing.PrimaryEmail)
            changes.Apply(existing.ChangePrimaryEmail(pr.Email, byUserId));
        if (pr.Phone is not null && pr.Phone != existing.PrimaryPhone)
            changes.Apply(existing.ChangePrimaryPhone(pr.Phone, byUserId));

        var policy = new ContactPolicy(
            pr.EmailOptIn ?? existing.ContactPolicy.EmailEnabled,
            pr.SmsOptIn ?? existing.ContactPolicy.SmsEnabled
        );
        if (policy != existing.ContactPolicy)
            changes.Apply(existing.ChangeContactPolicy(policy.EmailEnabled, policy.SmsEnabled, byUserId));
    }

    // Un email existente y los permisos de contacto no se pisan en Merge: son del existente.
    private static void MergeContact(
        DomainCustomer existing,
        ParsedRow pr,
        Guid? occupationId,
        Guid byUserId,
        ImportChanges changes
    )
    {
        if (existing.OccupationId is null && occupationId is not null)
            changes.Apply(existing.ChangeOccupation(occupationId, byUserId));
        if (existing.PrimaryPhone is null && pr.Phone is not null)
            changes.Apply(existing.ChangePrimaryPhone(pr.Phone, byUserId));
        if (existing.PrimaryEmail is null && pr.Email is not null)
            changes.Apply(existing.ChangePrimaryEmail(pr.Email, byUserId));
    }

    // Nombre, negocio y fecha de nacimiento. Si el tipo del archivo no es el del cliente, no se toca y se avisa.
    private static void ApplyIdentity(
        DomainCustomer existing,
        ParsedRow pr,
        BusinessIdentity? businessIdentity,
        Guid byUserId,
        bool overwrite,
        ImportChanges changes,
        List<string>? warnings
    )
    {
        if (pr.Kind != existing.Kind)
        {
            warnings?.Add(
                $"The file says {pr.Kind} but the client is {existing.Kind}; name and business data were not changed."
            );
            return;
        }

        if (pr.PersonalName is { } fileName && existing.PersonalName is { } currentName)
            ApplyName(existing, Choose(currentName, fileName, overwrite), currentName, byUserId, changes);
        if (businessIdentity is { } fileBusiness && existing.BusinessIdentity is { } currentBusiness)
            ApplyBusiness(
                existing,
                Choose(currentBusiness, fileBusiness, overwrite),
                currentBusiness,
                byUserId,
                changes
            );

        var dateOfBirth = overwrite ? pr.DateOfBirth ?? existing.DateOfBirth : existing.DateOfBirth ?? pr.DateOfBirth;
        if (dateOfBirth != existing.DateOfBirth)
            changes.Apply(existing.ChangeDateOfBirth(dateOfBirth, byUserId));
    }

    private static Result<PersonalName> Choose(PersonalName current, PersonalName file, bool overwrite) =>
        overwrite
            ? PersonalName.Create(
                file.FirstName,
                file.LastName,
                file.MiddleName ?? current.MiddleName,
                file.Prefix ?? current.Prefix,
                file.Suffix ?? current.Suffix
            )
            : PersonalName.Create(
                current.FirstName,
                current.LastName,
                current.MiddleName ?? file.MiddleName,
                current.Prefix ?? file.Prefix,
                current.Suffix ?? file.Suffix
            );

    private static Result<BusinessIdentity> Choose(BusinessIdentity current, BusinessIdentity file, bool overwrite) =>
        overwrite
            ? BusinessIdentity.Create(
                file.LegalName,
                file.Structure,
                file.Dba ?? current.Dba,
                file.FormationDate ?? current.FormationDate,
                file.PrincipalBusinessActivityId ?? current.PrincipalBusinessActivityId
            )
            : BusinessIdentity.Create(
                current.LegalName,
                current.Structure,
                current.Dba ?? file.Dba,
                current.FormationDate ?? file.FormationDate,
                current.PrincipalBusinessActivityId ?? file.PrincipalBusinessActivityId
            );

    private static void ApplyName(
        DomainCustomer existing,
        Result<PersonalName> name,
        PersonalName current,
        Guid byUserId,
        ImportChanges changes
    )
    {
        if (name.IsFailure)
            changes.Apply(name);
        else if (name.Value != current)
            changes.Apply(existing.ChangePersonalName(name.Value, byUserId));
    }

    private static void ApplyBusiness(
        DomainCustomer existing,
        Result<BusinessIdentity> identity,
        BusinessIdentity current,
        Guid byUserId,
        ImportChanges changes
    )
    {
        if (identity.IsFailure)
            changes.Apply(identity);
        else if (identity.Value != current)
            changes.Apply(existing.ChangeBusinessIdentity(identity.Value, byUserId));
    }

    // La dirección del archivo es la Home principal. Merge solo la agrega si el cliente no tiene una.
    private static void ApplyAddress(
        DomainCustomer existing,
        AddressValue? address,
        Guid byUserId,
        bool overwrite,
        ImportChanges changes
    )
    {
        if (address is null)
            return;
        var home = existing.Addresses.FirstOrDefault(a => a.Kind == AddressKind.Home && a.IsPrimary);
        if (home is null)
            changes.Apply(existing.AddAddress(AddressKind.Home, address, isPrimary: true, byUserId));
        else if (overwrite && home.Address != address)
            changes.Apply(existing.UpdateAddress(home.Id, AddressKind.Home, address, isPrimary: true, byUserId));
    }

    // El banco de reembolso no viene en el archivo: se conserva siempre.
    private static void ApplyFiscalUpdate(
        DomainCustomer existing,
        ParsedRow pr,
        Guid byUserId,
        bool overwrite,
        string blindIndex,
        ISensitiveDataProtector protector,
        ImportChanges changes
    )
    {
        var current = existing.FiscalProfile;
        var (filingStatus, priorYearAgi, isReturning) = FiscalData(current, pr, merge: !overwrite);

        // Merge nunca reemplaza un SSN ya registrado; sí completa uno pendiente.
        if (!overwrite && current is { HasTaxIdentifier: true })
        {
            if (!SameFiscalData(current, filingStatus, priorYearAgi, isReturning))
                changes.Apply(
                    existing.UpdateFiscalProfile(
                        filingStatus,
                        priorYearAgi,
                        isReturning,
                        refundBankAccountCipher: null,
                        refundBankRoutingCipher: null,
                        byUserId
                    )
                );
            return;
        }

        if (
            current is not null
            && current.TaxIdentifierBlindIndex == blindIndex
            && current.SubjectKind == pr.FiscalSubjectKind
            && SameFiscalData(current, filingStatus, priorYearAgi, isReturning)
        )
            return;

        changes.Apply(
            existing.SetFiscalProfile(
                subjectKind: pr.FiscalSubjectKind,
                taxIdentifierCipher: protector.Protect(pr.NormalizedTaxIdentifier),
                taxIdentifierBlindIndex: blindIndex,
                taxIdentifierLast4: pr.NormalizedTaxIdentifier[^4..],
                filingStatus: filingStatus,
                priorYearAgi: priorYearAgi,
                isReturningCustomer: isReturning,
                refundBankAccountCipher: current?.RefundBankAccountCipher,
                refundBankRoutingCipher: current?.RefundBankRoutingCipher,
                byUserId: byUserId,
                taxIdentifierKeyVersion: protector.CurrentKeyVersion
            )
        );
    }

    // Sin SSN en la fila: cambian solo filing status, AGI y "cliente recurrente"; el SSN existente se conserva.
    private static void ApplyFiscalDataWithoutTaxId(
        DomainCustomer existing,
        ParsedRow pr,
        Guid byUserId,
        bool overwrite,
        ISensitiveDataProtector protector,
        ImportChanges changes
    )
    {
        var current = existing.FiscalProfile;
        var (filingStatus, priorYearAgi, isReturning) = FiscalData(current, pr, merge: !overwrite);
        if (current is not null && SameFiscalData(current, filingStatus, priorYearAgi, isReturning))
            return;

        changes.Apply(
            existing.SetFiscalProfileWithoutTaxIdentifier(
                current?.SubjectKind ?? pr.FiscalSubjectKind,
                filingStatus,
                priorYearAgi,
                isReturning,
                byUserId,
                protector.CurrentKeyVersion
            )
        );
    }

    private static bool SameFiscalData(
        CustomerFiscalProfile current,
        FilingStatus? filingStatus,
        decimal? priorYearAgi,
        bool isReturning
    ) =>
        current.FilingStatus == filingStatus
        && current.PriorYearAgi == priorYearAgi
        && current.IsReturningCustomer == isReturning;

    // Merge solo completa lo vacío; Overwrite pone lo que trae el archivo y una celda vacía no borra.
    private static (FilingStatus? FilingStatus, decimal? PriorYearAgi, bool IsReturning) FiscalData(
        CustomerFiscalProfile? current,
        ParsedRow pr,
        bool merge
    ) =>
        merge
            ? (
                current?.FilingStatus ?? pr.FilingStatus,
                current?.PriorYearAgi ?? pr.PriorYearAgi,
                (current?.IsReturningCustomer ?? false) || pr.IsReturningCustomer
            )
            : (
                pr.FilingStatus ?? current?.FilingStatus,
                pr.PriorYearAgi ?? current?.PriorYearAgi,
                pr.IsReturningCustomerProvided || current is null ? pr.IsReturningCustomer : current.IsReturningCustomer
            );
}

/// <summary>Junta los resultados de una fila: el primer error gana; si no hay error, dice si algo cambió.</summary>
internal sealed class ImportChanges
{
    private Error? _error;

    public bool Changed { get; private set; }

    public void Apply(Result result)
    {
        if (result.IsFailure)
            _error ??= result.Error;
        else
            Changed = true;
    }

    public Result<bool> ToResult() => _error is not null ? Result.Failure<bool>(_error) : Result.Success(Changed);
}
