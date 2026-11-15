using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using Xunit;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// P8: la solicitud congela al enviarse la zona de la oficina; el campo Date del PDF firmado es la fecha civil en esa
/// zona (21:00 de Chicago el 12-nov ya no sale "11/13"). Los sellos UTC no cambian.
/// </summary>
public sealed class SignatureRequestDisplayZoneTests
{
    [Fact]
    public void Sending_freezes_the_office_zone()
    {
        var request = Draft();

        Assert.True(request.Send(DateTime.UtcNow, "America/Chicago").IsSuccess);

        Assert.Equal("America/Chicago", request.DisplayTimeZoneId);
    }

    [Fact]
    public void An_invalid_zone_is_not_frozen()
    {
        var request = Draft();

        request.Send(DateTime.UtcNow, "Mars/Olympus");

        Assert.Null(request.DisplayTimeZoneId);
    }

    [Fact]
    public void The_date_field_is_the_civil_date_in_the_frozen_zone()
    {
        // 21:00 del 12-nov en Chicago (CST, UTC-6) = 03:00Z del 13.
        var signedAt = new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 11, 12), SealedFieldDates.DateFor(signedAt, "America/Chicago"));
    }

    /// <summary>Una solicitud enviada antes no tiene zona: el campo conserva el día UTC de siempre.</summary>
    [Fact]
    public void Without_a_frozen_zone_the_date_field_keeps_the_utc_day()
    {
        Assert.Equal(
            new DateOnly(2026, 11, 13),
            SealedFieldDates.DateFor(new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc), null)
        );
    }

    [Fact]
    public void New_certificates_say_in_which_zone_the_dates_are()
    {
        Assert.Equal(
            "Chicago time, GMT-6",
            SealedFieldDates.CertificateZone("America/Chicago", new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc))
        );
        Assert.Null(SealedFieldDates.CertificateZone(null, DateTime.UtcNow));
    }

    private static SignatureRequest Draft()
    {
        var draft = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Test",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;
        var signer = draft
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create("s@example.com").Value,
                SignerFullName.Create("The Signer").Value,
                null
            )
            .Value;
        draft.PlaceField(
            TestSignatureLimits.MaxFieldsPerSigner,
            signer.Id,
            SignatureFieldKind.Signature,
            FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
            null,
            false
        );
        draft.AttachVerifiedDocument(
            draft.Documents[0].Id,
            VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 50).Value
        );
        return draft;
    }
}
