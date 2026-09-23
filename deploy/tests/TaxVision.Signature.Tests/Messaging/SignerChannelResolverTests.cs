using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Messaging;

/// <summary>
/// P3 — el canal preferido que Signature estampa en los eventos de integración: SMS solo si el
/// firmante requiere verificación por SMS/WhatsApp Y tiene teléfono; en cualquier otro caso, Email.
/// </summary>
public sealed class SignerChannelResolverTests
{
    [Fact]
    public void Sms_when_sms_otp_and_phone_present()
    {
        var signer = NewSigner(SignerVerificationMethod.SmsOtp, "+17865550123");

        Assert.Equal("Sms", SignerChannelResolver.PreferredChannelFor(signer));
    }

    [Fact]
    public void Sms_when_whatsapp_otp_and_phone_present()
    {
        var signer = NewSigner(SignerVerificationMethod.WhatsAppOtp, "+17865550123");

        Assert.Equal("Sms", SignerChannelResolver.PreferredChannelFor(signer));
    }

    [Fact]
    public void Email_when_sms_otp_but_no_phone()
    {
        var signer = NewSigner(SignerVerificationMethod.SmsOtp, phone: null);

        Assert.Equal("Email", SignerChannelResolver.PreferredChannelFor(signer));
    }

    [Fact]
    public void Email_when_email_otp()
    {
        var signer = NewSigner(SignerVerificationMethod.EmailOtp, "+17865550123");

        Assert.Equal("Email", SignerChannelResolver.PreferredChannelFor(signer));
    }

    [Fact]
    public void Email_when_no_verification_method()
    {
        var signer = NewSigner(method: null, "+17865550123");

        Assert.Equal("Email", SignerChannelResolver.PreferredChannelFor(signer));
    }

    private static Signer NewSigner(SignerVerificationMethod? method, string? phone)
    {
        var phoneVo = phone is null ? null : SignerPhoneNumber.Create(phone).Value;
        var draft = SignatureRequest
            .CreateDraft(
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
        return draft
            .AddSigner(
                SignerEmail.Create("s@example.com").Value,
                SignerFullName.Create("The Signer").Value,
                mappedCustomerId: null,
                phoneNumber: phoneVo,
                language: "En",
                requiredVerificationMethod: method
            )
            .Value;
    }
}
