using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Offboarding;
using TaxVision.Signature.Application.Requests.Preparer;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Application.Offboarding;

/// <summary>
/// Retiro de un empleado en Signature (D-2): borradores al sucesor o descartados, preparador quitado de
/// los borradores con aviso al autor, y preparador de las enviadas al sucesor o aviso a los admins.
/// </summary>
public sealed class SignatureRequestsUserOffboardedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private readonly Guid _leaver = Guid.NewGuid();
    private readonly Guid _successor = Guid.NewGuid();
    private readonly InMemoryStaffWork _staffWork = new();
    private readonly RemovalRecorder _requests = new();
    private readonly InMemoryMembers _members = new();
    private readonly InMemoryProfiles _profiles = new();
    private readonly ChainAudit _audit = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly RecordingListCache _listCache = new();
    private readonly CapturingBus _bus = new();

    private Task Offboard(Guid? successor) =>
        SignatureRequestsUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = _leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                SuccessorUserId = successor,
                RemovedAtUtc = DateTime.UtcNow,
            },
            _staffWork,
            _requests,
            _members,
            _profiles,
            new PreparerReassigner(
                _staffWork,
                _members,
                _profiles,
                new ProfileBackedEffectiveSignature(_profiles),
                _audit,
                PreparerRequests.Codes
            ),
            PreparerRequests.Codes,
            _unitOfWork,
            _listCache,
            _bus,
            new FixedCorrelation(),
            NullLogger<SignatureRequest>.Instance,
            CancellationToken.None
        );

    private void SuccessorCanPrepare()
    {
        _members.Items.Add(PreparerRequests.Member(Tenant, _successor));
        _staffWork.Identities[_successor] = new PreparerIdentitySnapshot("P99887766", "John Roe, CPA", null);
    }

    private SignatureRequest Track(SignatureRequest request)
    {
        _staffWork.Requests.Add(request);
        return request;
    }

    private IEnumerable<SignaturePreparerAttentionRequiredIntegrationEvent> Notices() =>
        _bus.Messages.OfType<SignaturePreparerAttentionRequiredIntegrationEvent>();

    [Fact]
    public async Task Drafts_of_the_leaver_go_to_the_successor_and_sent_ones_keep_their_author()
    {
        SuccessorCanPrepare();
        var draft = Track(PreparerRequests.Draft(Tenant, _leaver));
        var sent = Track(PreparerRequests.Sent(Tenant, _leaver, preparerId: null));

        await Offboard(_successor);

        Assert.Equal(_successor, draft.CreatedByUserId);
        Assert.Equal(_leaver, sent.CreatedByUserId);
        Assert.Empty(_requests.Removed);
        Assert.Equal(1, _listCache.Invalidations);
    }

    [Fact]
    public async Task Without_a_successor_plain_drafts_are_discarded_and_the_rest_is_reported()
    {
        var plain = Track(PreparerRequests.Draft(Tenant, _leaver));
        var held = Track(PreparerRequests.Draft(Tenant, _leaver));
        held.PlaceLegalHold(Guid.NewGuid(), "Subpoena 42");

        await Offboard(successor: null);

        Assert.Contains(plain, _requests.Removed);
        Assert.DoesNotContain(held, _requests.Removed);
        var notice = Assert.Single(Notices());
        Assert.Equal(held.Id, notice.SignatureRequestId);
        Assert.Equal(SignaturePreparerAttentionReasons.UnsentRequestKept, notice.Reason);
        Assert.Null(notice.RecipientUserId);
        Assert.Equal("signature.request.manage", notice.AudiencePermission);
    }

    [Fact]
    public async Task A_draft_of_someone_else_loses_the_leaver_as_preparer_and_its_author_is_told()
    {
        var author = Guid.NewGuid();
        var leaverSignature = _profiles.Add(Tenant, _leaver);
        var draft = Track(PreparerRequests.DraftWithPreparer(Tenant, author, _leaver, leaverSignature.FileId));

        await Offboard(_successor);

        Assert.Null(draft.Preparer);
        Assert.Null(draft.PreparerSignatureFileId);
        var notice = Assert.Single(Notices());
        Assert.Equal(SignaturePreparerAttentionReasons.PreparerCleared, notice.Reason);
        Assert.Equal(author, notice.RecipientUserId);
    }

    [Fact]
    public async Task The_office_signature_stays_on_a_draft_whose_preparer_left()
    {
        var office = _profiles.Add(Tenant, ownerUserId: null);
        var draft = Track(PreparerRequests.DraftWithPreparer(Tenant, Guid.NewGuid(), _leaver, office.FileId));

        await Offboard(successor: null);

        Assert.Null(draft.Preparer);
        Assert.Equal(office.FileId, draft.PreparerSignatureFileId);
    }

    [Fact]
    public async Task A_sent_request_passes_to_a_successor_with_identity_and_the_change_is_audited()
    {
        SuccessorCanPrepare();
        var sent = Track(PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver));

        await Offboard(_successor);

        Assert.Equal(_successor, sent.Preparer!.UserId);
        Assert.Contains(
            _audit.Appended,
            entry => entry.RequestId == sent.Id && entry.Kind == SignatureAuditEventKind.PreparerChanged
        );
        var changed = Assert.Single(_bus.Messages.OfType<SignaturePreparerChangedIntegrationEvent>());
        Assert.Equal(SignaturePreparerChangeReasons.Offboarding, changed.Reason);
        Assert.Null(changed.ChangedByUserId);
        Assert.Empty(Notices());
        Assert.True(_unitOfWork.Saves >= 1);
    }

    [Fact]
    public async Task A_sent_request_without_a_suitable_successor_alerts_the_admins_instead_of_staying_stuck()
    {
        _members.Items.Add(PreparerRequests.Member(Tenant, _successor)); // activo pero sin PTIN declarado
        var noIdentity = Track(PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver));

        await Offboard(_successor);

        Assert.Equal(_leaver, noIdentity.Preparer!.UserId);
        var notice = Assert.Single(Notices());
        Assert.Equal(SignaturePreparerAttentionReasons.PreparerChangeBlocked, notice.Reason);
        Assert.Null(notice.RecipientUserId);
        Assert.Empty(_audit.Appended);
    }

    [Fact]
    public async Task Without_successor_every_pending_preparer_signature_is_reported()
    {
        Track(PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver));
        Track(PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver));

        await Offboard(successor: null);

        Assert.Equal(2, Notices().Count(n => n.Reason == SignaturePreparerAttentionReasons.PreparerChangeBlocked));
    }

    [Fact]
    public async Task A_preparer_signature_already_given_is_never_touched()
    {
        SuccessorCanPrepare();
        var signed = Track(PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver));
        signed.MarkPreparerSigned(_leaver, DateTime.UtcNow, null, null);

        await Offboard(_successor);

        Assert.Equal(_leaver, signed.Preparer!.UserId);
        Assert.Equal(_leaver, signed.PreparerSignedByUserId);
        Assert.Empty(_bus.Messages);
        Assert.Empty(_audit.Appended);
    }

    [Fact]
    public async Task Redelivery_does_not_repeat_what_was_already_resolved()
    {
        SuccessorCanPrepare();
        Track(PreparerRequests.Draft(Tenant, _leaver));
        Track(PreparerRequests.DraftWithPreparer(Tenant, Guid.NewGuid(), _leaver));
        Track(PreparerRequests.Sent(Tenant, Guid.NewGuid(), _leaver));

        await Offboard(_successor);
        var firstMessages = _bus.Messages.Count;
        var firstAudits = _audit.Appended.Count;
        await Offboard(_successor);

        Assert.Equal(firstMessages, _bus.Messages.Count);
        Assert.Equal(firstAudits, _audit.Appended.Count);
    }

    [Fact]
    public async Task A_successor_known_to_be_inactive_is_ignored()
    {
        _members.Items.Add(PreparerRequests.Member(Tenant, _successor, active: false));
        var draft = Track(PreparerRequests.Draft(Tenant, _leaver));

        await Offboard(_successor);

        Assert.Contains(draft, _requests.Removed);
    }

    /// <summary>Solo interesa qué se borra; el resto del repositorio no se usa en el retiro.</summary>
    private sealed class RemovalRecorder : ISignatureRequestRepository
    {
        public List<SignatureRequest> Removed { get; } = [];

        public void Remove(SignatureRequest request) => Removed.Add(request);

        public Task<SignatureRequest?> GetByIdAsync(Guid tenantId, Guid requestId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SignatureRequest?> GetByExternalReferenceAsync(
            Guid tenantId,
            string sourceService,
            string sourceId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<SignatureRequest?> GetBySealedFileIdAsync(
            Guid tenantId,
            Guid sealedFileId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<SignatureRequest?> GetByCertificateFileIdAsync(
            Guid tenantId,
            Guid certificateFileId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureRequest>> ListDraftsWaitingForFileAsync(
            Guid tenantId,
            Guid fileId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureRequest>> ListStrandedDraftsAsync(
            DateTime createdBeforeUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureRequest>> ListExpiredCandidatesAsync(
            DateTime nowUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureRequest>> ListStaleUnsentAsync(
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureRequest>> ListReminderCandidatesAsync(
            DateTime nowUtc,
            int maxRemindersPerRequest,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureRequest>> ListPurgeCandidatesAsync(
            Guid tenantId,
            DateTime olderThanUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureRequest>> ListCompletedWithSealedFileAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureRequest>> ListCompletedWithSeveralCustomerSignersAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(SignatureRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureRequest>> ListScheduledReadyToSendAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
