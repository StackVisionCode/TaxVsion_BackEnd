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

    // -------------------- Turno e invitación (firma con orden) --------------------

    [Fact]
    public void In_order_only_the_first_pending_signer_has_the_turn()
    {
        var request = NewInProgressSequential(signers: 3);

        var turn = request.CurrentTurnSigners();

        Assert.Equal([1], turn.Select(s => s.Order));
    }

    [Fact]
    public void Without_order_every_pending_signer_has_the_turn()
    {
        var request = NewInProgressSequential(signers: 3, sequential: false);

        Assert.Equal(3, request.CurrentTurnSigners().Count);
    }

    [Fact]
    public void When_the_first_signs_the_turn_moves_to_the_second()
    {
        var request = NewInProgressSequential(signers: 3);
        var first = request.Signers.Single(s => s.Order == 1);

        request.MarkSignerSigned(first.Id, DateTime.UtcNow, null, null);

        Assert.Equal([2], request.CurrentTurnSigners().Select(s => s.Order));
        Assert.True(request.IsSignersTurn(request.Signers.Single(s => s.Order == 2).Id));
        Assert.False(request.IsSignersTurn(request.Signers.Single(s => s.Order == 3).Id));
    }

    // El enlace emitido (CurrentTokenId) es la marca de "ya invitado": no se invita dos veces.
    [Fact]
    public void A_signer_already_invited_is_not_due_again()
    {
        var request = NewInProgressSequential(signers: 2);
        var first = request.Signers.Single(s => s.Order == 1);
        Assert.Equal([first.Id], request.SignersDueForInvitation().Select(s => s.Id));

        request.RotateSignerToken(first.Id, "jti-1");

        Assert.Empty(request.SignersDueForInvitation());
    }

    [Fact]
    public void Only_signers_with_a_link_count_as_invited()
    {
        var request = NewInProgressSequential(signers: 3);
        var first = request.Signers.Single(s => s.Order == 1);
        request.RotateSignerToken(first.Id, "jti-1");

        Assert.Equal([first.Id], request.InvitedPendingSigners().Select(s => s.Id));
    }

    // Un enlace viejo de alguien sin turno no puede anular la solicitud de todos ni aceptar por adelantado.
    [Fact]
    public void A_signer_out_of_turn_cannot_reject()
    {
        var request = NewInProgressSequential(signers: 2);
        var second = request.Signers.Single(s => s.Order == 2);

        var result = request.MarkSignerRejected(second.Id, DateTime.UtcNow, "no", null, null);

        Assert.Equal("Signature.Request.NotYourTurn", result.Error.Code);
        Assert.Equal(SignatureRequestStatus.InProgress, request.Status);
    }

    [Fact]
    public void A_signer_out_of_turn_cannot_accept_consent()
    {
        var request = NewInProgressSequential(signers: 2, consent: true);
        var second = request.Signers.Single(s => s.Order == 2);

        var result = request.AcceptSignerConsent(second.Id, DateTime.UtcNow, null, null);

        Assert.Equal("Signature.Request.NotYourTurn", result.Error.Code);
    }

    // ================== helpers ==================

    private static SignatureRequest NewInProgressWithConsent()
    {
        var draft = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Consent Test",
                null,
                "ConsentToDisclose",
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
                TestSignatureLimits.Default,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "No Consent",
                null,
                "Fiscal",
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

    private static SignatureRequest NewInProgressSequential(int signers, bool sequential = true, bool consent = false)
    {
        var draft = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Sequential",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: sequential,
                requiresConsent: consent,
                generateCertificate: false
            )
            .Value;
        for (var i = 1; i <= signers; i++)
            AddSignerWithField(draft, $"s{i}@example.com", $"Signer {i}");
        var hash = DocumentHash.Create(new string('b', 64)).Value;
        draft.AttachVerifiedDocument(draft.Documents[0].Id, VerifiedDocument.Create(hash, 50).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    private static SignatureRequest NewInProgressSequential()
    {
        var draft = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Sequential",
                null,
                "Fiscal",
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
        draft.AttachVerifiedDocument(draft.Documents[0].Id, VerifiedDocument.Create(hash, 50).Value);
        draft.Send(DateTime.UtcNow);
        return draft;
    }

    private static void AddSignerAndReady(SignatureRequest request, string email)
    {
        AddSignerWithField(request, email, "Some Name");
        var hash = DocumentHash.Create(new string('a', 64)).Value;
        request.AttachVerifiedDocument(request.Documents[0].Id, VerifiedDocument.Create(hash, 50).Value);
    }

    private static void AddSignerWithField(SignatureRequest request, string email, string name)
    {
        var signer = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create(email).Value,
                SignerFullName.Create(name).Value,
                null
            )
            .Value;
        var pos = FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value;
        request.PlaceField(
            TestSignatureLimits.MaxFieldsPerSigner,
            signer.Id,
            SignatureFieldKind.Signature,
            pos,
            null,
            false
        );
    }
}
