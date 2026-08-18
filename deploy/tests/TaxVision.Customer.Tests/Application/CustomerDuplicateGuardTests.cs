using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Commands.Create;
using TaxVision.Customer.Application.Customers.Commands.Update;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Las dos puertas por las que un cliente podía repetirse dentro de un tenant: crearlo y editarle el
/// correo. Ninguna de las dos miraba nada — el alta ni siquiera consultaba, y el índice único de la
/// base sólo existía sobre el perfil fiscal, que es opcional.
/// </summary>
public sealed class CustomerDuplicateGuardTests
{
    private static readonly Guid Tenant = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid User = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");

    [Fact]
    public async Task Creating_a_second_customer_with_the_same_email_is_rejected()
    {
        var repo = new InMemoryCustomers();
        var detector = new MatchingDetector(existing: null);

        var first = await CreateAsync(repo, detector, "ada@example.com");
        Assert.True(first.IsSuccess);

        detector.Existing = new DuplicateMatch(0, first.Value.Id, "Ada Lovelace", "Email");
        var second = await CreateAsync(repo, detector, "ada@example.com");

        Assert.True(second.IsFailure);
        Assert.Equal("Customer.DuplicateFound", second.Error.Code);
        Assert.Single(repo.All);
    }

    /// <summary>
    /// Con <c>Overwrite</c> se le aplican los datos al que ya estaba. Lo que no puede pasar es que
    /// nazca una segunda fila: es exactamente el síntoma que se veía en producción.
    /// </summary>
    [Fact]
    public async Task Overwriting_updates_the_existing_customer_instead_of_creating_another()
    {
        var repo = new InMemoryCustomers();
        var detector = new MatchingDetector(existing: null);
        var bus = new FakeMessageBus();

        var first = await CreateAsync(repo, detector, "ada@example.com", bus: bus);
        detector.Existing = new DuplicateMatch(0, first.Value.Id, "Ada Lovelace", "Email");

        var second = await CreateAsync(
            repo,
            detector,
            "ada@example.com",
            firstName: "Ada Maria",
            overwrite: true,
            bus: bus
        );

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Single(repo.All);
        Assert.Equal("Ada Maria Lovelace", repo.All[0].DisplayName);
    }

    /// <summary>
    /// Sobreescribir publica <c>CustomerUpdated</c>, no <c>CustomerCreated</c>: los siete servicios que
    /// proyectan el directorio aplican por fecha, y anunciar como alta lo que fue una edición les diría
    /// que existe un cliente que no nació hoy.
    /// </summary>
    [Fact]
    public async Task Overwriting_announces_an_update_not_a_creation()
    {
        var repo = new InMemoryCustomers();
        var detector = new MatchingDetector(existing: null);
        var bus = new FakeMessageBus();

        var first = await CreateAsync(repo, detector, "ada@example.com", bus: bus);
        detector.Existing = new DuplicateMatch(0, first.Value.Id, "Ada Lovelace", "Email");
        bus.Published.Clear();

        await CreateAsync(repo, detector, "ada@example.com", overwrite: true, bus: bus);

        Assert.Empty(bus.Published.OfType<CustomerCreatedIntegrationEvent>());
        var updated = Assert.Single(bus.Published.OfType<CustomerUpdatedIntegrationEvent>());
        Assert.Equal(first.Value.Id, updated.CustomerId);
    }

    [Fact]
    public async Task Moving_a_customer_email_onto_another_customer_is_rejected()
    {
        var repo = new InMemoryCustomers();
        var detector = new MatchingDetector(existing: null);

        var ada = await CreateAsync(repo, detector, "ada@example.com");
        var grace = await CreateAsync(repo, detector, "grace@example.com", firstName: "Grace");

        detector.Existing = new DuplicateMatch(0, ada.Value.Id, "Ada Lovelace", "Email");
        var result = await UpdateAsync(repo, detector, grace.Value.Id, "ada@example.com");

        Assert.True(result.IsFailure);
        Assert.Equal("Customer.EmailAlreadyInUse", result.Error.Code);
    }

