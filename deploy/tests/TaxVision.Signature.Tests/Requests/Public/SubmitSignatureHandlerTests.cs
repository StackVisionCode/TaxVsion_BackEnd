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

    [Fact]
    public async Task TypedNameMismatch_FailsBeforeCapturingFieldValues()
    {
        var (request, first, _) = NewInProgressWithTwoSigners(withTextField: true);
        var textField = first.Fields.Single(field => field.Kind == SignatureFieldKind.Text);
        var bus = new CapturingBus();

        var result = await SubmitAsync(
            request,
            first,
            bus,
            new SubmitSignatureCommand(
                "token",
                SignatureCaptureMethod.Typed,
                "Someone Else",
                null,
                "127.0.0.1",
                "tests",
                FieldValues: [new SubmitFieldValueDto(textField.Id, "Spouse")]
            )
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Public.TypedNameMismatch", result.Error.Code);
        Assert.Equal("Typed name must match the signer full name.", result.Error.Message);
        Assert.Empty(first.FieldValues);
        Assert.NotEqual(SignerStatus.Signed, first.Status);
        Assert.Empty(bus.Messages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task TypedNameEmpty_Fails(string? typedName)
    {
        var (request, first, _) = NewInProgressWithTwoSigners();

        var result = await SubmitAsync(
            request,
            first,
            new CapturingBus(),
            new SubmitSignatureCommand("token", SignatureCaptureMethod.Typed, typedName, null, "127.0.0.1", "tests")
        );

        Assert.Equal("Signature.Public.TypedNameEmpty", result.Error.Code);
    }

    [Fact]
    public async Task TypedName_IgnoresCaseAndOuterSpaces_AndKeepsWhatWasTyped()
    {
        var (request, first, _) = NewInProgressWithTwoSigners();

        var result = await SubmitAsync(
            request,
            first,
            new CapturingBus(),
            new SubmitSignatureCommand("token", SignatureCaptureMethod.Typed, "  sIGNER oNE ", null, "127.0.0.1", "tests")
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal("sIGNER oNE", first.TypedName);
    }

    [Theory]
    [InlineData(SignatureCaptureMethod.Drawn)]
    [InlineData(SignatureCaptureMethod.Uploaded)]
    public async Task ImageMethodWithoutImage_Fails(SignatureCaptureMethod method)
    {
        var (request, first, _) = NewInProgressWithTwoSigners();

        var result = await SubmitAsync(
            request,
            first,
            new CapturingBus(),
            new SubmitSignatureCommand("token", method, null, Guid.Empty, "127.0.0.1", "tests")
        );

        Assert.Equal("Signature.Public.ImageRequired", result.Error.Code);
    }

    [Fact]
    public async Task UnknownMethod_Fails()
    {
        var (request, first, _) = NewInProgressWithTwoSigners();

        var result = await SubmitAsync(
            request,
            first,
            new CapturingBus(),
            new SubmitSignatureCommand("token", (SignatureCaptureMethod)99, null, null, "127.0.0.1", "tests")
        );

        Assert.Equal("Signature.Public.UnknownMethod", result.Error.Code);
    }

    private static Task<Result> SubmitAsync(
        SignatureRequest request,
        Signer signer,
        CapturingBus bus,
        SubmitSignatureCommand command
    ) =>
        SubmitSignatureHandler.Handle(
            command,
            new FixedToken(
                new SigningTokenPayload(request.TenantId, request.Id, signer.Id, request.RevocationEpoch, request.ExpiresAtUtc, "jti")
            ),
            new SingleRequestRepository(request),
            new NoDenylist(),
            new NoopUnitOfWork(),
            bus,
            new FixedCorrelation(),
            new NoopListCache(),
            CancellationToken.None
        );

    private static (SignatureRequest Request, Signer First, Signer Second) NewInProgressWithTwoSigners(
        bool withTextField = false
    )
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Engagement", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Engagement").Value;
        var first = AddSignerWithField(request, document.Id, "one@example.test", "Signer One");
        var second = AddSignerWithField(request, document.Id, "two@example.test", "Signer Two");
        if (withTextField)
            Assert.True(
                request
                    .PlaceField(
                        TestSignatureLimits.MaxFieldsPerSigner,
                        first.Id,
                        document.Id,
                        SignatureFieldKind.Text,
                        FieldPosition.Create(1, 0.3, 0.3, 0.2, 0.05).Value,
                        null,
                        false
                    )
                    .IsSuccess
            );
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
