using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Consumers;
using TaxVision.Customer.Domain.Employees;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Retiro con un import en curso: el aviso de fin pasa a quien ejecutó el retiro (o al sucesor) si está
/// activo; la procedencia (<c>CreatedByUserId</c>) no cambia y lo terminado no se toca.
/// </summary>
public sealed class CustomerImportsUserOffboardedConsumerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Leaver = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly Guid Successor = Guid.NewGuid();

    private static CustomerImportAttempt Attempt(Guid createdBy) =>
        CustomerImportAttempt
            .Create(Tenant, createdBy, $"key-{Guid.NewGuid():N}", DuplicateStrategy.Skip, ImportSourceKind.Csv, "c.csv")
            .Value;

    private static Task Run(InMemoryImports imports, Directory directory, Guid? offboardedBy, Guid? successor) =>
        CustomerImportsUserOffboardedConsumer.Handle(
            new UserOffboardedIntegrationEvent
            {
                TenantId = Tenant,
                UserId = Leaver,
                Email = "leaver@example.com",
                ActorType = "TenantEmployee",
                OffboardedByUserId = offboardedBy,
                SuccessorUserId = successor,
                RemovedAtUtc = DateTime.UtcNow,
            },
            imports,
            directory,
            new NoOpUnitOfWork(),
            new FixedCorrelationContext(),
            NullLogger<CustomerImportAttempt>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task The_completion_notice_of_an_import_in_flight_goes_to_whoever_offboarded()
    {
        var inFlight = Attempt(Leaver);
        inFlight.Start();
        var finished = Attempt(Leaver);
        finished.Start();
        finished.Complete();
        var others = Attempt(Guid.NewGuid());

        await Run(
            new InMemoryImports(inFlight, finished, others),
            new Directory((Admin, true), (Successor, true)),
            Admin,
            Successor
        );

        Assert.Equal(Admin, inFlight.NotifyUserId);
        Assert.Equal(Leaver, inFlight.CreatedByUserId);
        Assert.Equal(Leaver, finished.NotifyUserId);
        Assert.Null(others.FollowUpUserId);
    }

    [Fact]
    public async Task Falls_back_to_the_successor_when_whoever_offboarded_is_not_active()
    {
        var inFlight = Attempt(Leaver);

        await Run(new InMemoryImports(inFlight), new Directory((Admin, false), (Successor, true)), Admin, Successor);

        Assert.Equal(Successor, inFlight.NotifyUserId);
    }

    [Fact]
    public async Task Nobody_active_leaves_the_import_untouched()
    {
        var inFlight = Attempt(Leaver);

        await Run(new InMemoryImports(inFlight), new Directory(), offboardedBy: null, successor: Leaver);

        Assert.Null(inFlight.FollowUpUserId);
    }

    [Fact]
    public void A_finished_import_cannot_be_handed_over_and_handing_back_to_the_creator_clears_it()
    {
        var attempt = Attempt(Leaver);
        Assert.True(attempt.HandOverFollowUp(Admin).IsSuccess);
        Assert.True(attempt.HandOverFollowUp(Leaver).IsSuccess);
        Assert.Null(attempt.FollowUpUserId);

        attempt.Start();
        attempt.Complete();
        Assert.True(attempt.HandOverFollowUp(Admin).IsFailure);
    }

    private sealed class InMemoryImports(params CustomerImportAttempt[] attempts) : ICustomerImportRepository
    {
        public Task<CustomerImportAttempt?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(attempts.FirstOrDefault(a => a.Id == id));

        public Task<CustomerImportAttempt?> GetByIdempotencyKeyAsync(
            Guid tenantId,
            string idempotencyKey,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task AddAsync(CustomerImportAttempt attempt, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<CustomerImportAttempt>> ListInFlightNotifyingAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct
        ) =>
            Task.FromResult<IReadOnlyList<CustomerImportAttempt>>(
                attempts.Where(a => a.TenantId == tenantId && a.NotifyUserId == userId && !a.IsTerminal).ToList()
            );
    }

    private sealed class Directory(params (Guid UserId, bool IsActive)[] seed) : ITenantEmployeeDirectoryRepository
    {
        private readonly Dictionary<Guid, TenantEmployeeDirectoryEntry> _entries = seed.ToDictionary(
            s => s.UserId,
            s => TenantEmployeeDirectoryEntry.Create(s.UserId, Tenant, "TenantAdmin", s.IsActive)
        );

        public Task<TenantEmployeeDirectoryEntry?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_entries.GetValueOrDefault(userId));

        public Task UpsertAsync(
            Guid userId,
            Guid tenantId,
            string actorType,
            bool isActive,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task MarkActiveAsync(Guid userId, CancellationToken ct = default) => throw new NotImplementedException();

        public Task MarkInactiveAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task MarkOffboardedAsync(Guid userId, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
