using TaxVision.Signature.Domain.Platform;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Tests.Domain;

/// <summary>
/// Los techos llegan de afuera (plan + excepción) y el dominio los aplica: cantidad de documentos y
/// firmantes, horas del enlace, canales del código, tope de campos; y lo que eligió la oficina se acota a
/// ellos al leerlo.
/// </summary>
public sealed class SignatureLimitsTests
{
    private static SignatureLimits Limits(
        int documents = 20,
        int signers = 50,
        int tokenHours = 720,
        VerificationChannel channels = VerificationChannel.Email | VerificationChannel.Sms
    ) => SignatureLimits.Create(documents, signers, 25L * 1024 * 1024, 100, tokenHours, 7, false, channels);

    [Fact]
    public void A_request_cannot_take_more_documents_or_signers_than_the_plan_sells()
    {
        var limits = Limits(documents: 1, signers: 1);
        var request = Draft(limits);

        var secondDocument = request.AddDocument(limits, Guid.NewGuid(), "Second");
        request.AddSigner(limits, Email("a@example.com"), Name("Ann Lee"), null);
        var secondSigner = request.AddSigner(limits, Email("b@example.com"), Name("Bob Ray"), null);

        Assert.Equal("Signature.Request.TooManyDocuments", secondDocument.Error.Code);
        Assert.Equal("Signature.Request.TooManySigners", secondSigner.Error.Code);
    }

    [Fact]
    public void The_token_ceiling_applies_on_every_path()
    {
        var limits = Limits(tokenHours: 48);

        var tooLong = SignatureRequest.CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Engagement", null, "Other", 72, false, false, false);
        var request = Draft(limits);
        var update = request.UpdateMetadata(limits, "Engagement", null, "Other", 49);
        var enable = request.EnableExpiration(limits, 100);

        Assert.Equal("Signature.Request.TokenExpiration", tooLong.Error.Code);
        Assert.Equal("Signature.Request.TokenExpiration", update.Error.Code);
        Assert.Equal("Signature.Request.TokenExpiration", enable.Error.Code);
    }

    [Fact]
    public void A_signer_can_only_require_a_channel_the_office_has()
    {
        var limits = Limits(channels: VerificationChannel.Email);
        var request = Draft(limits);

        var sms = request.AddSigner(limits, Email("a@example.com"), Name("Ann Lee"), null, requiredVerificationMethod: SignerVerificationMethod.SmsOtp);
        var email = request.AddSigner(limits, Email("b@example.com"), Name("Bob Ray"), null, requiredVerificationMethod: SignerVerificationMethod.EmailOtp);

        Assert.Equal("Signature.Signer.ChannelNotAllowed", sms.Error.Code);
        Assert.True(email.IsSuccess);
    }

    [Fact]
    public void A_signer_holds_at_most_the_platform_number_of_fields()
    {
        var limits = Limits();
        var request = Draft(limits);
        var signer = request.AddSigner(limits, Email("a@example.com"), Name("Ann Lee"), null).Value;
        var position = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;

        var first = request.PlaceField(1, signer.Id, request.Documents[0].Id, SignatureFieldKind.Signature, position, null, true);
        var second = request.PlaceField(1, signer.Id, request.Documents[0].Id, SignatureFieldKind.Date, position, null, true);

        Assert.True(first.IsSuccess);
        Assert.Equal("Signature.Request.TooManyFields", second.Error.Code);
    }

    [Fact]
    public void A_null_exception_inherits_the_plan_and_a_value_replaces_it()
    {
        var plan = Limits(documents: 20, signers: 50);
        var exception = SignaturePlanConstraints.Create(40, null, null, null, null, null, null, null).Value;

        var ceilings = exception.ApplyTo(plan);

        Assert.Equal(40, ceilings.MaxDocumentsPerRequest);
        Assert.Equal(50, ceilings.MaxSignersPerRequest);
        Assert.Equal(plan.MaxTokenHours, SignaturePlanConstraints.None().ApplyTo(plan).MaxTokenHours);
    }

    [Fact]
    public void What_the_office_chose_is_clamped_to_a_lower_plan_when_read()
    {
        var settings = TenantSignatureSettings.CreateForNewTenant(Guid.NewGuid(), "secret").Value;
        settings.ChangeDefaultTokenExpiration(500, Limits());
        settings.ReplaceRetentionPolicy(RetentionPolicy.Default().WithYears(3).Value.WithPurgeAllowed());
        settings.AllowVerificationChannel(VerificationChannel.WhatsApp);
        settings.SetDefaultVerificationChannel(VerificationChannel.WhatsApp);

        var lowerPlan = SignatureLimits.Create(20, 50, 10L * 1024 * 1024, 30, 72, 7, false, VerificationChannel.Email);
        var effective = settings.EffectiveWithin(lowerPlan);

        Assert.Equal(72, effective.DefaultTokenHours);
        Assert.Equal(7, effective.RetentionYears);
        Assert.False(effective.PurgeEnabled);
        Assert.Equal(10L * 1024 * 1024, effective.ForRequests.MaxPdfBytes);
        Assert.Equal(30, effective.ForRequests.MaxPagesPerDocument);
        Assert.Equal(VerificationChannel.Email, effective.ForRequests.AllowedChannels);
        Assert.Equal(VerificationChannel.Email, effective.DefaultChannel);
    }

    [Fact]
    public void The_lockout_follows_the_platform_attempts()
    {
        var limits = Limits();
        var request = Draft(limits);
        var signer = request.AddSigner(limits, Email("a@example.com"), Name("Ann Lee"), null).Value;
        var position = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        request.PlaceField(10, signer.Id, request.Documents[0].Id, SignatureFieldKind.Signature, position, null, true);
        request.SetPractitionerPin("stored-hash", Guid.NewGuid(), DateTime.UtcNow);
        request.AttachVerifiedDocument(
            request.Documents[0].Id,
            VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
        );
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        var strict = new VerificationPolicy(6, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(5));
        var now = DateTime.UtcNow;

        request.VerifySignerWithPin(signer.Id, false, now, null, null, strict);
        request.VerifySignerWithPin(signer.Id, false, now, null, null, strict);

        Assert.True(signer.IsPinLockedAt(now.AddMinutes(4)));
        Assert.False(signer.IsPinLockedAt(now.AddMinutes(6)));
    }

    [Theory]
    [InlineData(3, 10)] // código demasiado corto
    [InlineData(6, 0)] // vida del código fuera de rango
    public void Platform_settings_reject_values_out_of_range(int otpLength, int otpLifetimeMinutes)
    {
        var result = SignaturePlatformSettings
            .Defaults()
            .Update(otpLength, otpLifetimeMinutes, 30, 5, 30, 1_500_000, 10, 20, 20, 200, 6, 12, Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal(SignaturePlatformSettingsErrors.OutOfRange, result.Error);
    }

    private static SignatureRequest Draft(SignatureLimits limits)
    {
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Engagement", null, "Other", 24, false, false, false)
            .Value;
        request.AddDocument(limits, Guid.NewGuid(), "Engagement letter");
        return request;
    }

    private static SignerEmail Email(string raw) => SignerEmail.Create(raw).Value;

    private static SignerFullName Name(string raw) => SignerFullName.Create(raw).Value;
}
