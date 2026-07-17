using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Domain;

public sealed class SignatureRequestConsentAndSequentialTests
{
    // -------------------- Consent required --------------------

    [Fact]
    public void MarkSignerSigned_fails_when_consent_required_and_not_accepted()
    {
        var request = NewInProgressWithConsent();
        var signer = request.Signers.Single();

        var result = request.MarkSignerSigned(signer.Id, DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.ConsentRequired", result.Error.Code);
    }

    [Fact]
    public void AcceptSignerConsent_then_sign_succeeds()
    {
        var request = NewInProgressWithConsent();
        var signer = request.Signers.Single();

        var accept = request.AcceptSignerConsent(signer.Id, DateTime.UtcNow, "127.0.0.1", "ua/1.0");
        var sign = request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "ua/1.0");

        Assert.True(accept.IsSuccess);
        Assert.True(sign.IsSuccess);
        Assert.True(signer.HasAcceptedConsent);
        Assert.Equal(SignerStatus.Signed, signer.Status);
    }

    [Fact]
    public void AcceptSignerConsent_is_idempotent()
    {
        var request = NewInProgressWithConsent();
        var signer = request.Signers.Single();
        request.AcceptSignerConsent(signer.Id, DateTime.UtcNow, null, null);
        var acceptedAt = signer.ConsentAcceptedAtUtc;

        var second = request.AcceptSignerConsent(signer.Id, DateTime.UtcNow.AddSeconds(30), null, null);

        Assert.True(second.IsSuccess);
        Assert.Equal(acceptedAt, signer.ConsentAcceptedAtUtc);
    }

    [Fact]
    public void AcceptSignerConsent_fails_when_request_does_not_require_consent()
    {
        var request = NewInProgressWithoutConsent();
        var signer = request.Signers.Single();

        var result = request.AcceptSignerConsent(signer.Id, DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.ConsentNotRequired", result.Error.Code);
    }

    // -------------------- RecordSignerFirstView --------------------

    [Fact]
    public void RecordSignerFirstView_sets_only_once_and_is_idempotent()
    {
        var request = NewInProgressWithoutConsent();
        var signer = request.Signers.Single();
        var firstViewedAt = DateTime.UtcNow;

        request.RecordSignerFirstView(signer.Id, firstViewedAt, "1.2.3.4", "ua/1.0");
        request.RecordSignerFirstView(signer.Id, firstViewedAt.AddMinutes(5), "5.6.7.8", "ua/2.0");

        Assert.Equal(firstViewedAt, signer.FirstViewedAtUtc);
    }

    // -------------------- Sequential signing --------------------

    [Fact]
    public void MarkSignerSigned_fails_out_of_order_when_sequential()
    {
        var request = NewInProgressSequential();
        var second = request.Signers.First(s => s.Order == 2);

        var result = request.MarkSignerSigned(second.Id, DateTime.UtcNow, null, null);

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotYourTurn", result.Error.Code);
    }

    [Fact]
    public void MarkSignerSigned_in_order_succeeds_when_sequential()
    {
        var request = NewInProgressSequential();
        var first = request.Signers.First(s => s.Order == 1);
        var second = request.Signers.First(s => s.Order == 2);

        var r1 = request.MarkSignerSigned(first.Id, DateTime.UtcNow, null, null);
        var r2 = request.MarkSignerSigned(second.Id, DateTime.UtcNow, null, null);

        Assert.True(r1.IsSuccess);
        Assert.True(r2.IsSuccess);
        Assert.Equal(SignatureRequestStatus.Completed, request.Status);
    }

    // ================== helpers ==================

    private static SignatureRequest NewInProgressWithConsent()
    {
        var draft = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Consent Test",
                null,
                SignatureCategory.ConsentToDisclose,
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: true,
                generateCertificate: false
            )
            .Value;
        AddSignerAndReady(draft, "s@example.com");
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    private static SignatureRequest NewInProgressWithoutConsent()
    {
        var draft = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "No Consent",
                null,
                SignatureCategory.Fiscal,
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;
        AddSignerAndReady(draft, "s@example.com");
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    private static SignatureRequest NewInProgressSequential()
    {
        var draft = SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Sequential",
                null,
                SignatureCategory.Fiscal,
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: true,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;
        AddSignerWithField(draft, "one@example.com", "Signer One");
        AddSignerWithField(draft, "two@example.com", "Signer Two");
        var hash = DocumentHash.Create(new string('b', 64)).Value;
        draft.MarkReadyForSending(hash);
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    private static void AddSignerAndReady(SignatureRequest request, string email)
    {
        AddSignerWithField(request, email, "Some Name");
        var hash = DocumentHash.Create(new string('a', 64)).Value;
        request.MarkReadyForSending(hash);
    }

    private static void AddSignerWithField(SignatureRequest request, string email, string name)
    {
        var signer = request.AddSigner(SignerEmail.Create(email).Value, SignerFullName.Create(name).Value, null).Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        request.PlaceField(signer.Id, SignatureFieldKind.Signature, pos, null, false);
    }
}
