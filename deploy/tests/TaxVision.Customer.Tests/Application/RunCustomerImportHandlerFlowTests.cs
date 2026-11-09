using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Imports;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// CUS-6: caracteriza el import de punta a punta por <see cref="RunCustomerImportHandler.Handle"/>, con dobles en
/// memoria: fases, resultado por fila, candados, cancelación y avisos. No ejercita SQL (eso lo cubren las T-INT).
/// </summary>
public sealed class RunCustomerImportHandlerFlowTests
{
    [Fact]
    public async Task A_missing_attempt_is_dropped()
    {
        var world = new ImportWorld();

        await world.RunAsync(Guid.NewGuid());

        Assert.Equal(0, world.Files.Downloads);
        Assert.Empty(world.Published);
    }

    [Fact]
    public async Task A_finished_attempt_is_not_run_again()
    {
        var world = new ImportWorld();
        world.Attempt.Start();
        world.Attempt.Fail("before");

        await world.RunAsync();

        Assert.Equal(0, world.Files.Downloads);
        Assert.Empty(world.Published);
    }

    [Fact]
    public async Task A_file_that_cannot_be_downloaded_fails_the_attempt_and_warns()
    {
        var world = new ImportWorld();
        world.Files.FailDownload = true;

        await world.RunAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        var failed = Assert.IsType<CustomerImportFailedIntegrationEvent>(Assert.Single(world.Published));
        Assert.Equal(world.Attempt.Id, failed.ImportJobId);
        Assert.Equal(world.Attempt.TenantId, failed.TenantId);
        Assert.Equal(world.Attempt.CreatedByUserId, failed.CreatedByUserId);
        Assert.Equal(world.Attempt.NotifyUserId, failed.NotifyUserId);
        Assert.Equal(world.Correlation.CorrelationId, failed.CorrelationId);
        Assert.Equal("Could not download the import file from CloudStorage: boom", failed.Reason);
    }

    [Fact]
    public async Task An_empty_file_fails_with_its_reason()
    {
        var world = new ImportWorld();

        await world.RunAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        Assert.Equal("File is empty.", world.Attempt.FailureReason);
        Assert.IsType<CustomerImportFailedIntegrationEvent>(Assert.Single(world.Published));
    }

    [Fact]
    public async Task A_file_over_the_limit_fails_with_its_reason()
    {
        var world = new ImportWorld(maxRows: 1);
        world.Rows.AddRange([ImportWorld.Person(2), ImportWorld.Person(3)]);

        await world.RunAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        Assert.Equal("File has 2 rows; maximum allowed is 1.", world.Attempt.FailureReason);
    }