    /// <summary>Nadie es duplicado de sí mismo: guardar sin tocar el correo tiene que pasar.</summary>
    [Fact]
    public async Task Saving_a_customer_without_changing_its_email_still_works()
    {
        var repo = new InMemoryCustomers();
        var detector = new MatchingDetector(existing: null);

        var ada = await CreateAsync(repo, detector, "ada@example.com");
        var result = await UpdateAsync(repo, detector, ada.Value.Id, "ada@example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal(ada.Value.Id, detector.LastExcluded);
    }

    private static Task<Result<CustomerResponse>> CreateAsync(
        InMemoryCustomers repo,
        ICustomerDuplicateDetector detector,
        string email,
        string firstName = "Ada",
        bool overwrite = false,
        FakeMessageBus? bus = null
    ) =>
        CreateCustomerHandler.Handle(
            new CreateCustomerCommand(
                Tenant,
                User,
                CustomerKind.Individual,
                firstName,
                null,
                "Lovelace",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                email,
                null,
                Language.En,
                PreferredChannel.Email,
                overwrite
            ),
            repo,
            detector,
            new NoOpUnitOfWork(),
            bus ?? new FakeMessageBus(),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

    private static Task<Result<CustomerResponse>> UpdateAsync(
        InMemoryCustomers repo,
        ICustomerDuplicateDetector detector,
        Guid customerId,
        string email
    ) =>
        UpdateCustomerHandler.Handle(
            new UpdateCustomerCommand(
                TenantId: Tenant,
                CustomerId: customerId,
                ModifiedByUserId: User,
                Language: Language.En,
                PreferredChannel: PreferredChannel.Email,
                OccupationId: null,
                ProfilePictureFileId: null,
                PrimaryEmail: email,
                PrimaryPhone: null,
                FirstName: null,
                MiddleName: null,
                LastName: null,
                Prefix: null,
                Suffix: null,
                DateOfBirth: null,
                LegalName: null,
                Dba: null,
                BusinessStructure: null,
                FormationDate: null,
                PrincipalBusinessActivityId: null
            ),
            repo,
            detector,
            new FakeMessageBus(),
            new NoOpCorrelationContext(),
            new NoOpUnitOfWork(),
            CancellationToken.None
        );

    /// <summary>Devuelve el match que le pongan: lo que se prueba acá es la decisión, no la consulta.</summary>
    private sealed class MatchingDetector(DuplicateMatch? existing) : ICustomerDuplicateDetector
    {
        public DuplicateMatch? Existing { get; set; } = existing;
        public Guid? LastExcluded { get; private set; }

        public Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
            Guid tenantId,
            IReadOnlyList<ImportCustomerRow> chunk,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DuplicateMatch>>([]);

        public Task<DuplicateMatch?> FindDuplicateAsync(
            Guid tenantId,
            CustomerDuplicateCandidate candidate,
            Guid? excludeCustomerId,
            CancellationToken ct
        )
        {
            LastExcluded = excludeCustomerId;
            if (excludeCustomerId is not null && Existing?.ExistingCustomerId == excludeCustomerId)
                return Task.FromResult<DuplicateMatch?>(null);

            return Task.FromResult(Existing);
        }
    }

    private sealed class InMemoryCustomers : ICustomerRepository
    {
        public List<DomainCustomer> All { get; } = [];

        public Task AddAsync(DomainCustomer customer, CancellationToken ct)
        {
            All.Add(customer);
            return Task.CompletedTask;
        }

        public Task<DomainCustomer?> GetByIdAsync(Guid customerId, CancellationToken ct) =>
            Task.FromResult(All.Find(c => c.Id == customerId));

        public Task<IReadOnlyList<DomainCustomer>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> ids,
            CancellationToken ct
        ) => Task.FromResult<IReadOnlyList<DomainCustomer>>(All.FindAll(c => ids.Contains(c.Id)));

        public Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
            Guid tenantId,
            string blindIndex,
            Guid? excludeCustomerId,
            CancellationToken ct
        ) => Task.FromResult<Guid?>(null);

        public Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
            Guid tenantId,
            string blindIndex,
            Guid? excludeRelationId,
            CancellationToken ct
        ) => Task.FromResult<Guid?>(null);
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
