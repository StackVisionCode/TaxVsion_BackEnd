using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Profiles;
using TaxVision.Signature.Domain.Profiles;
using TaxVision.Signature.Tests.Delivery;
using TaxVision.Signature.Tests.Projections;

namespace TaxVision.Signature.Tests.Application;

/// <summary>
/// Una firma reutilizable cuya imagen CloudStorage no aceptó se quita (como Delete), solo si su FileId es
/// exactamente el del evento; nada más se toca.
/// </summary>
public sealed class SignatureProfileImageRejectedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task A_rejected_save_of_the_profile_image_removes_that_profile_only()
    {
        var rejected = Profile();
        var other = Profile();
        var store = new ProfileStore(rejected, other);
        var unitOfWork = new SavingUnitOfWork();

        await SignatureProfileImageRejectedConsumer.Handle(
            SaveRejected(rejected.FileId, SignatureStorage.RequestingService),
            store,
            unitOfWork,
            new FixedCorrelation(),
            NullLogger<SignatureProfile>.Instance,
            CancellationToken.None
        );

        Assert.Equal([other], store.Items);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task A_rejection_requested_by_another_service_is_ignored()
    {
        var profile = Profile();
        var store = new ProfileStore(profile);

        await SignatureProfileImageRejectedConsumer.Handle(
            SaveRejected(profile.FileId, "documents"),
            store,
            new SavingUnitOfWork(),
            new FixedCorrelation(),
            NullLogger<SignatureProfile>.Instance,
            CancellationToken.None
        );

        Assert.Single(store.Items);
    }

    [Fact]
    public async Task An_unknown_file_or_another_tenant_is_a_no_op()
    {
        var profile = Profile();
        var store = new ProfileStore(profile);
        var unitOfWork = new SavingUnitOfWork();

        await SignatureProfileImageRejectedConsumer.Handle(
            SaveRejected(Guid.NewGuid(), SignatureStorage.RequestingService),
            store,
            unitOfWork,
            new FixedCorrelation(),
            NullLogger<SignatureProfile>.Instance,
            CancellationToken.None
        );
        await SignatureProfileImageRejectedConsumer.Handle(
            SaveRejected(profile.FileId, SignatureStorage.RequestingService) with
            {
                TenantId = Guid.NewGuid(),
            },
            store,
            unitOfWork,
            new FixedCorrelation(),
            NullLogger<SignatureProfile>.Instance,
            CancellationToken.None
        );

        Assert.Single(store.Items);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_infected_profile_image_removes_the_profile()
    {
        var profile = Profile();
        var store = new ProfileStore(profile);

        await SignatureProfileImageRejectedConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent
            {
                TenantId = Tenant,
                FileId = profile.FileId,
                ObjectKey = "k",
                ScanReport = "Eicar",
            },
            store,
            new SavingUnitOfWork(),
            new FixedCorrelation(),
            NullLogger<SignatureProfile>.Instance,
            CancellationToken.None
        );

        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task A_blocked_profile_image_removes_the_profile()
    {
        var profile = Profile();
        var store = new ProfileStore(profile);

        await SignatureProfileImageRejectedConsumer.Handle(
            new FileBlockedByPolicyIntegrationEvent
            {
                TenantId = Tenant,
                FileId = profile.FileId,
                ObjectKey = "k",
                PolicyReason = "Explicit content",
                CreatedBy = Guid.NewGuid(),
            },
            store,
            new SavingUnitOfWork(),
            new FixedCorrelation(),
            NullLogger<SignatureProfile>.Instance,
            CancellationToken.None
        );

        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task A_failed_scan_of_the_profile_image_removes_the_profile()
    {
        var profile = Profile();
        var store = new ProfileStore(profile);

        await SignatureProfileImageRejectedConsumer.Handle(
            new FileScanFailedIntegrationEvent
            {
                TenantId = Tenant,
                FileId = profile.FileId,
                ObjectKey = "k",
                Reason = "content_type_mismatch",
            },
            store,
            new SavingUnitOfWork(),
            new FixedCorrelation(),
            NullLogger<SignatureProfile>.Instance,
            CancellationToken.None
        );

        Assert.Empty(store.Items);
    }

    private static SignatureProfile Profile() =>
        SignatureProfile.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), "My signature", Guid.NewGuid(), 300, 100).Value;

    private static FileSaveRejectedIntegrationEvent SaveRejected(Guid fileId, string requestingService) =>
        new()
        {
            TenantId = Tenant,
            FileId = fileId,
            RequestingService = requestingService,
            OwnerType = "Signature",
            FolderType = "Signatures",
            Reason = "UploadPolicy",
        };

    private sealed class ProfileStore(params SignatureProfile[] seed) : ISignatureProfileRepository
    {
        private readonly List<SignatureProfile> _store = [.. seed];
        public IReadOnlyList<SignatureProfile> Items => _store;

        public Task<SignatureProfile?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(p => p.TenantId == tenantId && p.FileId == fileId));

        public void Remove(SignatureProfile profile) => _store.Remove(profile);

        public Task<SignatureProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureProfile>> ListVisibleAsync(
            Guid tenantId,
            Guid userId,
            bool includePersonal,
            bool includeArchived,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<SignatureProfile>> ListByOwnerAsync(
            Guid tenantId,
            Guid? ownerUserId,
            bool includeArchived,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<SignatureProfile?> GetDefaultAsync(
            Guid tenantId,
            Guid? ownerUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(SignatureProfile profile, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
