using System.Globalization;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Application.Profiles;

/// <summary>Arma la respuesta del perfil. Sin perfil guardado devuelve todo vacío con versión 0, nunca 404.</summary>
public static class OfficeProfileResponses
{
    private static readonly OfficeAddressResponse EmptyAddress = new(
        null,
        null,
        null,
        null,
        null,
        OfficeAddress.UnitedStates
    );

    public static OfficeProfileResponse From(OfficeProfile? profile) =>
        profile is null
            ? new OfficeProfileResponse(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                EmptyAddress,
                MailingSameAsPhysical: true,
                MailingAddress: null,
                OfficeLanguage.English,
                [],
                null,
                null,
                Version: 0,
                UpdatedAtUtc: null
            )
            : new OfficeProfileResponse(
                profile.LegalName,
                profile.DisplayName,
                profile.EntityType?.ToString(),
                profile.Ein,
                profile.Phone,
                profile.Email,
                profile.Website,
                ToResponse(profile.PhysicalAddress) ?? EmptyAddress,
                MailingSameAsPhysical: profile.MailingAddress is null,
                ToResponse(profile.MailingAddress),
                profile.DefaultLanguage,
                profile.BusinessHours.Select(ToResponse).ToList(),
                profile.BookingUrl,
                profile.ReviewUrl,
                profile.Version,
                profile.UpdatedAtUtc
            );

    internal static OfficeAddressResponse? ToResponse(OfficeAddress? address) =>
        address is null
            ? null
            : new(address.Line1, address.Line2, address.City, address.State, address.PostalCode, address.Country);

    internal static BusinessHoursResponse ToResponse(DailyOpeningHours hours) =>
        new(
            hours.Day.ToString(),
            hours.Opens.ToString(DailyOpeningHours.TimeFormat, CultureInfo.InvariantCulture),
            hours.Closes.ToString(DailyOpeningHours.TimeFormat, CultureInfo.InvariantCulture)
        );
}
