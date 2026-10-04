using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Application.Requests.Commands.UpsertDraft;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Tests.Application;

public sealed class UpsertSignatureDraftHandlerTests
{
    [Fact]
    public async Task Rejects_when_version_is_stale()
    {
        var draft = NewDraftWithOneSigner();
        var repo = new FakeRepository(draft);

        var result = await UpsertSignatureDraftHandler.Handle(
            NewCommand(draft) with
            {
                ExpectedUpdatedAtUtc = draft.UpdatedAtUtc.AddSeconds(-1),
            },
            repo,
            new FakeProjection(),
            new FakeCategoryResolver(),
            new FakeUnitOfWork(),
            new FakeCache(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.VersionConflict", result.Error.Code);
    }

    [Fact]
    public async Task Adds_a_new_signer_and_a_new_field()
    {
        var draft = NewDraft();
        var repo = new FakeRepository(draft);

        var result = await UpsertSignatureDraftHandler.Handle(
            NewCommand(draft) with
            {
                Signers = new[] { new DraftSignerSpec(null, "a@example.com", "Alice A", null, "En", null) },
                Fields = new[]
                {
                    new DraftFieldSpec(null, 0, SignatureFieldKind.Signature, 1, 0.1, 0.1, 0.2, 0.05, null, true),
                },
            },
            repo,
            new FakeProjection(),
            new FakeCategoryResolver(),
            new FakeUnitOfWork(),
            new FakeCache(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(draft.Signers);
        Assert.Single(draft.Signers.First().Fields);
    }

    [Fact]
    public async Task Removes_signers_not_in_payload()
    {
        var draft = NewDraftWithOneSigner();
        var existingSigner = draft.Signers.Single();
        var repo = new FakeRepository(draft);

        var result = await UpsertSignatureDraftHandler.Handle(
            NewCommand(draft) with
            {
                Signers = Array.Empty<DraftSignerSpec>(),
                Fields = Array.Empty<DraftFieldSpec>(),
            },
            repo,
            new FakeProjection(),
            new FakeCategoryResolver(),
            new FakeUnitOfWork(),
            new FakeCache(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(draft.Signers);
        _ = existingSigner;
    }

    [Fact]
    public async Task NotFound_when_request_does_not_exist()
    {
        var repo = new FakeRepository(null);
        var result = await UpsertSignatureDraftHandler.Handle(
            NewCommand(TenantId: Guid.NewGuid(), SignatureRequestId: Guid.NewGuid()),
            repo,
            new FakeProjection(),
            new FakeCategoryResolver(),
            new FakeUnitOfWork(),
            new FakeCache(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Signature.Request.NotFound", result.Error.Code);
    }

    // ============== Fábricas ==============

    private static SignatureRequest NewDraft() =>
        SignatureRequest
            .CreateDraft(
                tenantId: Guid.NewGuid(),
                createdByUserId: Guid.NewGuid(),
                title: "Autosave test",
                description: null,
                category: "ConsentToDisclose",
                originalFileId: Guid.NewGuid(),
                tokenExpirationHours: 72,
                requiresSequentialSigning: false,
                requiresConsent: false,
                generateCertificate: false
            )
            .Value;

    private static SignatureRequest NewDraftWithOneSigner()
    {
        var draft = NewDraft();
        draft.AddSigner(SignerEmail.Create("s@example.com").Value, SignerFullName.Create("Sam").Value, null);
        return draft;
    }

    private static UpsertSignatureDraftCommand NewCommand(SignatureRequest draft) =>
        NewCommand(draft.TenantId, draft.Id, draft.UpdatedAtUtc);

    private static UpsertSignatureDraftCommand NewCommand(
        Guid TenantId,
        Guid SignatureRequestId,
        DateTime? expectedUpdatedAt = null
    ) =>
        new(
            TenantId,
            SignatureRequestId,
            expectedUpdatedAt,
            Title: "Autosave test",
            Description: null,
            Category: "ConsentToDisclose",
            TokenExpirationHours: 72,
            SendSignedDocumentToSigners: null,
            SendCertificateToSigners: null,
            AutoRemindersEnabled: null,
            ReminderIntervalHours: null,
            Signers: Array.Empty<DraftSignerSpec>(),
            Fields: Array.Empty<DraftFieldSpec>()
        );

    // ============== Fakes ==============

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

        public Task<IReadOnlyList<SignatureRequest>> ListScheduledReadyToSendAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class FakeProjection : ICustomerEmailProjectionRepository
    {
        public Task<CustomerEmailProjection?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerEmailProjection?>(null);

        public Task<CustomerEmailProjection?> FindActiveByEmailAsync(
            Guid tenantId,
            string normalizedEmail,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerEmailProjection?>(null);

        public Task AddAsync(CustomerEmailProjection projection, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class FakeCategoryResolver : ISignatureCategoryResolver
    {
        public Task<Result<string>> ResolveAsync(Guid tenantId, string category, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(category));
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeCache : ISignatureRequestListCacheInvalidator
    {
        public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
