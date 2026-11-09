using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Onboarding.Application.Abstractions;
using TaxVision.Auth.Application.Terms.Versions.Consumers;
using TaxVision.Auth.Domain.Terms;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>
/// Documento legal rechazado por CloudStorage: la versión que lo referencia se retira (sin contenido ni
/// vigencia) y vuelve a regir la anterior; lo ajeno se ignora y un FileId desconocido no hace nada.
/// </summary>
public sealed class TermsContentSaveRejectedConsumerTests
{
    private static readonly string ValidHash = new('a', 64);

    [Fact]
    public async Task Rechazo_propio_retira_la_version_y_vuelve_a_regir_la_anterior()
    {
        var repository = new FakeTermsVersionRepository();
        var previous = repository.Seed("1.0", DateTime.UtcNow.AddDays(-10));
        var rejected = repository.Seed("2.0", DateTime.UtcNow.AddMinutes(-1));
        var rejectedFileId = rejected.ContentFileId!.Value;
        var unitOfWork = new OrderedUnitOfWork(new FakeMessageBus());

        await HandleAsync(repository, Rejected(rejectedFileId), unitOfWork);

        Assert.Null(rejected.ContentFileId);
        Assert.NotNull(rejected.EffectiveUntilUtc);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Same(
            previous,
            await repository.GetCurrentAsync(TermsKind.TermsOfService, "en-US", DateTime.UtcNow.AddSeconds(1))
        );

        // Redelivery: la versión ya soltó el archivo, no se vuelve a guardar.
        await HandleAsync(repository, Rejected(rejectedFileId), unitOfWork);
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task Rechazo_de_otro_archivo_no_toca_la_version_vigente()
    {
        var repository = new FakeTermsVersionRepository();
        var current = repository.Seed("2.0", DateTime.UtcNow.AddMinutes(-1));
        var contentFileId = current.ContentFileId;
        var unitOfWork = new OrderedUnitOfWork(new FakeMessageBus());

        await HandleAsync(repository, Rejected(Guid.NewGuid()), unitOfWork);

        Assert.Equal(contentFileId, current.ContentFileId);
        Assert.Null(current.EffectiveUntilUtc);
        Assert.Equal(0, unitOfWork.Saves);
    }

    [Theory]
    [InlineData("documents", "Tenant", "Templates", true)]
    [InlineData("auth", "User", "Avatars", true)]
    [InlineData("auth", "Tenant", "Templates", false)]
    public async Task Rechazo_ajeno_se_ignora_sin_consultar(
        string requestingService,
        string ownerType,
        string folderType,
        bool platformTenant
    )
    {
        var throwingRepository = StubProxy<ITermsVersionRepository>.Create((_, _) => (false, null));
        var unitOfWork = new OrderedUnitOfWork(new FakeMessageBus());

        await HandleAsync(
            throwingRepository,
            Rejected(Guid.NewGuid()) with
            {
                RequestingService = requestingService,
                OwnerType = ownerType,
                FolderType = folderType,
                TenantId = platformTenant ? PlatformTenant.Id : Guid.NewGuid(),
            },
            unitOfWork
        );

        Assert.Equal(0, unitOfWork.Saves);
    }

    [Fact]
    public async Task FileId_desconocido_es_no_op()
    {
        var unitOfWork = new OrderedUnitOfWork(new FakeMessageBus());

        await HandleAsync(new FakeTermsVersionRepository(), Rejected(Guid.NewGuid()), unitOfWork);

        Assert.Equal(0, unitOfWork.Saves);
    }

    private static FileSaveRejectedIntegrationEvent Rejected(Guid fileId) =>
        new()
        {
            TenantId = PlatformTenant.Id,
            FileId = fileId,
            RequestingService = "auth",
            OwnerType = "Tenant",
            OwnerId = null,
            FolderType = "Templates",
            Reason = "StorageQuota.Exceeded",
        };

    private static Task HandleAsync(
        ITermsVersionRepository repository,
        FileSaveRejectedIntegrationEvent msg,
        OrderedUnitOfWork unitOfWork
    ) =>
        TermsContentSaveRejectedConsumer.Handle(
            msg,
            repository,
            unitOfWork,
            new AvatarCorrelation(),
            NullLogger<TermsVersion>.Instance,
            CancellationToken.None
        );

    private sealed class FakeTermsVersionRepository : ITermsVersionRepository
    {
        private readonly List<TermsVersion> _all = [];

        public TermsVersion Seed(string version, DateTime effectiveFromUtc)
        {
            var published = TermsVersion
                .Publish(
                    TermsKind.TermsOfService,
                    version,
                    Guid.NewGuid(),
                    ValidHash,
                    "en-US",
                    Guid.NewGuid(),
                    effectiveFromUtc
                )
                .Value;
            _all.Add(published);
            return published;
        }

        public Task AddAsync(TermsVersion version, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TermsVersion?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<TermsVersion?> GetByContentFileIdAsync(Guid contentFileId, CancellationToken ct = default) =>
            Task.FromResult(_all.FirstOrDefault(v => v.ContentFileId == contentFileId));

        // Misma regla que el repositorio real.
        public Task<TermsVersion?> GetCurrentAsync(
            TermsKind kind,
            string locale,
            DateTime nowUtc,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _all.Where(v =>
                        v.Kind == kind
                        && v.Locale == locale
                        && v.EffectiveFromUtc <= nowUtc
                        && (v.EffectiveUntilUtc == null || v.EffectiveUntilUtc > nowUtc)
                    )
                    .OrderByDescending(v => v.EffectiveFromUtc)
                    .FirstOrDefault()
            );
    }
}
