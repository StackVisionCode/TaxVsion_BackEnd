using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.Types;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

public sealed class BookingPageTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

    /// <summary>Un tipo de cita del tenant; por defecto sirve para una demo pública.</summary>
    internal static AppointmentType DemoType(Guid tenant, bool isVirtual = false, bool blocksOnConflict = true) =>
        AppointmentType
            .Create(tenant, "Product demo", TimeSpan.FromMinutes(30), "#2563EB", Now, isVirtual, blocksOnConflict)
            .Value;

    internal static BookingPage NewPage()
    {
        var page = BookingPage.Create(
            Tenant,
            BookingPageSlug.Create("demo").Value,
            LocalizedText.Create("Book a live demo", "Agenda una demo en vivo", BookingPage.MaxTitleLength).Value,
            LocalizedText.Create("See it live.", null, BookingPage.MaxDescriptionLength).Value,
            DemoType(Tenant),
            Admin,
            Now
        );
        Assert.True(page.IsSuccess, page.IsFailure ? page.Error.Code : null);
        return page.Value;
    }

    internal static BookingPageHostInput Host(Guid userId, int order = 0, bool active = true) =>
        new(userId, "Ana Pérez", "ana@taxoffix.io", "https://meet.google.com/abc-defg-hij", order, active);

    internal static LocalizedText Privacy =>
        LocalizedText
            .Create("We use this information only to schedule your demo.", null, BookingPage.MaxConsentTextLength)
            .Value;

    internal static LocalizedText Marketing =>
        LocalizedText.Create("Send me product news by email.", null, BookingPage.MaxConsentTextLength).Value;

    internal static HeardAboutOptionInput Option(string code, bool allowsDetail = false, bool active = true) =>
        new(code, code, null, 0, active, allowsDetail);

    [Fact]
    public void A_new_page_starts_inactive_with_safe_defaults()
    {
        var page = NewPage();

        Assert.False(page.IsActive);
        Assert.True(page.RequireEmailVerification);
        Assert.Equal(24, page.ReminderHoursBefore);
        Assert.Equal([1440, 60], page.IcsAlarmMinutes);
        Assert.Equal(BookingFieldMode.Optional, page.PhoneField);
        Assert.Empty(page.Hosts);
    }

    [Fact]
    public void It_cannot_be_activated_without_an_active_host_an_active_option_and_the_consent()
    {
        var page = NewPage();
        Assert.Equal("Calendar.BookingPage.NoActiveHost", page.Activate(Admin, Now).Error.Code);

        Assert.True(page.SetHosts([Host(Guid.NewGuid())], Admin, Now).IsSuccess);
        Assert.Equal("Calendar.BookingPage.NoHeardAboutOption", page.Activate(Admin, Now).Error.Code);

        Assert.True(page.SetHeardAboutOptions([Option("google_search")], Admin, Now).IsSuccess);
        Assert.Equal("Calendar.BookingPage.NoConsent", page.Activate(Admin, Now).Error.Code);

        Assert.True(page.ChangeConsent(Privacy, Marketing, "2026-11-demo-v1", Admin, Now).IsSuccess);
        Assert.True(page.Activate(Admin, Now).IsSuccess);
        Assert.True(page.IsActive);
    }

    [Fact]
    public void Deactivating_the_last_active_host_of_an_active_page_is_rejected()
    {
        var host = Guid.NewGuid();
        var page = NewPage();
        page.SetHosts([Host(host)], Admin, Now);
        page.SetHeardAboutOptions([Option("google_search")], Admin, Now);
        page.ChangeConsent(Privacy, Marketing, "2026-11-demo-v1", Admin, Now);
        Assert.True(page.Activate(Admin, Now).IsSuccess);

        var result = page.SetHosts([Host(host, active: false)], Admin, Now);

        Assert.Equal("Calendar.BookingPage.NoActiveHost", result.Error.Code);
    }

    [Fact]
    public void Hosts_are_offered_in_a_stable_order()
    {
        var first = new Guid("00000000-0000-0000-0000-000000000002");
        var second = new Guid("00000000-0000-0000-0000-000000000001");
        var page = NewPage();

        page.SetHosts(
            [Host(first, order: 0), Host(second, order: 0), Host(Guid.NewGuid(), order: 5, active: false)],
            Admin,
            Now
        );

        Assert.Equal([second, first], page.ActiveHosts.Select(host => host.UserId));
    }

    [Fact]
    public void Duplicated_hosts_or_invalid_links_are_rejected()
    {
        var page = NewPage();
        var host = Guid.NewGuid();

        Assert.Equal(
            "Calendar.BookingPage.DuplicatedHost",
            page.SetHosts([Host(host), Host(host)], Admin, Now).Error.Code
        );
        Assert.Equal(
            "Calendar.BookingPage.MeetingUrlInvalid",
            page.SetHosts([Host(host) with { MeetingUrl = "http://not-https.example" }], Admin, Now).Error.Code
        );
        Assert.Equal(
            "Calendar.BookingPage.EmailInvalid",
            page.SetHosts([Host(host) with { NotificationEmail = "nope" }], Admin, Now).Error.Code
        );
    }

    [Fact]
    public void Heard_about_options_must_have_unique_valid_codes()
    {
        var page = NewPage();

        Assert.Equal(
            "Calendar.BookingPage.DuplicatedOption",
            page.SetHeardAboutOptions([Option("whatsapp"), Option("whatsapp")], Admin, Now).Error.Code
        );
        Assert.Equal(
            "Calendar.BookingPage.OptionCodeInvalid",
            page.SetHeardAboutOptions([Option("Whats App")], Admin, Now).Error.Code
        );
        Assert.True(
            page.SetHeardAboutOptions([Option("whatsapp"), Option("other", allowsDetail: true)], Admin, Now).IsSuccess
        );
        Assert.True(page.FindActiveOption("other")!.AllowsDetail);
        Assert.Null(page.FindActiveOption("tiktok"));
    }

    [Fact]
    public void The_consent_needs_both_texts_and_a_version()
    {
        var page = NewPage();

        Assert.Equal(
            "Calendar.BookingPage.ConsentVersionInvalid",
            page.ChangeConsent(Privacy, Marketing, " ", Admin, Now).Error.Code
        );
        Assert.True(page.ChangeConsent(Privacy, Marketing, "2026-12-demo-v2", Admin, Now).IsSuccess);
        Assert.Equal("2026-12-demo-v2", page.ConsentVersion);
        Assert.Equal(Privacy, page.PrivacyNotice);
    }

    [Fact]
    public void Rules_out_of_range_are_rejected()
    {
        var page = NewPage();

        Assert.Equal(
            "Calendar.BookingPage.RulesOutOfRange",
            page.ChangeRules(-1, 60, 0, 30, true, 24, [60], Admin, Now).Error.Code
        );
        Assert.Equal(
            "Calendar.BookingPage.RulesOutOfRange",
            page.ChangeRules(60, 60, 0, 7, true, 24, [60], Admin, Now).Error.Code
        );
        Assert.Equal(
            "Calendar.BookingPage.RulesOutOfRange",
            page.ChangeRules(60, 60, 0, 30, true, 24, [0], Admin, Now).Error.Code
        );
        Assert.True(page.ChangeRules(240, 30, 10, 30, false, null, [], Admin, Now).IsSuccess);
        Assert.False(page.RequireEmailVerification);
        Assert.Null(page.ReminderHoursBefore);
    }

    [Fact]
    public void The_appointment_type_must_be_active_not_virtual_blocking_and_of_the_same_tenant()
    {
        var page = NewPage();

        Assert.Equal(
            "Calendar.BookingPage.TypeNotUsable",
            page.ChangeAppointmentType(DemoType(Tenant, isVirtual: true), Admin, Now).Error.Code
        );
        Assert.Equal(
            "Calendar.BookingPage.TypeNotUsable",
            page.ChangeAppointmentType(DemoType(Tenant, blocksOnConflict: false), Admin, Now).Error.Code
        );
        Assert.Equal(
            "Calendar.BookingPage.TypeNotUsable",
            page.ChangeAppointmentType(DemoType(Guid.NewGuid()), Admin, Now).Error.Code
        );
        Assert.True(page.ChangeAppointmentType(DemoType(Tenant), Admin, Now).IsSuccess);
    }

    [Fact]
    public void A_page_cannot_be_born_with_an_unusable_type()
    {
        var created = BookingPage.Create(
            Tenant,
            BookingPageSlug.Create("demo").Value,
            LocalizedText.Create("Book a live demo", null, BookingPage.MaxTitleLength).Value,
            LocalizedText.Create("See it live.", null, BookingPage.MaxDescriptionLength).Value,
            DemoType(Tenant, isVirtual: true),
            Admin,
            Now
        );

        Assert.Equal("Calendar.BookingPage.TypeNotUsable", created.Error.Code);
    }

    [Fact]
    public void The_spanish_text_must_fit_too()
    {
        var page = NewPage();
        var longSpanish = LocalizedText.Create("Book a live demo", new string('x', 500), 1000).Value;

        var changed = page.ChangeTexts(page.Slug, longSpanish, page.Description, Admin, Now);

        Assert.Equal("Calendar.BookingPage.TextTooLong", changed.Error.Code);
    }

    [Fact]
    public void Every_change_leaves_who_and_when()
    {
        var page = NewPage();
        var later = Now.AddHours(1);
        var other = Guid.NewGuid();

        page.ConfigureFields(
            BookingFieldMode.Hidden,
            BookingFieldMode.Required,
            BookingFieldMode.Optional,
            other,
            later
        );

        Assert.Equal(other, page.UpdatedByUserId);
        Assert.Equal(later, page.UpdatedAtUtc);
        Assert.Equal(BookingFieldMode.Required, page.OfficeSizeField);
    }
}
