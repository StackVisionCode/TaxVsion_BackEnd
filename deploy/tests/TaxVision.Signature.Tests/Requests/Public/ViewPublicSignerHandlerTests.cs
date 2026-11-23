using System.Text.Json;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Requests.Public;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Requests.Public;

/// <summary>La vista pública no revela el orden, la posición ni cuántos firmantes hay.</summary>
public sealed class ViewPublicSignerHandlerTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task The_public_view_does_not_reveal_order_or_sequence()
    {
        var (request, first, _) = InOrderWithTwoSigners();

        var json = await ViewAsJson(request, first);

        Assert.False(json.TryGetProperty("order", out _));
        Assert.False(json.TryGetProperty("requiresSequentialSigning", out _));
        Assert.False(json.TryGetProperty("isSignerNextInSequence", out _));
    }

    [Fact]
    public async Task Only_the_current_signer_sees_the_document_as_ready()
    {
        var (request, first, second) = InOrderWithTwoSigners();

        Assert.True((await ViewAsJson(request, first)).GetProperty("isReadyForSigner").GetBoolean());
        Assert.False((await ViewAsJson(request, second)).GetProperty("isReadyForSigner").GetBoolean());
    }

    private static async Task<JsonElement> ViewAsJson(SignatureRequest request, Signer signer)
    {
        var result = await ViewPublicSignerHandler.Handle(
            new ViewPublicSignerCommand("token", "127.0.0.1", "tests"),
            new FixedToken(
                new SigningTokenPayload(request.TenantId, request.Id, signer.Id, request.RevocationEpoch, null, "jti")
            ),
            new SingleRequestRepository(request),
            new NoBranding(),
            new FixedStampStyle(),
            new NoDenylist(),
            new NoopUnitOfWork(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return JsonSerializer.SerializeToElement(result.Value, Web);
    }

    private static (SignatureRequest Request, Signer First, Signer Second) InOrderWithTwoSigners()
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, Guid.NewGuid(), Guid.NewGuid(), "Engagement", null, "Fiscal", 72, true, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Engagement").Value;
        var first = AddSigner(request, document.Id, "one");
        var second = AddSigner(request, document.Id, "two");
        request.AttachVerifiedDocument(
            document.Id,
            VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 1).Value
        );
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        return (request, first, second);
    }

    private static Signer AddSigner(SignatureRequest request, Guid documentId, string name)
    {
        var signer = request
            .AddSigner(
                TestSignatureLimits.Default,
                SignerEmail.Create($"{name}@example.test").Value,
                SignerFullName.Create($"Signer {name}").Value,
                null
            )
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

    private sealed class NoBranding : ITenantBrandingRefRepository
    {
        public Task<TenantBrandingRef?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantBrandingRef?>(null);

        public Task AddAsync(TenantBrandingRef branding, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TenantBrandingRef>> ListAllAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TenantLogoRef?> GetLogoAsync(
            Guid tenantId,
            TenantLogoSurface surface,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantLogoRef>> ListLogosAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddLogoAsync(TenantLogoRef logo, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedStampStyle : IStampStyleResolver
    {
        public Task<StampStyle> ResolveAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(new StampStyle(true, "MM/dd/yyyy", 8, 14));
    }

    private sealed class NoDenylist : IJtiDenylist
    {
        public Task<bool> IsRevokedAsync(string jti, CancellationToken ct = default) => Task.FromResult(false);

        public Task RevokeAsync(string jti, DateTime expiresAtUtc, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