    [Fact]
    public async Task New_rows_become_customers_without_a_notice_per_row()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2, taxIdentifier: "123-45-6789", withAddress: true, withSpouse: true));

        await world.RunAsync();

        Assert.Equal(ImportStatus.Completed, world.Attempt.Status);
        Assert.Equal(1, world.Attempt.TotalRows);
        Assert.Equal(1, world.Attempt.SuccessCount);
        var customer = Assert.Single(world.Customers.All);
        Assert.Equal(world.Attempt.TenantId, customer.TenantId);
        Assert.Equal("Row 2", customer.DisplayName);
        Assert.Empty(customer.DomainEvents);
        Assert.Single(customer.Addresses);
        Assert.Single(customer.Relations);
        Assert.NotNull(customer.FiscalProfile);
        Assert.Equal("6789", customer.FiscalProfile!.TaxIdentifierLast4);

        var done = Assert.IsType<CustomersBulkImportedIntegrationEvent>(Assert.Single(world.Published));
        Assert.Equal(world.Attempt.Id, done.ImportJobId);
        Assert.Equal(world.Attempt.CreatedByUserId, done.CreatedByUserId);
        Assert.Equal(world.Attempt.NotifyUserId, done.NotifyUserId);
        Assert.Equal(world.Attempt.CompletedAtUtc, done.CompletedAtUtc);
        Assert.Equal(world.Correlation.CorrelationId, done.CorrelationId);
        Assert.Equal(1, done.TotalRows);
        Assert.Equal(1, done.SuccessCount);
        Assert.Equal([customer.Id], done.CreatedCustomerIds);
        Assert.Empty(done.UpdatedCustomerIds);
        Assert.Equal(1, world.Files.Deletes);
    }

    [Fact]
    public async Task Each_failing_row_keeps_its_reason_and_the_rest_go_on()
    {
        var world = new ImportWorld();
        world.Catalogs.Unknown = true;
        world.Rows.AddRange([
            new ImportCustomerRow
            {
                RowNumber = 2,
                Kind = "Alien",
                FirstName = "Bad",
                LastName = "Kind",
            },
            ImportWorld.Person(3, occupation: "Astronaut"),
            ImportWorld.Business(4, naics: "999999"),
            ImportWorld.Person(5),
        ]);

        await world.RunAsync();

        Assert.Equal(ImportStatus.Completed, world.Attempt.Status);
        Assert.Equal("Row.Kind", world.RowOf(2).ErrorCode);
        Assert.Equal("Bad Kind", world.RowOf(2).DisplayName);
        Assert.Equal("Catalog.UnknownOccupation", world.RowOf(3).ErrorCode);
        Assert.Equal("Occupation 'Astronaut' is not in the catalog.", world.RowOf(3).Message);
        Assert.Equal("Catalog.UnknownNaics", world.RowOf(4).ErrorCode);
        Assert.Equal("Acme 4", world.RowOf(4).DisplayName);
        Assert.Equal(RowStatus.Success, world.RowOf(5).Status);
        var done = Assert.IsType<CustomersBulkImportedIntegrationEvent>(Assert.Single(world.Published));
        Assert.Equal(3, done.FailedCount);
        Assert.Equal(1, done.SuccessCount);
    }

    [Fact]
    public async Task A_duplicate_inside_the_chunk_fails_the_second_row()
    {
        var world = new ImportWorld();
        world.Rows.AddRange([ImportWorld.Person(2), ImportWorld.Person(3, email: "row2@example.com")]);

        await world.RunAsync();

        Assert.Equal(RowStatus.Success, world.RowOf(2).Status);
        Assert.Equal("Import.DuplicateInChunk", world.RowOf(3).ErrorCode);
        Assert.Single(world.Customers.All);
    }

    [Fact]
    public async Task Emails_are_locked_once_in_order_before_looking_for_duplicates()
    {
        var world = new ImportWorld();
        world.Rows.AddRange([
            ImportWorld.Person(2, email: "zed@example.com"),
            ImportWorld.Person(3, email: "amy@example.com"),
            ImportWorld.Person(4, email: "ZED@example.com"),
        ]);

        await world.RunAsync();

        Assert.Equal(["lock:amy@example.com", "lock:zed@example.com", "detect"], world.Steps);
    }

    [Fact]
    public async Task Skip_strategy_leaves_the_existing_customer_alone()
    {
        var world = new ImportWorld(DuplicateStrategy.Skip);
        var existing = world.AddExisting();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Match(2, existing, "email");

        await world.RunAsync();

        Assert.Equal(RowStatus.Skipped, world.RowOf(2).Status);
        Assert.Equal("old@example.com", existing.PrimaryEmail.Value);
        var done = Assert.IsType<CustomersBulkImportedIntegrationEvent>(Assert.Single(world.Published));
        Assert.Equal(1, done.SkippedCount);
        Assert.Empty(done.UpdatedCustomerIds);
    }

    [Fact]
    public async Task Overwrite_strategy_updates_the_existing_customer_without_a_notice_per_row()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var existing = world.AddExisting();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Match(2, existing, "email");

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
        Assert.Equal("row2@example.com", existing.PrimaryEmail.Value);
        Assert.Empty(existing.DomainEvents);
        var done = Assert.IsType<CustomersBulkImportedIntegrationEvent>(Assert.Single(world.Published));
        Assert.Equal(1, done.UpdatedCount);
        Assert.Equal([existing.Id], done.UpdatedCustomerIds);
    }

    [Fact]
    public async Task Merge_strategy_keeps_the_existing_email()
    {
        var world = new ImportWorld(DuplicateStrategy.Merge);
        var existing = world.AddExisting();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Match(2, existing, "email");

        await world.RunAsync();

        Assert.Equal(RowStatus.Updated, world.RowOf(2).Status);
        Assert.Equal("old@example.com", existing.PrimaryEmail.Value);
    }

    [Fact]
    public async Task A_match_that_disappeared_fails_the_row()
    {
        var world = new ImportWorld(DuplicateStrategy.Overwrite);
        var ghost = world.AddExisting();
        world.Customers.All.Clear();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Match(2, ghost, "email");

        await world.RunAsync();

        Assert.Equal("Import.MatchedThenMissing", world.RowOf(2).ErrorCode);
    }

    [Fact]
    public async Task Rows_run_in_chunks_of_500_with_one_lookup_each()
    {
        var world = new ImportWorld();
        for (var row = 2; row < 503; row++)
            world.Rows.Add(ImportWorld.Person(row));

        await world.RunAsync();

        Assert.Equal(2, world.Steps.Count(step => step == "detect"));
        Assert.Equal(501, world.Attempt.SuccessCount);
    }

    [Fact]
    public async Task A_cancel_between_chunks_stops_and_confirms_with_what_was_done()
    {
        var world = new ImportWorld();
        for (var row = 2; row < 503; row++)
            world.Rows.Add(ImportWorld.Person(row));
        world.Detector.AfterFirstChunk = () => world.Attempt.RequestCancel(world.Attempt.CreatedByUserId);

        await world.RunAsync();

        Assert.Equal(ImportStatus.Canceled, world.Attempt.Status);
        var done = Assert.IsType<CustomersBulkImportedIntegrationEvent>(Assert.Single(world.Published));
        Assert.Equal(500, done.SuccessCount);
        Assert.Equal(501, done.TotalRows);
        Assert.Equal(1, world.Steps.Count(step => step == "detect"));
    }

    [Fact]
    public async Task A_file_that_cannot_be_deleted_does_not_undo_the_import()
    {
        var world = new ImportWorld();
        world.Files.FailDelete = true;
        world.Rows.Add(ImportWorld.Person(2));

        await world.RunAsync();

        Assert.Equal(ImportStatus.Completed, world.Attempt.Status);
        Assert.IsType<CustomersBulkImportedIntegrationEvent>(Assert.Single(world.Published));
    }

    [Fact]
    public async Task A_chunk_that_cannot_commit_fails_the_attempt()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2));
        world.Detector.Fail = true;

        await world.RunAsync();

        Assert.Equal(ImportStatus.Failed, world.Attempt.Status);
        Assert.Equal("detector down", world.Attempt.FailureReason);
        Assert.IsType<CustomerImportFailedIntegrationEvent>(Assert.Single(world.Published));
    }

    [Fact]
    public async Task A_host_shutdown_is_rethrown_without_failing_the_attempt()
    {
        var world = new ImportWorld();
        world.Rows.Add(ImportWorld.Person(2));
        using var cancelled = new CancellationTokenSource();
        world.Detector.BeforeLookup = cancelled.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.RunAsync(ct: cancelled.Token));

        Assert.NotEqual(ImportStatus.Failed, world.Attempt.Status);
        Assert.Empty(world.Published);
    }

    private sealed class ImportWorld
    {
        private readonly ServiceProvider _provider;

        public ImportWorld(DuplicateStrategy strategy = DuplicateStrategy.Skip, int maxRows = 10_000)
        {
            Attempt = CustomerImportAttempt
                .Create(Guid.NewGuid(), Guid.NewGuid(), "key", strategy, ImportSourceKind.Csv, "clients.csv")
                .Value;
            Customers = new CustomerCommandEventCountTests.AllCustomers([]);
            Detector = new ScriptedDetector(Steps);
            var services = new ServiceCollection();
            services.AddSingleton<ICustomerImportRepository>(new SingleAttempt(Attempt));
            services.AddSingleton<ICustomerRepository>(Customers);
            services.AddSingleton<ICustomerDuplicateDetector>(Detector);
            services.AddSingleton<ICatalogResolver>(Catalogs);
            services.AddSingleton<ICustomerCreationLock>(new StepLock(Steps));
            services.AddSingleton<ISensitiveDataProtector>(new TestProtector());
            services.AddSingleton<ICustomerImportCloudStorageClient>(Files);
            services.AddSingleton<ICustomerImportReaderFactory>(new FixedRows(Rows));
            services.AddSingleton<ICorrelationContext>(Correlation);
            services.AddSingleton<IUnitOfWork>(new NoUnitOfWork());
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?> { ["CustomerImport:MaxRows"] = maxRows.ToString() }
                    )
                    .Build()
            );
            services.AddSingleton<ITransactionalUnitRunner>(sp => new InlineRunner(sp, Published));
            _provider = services.BuildServiceProvider();
        }

        public CustomerImportAttempt Attempt { get; }
        public CustomerCommandEventCountTests.AllCustomers Customers { get; }
        public ScriptedDetector Detector { get; }
        public ScriptedCatalogs Catalogs { get; } = new();
        public ScriptedFiles Files { get; } = new();
        public List<ImportCustomerRow> Rows { get; } = [];
        public List<object> Published { get; } = [];
        public List<string> Steps { get; } = [];
        public TestCorrelation Correlation { get; } = new();

        public CustomerImportRow RowOf(int number) => Attempt.Rows.Single(row => row.RowNumber == number);

        public DomainCustomer AddExisting()
        {
            var customer = DomainCustomer
                .Register(
                    Attempt.TenantId,
                    CustomerKind.Individual,
                    PersonalName.Create("Old", "Client").Value,
                    null,
                    EmailAddress.Create("old@example.com").Value,
                    null,
                    Language.En,
                    Guid.NewGuid()
                )
                .Value;
            customer.ClearDomainEvents();
            Customers.All.Add(customer);
            return customer;
        }

        public Task RunAsync(Guid? attemptId = null, CancellationToken ct = default) =>
            RunCustomerImportHandler.Handle(
                new RunCustomerImportMessage(attemptId ?? Attempt.Id),
                _provider.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RunCustomerImportMessage>.Instance,
                ct
            );

        public static ImportCustomerRow Person(
            int number,
            string? email = null,
            string? taxIdentifier = null,
            bool withAddress = false,
            bool withSpouse = false,
            string? occupation = null
        ) =>
            new()
            {
                RowNumber = number,
                Kind = "Individual",
                FirstName = "Row",
                LastName = number.ToString(),
                Email = email ?? $"row{number}@example.com",
                OccupationName = occupation,
                TaxIdentifier = taxIdentifier,
                AddressLine1 = withAddress ? "1 Main St" : null,
                City = withAddress ? "Miami" : null,
                Region = withAddress ? "FL" : null,
                PostalCode = withAddress ? "33101" : null,
                CountryCode = withAddress ? "US" : null,
                SpouseFirstName = withSpouse ? "Sam" : null,
                SpouseLastName = withSpouse ? "Row" : null,
                SpouseTaxIdentifier = withSpouse ? "987-65-4321" : null,
            };

        public static ImportCustomerRow Business(int number, string naics) =>
            new()
            {
                RowNumber = number,
                Kind = "Business",
                LegalName = $"Acme {number}",
                BusinessStructure = "LLC",
                PrincipalBusinessActivityCode = naics,
                Email = $"acme{number}@example.com",
            };
    }

    private sealed class SingleAttempt(CustomerImportAttempt attempt) : ICustomerImportRepository
    {
        public Task<CustomerImportAttempt?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(id == attempt.Id ? attempt : null);

        public Task<CustomerImportAttempt?> GetByIdempotencyKeyAsync(
            Guid tenantId,
            string idempotencyKey,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task AddAsync(CustomerImportAttempt attempt, CancellationToken ct) => throw new NotSupportedException();

        public Task<IReadOnlyList<CustomerImportAttempt>> ListInFlightNotifyingAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private sealed class InlineRunner(IServiceProvider services, List<object> published) : ITransactionalUnitRunner
    {
        public async Task RunAsync(Func<TransactionalUnit, CancellationToken, Task<bool>> work, CancellationToken ct)
        {
            var pending = new List<object>();
            var saved = await work(
                new TransactionalUnit(
                    services,
                    message =>
                    {
                        pending.Add(message);
                        return ValueTask.CompletedTask;
                    }
                ),
                ct
            );
            if (saved)
                published.AddRange(pending);
        }
    }

    private sealed class ScriptedDetector(List<string> steps) : ICustomerDuplicateDetector
    {
        private readonly List<DuplicateMatch> _matches = [];

        public Action? AfterFirstChunk { get; set; }
        public Action? BeforeLookup { get; set; }
        public bool Fail { get; set; }

        public void Match(int row, DomainCustomer existing, string matchedBy) =>
            _matches.Add(new DuplicateMatch(row, existing.Id, existing.DisplayName, matchedBy));

        public Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
            Guid tenantId,
            IReadOnlyList<ImportCustomerRow> chunk,
            CancellationToken ct
        )
        {
            BeforeLookup?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (Fail)
                throw new InvalidOperationException("detector down");
            steps.Add("detect");
            AfterFirstChunk?.Invoke();
            AfterFirstChunk = null;
            return Task.FromResult<IReadOnlyList<DuplicateMatch>>(_matches);
        }

        public Task<DuplicateMatch?> FindDuplicateAsync(
            Guid tenantId,
            CustomerDuplicateCandidate candidate,
            Guid? excludeCustomerId,
            CancellationToken ct
        ) => throw new NotSupportedException();
    }

    private sealed class ScriptedCatalogs : ICatalogResolver
    {
        public bool Unknown { get; set; }

        public Task<Guid?> ResolveOccupationIdAsync(string occupationName, CancellationToken ct) =>
            Task.FromResult(Unknown ? null : (Guid?)Guid.NewGuid());

        public Task<Guid?> ResolvePrincipalBusinessActivityIdAsync(string naicsCode, CancellationToken ct) =>
            Task.FromResult(Unknown ? null : (Guid?)Guid.NewGuid());
    }

    private sealed class StepLock(List<string> steps) : ICustomerCreationLock
    {
        public Task AcquireAsync(Guid tenantId, string normalizedEmail, CancellationToken ct)
        {
            steps.Add($"lock:{normalizedEmail}");
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedFiles : ICustomerImportCloudStorageClient
    {
        public bool FailDownload { get; set; }
        public bool FailDelete { get; set; }
        public int Downloads { get; private set; }
        public int Deletes { get; private set; }

        public Task<Result> UploadAsync(
            Guid tenantId,
            Guid fileId,
            byte[] content,
            string fileName,
            string contentType,
            Guid actorId,
            Wolverine.IMessageBus bus,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success());

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            Downloads++;
            return Task.FromResult(
                FailDownload ? Result.Failure<byte[]>(new Error("Storage.Down", "boom")) : Result.Success<byte[]>([])
            );
        }

        public Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            Deletes++;
            return Task.FromResult(FailDelete ? Result.Failure(new Error("Storage.Down", "boom")) : Result.Success());
        }
    }

    private sealed class FixedRows(List<ImportCustomerRow> rows) : ICustomerImportReaderFactory, ICustomerImportReader
    {
        public ImportSourceKind SourceKind => ImportSourceKind.Csv;

        public ICustomerImportReader Resolve(ImportSourceKind sourceKind) => this;

        public async IAsyncEnumerable<ImportCustomerRow> ReadAsync(
            Stream stream,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct
        )
        {
            foreach (var row in rows)
                yield return row;
            await Task.CompletedTask;
        }
    }

    private sealed class TestProtector : ISensitiveDataProtector
    {
        public short CurrentKeyVersion => 1;

        public byte[] Protect(string plainText) => [1, 2, 3];

        public string Unprotect(byte[] cipher) => string.Empty;

        public string ComputeBlindIndex(string plainText, Guid tenantId) => $"blind-{plainText}";

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) => [];
    }

    private sealed class NoUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class TestCorrelation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr-import";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new NoScope();
        }

        private sealed class NoScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
