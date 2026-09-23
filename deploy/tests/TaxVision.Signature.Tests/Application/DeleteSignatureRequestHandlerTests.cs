using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Commands.Delete;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Application;

public sealed class DeleteSignatureRequestHandlerTests
{
    [Fact]
    public async Task Handle_deletes_an_unsent_draft_and_invalidates_the_list()
    {
        var draft = Draft();
        var repo = new FakeRepository(draft);
        var cache = new FakeCache();

        var result = await DeleteSignatureRequestHandler.Handle(
            new DeleteSignatureRequestCommand(draft.TenantId, draft.Id),
            repo,
            new FakeUnitOfWork(),
            cache,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Contains(draft, repo.Removed);
        Assert.Equal(draft.TenantId, cache.InvalidatedTenant);
    }

    [Fact]
    public async Task Handle_blocks_a_sent_request()
    {
        var sent = ReadyWithField();
        sent.Send(DateTime.UtcNow);
        var repo = new FakeRepository(sent);

        var result = await DeleteSignatureRequestHandler.Handle(
            new DeleteSignatureRequestCommand(sent.TenantId, sent.Id),
            repo,
            new FakeUnitOfWork(),
            new FakeCache(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotDeletable", result.Error.Code);
        Assert.Empty(repo.Removed);
    }

    [Fact]
    public async Task Handle_returns_not_found_when_missing()
    {
        var repo = new FakeRepository(null);

        var result = await DeleteSignatureRequestHandler.Handle(
            new DeleteSignatureRequestCommand(Guid.NewGuid(), Guid.NewGuid()),
            repo,
            new FakeUnitOfWork(),
            new FakeCache(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotFound", result.Error.Code);
    }

    // ---------- helpers ----------

    private static SignatureRequest Draft() =>
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
                generateCertificate: false
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
        public List<SignatureRequest> Removed { get; } = [];

        public Task<SignatureRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(stored is not null && stored.Id == requestId ? stored : null);

        public void Remove(SignatureRequest request) => Removed.Add(request);

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
