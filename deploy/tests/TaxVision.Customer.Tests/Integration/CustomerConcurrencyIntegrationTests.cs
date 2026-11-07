using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.Archive;
using TaxVision.Customer.Application.Customers.Commands.SetRelationFiscalProfile;
using TaxVision.Customer.Application.Employees.Consumers;
using TaxVision.Customer.Domain.Addresses;
using TaxVision.Customer.Domain.ContactPoints;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Employees;
using TaxVision.Customer.Domain.FiscalProfiles;
using TaxVision.Customer.Domain.Relations;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.Transports.Tcp;
using Xunit;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// CUS-1 contra SQL Server, en base temporal: dos escrituras desde lecturas viejas no se pisan, todo cambio en
/// hijos mueve la RowVersion de la raíz y el retiro de empleados sobrevive a un conflicto sin quedar en bucle.
/// </summary>
public sealed class CustomerConcurrencyIntegrationTests(CustomerTemporaryDatabase database)
    : IClassFixture<CustomerTemporaryDatabase>
{
    private const string ConflictCode = "Persistence.ConcurrencyConflict";
    private static readonly Guid Actor = Guid.NewGuid();
    private readonly Guid _tenant = Guid.NewGuid();

    [Fact]
    public async Task OffboardingSweepAndAdmin_AssignSamePreparer_SecondSaveConflicts()
    {
        var (leaving, successor, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var id = await SeedAsync(customer => customer.AssignPreparer(leaving, Actor));

        await using var sweep = database.NewContext(_tenant);
        await using var admin = database.NewContext(_tenant);
        var first = await LoadAsync(sweep, id);
        var second = await LoadAsync(admin, id);
        Assert.True(first.HandOverPreparer(leaving, successor, Actor).IsSuccess);
        Assert.True(second.AssignPreparer(other, Actor).IsSuccess);

        await sweep.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => admin.SaveChangesAsync());

        Assert.Equal(ConflictCode, conflict.Code);
        Assert.Equal(successor, await PreparerAsync(id));
    }

    public static TheoryData<string> RootAndChildChanges =>
        new(
            "AddAddress",
            "UpdateAddress",
            "RemoveAddress",
            "AddContactPoint",
            "UpdateContactPoint",
            "RemoveContactPoint",
            "AddRelation",
            "UpdateRelation",
            "RemoveRelation",
            "SetFiscalProfile",
            "UpdateFiscalProfile",
            "AssignPreparer",
            "UnassignPreparer",
            "GrantAccess",
            "RevokeAccess",
            "ChangeContactPolicy",
            "ChangePrimaryEmail",
            "ChangePersonalName"
        );

    [Theory]
    [MemberData(nameof(RootAndChildChanges))]
    public async Task ChildOnlyChange_BumpsRootRowVersion(string change)
    {
        var id = await SeedFullCustomerAsync();
        var before = await RowVersionAsync(id);

        await using (var db = database.NewContext(_tenant))
        {
            var customer = await LoadAsync(db, id);
            Assert.True(Apply(customer, change).IsSuccess);
            await db.SaveChangesAsync();
        }

        Assert.NotEqual(before, await RowVersionAsync(id));
    }

    [Fact]
    public async Task RelationFiscalProfileChange_BumpsRootRowVersion()
    {
        var id = await SeedFullCustomerAsync();
        var before = await RowVersionAsync(id);

        await using (var db = database.NewContext(_tenant))
        {
            var relationId = (await LoadAsync(db, id)).Relations.Single().Id;
            var result = await SetRelationFiscalProfileHandler.Handle(
                new SetRelationFiscalProfileCommand(
                    _tenant,
                    id,
                    relationId,
                    Actor,
                    TaxRelationshipRole.Spouse,
                    "123456789",
                    2026,
                    QualifiesAsDependent: false,
                    LivedWithTaxpayer: true
                ),
                new CustomerRepository(db),
                new FixedProtector(),
                db,
                NullLogger<SetRelationFiscalProfileCommand>.Instance,
                CancellationToken.None
            );
            Assert.True(result.IsSuccess);
        }

        Assert.NotEqual(before, await RowVersionAsync(id));
    }

    // EF refresca la RowVersion tras cada guardado, también con tipos owned reemplazados.
    [Fact]
    public async Task SameContext_TwoSaves_DoNotConflictWithThemselves()
    {
        var id = await SeedFullCustomerAsync();

        await using var db = database.NewContext(_tenant);
        var customer = await LoadAsync(db, id);
        Assert.True(customer.ChangeContactPolicy(false, true, Actor).IsSuccess);
        await db.SaveChangesAsync();
        Assert.True(customer.ChangeContactPolicy(true, false, Actor).IsSuccess);
        await db.SaveChangesAsync();
        Assert.True(Apply(customer, "AddAddress").IsSuccess);
        await db.SaveChangesAsync();
    }

    // BB-11: por HTTP el conflicto sale de inmediato y el CRM recibe el 409; el perdedor no relee ni pisa al otro.
    [Fact]
    public async Task HttpCommand_ConflictOnce_ThrowsWithoutRetry()
    {
        var id = await SeedAsync(_ => Result.Success());
        var admin = new ConcurrentAdminEdit(database, conflicts: 1);
        using var host = await CustomerWolverineHost.StartAsync(
            database,
            _tenant,
            options => options.Discovery.IncludeType(typeof(ArchiveCustomerHandler)),
            admin
        );

        var conflict = await Assert.ThrowsAsync<ConflictException>(() =>
            host
                .Services.GetRequiredService<IMessageBus>()
                .InvokeAsync<Result>(new ArchiveCustomerCommand(_tenant, id, Actor))
        );

        Assert.Equal(ConflictCode, conflict.Code);
        Assert.Equal(1, admin.SavesWithCustomers);
    }

    // BB-11, con outbox durable real: el intento revertido no deja salir lo que publicó antes de guardar.
    [Fact]
    public async Task HttpCommand_PublishBeforeSave_ConflictOnce_LeavesNothingInTheOutbox()
    {
        var id = await SeedAsync(_ => Result.Success());
        var admin = new ConcurrentAdminEdit(database, conflicts: 1);
        using var host = await StartPublishBeforeSaveHostAsync(admin);

        await Assert.ThrowsAsync<ConflictException>(() =>
            host
                .Services.GetRequiredService<IMessageBus>()
                .InvokeAsync(new PublishBeforeSaveProbe(_tenant, id, Guid.NewGuid()))
        );

        Assert.Equal(0, await CountAnnouncementsAsync());
    }

    [Fact]
    public async Task Message_PublishBeforeSave_ConflictOnce_AnnouncesOnlyTheAttemptThatPasses()
    {
        var preparer = Guid.NewGuid();
        var id = await SeedAsync(_ => Result.Success());
        var admin = new ConcurrentAdminEdit(database, conflicts: 1);
        using var host = await StartPublishBeforeSaveHostAsync(admin);

        await host
            .Services.GetRequiredService<IMessageBus>()
            .PublishAsync(new PublishBeforeSaveProbe(_tenant, id, preparer));

        await WaitUntilAsync(async () => await PreparerAsync(id) == preparer);
        await WaitUntilAsync(async () => await CountAnnouncementsAsync() > 0);
        Assert.Equal(2, admin.SavesWithCustomers);
        Assert.Equal(1, await CountAnnouncementsAsync());
    }

    private Task<IHost> StartPublishBeforeSaveHostAsync(ConcurrentAdminEdit admin)
    {
        var closedPort = CustomerWolverineHost.ClosedPort();
        return CustomerWolverineHost.StartAsync(
            database,
            _tenant,
            options =>
            {
                options.Discovery.IncludeType(typeof(PublishBeforeSaveProbeHandler));
                options.PublishMessage<PublishBeforeSaveProbe>().ToLocalQueue("publish-before-save").UseDurableInbox();
                options
                    .PublishMessage<CustomerPreparerAssignedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
            },
            admin
        );
    }

    [Fact]
    public async Task OffboardingSweep_ConflictOnce_RetriesOnFreshRead_AndAnnouncesOnce()
    {
        var (leaving, successor) = (Guid.NewGuid(), Guid.NewGuid());
        var id = await SeedAsync(customer => customer.AssignPreparer(leaving, Actor));
        await SeedDirectoryAsync(leaving, successor);
        var admin = new ConcurrentAdminEdit(database, conflicts: 1);

        using var host = await StartOffboardingHostAsync(admin);
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(Offboarded(leaving, successor));

        await WaitUntilAsync(async () => await PreparerAsync(id) == successor);
        await WaitUntilAsync(async () => await CountAnnouncementsAsync() > 0);
        Assert.Equal(2, admin.SavesWithCustomers);
        Assert.Equal(1, await CountAnnouncementsAsync());
    }

    [Fact]
    public async Task OffboardingSweep_ConflictOnEveryAttempt_EndsInDeadLetters_NotInALoop()
    {
        var (leaving, successor) = (Guid.NewGuid(), Guid.NewGuid());
        var id = await SeedAsync(customer => customer.AssignPreparer(leaving, Actor));
        await SeedDirectoryAsync(leaving, successor);
        var admin = new ConcurrentAdminEdit(database, conflicts: int.MaxValue);

        using var host = await StartOffboardingHostAsync(admin);
        await host.Services.GetRequiredService<IMessageBus>().PublishAsync(Offboarded(leaving, successor));

        await WaitUntilAsync(async () =>
            await database.CountEnvelopesAsync("wolverine_dead_letters", nameof(UserOffboardedIntegrationEvent)) > 0
        );
        Assert.Equal(4, admin.SavesWithCustomers);
        Assert.Equal(leaving, await PreparerAsync(id));
        Assert.Equal(0, await CountAnnouncementsAsync());
    }

    private Task<IHost> StartOffboardingHostAsync(ConcurrentAdminEdit admin)
    {
        var closedPort = CustomerWolverineHost.ClosedPort();
        return CustomerWolverineHost.StartAsync(
            database,
            _tenant,
            options =>
            {
                options.Discovery.IncludeType(typeof(CustomerUserOffboardedConsumer));
                options.PublishMessage<UserOffboardedIntegrationEvent>().ToLocalQueue("offboarding").UseDurableInbox();
                options
                    .PublishMessage<CustomerPreparerAssignedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
                options
                    .PublishMessage<CustomerPreparerUnassignedIntegrationEvent>()
                    .ToPort(closedPort)
                    .UseDurableOutbox();
            },
            admin
        );
    }

    private UserOffboardedIntegrationEvent Offboarded(Guid leaving, Guid successor) =>
        new()
        {
            TenantId = _tenant,
            CorrelationId = "cus-1",
            UserId = leaving,
            Email = "leaving@example.test",
            ActorType = "TenantEmployee",
            OffboardedByUserId = Actor,
            SuccessorUserId = successor,
            RemovedAtUtc = DateTime.UtcNow,
        };

    private async Task SeedDirectoryAsync(params Guid[] users)
    {
        await using var db = database.NewContext(_tenant);
        foreach (var user in users)
            db.TenantEmployeeDirectoryEntries.Add(
                TenantEmployeeDirectoryEntry.CreateReconciled(user, _tenant, "TenantEmployee", true, false)
            );
        await db.SaveChangesAsync();
    }

    // La base es de toda la clase: se cuenta solo lo de este tenant.
    private async Task<int> CountAnnouncementsAsync() =>
        Convert.ToInt32(
            await database.ScalarAsync(
                "IF OBJECT_ID('[wolverine].[wolverine_outgoing_envelopes]') IS NULL SELECT 0 ELSE "
                    + "SELECT COUNT(*) FROM [wolverine].[wolverine_outgoing_envelopes] "
                    + "WHERE message_type LIKE @type "
                    + "AND CHARINDEX(CONVERT(varbinary(max), CONVERT(varchar(36), @tenant)), body) > 0",
                ("@type", $"%{nameof(CustomerPreparerAssignedIntegrationEvent)}%"),
                ("@tenant", _tenant.ToString())
            )
        );

    private async Task<Guid?> PreparerAsync(Guid id)
    {
        await using var db = database.NewContext(_tenant);
        return await db
            .Customers.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => c.AssignedPreparerUserId)
            .SingleAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(200);
        }
        Assert.Fail("The expected state was not reached in time.");
    }

    private static Result Apply(DomainCustomer customer, string change)
    {
        var address = AddressValue.Create("2 New St", "Miami", "33101", "US").Value;
        var name = PersonalName.Create("Other", "Name").Value;
        var addressId = customer.Addresses.Single().Id;
        var contactId = customer.ContactPoints.Single().Id;
        var relationId = customer.Relations.Single().Id;
        return change switch
        {
            "AddAddress" => customer.AddAddress(AddressKind.Mailing, address, false, Actor),
            "UpdateAddress" => customer.UpdateAddress(addressId, AddressKind.Home, address, true, Actor),
            "RemoveAddress" => customer.RemoveAddress(addressId, Actor),
            "AddContactPoint" => customer.AddContactPoint(
                ContactPointType.Phone,
                "+13055550100",
                "+13055550100",
                null,
                false,
                Actor
            ),
            "UpdateContactPoint" => customer.UpdateContactPoint(
                contactId,
                ContactPointType.Email,
                "b@example.test",
                "b@example.test",
                "Work",
                false,
                Actor
            ),
            "RemoveContactPoint" => customer.RemoveContactPoint(contactId, Actor),
            "AddRelation" => customer.AddRelation(
                RelationshipKind.Spouse,
                RelationPurpose.Dependent,
                name,
                null,
                null,
                null,
                null,
                Actor
            ),
            "UpdateRelation" => customer.UpdateRelation(
                relationId,
                RelationshipKind.Spouse,
                RelationPurpose.TaxHouseholdMember,
                name,
                null,
                null,
                null,
                null,
                Actor
            ),
            "RemoveRelation" => customer.RemoveRelation(relationId, Actor),
            "SetFiscalProfile" => customer.SetFiscalProfile(
                FiscalSubjectKind.Individual,
                [9, 9, 9],
                Blind(),
                "1111",
                FilingStatus.Single,
                null,
                false,
                null,
                null,
                Actor
            ),
            "UpdateFiscalProfile" => customer.UpdateFiscalProfile(
                FilingStatus.HeadOfHousehold,
                10m,
                true,
                null,
                null,
                Actor
            ),
            "AssignPreparer" => customer.AssignPreparer(Guid.NewGuid(), Actor),
            "UnassignPreparer" => customer.UnassignPreparer(Actor),
            "GrantAccess" => customer.GrantAccess(Guid.NewGuid(), Actor),
            "RevokeAccess" => customer.RevokeAccess(customer.Assignments.First().UserId, Actor),
            "ChangeContactPolicy" => customer.ChangeContactPolicy(false, false, Actor),
            "ChangePrimaryEmail" => customer.ChangePrimaryEmail(
                EmailAddress.Create($"{Guid.NewGuid():N}@example.test").Value,
                Actor
            ),
            "ChangePersonalName" => customer.ChangePersonalName(name, Actor),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
    }

    private static string Blind() => Guid.NewGuid().ToString("N");

    private Task<Guid> SeedFullCustomerAsync() =>
        SeedAsync(customer =>
        {
            customer.AddAddress(
                AddressKind.Home,
                AddressValue.Create("1 Old St", "Miami", "33101", "US").Value,
                true,
                Actor
            );
            customer.AddContactPoint(ContactPointType.Email, "a@example.test", "a@example.test", null, false, Actor);
            customer.AddRelation(
                RelationshipKind.Spouse,
                RelationPurpose.TaxHouseholdMember,
                PersonalName.Create("Spouse", "Client").Value,
                null,
                null,
                null,
                null,
                Actor
            );
            customer.SetFiscalProfile(
                FiscalSubjectKind.Individual,
                [1, 2, 3],
                Blind(),
                "6789",
                FilingStatus.Single,
                null,
                false,
                null,
                null,
                Actor
            );
            return customer.AssignPreparer(Guid.NewGuid(), Actor);
        });

    private async Task<Guid> SeedAsync(Func<DomainCustomer, Result> arrange)
    {
        var customer = DomainCustomer
            .Register(
                _tenant,
                CustomerKind.Individual,
                PersonalName.Create("Race", "Client").Value,
                null,
                EmailAddress.Create($"cus1-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                Actor
            )
            .Value;
        Assert.True(arrange(customer).IsSuccess);
        await using var db = database.NewContext(_tenant);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static async Task<DomainCustomer> LoadAsync(CustomerDbContext db, Guid id) =>
        (await new CustomerRepository(db).GetByIdAsync(id, CancellationToken.None))!;

    // Por SQL: la columna es del esquema, no de lo que la entidad exponga.
    private async Task<byte[]> RowVersionAsync(Guid id) =>
        (byte[])(await database.ScalarAsync("SELECT RowVersion FROM Customers WHERE Id = @id", ("@id", id)))!;

    /// <summary>Justo antes de guardar clientes, "otro usuario" edita la fila por otra conexión.</summary>
    internal sealed class ConcurrentAdminEdit(CustomerTemporaryDatabase database, int conflicts)
        : SaveChangesInterceptor
    {
        private int _saves;

        public int SavesWithCustomers => Volatile.Read(ref _saves);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var edited = eventData
                .Context!.ChangeTracker.Entries<DomainCustomer>()
                .Where(entry => entry.State == EntityState.Modified)
                .Select(entry => entry.Entity.Id)
                .ToList();
            if (edited.Count > 0 && Interlocked.Increment(ref _saves) <= conflicts)
                foreach (var id in edited)
                    await database.ScalarAsync(
                        "UPDATE Customers SET UpdatedAtUtc = SYSUTCDATETIME() WHERE Id = @id",
                        ("@id", id)
                    );
            return result;
        }
    }

    private sealed class FixedProtector : ISensitiveDataProtector
    {
        public short CurrentKeyVersion => 1;

        public byte[] Protect(string plainText) => [1, 2, 3];

        public string Unprotect(byte[] cipher) => "123456789";

        public string ComputeBlindIndex(string plainText, Guid tenantId) => Blind();

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) => [Blind()];
    }
}

public sealed record PublishBeforeSaveProbe(Guid TenantId, Guid CustomerId, Guid PreparerUserId);

/// <summary>Publica antes de guardar a propósito: mide qué sale cuando el guardado choca.</summary>
public static class PublishBeforeSaveProbeHandler
{
    public static async Task Handle(
        PublishBeforeSaveProbe message,
        ICustomerRepository customers,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var customer = await customers.GetByIdAsync(message.CustomerId, ct);
        Assert.True(customer!.AssignPreparer(message.PreparerUserId, message.PreparerUserId).IsSuccess);

        await bus.PublishAsync(
            new CustomerPreparerAssignedIntegrationEvent
            {
                TenantId = message.TenantId,
                CorrelationId = "bb-11",
                CustomerId = message.CustomerId,
                PreparerUserId = message.PreparerUserId,
                AssignedByUserId = message.PreparerUserId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
    }
}
