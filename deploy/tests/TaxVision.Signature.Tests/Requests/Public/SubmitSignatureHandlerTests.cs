using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Public;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Requests.Public;

/// <summary>SIG-BB1 (red): firmar emite un solo <c>DocumentSigned</c>, con o sin eventos de dominio.</summary>
public sealed class SubmitSignatureHandlerTests
{
    [Fact]
    public async Task SignDocument_EmitsExactlyOneDocumentSigned()
    {
        var (request, first, _) = NewInProgressWithTwoSigners();
        var bus = new CapturingBus();

        var result = await SubmitSignatureHandler.Handle(
            new SubmitSignatureCommand("token", SignatureCaptureMethod.Typed, "Signer One", null, "127.0.0.1", "tests"),
            new FixedToken(new SigningTokenPayload(request.TenantId, request.Id, first.Id, request.RevocationEpoch, request.ExpiresAtUtc, "jti")),
            new SingleRequestRepository(request),
            new NoDenylist(),
            new NoopUnitOfWork(),
            bus,
            new FixedCorrelation(),
            new NoopListCache(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var signed = Assert.Single(bus.Messages.OfType<DocumentSignedIntegrationEvent>());
        Assert.Equal(first.Id, signed.SignerId);
        Assert.False(signed.IsRequestCompleted);
        Assert.Empty(bus.Messages.OfType<SignatureRequestCompletedIntegrationEvent>());
    }

    private static (SignatureRequest Request, Signer First, Signer Second) NewInProgressWithTwoSigners()
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Engagement", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Engagement").Value;
        var first = AddSignerWithField(request, document.Id, "one@example.test", "Signer One");
        var second = AddSignerWithField(request, document.Id, "two@example.test", "Signer Two");
        Assert.True(
            request
                .AttachVerifiedDocument(
                    document.Id,
                    VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 1).Value
                )
                .IsSuccess
        );
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        return (request, first, second);
    }

    private static Signer AddSignerWithField(SignatureRequest request, Guid documentId, string email, string name)
    {
        var signer = request
            .AddSigner(TestSignatureLimits.Default, SignerEmail.Create(email).Value, SignerFullName.Create(name).Value, null)
            .Value;
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    documentId,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
        return signer;
    }

    private sealed class FixedToken(SigningTokenPayload payload) : ISigningTokenService
    {
        public string Issue(SigningTokenPayload p) => "token";

        public Result<SigningTokenPayload> Verify(string token) => Result.Success(payload);
    }

    private sealed class NoDenylist : IJtiDenylist
    {
        public Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default) => Task.FromResult(false);

        public Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class NoopListCache : ISignatureRequestListCacheInvalidator
    {
        public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
