using System.Globalization;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using TaxVision.Tenant.Domain.Profiles;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Application.Profiles;

/// <summary>Estado completo del perfil público para las proyecciones. Nunca lleva datos de cumplimiento.</summary>
public static class OfficeProfileChangedEvents
{
    public static TenantProfileChangedIntegrationEvent From(OfficeProfile profile, string correlationId) =>
        new()
        {
            TenantId = profile.TenantId,
            ProfileTenantId = profile.TenantId,
            Version = profile.Version,
            UpdatedAtUtc =
                profile.UpdatedAtUtc
                ?? throw new InvalidOperationException("A profile is published only after it has been saved."),
            LegalName = profile.LegalName,
            DisplayName = profile.DisplayName,
            EntityType = profile.EntityType?.ToString(),
            Ein = profile.Ein,
            Phone = profile.Phone,
            Email = profile.Email,
            Website = profile.Website,
            PhysicalAddress = ToData(profile.PhysicalAddress),
            MailingAddress = ToData(profile.MailingAddress),
            DefaultLanguage = profile.DefaultLanguage,
            BusinessHours = profile.BusinessHours.Select(ToData).ToList(),
            Closures = profile.Closures.Select(ToData).ToList(),
            BookingUrl = profile.BookingUrl,
            ReviewUrl = profile.ReviewUrl,
            CorrelationId = correlationId,
        };

    private static OfficeAddressData? ToData(OfficeAddress? address) =>
        address is null
            ? null
            : new(address.Line1, address.Line2, address.City, address.State, address.PostalCode, address.Country);

    private static OfficeClosureData ToData(OfficeClosure closure) =>
        new(
            closure.Date.ToString(OfficeClosure.DateFormat, CultureInfo.InvariantCulture),
            closure.EndDate?.ToString(OfficeClosure.DateFormat, CultureInfo.InvariantCulture),
            closure.Label,
            closure.RepeatsYearly
        );

    private static OfficeBusinessHoursData ToData(DailyOpeningHours hours) =>
        new(
            hours.Day.ToString(),
            hours.Opens.ToString(DailyOpeningHours.TimeFormat, CultureInfo.InvariantCulture),
            hours.Closes.ToString(DailyOpeningHours.TimeFormat, CultureInfo.InvariantCulture)
        );
}
