using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Domain;

/// <summary>Los value objects de la página pública y de la reserva del invitado.</summary>
public sealed class PublicBookingValueObjectsTests
{
    [Theory]
    [InlineData("demo", true)]
    [InlineData("product-demo", true)]
    [InlineData("de", false)]
    [InlineData("-demo", false)]
    [InlineData("Demo", false)]
    [InlineData("demo page", false)]
    public void The_slug_is_short_lowercase_and_url_safe(string slug, bool valid) =>
        Assert.Equal(valid, BookingPageSlug.Create(slug).IsSuccess);

    [Fact]
    public void A_work_email_is_normalized()
    {
        var mixed = WorkEmail.Create("  Jane@Acme-Tax.com ").Value;

        Assert.Equal("jane@acme-tax.com", mixed.Value);
        Assert.Equal(WorkEmail.Create("jane@acme-tax.com").Value, mixed);
        Assert.True(WorkEmail.Create("not-an-email").IsFailure);
    }

    [Fact]
    public void Text_errors_say_what_is_wrong()
    {
        Assert.Equal("Calendar.PublicBooking.InterestRequired", DemoInterest.Create(" ").Error.Code);
        Assert.Equal("Calendar.PublicBooking.TextInvalid", ProspectName.Create("Jane\u0007Doe").Error.Code);
        Assert.Equal("Calendar.PublicBooking.NameTooLong", ProspectName.Create(new string('x', 201)).Error.Code);
        Assert.Equal("Calendar.PublicBooking.TextInvalid", CompanyName.Create("Acme\nTax").Error.Code);
        Assert.True(DemoInterest.Create("Line one\nLine two").IsSuccess);
    }

    [Fact]
    public void The_heard_about_detail_only_goes_with_options_that_allow_it()
    {
        Assert.Equal(
            "Calendar.PublicBooking.HeardAboutDetailNotAllowed",
            HeardAboutAnswer.Create("whatsapp", "x", false).Error.Code
        );
        Assert.Equal(
            "Calendar.PublicBooking.HeardAboutDetailTooLong",
            HeardAboutAnswer.Create("other", new string('x', 101), true).Error.Code
        );
        Assert.Equal("Podcast", HeardAboutAnswer.Create("other", " Podcast ", true).Value.Detail);
        Assert.Null(HeardAboutAnswer.Create("other", "  ", true).Value.Detail);
    }

    [Fact]
    public void The_attribution_keeps_only_the_referrer_domain()
    {
        Assert.Equal(
            "google.com",
            BookingAttribution.Create(BookingEntrySource.Faq, null, null, null, "google.com").Value.ReferrerDomain
        );
        Assert.True(
            BookingAttribution
                .Create(BookingEntrySource.Faq, null, null, null, "https://google.com/search?q=x")
                .IsFailure
        );
        Assert.True(
            BookingAttribution.Create(BookingEntrySource.Faq, new string('u', 101), null, null, null).IsFailure
        );
    }

    [Fact]
    public void A_meeting_url_must_be_absolute_https_and_fit_the_location()
    {
        Assert.True(MeetingUrl.Create("https://zoom.us/j/123456789").IsSuccess);
        Assert.True(MeetingUrl.Create("http://zoom.us/j/1").IsFailure);
        Assert.True(MeetingUrl.Create("zoom.us/j/1").IsFailure);
        Assert.True(MeetingUrl.Create("https://x.io/" + new string('a', Location.MaxLength)).IsFailure);
    }

    [Fact]
    public void The_verification_code_is_bound_to_its_booking()
    {
        var booking = Guid.NewGuid();
        var code = VerificationCode.Generate();

        Assert.Matches("^[0-9]{6}$", code);
        var hash = VerificationCode.HashOf(booking, code);
        Assert.True(VerificationCode.Matches(booking, code, hash));
        Assert.False(VerificationCode.Matches(Guid.NewGuid(), code, hash));
        Assert.False(VerificationCode.Matches(booking, "abc", hash));
    }

    [Fact]
    public void A_phone_needs_at_least_seven_digits()
    {
        Assert.True(ProspectPhone.Create("+1 (305) 555-0100").IsSuccess);
        Assert.True(ProspectPhone.Create("12345").IsFailure);
        Assert.True(ProspectPhone.Create("call me").IsFailure);
    }

    [Fact]
    public void Localized_text_needs_english()
    {
        Assert.True(LocalizedText.Create(null, "Hola", 50).IsFailure);
        Assert.Equal("Hello", LocalizedText.Create("Hello", null, 50).Value.For(BookingLanguage.Es));
        Assert.Equal("Hola", LocalizedText.Create("Hello", "Hola", 50).Value.For(BookingLanguage.Es));
        Assert.True(LocalizedText.Create(new string('x', 51), null, 50).IsFailure);
    }
}
