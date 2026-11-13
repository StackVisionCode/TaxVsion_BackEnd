using System.Security.Cryptography;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.PublicBookings;
using TaxVision.Calendar.Domain.PublicBookings;
using TaxVision.Calendar.Tests.Domain;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>Los avisos que llevan un secreto lo llevan cifrado y atado a su emisión: copiado a otra no se descifra.</summary>
public sealed class PublicBookingNoticesTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Slot = new(2026, 11, 18, 14, 0, 0, DateTimeKind.Utc);

    private readonly JsonProtectedPayloadCodec _codec = new(
        new AesGcmRotatingSecretProtector(
            Options.Create(
                new RotatingSecretProtectionOptions
                {
                    MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    MasterKeyVersion = 1,
                }
            )
        )
    );
    private readonly Guid _host = Guid.NewGuid();

    [Fact]
    public void The_code_is_bound_to_its_send()
    {
        var (page, booking, code) = Pending();

        var evt = Notices().CodeRequested(page, booking, code);

        Assert.Equal("test", evt.CorrelationId);
        Assert.Equal("Book a live demo", evt.PageTitle);
        Assert.Equal(1, evt.Send);
        Assert.DoesNotContain(code, evt.ProtectedCode.Cipher);
        Assert.Equal(
            code,
            Unprotect(
                evt.ProtectedCode,
                PublicBookingPayloadSchemas.VerificationCode,
                PublicBookingPayloadSchemas.CodeBinding(booking.TenantId, booking.Id, 1)
            )
        );
        Assert.ThrowsAny<CryptographicException>(() =>
            Unprotect(
                evt.ProtectedCode,
                PublicBookingPayloadSchemas.VerificationCode,
                PublicBookingPayloadSchemas.CodeBinding(booking.TenantId, booking.Id, 2)
            )
        );
    }

    [Fact]
    public async Task The_confirmation_carries_both_parties_and_a_token_bound_to_its_version()
    {
        var (page, booking, code) = Pending();
        booking.VerifyCode(code, Now);
        var token = booking.Confirm(_host, Guid.NewGuid(), Now).Value;

        var evt = await Notices().ConfirmedAsync(page, booking, token, default);

        Assert.Equal(Slot, evt.StartUtc);
        Assert.Equal("jane@acme-tax.com", evt.Visitor.Email);
        Assert.Equal("https://meet.google.com/abc-defg-hij", evt.Host.MeetingUrl);
        // Sin disponibilidad cargada, el anfitrión ve la hora en la zona del visitante.
        Assert.Equal("America/New_York", evt.Host.TimeZoneId);
        Assert.Equal($"public-booking-{booking.Id:N}", evt.IcsUid);
        Assert.Equal([1440, 60], evt.IcsAlarmMinutes);
        Assert.Equal(
            token,
            Unprotect(
                evt.ProtectedManagementToken,
                PublicBookingPayloadSchemas.ManagementToken,
                PublicBookingPayloadSchemas.ManagementTokenBinding(booking.TenantId, booking.Id, 1)
            )
        );
    }

    [Fact]
    public async Task The_reminder_token_does_not_open_with_the_previous_version()
    {
        var (page, booking, code) = Pending();
        booking.VerifyCode(code, Now);
        booking.Confirm(_host, Guid.NewGuid(), Now);
        var token = booking.MarkReminderSent(24, Slot.AddHours(-24)).Value;

        var evt = await Notices().ReminderAsync(page, booking, 24, token, default);

        Assert.Equal(2, evt.ManagementTokenVersion);
        Assert.ThrowsAny<CryptographicException>(() =>
            Unprotect(
                evt.ProtectedManagementToken,
                PublicBookingPayloadSchemas.ManagementToken,
                PublicBookingPayloadSchemas.ManagementTokenBinding(booking.TenantId, booking.Id, 1)
            )
        );
    }

    private (BookingPage Page, PublicBooking Booking, string Code) Pending()
    {
        var page = BookingPageTests.NewPage();
        page.SetHosts([BookingPageTests.Host(_host)], Guid.NewGuid(), Now);
        page.SetHeardAboutOptions([BookingPageTests.Option("whatsapp")], Guid.NewGuid(), Now);
        var issued = PublicBooking
            .Request(page.TenantId, page.Id, PublicBookingTests.Data(), Slot, 30, _host, "key-1", true, 10, Now)
            .Value;
        return (page, issued.Booking, issued.Code!);
    }

    private PublicBookingNotices Notices() => new(new EmptyAvailability(), _codec, new FixedCorrelation());

    private string Unprotect(
        BuildingBlocks.Messaging.ClientFormsIntegrationEvents.ProtectedPayload payload,
        string schema,
        BuildingBlocks.Messaging.ClientFormsIntegrationEvents.ProtectedPayloadBinding binding
    ) => _codec.Unprotect<PublicBookingSecretPayload>(payload, schema, binding).Value;
}
