using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Application.Requests.Commands.Update;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Application;

public sealed class UpdateSignatureRequestHandlerTests
{
    [Fact]
    public async Task Handle_updates_a_draft_and_invalidates_the_list()
    {
        var draft = Draft(generateCertificate: false);
        var repo = new FakeRepository(draft);
        var cache = new FakeCache();

        var result = await UpdateSignatureRequestHandler.Handle(
            Command(draft) with
            {
                Title = "Renamed 2026",
                Category = "ConsentToDisclose",
                TokenExpirationHours = 120,
            },
            repo,
            new FakeUnitOfWork(),
            cache,
            new FakeCategoryResolver(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed 2026", draft.Title);
        Assert.Equal("ConsentToDisclose", draft.Category);
        Assert.Equal(120, draft.TokenExpirationHours);
        Assert.Equal(draft.TenantId, cache.InvalidatedTenant);
    }

    [Fact]
    public async Task Handle_blocks_after_send()
    {
        var sent = ReadyWithField();
        sent.Send(DateTime.UtcNow);
        var repo = new FakeRepository(sent);

        var result = await UpdateSignatureRequestHandler.Handle(
            Command(sent),
            repo,
            new FakeUnitOfWork(),
            new FakeCache(),
            new FakeCategoryResolver(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotEditable", result.Error.Code);
    }

    [Fact]
    public async Task Handle_surfaces_certificate_without_generation_error()
    {
        var draft = Draft(generateCertificate: false);
        var repo = new FakeRepository(draft);

        var result = await UpdateSignatureRequestHandler.Handle(
            Command(draft) with
            {
                SendCertificateToSigners = true,
            },
            repo,
            new FakeUnitOfWork(),
            new FakeCache(),
            new FakeCategoryResolver(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.CertificateNotGenerated", result.Error.Code);
    }

    [Fact]
    public async Task Handle_returns_not_found_when_missing()
    {
        var repo = new FakeRepository(null);

        var result = await UpdateSignatureRequestHandler.Handle(
            new UpdateSignatureRequestCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "A valid title",
                null,
                "Fiscal",
                72,
                true,
                false,
                true,
                48
            ),
            repo,
            new FakeUnitOfWork(),
            new FakeCache(),
            new FakeCategoryResolver(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotFound", result.Error.Code);
    }

    // ---------- helpers ----------

    private static UpdateSignatureRequestCommand Command(SignatureRequest request) =>
        new(
            request.TenantId,
            request.Id,
            "A valid title",
            null,
            "Fiscal",
            72,
            SendSignedDocumentToSigners: true,
            SendCertificateToSigners: false,
            AutoRemindersEnabled: true,
            ReminderIntervalHours: 48
        );

    private static SignatureRequest Draft(bool generateCertificate = false) =>
        SignatureRequest
            .CreateDraft(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Consent 2026",
                null,
                "Fiscal",
                Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: generateCertificate
            )
            .Value;

    private static SignatureRequest ReadyWithField()
    {
        var request = Draft();
        var signer = request
            .AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Sam").Value, null)
            .Value;
        var position = FieldPosition.Create(1, 0.5, 0.5, 0.1, 0.05).Value;
        request.PlaceField(signer.Id, SignatureFieldKind.Signature, position, null, false);
        request.MarkReadyForSending(DocumentHash.Create(new string('a', 64)).Value);
        return request;
    }

    private sealed class FakeRepository(SignatureRequest? stored) : ISignatureRequestRepository
    {
        public Task<SignatureRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(stored is not null && stored.Id == requestId ? stored : null);

        public void Remove(SignatureRequest request) => throw new NotImplementedException();

        public Task AddAsync(SignatureRequest request, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<SignatureRequest?> GetBySealedFileIdAsync(
            Guid tenantId,
            Guid sealedFileId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<SignatureRequest?> GetByCertificateFileIdAsync(
            Guid tenantId,
            Guid certificateFileId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListDraftsWaitingForFileAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListStrandedDraftsAsync(
            DateTime createdBeforeUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListExpiredCandidatesAsync(
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListReminderCandidatesAsync(
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListPurgeCandidatesAsync(
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListStaleUnsentAsync(
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureRequest>> ListCompletedWithSealedFileAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeCategoryResolver : ISignatureCategoryResolver
    {
        // Devuelve la categoría tal cual (válida) — la validación real se prueba en el resolver.
        public Task<BuildingBlocks.Results.Result<string>> ResolveAsync(
            Guid tenantId,
            string category,
            CancellationToken ct = default
        ) => Task.FromResult(BuildingBlocks.Results.Result.Success(category));
    }

    private sealed class FakeCache : ISignatureRequestListCacheInvalidator
    {
        public Guid? InvalidatedTenant { get; private set; }

        public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
        {
            InvalidatedTenant = tenantId;
            return Task.CompletedTask;
        }
    }
}
