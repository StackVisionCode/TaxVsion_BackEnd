using BuildingBlocks.Results;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Domain.Profiles;

/// <summary>
/// El perfil ya validado y normalizado, listo para aplicarse. Se arma completo antes de tocar el
/// agregado: así un campo inválido nunca deja la mitad del perfil cambiada.
/// </summary>
public sealed record OfficeProfileDetails(
    string? LegalName,
    string? DisplayName,
    OfficeEntityType? EntityType,
    string? Ein,
    string? Phone,
    string? Email,
    string? Website,
    OfficeAddress? PhysicalAddress,
    OfficeAddress? MailingAddress,
    string DefaultLanguage,
    WeeklyBusinessHours BusinessHours,
    string? BookingUrl,
    string? ReviewUrl
)
{
    // El sitio web llega a columnas de 256 en las proyecciones (Billing); los enlaces no.
    public const int WebsiteMaxLength = 256;
    public const int LinkMaxLength = 512;

    /// <summary>Edición: el primer campo inválido rechaza todo.</summary>
    public static Result<OfficeProfileDetails> Create(OfficeProfileDraft draft)
    {
        var collector = ProfileFieldCollector.Strict();
        var details = Build(draft, collector);
        return collector.FirstError is { } error
            ? Result.Failure<OfficeProfileDetails>(error)
            : Result.Success(details);
    }

    /// <summary>Importación de datos viejos: conserva lo válido y anota cada campo descartado.</summary>
    public static OfficeProfileDetails CreateLenient(OfficeProfileDraft draft, List<Error> dropped) =>
        Build(draft, ProfileFieldCollector.Lenient(dropped));

    private static OfficeProfileDetails Build(OfficeProfileDraft draft, ProfileFieldCollector fields)
    {
        var physical = TakeAddress(draft.PhysicalAddress, fields);
        var mailing = draft.MailingSameAsPhysical ? null : TakeAddress(draft.MailingAddress, fields);

        return new OfficeProfileDetails(
            fields.Take(OfficeLegalName.CreateOptional(draft.LegalName), null)?.Value,
            fields.Take(DoingBusinessAsName.CreateOptional(draft.DisplayName), null)?.Value,
            fields.Take(ParseEntityType(draft.EntityType), null),
            fields.Take(EmployerIdentificationNumber.CreateOptional(draft.Ein), null)?.Value,
            fields.Take(ContactPhone.CreateOptional(draft.Phone, OfficeProfileErrors.PhoneCode), null)?.Value,
            fields.Take(ContactEmail.CreateOptional(draft.Email, OfficeProfileErrors.EmailCode), null)?.Value,
            fields
                .Take(WebUrl.CreateOptional(draft.Website, WebsiteMaxLength, OfficeProfileErrors.Website), null)
                ?.Value,
            physical,
            mailing,
            fields.Take(OfficeLanguage.Create(draft.DefaultLanguage), OfficeLanguage.Default).Value,
            fields.Take(WeeklyBusinessHours.Create(draft.BusinessHours), WeeklyBusinessHours.None),
            fields
                .Take(WebUrl.CreateOptional(draft.BookingUrl, LinkMaxLength, OfficeProfileErrors.BookingUrl), null)
                ?.Value,
            fields
                .Take(WebUrl.CreateOptional(draft.ReviewUrl, LinkMaxLength, OfficeProfileErrors.ReviewUrl), null)
                ?.Value
        );
    }

    private static OfficeAddress? TakeAddress(OfficeAddressDraft? draft, ProfileFieldCollector fields)
    {
        if (draft is null)
            return null;

        var state = draft.State;
        var postalCode = draft.PostalCode;
        var address = Create(draft, state, postalCode);

        // Importación: un estado o ZIP malo se descarta solo, sin perder la calle ni la ciudad.
        if (fields.IsLenient && address.IsFailure && address.Error == OfficeAddress.InvalidState)
        {
            fields.Drop(address.Error);
            state = null;
            address = Create(draft, state, postalCode);
        }
        if (fields.IsLenient && address.IsFailure && address.Error == OfficeAddress.InvalidPostalCode)
        {
            fields.Drop(address.Error);
            address = Create(draft, state, postalCode: null);
        }

        return fields.Take(address, null);
    }

    private static Result<OfficeAddress?> Create(OfficeAddressDraft draft, string? state, string? postalCode) =>
        OfficeAddress.CreateOptional(draft.Line1, draft.Line2, draft.City, state, postalCode, draft.Country);

    private static Result<OfficeEntityType?> ParseEntityType(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return Result.Success<OfficeEntityType?>(null);

        // Enum.TryParse acepta números ("3"): solo valen los nombres.
        var isName = !char.IsAsciiDigit(trimmed[0]) && trimmed[0] != '-';
        return
            isName
            && Enum.TryParse<OfficeEntityType>(trimmed, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            ? Result.Success<OfficeEntityType?>(parsed)
            : Result.Failure<OfficeEntityType?>(OfficeProfileErrors.EntityType);
    }
}
