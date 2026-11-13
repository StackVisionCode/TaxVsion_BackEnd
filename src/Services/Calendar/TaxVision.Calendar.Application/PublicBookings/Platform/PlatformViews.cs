using TaxVision.Calendar.Domain.PublicBookings;

namespace TaxVision.Calendar.Application.PublicBookings.Platform;

/// <summary>Traduce los agregados a lo que ve el equipo de la plataforma.</summary>
internal static class PlatformViews
{
    public static BookingPageAdminView Page(BookingPage page)
    {
        var hosts = new List<BookingPageHostView>();
        foreach (var host in page.Hosts)
            hosts.Add(
                new BookingPageHostView(
                    host.UserId,
                    host.DisplayName,
                    host.NotificationEmail,
                    host.MeetingUrl,
                    host.SortOrder,
                    host.IsActive
                )
            );
        hosts.Sort(
            (a, b) => a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : a.UserId.CompareTo(b.UserId)
        );

        var options = new List<HeardAboutOptionView>();
        foreach (var option in page.HeardAboutOptions)
            options.Add(
                new HeardAboutOptionView(
                    option.Code,
                    option.LabelEn,
                    option.LabelEs,
                    option.SortOrder,
                    option.IsActive,
                    option.AllowsDetail
                )
            );
        options.Sort(
            (a, b) =>
                a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : string.CompareOrdinal(a.Code, b.Code)
        );

        return new BookingPageAdminView(
            page.Id,
            page.Slug.Value,
            page.Title.En,
            page.Title.Es,
            page.Description.En,
            page.Description.Es,
            page.AppointmentTypeId,
            page.IsActive,
            page.MinNoticeMinutes,
            page.MaxDaysAhead,
            page.BufferMinutes,
            page.SlotStepMinutes,
            page.RequireEmailVerification,
            page.ReminderHoursBefore,
            page.IcsAlarmMinutes,
            page.PhoneField.ToString(),
            page.OfficeSizeField.ToString(),
            page.InterestField.ToString(),
            page.PrivacyNotice?.En,
            page.PrivacyNotice?.Es,
            page.MarketingConsent?.En,
            page.MarketingConsent?.Es,
            page.ConsentVersion,
            hosts,
            options,
            page.UpdatedByUserId,
            page.UpdatedAtUtc
        );
    }

    public static PublicBookingAdminItem Booking(BookingPage page, PublicBooking booking)
    {
        BookingPageHost? host = null;
        foreach (var candidate in page.Hosts)
        {
            if (candidate.UserId == booking.HostUserId)
                host = candidate;
        }

        return new PublicBookingAdminItem(
            booking.Id,
            booking.Status.ToString(),
            booking.SlotStartUtc,
            booking.DurationMinutes,
            booking.HostUserId,
            host?.DisplayName ?? string.Empty,
            booking.Name,
            booking.Email,
            booking.Company,
            booking.OfficeSize?.ToString(),
            booking.Phone,
            booking.Interest,
            booking.HeardAboutCode,
            booking.HeardAboutDetail,
            booking.EntrySource.ToString(),
            booking.UtmSource,
            booking.UtmMedium,
            booking.UtmCampaign,
            booking.ReferrerDomain,
            booking.Language.ToString(),
            booking.VisitorTimeZone,
            booking.MarketingOptIn,
            booking.CreatedAtUtc,
            booking.ConfirmedAtUtc,
            booking.CancelledAtUtc,
            booking.CancelledBy?.ToString(),
            booking.CancelReason,
            booking.OutcomeAtUtc
        );
    }
}
