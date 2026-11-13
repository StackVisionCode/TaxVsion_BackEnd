using BuildingBlocks.Results;
using TaxVision.Calendar.Application.PublicBookings.Abstractions;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.PublicBookings;

/// <summary>Convierte el formulario en value objects según los campos que pide la página.</summary>
internal static class PublicBookingFormReader
{
    public static Result<PublicBookingRequestData> Read(
        BookingPage page,
        PublicBookingForm form,
        IRequesterKeyHasher hasher
    )
    {
        var name = ProspectName.Create(form.Name);
        if (name.IsFailure)
            return Result.Failure<PublicBookingRequestData>(name.Error);
        var email = WorkEmail.Create(form.Email);
        if (email.IsFailure)
            return Result.Failure<PublicBookingRequestData>(email.Error);
        var company = CompanyName.Create(form.Company);
        if (company.IsFailure)
            return Result.Failure<PublicBookingRequestData>(company.Error);
        var zone = CalendarTimeZone.Create(form.TimeZone);
        if (zone.IsFailure)
            return Result.Failure<PublicBookingRequestData>(zone.Error);
        if (form.ConsentVersion != page.ConsentVersion)
            return Result.Failure<PublicBookingRequestData>(PublicBookingErrors.ConsentOutdated);

        var officeSize = page.OfficeSizeField == BookingFieldMode.Hidden ? null : form.OfficeSize;
        if (page.OfficeSizeField == BookingFieldMode.Required && officeSize is null)
            return Result.Failure<PublicBookingRequestData>(PublicBookingErrors.FieldRequired);
        var phone = Optional(page.PhoneField, form.Phone, ProspectPhone.Create);
        if (phone.IsFailure)
            return Result.Failure<PublicBookingRequestData>(phone.Error);
        var interest = Optional(page.InterestField, form.Interest, DemoInterest.Create);
        if (interest.IsFailure)
            return Result.Failure<PublicBookingRequestData>(interest.Error);

        var option = page.FindActiveOption(form.HeardAboutCode);
        if (option is null)
            return Result.Failure<PublicBookingRequestData>(PublicBookingErrors.HeardAboutRequired);
        var heardAbout = HeardAboutAnswer.Create(option.Code, form.HeardAboutDetail, option.AllowsDetail);
        if (heardAbout.IsFailure)
            return Result.Failure<PublicBookingRequestData>(heardAbout.Error);

        return Result.Success(
            new PublicBookingRequestData(
                name.Value,
                email.Value,
                hasher.KeyOf(email.Value),
                company.Value,
                officeSize,
                phone.Value,
                interest.Value,
                heardAbout.Value,
                Attribution(form),
                form.Language,
                zone.Value,
                form.MarketingOptIn,
                form.ConsentVersion
            )
        );
    }

    /// <summary>Un dato de seguimiento mal formado no frena una reserva: se queda solo el origen.</summary>
    private static BookingAttribution Attribution(PublicBookingForm form)
    {
        var full = BookingAttribution.Create(
            form.EntrySource,
            form.UtmSource,
            form.UtmMedium,
            form.UtmCampaign,
            form.ReferrerDomain
        );
        if (full.IsSuccess)
            return full.Value;
        var source = Enum.IsDefined(form.EntrySource) ? form.EntrySource : BookingEntrySource.Direct;
        return BookingAttribution.Create(source, null, null, null, null).Value;
    }

    private static Result<T?> Optional<T>(BookingFieldMode mode, string? value, Func<string?, Result<T>> create)
        where T : class
    {
        if (mode == BookingFieldMode.Hidden)
            return Result.Success<T?>(null);
        if (string.IsNullOrWhiteSpace(value))
            return mode == BookingFieldMode.Required
                ? Result.Failure<T?>(PublicBookingErrors.FieldRequired)
                : Result.Success<T?>(null);
        var created = create(value);
        return created.IsFailure ? Result.Failure<T?>(created.Error) : Result.Success<T?>(created.Value);
    }
}
