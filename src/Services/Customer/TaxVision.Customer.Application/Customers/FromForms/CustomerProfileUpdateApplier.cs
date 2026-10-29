using System.Globalization;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;
using Outcomes = BuildingBlocks.Messaging.ClientFormsIntegrationEvents.CustomerProfileUpdateFromFormOutcomes;

namespace TaxVision.Customer.Application.Customers.FromForms;

/// <summary>
/// P-13 — Aplica los destinos de UpdateCustomerProfile al cliente con métodos de dominio explícitos. Idempotente:
/// teléfono/dirección/filing se re-aplican igual, los dependientes se deduplican y SetFiscalProfile reemplaza.
/// Rev. 6.1: para el SSN pasa los valores ACTUALES de lo que el formulario no mapea, para no borrarlos.
/// </summary>
public static class CustomerProfileUpdateApplier
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string PhoneTarget = "customer.primary_phone";
    private const string HomeAddressTarget = "customer.address.home";
    private const string MailingAddressTarget = "customer.address.mailing";
    private const string DependentsTarget = "customer.relation.dependents";
    private const string FilingStatusTarget = "customer.fiscal.filing_status";
    private const string TaxIdentifierTarget = "customer.fiscal.tax_identifier";

    // Identidad y contacto (P-13): se aplican con los métodos de dominio existentes.
    private const string EmailTarget = "customer.primary_email";
    private const string LanguageTarget = "customer.language";
    private const string DobTarget = "customer.date_of_birth";
    private const string FirstNameTarget = "customer.person.first_name";
    private const string LastNameTarget = "customer.person.last_name";
    private const string MiddleNameTarget = "customer.person.middle_name";
    private const string PrefixTarget = "customer.person.prefix";
    private const string SuffixTarget = "customer.person.suffix";
    private const string BusinessLegalNameTarget = "customer.business.legal_name";
    private const string BusinessStructureTarget = "customer.business.structure";
    private const string BusinessDbaTarget = "customer.business.dba";
    private const string BusinessFormationDateTarget = "customer.business.formation_date";

    public sealed record ProfileUpdateResult(
        IReadOnlyList<string> AppliedTargets,
        IReadOnlyList<ClientFormProfileUpdateFailure> Failures
    );

    public static async Task<ProfileUpdateResult> ApplyAsync(
        CustomerEntity customer,
        IReadOnlyList<ClientFormTargetValue> values,
        ISensitiveDataProtector protector,
        ICustomerRepository repository,
        bool isAdmin,
        Guid byUserId,
        CancellationToken ct
    )
    {
        var applied = new List<string>();
        var failures = new List<ClientFormProfileUpdateFailure>();
        ClientFormTargetValue? filing = null;
        ClientFormTargetValue? taxId = null;
        // Partes de nombre/negocio: se juntan y se aplican en un solo cambio al final.
        var nameParts = new Dictionary<string, string?>(StringComparer.Ordinal);
        var businessParts = new Dictionary<string, ClientFormTargetValue>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            switch (value.TargetKey)
            {
                case PhoneTarget:
                    ApplyPhone(customer, value, byUserId, applied, failures);
                    break;
                case HomeAddressTarget:
                    ApplyAddress(customer, value, AddressKind.Home, byUserId, applied, failures);
                    break;
                case MailingAddressTarget:
                    ApplyAddress(customer, value, AddressKind.Mailing, byUserId, applied, failures);
                    break;
                case DependentsTarget:
                    ApplyDependents(customer, value, byUserId, applied, failures);
                    break;
                case EmailTarget:
                    ApplyEmail(customer, value, byUserId, applied, failures);
                    break;
                case LanguageTarget:
                    ApplyLanguage(customer, value, byUserId, applied, failures);
                    break;
                case DobTarget:
                    ApplyDateOfBirth(customer, value, byUserId, applied, failures);
                    break;
                case FirstNameTarget:
                case LastNameTarget:
                case MiddleNameTarget:
                case PrefixTarget:
                case SuffixTarget:
                    nameParts[value.TargetKey] = value.Value;
                    break;
                case BusinessLegalNameTarget:
                case BusinessStructureTarget:
                case BusinessDbaTarget:
                case BusinessFormationDateTarget:
                    businessParts[value.TargetKey] = value;
                    break;
                case FilingStatusTarget:
                    filing = value;
                    break;
                case TaxIdentifierTarget:
                    taxId = value;
                    break;
                default:
                    failures.Add(new ClientFormProfileUpdateFailure(value.TargetKey, Outcomes.TargetNotSupported));
                    break;
            }
        }

        if (nameParts.Count > 0)
            ApplyPersonalName(customer, nameParts, byUserId, applied, failures);
        if (businessParts.Count > 0)
            ApplyBusinessIdentity(customer, businessParts, byUserId, applied, failures);

        await ApplyFiscalAsync(
            customer,
            filing,
            taxId,
            protector,
            repository,
            isAdmin,
            byUserId,
            applied,
            failures,
            ct
        );
        return new ProfileUpdateResult(applied, failures);
    }

    private static void ApplyPhone(
        CustomerEntity customer,
        ClientFormTargetValue value,
        Guid byUserId,
        ICollection<string> applied,
        ICollection<ClientFormProfileUpdateFailure> failures
    )
    {
        var phone = PhoneNumber.Create(value.Value ?? string.Empty);
        if (phone.IsFailure)
        {
            failures.Add(new ClientFormProfileUpdateFailure(PhoneTarget, Outcomes.ValueInvalid));
            return;
        }
        var result = customer.ChangePrimaryPhone(phone.Value, byUserId);
        if (result.IsFailure)
            failures.Add(new ClientFormProfileUpdateFailure(PhoneTarget, Outcomes.ValueInvalid));
        else
            applied.Add(PhoneTarget);
    }

    private static void ApplyEmail(
        CustomerEntity customer,
        ClientFormTargetValue value,
        Guid byUserId,
        ICollection<string> applied,
        ICollection<ClientFormProfileUpdateFailure> failures
    )
    {
        var email = EmailAddress.Create(value.Value ?? string.Empty);
        if (email.IsFailure || customer.ChangePrimaryEmail(email.Value, byUserId).IsFailure)
        {
            failures.Add(new ClientFormProfileUpdateFailure(EmailTarget, Outcomes.ValueInvalid));
            return;
        }
        applied.Add(EmailTarget);
    }

    private static void ApplyLanguage(
        CustomerEntity customer,
        ClientFormTargetValue value,
        Guid byUserId,
        ICollection<string> applied,
        ICollection<ClientFormProfileUpdateFailure> failures
    )
    {
        // El valor ya viene mapeado al código del enum (Es/En/Pt/Fr) por el optionMap de la plantilla.
        if (!Enum.TryParse<Language>(value.Value, ignoreCase: true, out var language) || !Enum.IsDefined(language))
        {
            failures.Add(new ClientFormProfileUpdateFailure(LanguageTarget, Outcomes.ValueInvalid));
            return;
        }
        if (customer.ChangeLanguage(language, byUserId).IsFailure)
        {
            failures.Add(new ClientFormProfileUpdateFailure(LanguageTarget, Outcomes.ValueInvalid));
            return;
        }
        applied.Add(LanguageTarget);
    }

    private static void ApplyDateOfBirth(
        CustomerEntity customer,
        ClientFormTargetValue value,
        Guid byUserId,
        ICollection<string> applied,
        ICollection<ClientFormProfileUpdateFailure> failures
    )
    {
        var dob = ParseDate(value.Value);
        if (dob is null || customer.ChangeDateOfBirth(dob, byUserId).IsFailure)
        {
            failures.Add(new ClientFormProfileUpdateFailure(DobTarget, Outcomes.ValueInvalid));
            return;
        }
        applied.Add(DobTarget);
    }

    // Nombre: solo se aplica a Individual (el dominio lo valida). Merge parcial con el nombre actual.
    private static void ApplyPersonalName(
        CustomerEntity customer,
        IReadOnlyDictionary<string, string?> parts,
        Guid byUserId,
        ICollection<string> applied,
        ICollection<ClientFormProfileUpdateFailure> failures
    )
    {
        var current = customer.PersonalName;
        var first = parts.TryGetValue(FirstNameTarget, out var f) ? f : current?.FirstName;
        var last = parts.TryGetValue(LastNameTarget, out var l) ? l : current?.LastName;
        var middle = parts.TryGetValue(MiddleNameTarget, out var m) ? m : current?.MiddleName;
        var prefix = parts.TryGetValue(PrefixTarget, out var p) ? p : current?.Prefix;
        var suffix = parts.TryGetValue(SuffixTarget, out var s) ? s : current?.Suffix;

        var name = PersonalName.Create(first ?? string.Empty, last ?? string.Empty, middle, prefix, suffix);
        var ok = name.IsSuccess && customer.ChangePersonalName(name.Value, byUserId).IsSuccess;
        foreach (var target in parts.Keys)
        {
            if (ok)
                applied.Add(target);
            else
                failures.Add(new ClientFormProfileUpdateFailure(target, Outcomes.ValueInvalid));
        }
    }

    // Negocio: solo se aplica a Business (el dominio lo valida). Merge parcial con la identidad actual.
    private static void ApplyBusinessIdentity(
        CustomerEntity customer,
        IReadOnlyDictionary<string, ClientFormTargetValue> parts,
        Guid byUserId,
        ICollection<string> applied,
        ICollection<ClientFormProfileUpdateFailure> failures
    )
    {
        var current = customer.BusinessIdentity;
        var legalName = parts.TryGetValue(BusinessLegalNameTarget, out var ln) ? ln.Value : current?.LegalName;
        var dba = parts.TryGetValue(BusinessDbaTarget, out var d) ? d.Value : current?.Dba;

        var structure = current?.Structure ?? BusinessStructure.Other;
        if (
            parts.TryGetValue(BusinessStructureTarget, out var st)
            && (!Enum.TryParse(st.Value, ignoreCase: true, out structure) || !Enum.IsDefined(structure))
        )
        {
            FailAll(parts.Keys, failures);
            return;
        }

        var formationDate = current?.FormationDate;
        if (parts.TryGetValue(BusinessFormationDateTarget, out var fd))
        {
            formationDate = ParseDate(fd.Value);
            if (formationDate is null)
            {
                FailAll(parts.Keys, failures);
                return;
            }
        }

        var identity = BusinessIdentity.Create(
            legalName ?? string.Empty,
            structure,
            dba,
            formationDate,
            current?.PrincipalBusinessActivityId
        );
        var ok = identity.IsSuccess && customer.ChangeBusinessIdentity(identity.Value, byUserId).IsSuccess;
        if (ok)
            foreach (var target in parts.Keys)
                applied.Add(target);
        else
            FailAll(parts.Keys, failures);
    }

    private static void FailAll(IEnumerable<string> targets, ICollection<ClientFormProfileUpdateFailure> failures)
    {
        foreach (var target in targets)
            failures.Add(new ClientFormProfileUpdateFailure(target, Outcomes.ValueInvalid));
    }

    private static void ApplyAddress(
        CustomerEntity customer,
        ClientFormTargetValue value,
        AddressKind kind,
        Guid byUserId,
        ICollection<string> applied,
        ICollection<ClientFormProfileUpdateFailure> failures
    )
    {
        var targetKey = kind == AddressKind.Home ? HomeAddressTarget : MailingAddressTarget;
        if (value.Address is null)
        {
            failures.Add(new ClientFormProfileUpdateFailure(targetKey, Outcomes.ValueInvalid));
            return;
        }
        var address = AddressValue.Create(
            value.Address.Line1,
            value.Address.City,
            value.Address.PostalCode,
            value.Address.CountryCode,
            value.Address.Line2,
            value.Address.Region
        );
        if (address.IsFailure)
        {
            failures.Add(new ClientFormProfileUpdateFailure(targetKey, Outcomes.ValueInvalid));
            return;
        }

        // Actualiza la dirección existente del mismo tipo, o crea una nueva como principal.
        var existing = customer.Addresses.FirstOrDefault(item => item.Kind == kind);
        var succeeded = existing is null
            ? customer.AddAddress(kind, address.Value, isPrimary: true, byUserId).IsSuccess
            : customer.UpdateAddress(existing.Id, kind, address.Value, existing.IsPrimary, byUserId).IsSuccess;
        if (succeeded)
            applied.Add(targetKey);
        else
            failures.Add(new ClientFormProfileUpdateFailure(targetKey, Outcomes.ValueInvalid));
    }

    // Solo agrega relaciones nuevas; nunca modifica ni borra. Deduplica por nombre + fecha de nacimiento.
    private static void ApplyDependents(
        CustomerEntity customer,
        ClientFormTargetValue value,
        Guid byUserId,
        ICollection<string> applied,
        ICollection<ClientFormProfileUpdateFailure> failures
    )
    {
        if (value.Rows is null || value.Rows.Count == 0)
        {
            failures.Add(new ClientFormProfileUpdateFailure(DependentsTarget, Outcomes.ValueInvalid));
            return;
        }
        var anyValid = false;
        foreach (var row in value.Rows)
        {
            var fields = row.Fields;
            var name = PersonalName.Create(
                fields.GetValueOrDefault("first_name") ?? string.Empty,
                fields.GetValueOrDefault("last_name") ?? string.Empty,
                fields.GetValueOrDefault("middle_name"),
                null,
                fields.GetValueOrDefault("suffix")
            );
            if (name.IsFailure)
                continue;
            var dob = ParseDate(fields.GetValueOrDefault("date_of_birth"));
            var kind = ParseRelationshipKind(fields.GetValueOrDefault("relationship_kind"));
            anyValid = true;
            if (IsDuplicateDependent(customer, name.Value, dob))
                continue;
            customer.AddRelation(kind, RelationPurpose.Dependent, name.Value, null, null, dob, null, byUserId);
        }
        if (anyValid)
            applied.Add(DependentsTarget);
        else
            failures.Add(new ClientFormProfileUpdateFailure(DependentsTarget, Outcomes.ValueInvalid));
    }

    private static bool IsDuplicateDependent(CustomerEntity customer, PersonalName name, DateOnly? dob) =>
        customer.Relations.Any(relation =>
            relation.IsActive
            && relation.Purposes.HasFlag(RelationPurpose.Dependent)
            && string.Equals(relation.Name.FirstName, name.FirstName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(relation.Name.LastName, name.LastName, StringComparison.OrdinalIgnoreCase)
            && relation.DateOfBirth == dob
        );

    private static async Task ApplyFiscalAsync(
        CustomerEntity customer,
        ClientFormTargetValue? filing,
        ClientFormTargetValue? taxId,
        ISensitiveDataProtector protector,
        ICustomerRepository repository,
        bool isAdmin,
        Guid byUserId,
        ICollection<string> applied,
        ICollection<ClientFormProfileUpdateFailure> failures,
        CancellationToken ct
    )
    {
        if (filing is null && taxId is null)
            return;
        if (!isAdmin)
        {
            if (filing is not null)
                failures.Add(new ClientFormProfileUpdateFailure(FilingStatusTarget, Outcomes.NotAuthorized));
            if (taxId is not null)
                failures.Add(new ClientFormProfileUpdateFailure(TaxIdentifierTarget, Outcomes.NotAuthorized));
            return;
        }

        FilingStatus? mappedFiling = null;
        var filingValid = filing is null;
        if (filing is not null && Enum.TryParse<FilingStatus>(filing.Value, out var parsedFiling))
        {
            mappedFiling = parsedFiling;
            filingValid = true;
        }

        if (taxId is not null)
        {
            var outcome = await TryApplyTaxIdentifierAsync(
                customer,
                taxId,
                mappedFiling,
                protector,
                repository,
                byUserId,
                ct
            );
            if (outcome == TaxApplyOutcome.Applied)
            {
                applied.Add(TaxIdentifierTarget);
                if (filing is not null)
                    applied.Add(FilingStatusTarget);
                return;
            }
            failures.Add(
                new ClientFormProfileUpdateFailure(
                    TaxIdentifierTarget,
                    outcome == TaxApplyOutcome.Duplicate ? Outcomes.TaxIdentifierAlreadyExists : Outcomes.ValueInvalid
                )
            );
        }

        if (filing is null)
            return;
        if (!filingValid)
        {
            failures.Add(new ClientFormProfileUpdateFailure(FilingStatusTarget, Outcomes.ValueInvalid));
            return;
        }
        if (customer.FiscalProfile is null)
        {
            failures.Add(new ClientFormProfileUpdateFailure(FilingStatusTarget, Outcomes.NoFiscalProfile));
            return;
        }
        var update = customer.UpdateFiscalProfile(
            filingStatus: mappedFiling,
            priorYearAgi: customer.FiscalProfile.PriorYearAgi,
            isReturningCustomer: customer.FiscalProfile.IsReturningCustomer,
            refundBankAccountCipher: null,
            refundBankRoutingCipher: null,
            byUserId: byUserId
        );
        if (update.IsFailure)
            failures.Add(new ClientFormProfileUpdateFailure(FilingStatusTarget, Outcomes.ValueInvalid));
        else
            applied.Add(FilingStatusTarget);
    }

    private enum TaxApplyOutcome
    {
        Applied,
        InvalidValue,
        Duplicate,
    }

    // Reemplaza el perfil entero conservando AGI, returning y banco actuales (Rev. 6.1). filing mapeado si vino.
    private static async Task<TaxApplyOutcome> TryApplyTaxIdentifierAsync(
        CustomerEntity customer,
        ClientFormTargetValue taxId,
        FilingStatus? mappedFiling,
        ISensitiveDataProtector protector,
        ICustomerRepository repository,
        Guid byUserId,
        CancellationToken ct
    )
    {
        var subjectKind = FiscalTaxIdentifierService.SubjectKindFor(taxId.Kind);
        var normalized = FiscalTaxIdentifierService.ValidateAndNormalize(taxId.Value ?? string.Empty, subjectKind);
        if (normalized.IsFailure)
            return TaxApplyOutcome.InvalidValue;

        var encrypted = FiscalTaxIdentifierService.Encrypt(normalized.Value, customer.TenantId, protector);
        var unique = await FiscalTaxIdentifierService.EnsureUniqueAsync(
            repository,
            customer.TenantId,
            encrypted.BlindIndexCandidates,
            customer.Id,
            ct
        );
        if (unique.IsFailure)
            return TaxApplyOutcome.Duplicate;

        var current = customer.FiscalProfile;
        var result = customer.SetFiscalProfile(
            subjectKind: subjectKind,
            taxIdentifierCipher: encrypted.Cipher,
            taxIdentifierBlindIndex: encrypted.BlindIndex,
            taxIdentifierLast4: encrypted.Last4,
            filingStatus: mappedFiling ?? current?.FilingStatus,
            priorYearAgi: current?.PriorYearAgi,
            isReturningCustomer: current?.IsReturningCustomer ?? false,
            refundBankAccountCipher: current?.RefundBankAccountCipher,
            refundBankRoutingCipher: current?.RefundBankRoutingCipher,
            byUserId: byUserId,
            taxIdentifierKeyVersion: encrypted.KeyVersion
        );
        return result.IsSuccess ? TaxApplyOutcome.Applied : TaxApplyOutcome.InvalidValue;
    }

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static RelationshipKind ParseRelationshipKind(string? value) =>
        Enum.TryParse<RelationshipKind>(value, ignoreCase: false, out var kind) && Enum.IsDefined(kind)
            ? kind
            : RelationshipKind.Child;
}
