using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Projections.AuthEvents;
using TaxVision.Signature.Domain.Profiles;

namespace TaxVision.Signature.Tests.Application;

/// <summary>
/// Al retirar (offboard) a un empleado: se archivan sus perfiles de firma PERSONALES; los de OFICINA
/// y los de otros empleados no se tocan.
/// </summary>
public sealed class SignatureUserOffboardedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static SignatureProfile PersonalProfile(Guid ownerUserId) =>
        SignatureProfile.Create(Tenant, ownerUserId, ownerUserId, "My signature", Guid.NewGuid(), 300, 100).Value;

    private static SignatureProfile OfficeProfile() =>
        SignatureProfile.Create(Tenant, Guid.NewGuid(), null, "Office signature", Guid.NewGuid(), 300, 100).Value;

    private static Task Run(FakeProfileRepository profiles, FakeUnitOfWork unitOfWork, Guid leaver) =>
        SignatureUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                RemovedAtUtc = DateTime.UtcNow,
            },
            profiles,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SignatureProfile>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Personal_profiles_of_the_leaver_are_archived()
    {
        var leaver = Guid.NewGuid();
        var otherUser = Guid.NewGuid();
        var profiles = new FakeProfileRepository();
        var leaverA = PersonalProfile(leaver);
        var leaverB = PersonalProfile(leaver);
        var office = OfficeProfile();
        var otherPersonal = PersonalProfile(otherUser);
        profiles.Seed(leaverA);
        profiles.Seed(leaverB);
        profiles.Seed(office);
        profiles.Seed(otherPersonal);
        var unitOfWork = new FakeUnitOfWork();

        await Run(profiles, unitOfWork, leaver);

        Assert.True(leaverA.IsArchived);
        Assert.True(leaverB.IsArchived);
        Assert.False(office.IsArchived);
        Assert.False(otherPersonal.IsArchived);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task No_personal_profiles_is_a_noop()
    {
        var profiles = new FakeProfileRepository();
        profiles.Seed(OfficeProfile());
        var unitOfWork = new FakeUnitOfWork();

        await Run(profiles, unitOfWork, Guid.NewGuid());

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private sealed class FakeProfileRepository : ISignatureProfileRepository
    {
        private readonly List<SignatureProfile> _store = [];

        public void Seed(SignatureProfile profile) => _store.Add(profile);

        public Task<IReadOnlyList<SignatureProfile>> ListByOwnerAsync(
            Guid tenantId,
            Guid? ownerUserId,
            bool includeArchived,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<SignatureProfile>>(
                _store
                    .Where(p =>
                        p.TenantId == tenantId && p.OwnerUserId == ownerUserId && (includeArchived || !p.IsArchived)
                    )
                    .ToList()
            );

        public Task<SignatureProfile?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SignatureProfile>> ListVisibleAsync(
            Guid tenantId,
            Guid userId,
            bool includePersonal,
            bool includeArchived,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<SignatureProfile?> GetDefaultAsync(
            Guid tenantId,
            Guid? ownerUserId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task AddAsync(SignatureProfile profile, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public void Remove(SignatureProfile profile) => throw new NotImplementedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new NoOpScope();
        }

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
