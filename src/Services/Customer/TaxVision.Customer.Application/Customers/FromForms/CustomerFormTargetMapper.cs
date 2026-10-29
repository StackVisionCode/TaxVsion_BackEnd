using System.Globalization;
using System.Text.RegularExpressions;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents.Targets;
using BuildingBlocks.Results;
using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers.FromForms;

/// <summary>
/// Traduce los valores del formulario al alta de Customer, destino por destino, con el catálogo como
/// única fuente de tipos y límites. Las traducciones entre esquemas solo salen de LegacyValueMaps.
/// </summary>
public static class CustomerFormTargetMapper
{
    private const string DateFormat = "yyyy-MM-dd";
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(1);

    private static readonly Dictionary<
        string,
        Func<CustomerFormDraft, string?, ClientFormAddressValue?, bool>
    > Appliers = new(StringComparer.Ordinal)
    {
        ["customer.kind"] = (d, v, _) => SetEnum<CustomerKind>(v, x => d.Kind = x),
        ["customer.person.first_name"] = (d, v, _) => SetText(v, x => d.FirstName = x),
        ["customer.person.last_name"] = (d, v, _) => SetText(v, x => d.LastName = x),
        ["customer.person.middle_name"] = (d, v, _) => SetText(v, x => d.MiddleName = x),
        ["customer.person.prefix"] = (d, v, _) => SetText(v, x => d.Prefix = x),
        ["customer.person.suffix"] = (d, v, _) => SetText(v, x => d.Suffix = x),
        ["customer.business.legal_name"] = (d, v, _) => SetText(v, x => d.LegalName = x),
        ["customer.business.structure"] = (d, v, _) => SetEnum<BusinessStructure>(v, x => d.BusinessStructure = x),
        ["customer.business.dba"] = (d, v, _) => SetText(v, x => d.Dba = x),
        ["customer.business.formation_date"] = (d, v, _) => SetDate(v, x => d.FormationDate = x),
        ["customer.date_of_birth"] = (d, v, _) => SetDate(v, x => d.DateOfBirth = x),
        ["customer.primary_email"] = (d, v, _) => SetText(v, x => d.PrimaryEmail = x),
        ["customer.primary_phone"] = (d, v, _) => SetText(v, x => d.PrimaryPhone = x),
        ["customer.language"] = (d, v, _) => SetEnum<Language>(v, x => d.Language = x),
        ["customer.contact.email_enabled"] = (d, v, _) => SetBoolean(v, x => d.EmailEnabled = x),
        ["customer.contact.sms_enabled"] = (d, v, _) => SetBoolean(v, x => d.SmsEnabled = x),
        ["customer.address.home"] = (d, _, a) => SetAddress(a, x => d.HomeAddress = x),
        ["customer.address.mailing"] = (d, _, a) => SetAddress(a, x => d.MailingAddress = x),
    };

    public static IReadOnlyCollection<string> SupportedTargetKeys => Appliers.Keys;

    public static Result<CustomerFormDraft> Map(IReadOnlyList<ClientFormTargetValue>? values) =>
        Map(values, CustomerDataCollectionTargets.All);

    public static Result<CustomerFormDraft> Map(
        IReadOnlyList<ClientFormTargetValue>? values,
        IReadOnlyList<DataCollectionTarget> catalog
    )
    {
        var draft = new CustomerFormDraft();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values ?? [])
        {
            var applied = Apply(draft, value, catalog, present);
            if (applied.IsFailure)
                return Result.Failure<CustomerFormDraft>(applied.Error);
        }

        var required = EnsureRequired(draft, catalog, present);
        return required.IsFailure ? Result.Failure<CustomerFormDraft>(required.Error) : Result.Success(draft);
    }

    private static Result Apply(
        CustomerFormDraft draft,
        ClientFormTargetValue value,
        IReadOnlyList<DataCollectionTarget> catalog,
        HashSet<string> present
    )
    {
        var target = FindExecutableTarget(catalog, value.TargetKey);
        if (target is null || !Appliers.TryGetValue(target.TargetKey, out var applier))
            return Result.Failure(NotSupported(value.TargetKey));
        if (string.IsNullOrWhiteSpace(value.Value) && value.Address is null)
            return Result.Success();
        if (!present.Add(target.TargetKey))
            return Result.Failure(Invalid(target.TargetKey));

        var resolved = ResolveLegacyValue(target, value);
        if (resolved.IsFailure)
            return Result.Failure(resolved.Error);

        return Satisfies(target, resolved.Value, value.Address) && applier(draft, resolved.Value, value.Address)
            ? Result.Success()
            : Result.Failure(Invalid(target.TargetKey));
    }

    // Retired no se ejecuta; Deprecated sí, porque las requests viejas siguen fijadas a su versión.
    private static DataCollectionTarget? FindExecutableTarget(
        IReadOnlyList<DataCollectionTarget> catalog,
        string? targetKey
    ) =>
        catalog.FirstOrDefault(t =>
            string.Equals(t.TargetKey, targetKey, StringComparison.Ordinal)
            && t.Status != DataCollectionTargetStatus.Retired
            && t.Operations.Contains(DataCollectionOperation.CreateCustomer)
        );

    private static Result<string?> ResolveLegacyValue(DataCollectionTarget target, ClientFormTargetValue value)
    {
        if (value.TargetSchemaVersion == target.TargetSchemaVersion)
            return Result.Success(value.Value);
        if (value.TargetSchemaVersion < 1 || value.TargetSchemaVersion > target.TargetSchemaVersion)
            return Result.Failure<string?>(NotSupported(target.TargetKey));

        var map = target.LegacyValueMaps?.FirstOrDefault(m => m.FromSchemaVersion == value.TargetSchemaVersion);
        if (map is null || value.Value is null)
            return Result.Success(value.Value);

        return map.Values.TryGetValue(value.Value, out var translated)
            ? Result.Success<string?>(translated)
            : Result.Failure<string?>(Invalid(target.TargetKey));
    }

    private static bool Satisfies(DataCollectionTarget target, string? value, ClientFormAddressValue? address) =>
        target.DataType switch
        {
            DataCollectionTargetType.Address => address is not null,
            DataCollectionTargetType.String or DataCollectionTargetType.Email => value is not null
                && (target.Constraints.MaxLength is not { } max || value.Length <= max),
            DataCollectionTargetType.Enum => value is not null
                && target.Constraints.EnumValues?.Contains(value, StringComparer.Ordinal) == true,
            DataCollectionTargetType.Phone => value is not null
                && (
                    target.Constraints.Pattern is not { } pattern
                    || Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, PatternTimeout)
                ),
            DataCollectionTargetType.Date or DataCollectionTargetType.Boolean => value is not null,
            _ => false,
        };

    private static Result EnsureRequired(
        CustomerFormDraft draft,
        IReadOnlyList<DataCollectionTarget> catalog,
        HashSet<string> present
    )
    {
        var kind = draft.Kind?.ToString();
        foreach (var target in catalog)
        {
            var required = target.RequiredFor.Any(r =>
                r.Operation == DataCollectionOperation.CreateCustomer && (r.Kind is null || r.Kind == kind)
            );
            if (required && target.Status != DataCollectionTargetStatus.Retired && !present.Contains(target.TargetKey))
                return Result.Failure(Invalid(target.TargetKey));
        }

        return Result.Success();
    }

    private static bool SetText(string? value, Action<string> assign)
    {
        assign(value!);
        return true;
    }

    private static bool SetEnum<TEnum>(string? value, Action<TEnum> assign)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed))
            return false;
        assign(parsed);
        return true;
    }

    private static bool SetDate(string? value, Action<DateOnly> assign)
    {
        if (!DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return false;
        assign(date);
        return true;
    }

    private static bool SetBoolean(string? value, Action<bool> assign)
    {
        if (!bool.TryParse(value, out var flag))
            return false;
        assign(flag);
        return true;
    }

    private static bool SetAddress(ClientFormAddressValue? address, Action<ClientFormAddressValue> assign)
    {
        if (address is null)
            return false;
        assign(address);
        return true;
    }

    private static Error NotSupported(string? targetKey) =>
        new(CustomerCreationFromFormOutcomes.FormTargetNotSupported, $"Target '{targetKey}' is not supported.");

    private static Error Invalid(string targetKey) =>
        new(CustomerCreationFromFormOutcomes.FormValueInvalid, $"The value for target '{targetKey}' is invalid.");
}
