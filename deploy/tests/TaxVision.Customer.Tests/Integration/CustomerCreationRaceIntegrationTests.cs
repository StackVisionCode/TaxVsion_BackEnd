using System.Collections.Concurrent;
using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Commands.Create;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Infrastructure.Imports;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using Wolverine;
using Xunit;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// CUS-2 contra SQL Server, en base temporal: el alta por HTTP y la importación toman el candado por oficina y
/// email antes de buscar duplicados. Una barrera detiene a cada uno después de buscar: sin candado, los dos
/// llegan a ella sin ver al otro; con candado, el segundo sigue esperando el candado y la barrera vence.
/// </summary>
public sealed class CustomerCreationRaceIntegrationTests(CustomerTemporaryDatabase database)
    : IClassFixture<CustomerTemporaryDatabase>
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly RaceBarrier _barrier = new();
    private readonly ConcurrentQueue<string> _steps = new();

    [Fact]
    public async Task TwoHttpCreatesSameEmail_InParallel_SecondSeesDuplicate()
    {
        var email = NewEmail();
        using var host = await StartHostAsync();

        var results = await Task.WhenAll(CreateAsync(host, email), CreateAsync(host, email));

        Assert.Equal(1, await CountAsync(email));
        Assert.Single(results, result => result.IsSuccess);
        var loser = Assert.Single(results, result => result.IsFailure);
        Assert.Equal("Customer.DuplicateFound", loser.Error.Code);
    }

    [Fact]
    public async Task ImportRowAndHttpCreateSameEmail_DoNotDuplicate()
    {
        var email = NewEmail();
        using var host = await StartHostAsync([Row(2, email)]);
        var attemptId = await SeedAttemptAsync();

        var import = RunImportAsync(host, attemptId);
        var create = CreateAsync(host, email);
        await import;
        var created = await create;

        Assert.Equal(1, await CountAsync(email));
        await using var db = database.NewContext(_tenant);
        var row = await db
            .CustomerImportRows.IgnoreQueryFilters()
            .SingleAsync(r => r.CustomerImportAttemptId == attemptId);
        Assert.Equal(created.IsSuccess ? RowStatus.Skipped : RowStatus.Success, row.Status);
    }

    [Fact]
    public async Task ImportChunk_LocksEachEmailOnce_InOrder_BeforeSearchingDuplicates()
    {
        string[] emails = [$"c{NewEmail()}", $"a{NewEmail()}", $"b{NewEmail()}"];
        _barrier.Disable();
        using var host = await StartHostAsync([
            Row(2, emails[0]),
            Row(3, emails[1]),
            Row(4, emails[2]),
            Row(5, emails[1].ToUpperInvariant()),
        ]);

        await RunImportAsync(host, await SeedAttemptAsync());

        var steps = _steps.ToArray();
        Assert.Equal(
            emails.Order(StringComparer.Ordinal).Select(email => $"lock:{email}").Append("detect"),
            steps.Take(4)
        );
    }

    // D-CUS-BB3: un conflicto al actualizar un cliente deja la importación fallida y visible.
    [Fact]
    public async Task ImportUpdate_CustomerEditedMeanwhile_FailsVisibly()
    {
        var email = NewEmail();
        _barrier.Disable();
        await using (var seed = database.NewContext(_tenant))
        {
            seed.Customers.Add(
                DomainCustomer
                    .Register(
                        _tenant,
                        CustomerKind.Individual,
                        PersonalName.Create("Race", "Client2").Value,
                        null,
                        EmailAddress.Create(email).Value,
                        null,
                        Language.En,
                        _user
                    )
                    .Value
            );
            await seed.SaveChangesAsync();
        }
        var admin = new CustomerConcurrencyIntegrationTests.ConcurrentAdminEdit(database, conflicts: 1);
        using var host = await StartHostAsync([Row(2, email)], admin);
        var attemptId = await SeedAttemptAsync(DuplicateStrategy.Overwrite);

        await RunImportAsync(host, attemptId);

        await using var db = database.NewContext(_tenant);
        var attempt = await db.CustomerImportAttempts.IgnoreQueryFilters().SingleAsync(a => a.Id == attemptId);
        Assert.Equal(ImportStatus.Failed, attempt.Status);
        Assert.False(string.IsNullOrWhiteSpace(attempt.FailureReason));
    }

    // Notification y Sms crean la fila con la política por defecto (CUS-9): cuentan con esto.
    [Fact]
    public async Task ImportedCustomer_StartsWithTheDefaultContactPolicy()
    {
        var email = NewEmail();
        _barrier.Disable();
        using var host = await StartHostAsync([Row(2, email)]);

        await RunImportAsync(host, await SeedAttemptAsync());

        await using var db = database.NewContext(_tenant);
        var policy = await db.Customers.AsNoTracking().Select(c => c.ContactPolicy).SingleAsync();
        Assert.Equal(ContactPolicy.Default, policy);
    }

    private Task<IHost> StartHostAsync(
        IReadOnlyList<ImportCustomerRow>? rows = null,
        params IInterceptor[] interceptors
    ) =>
        CustomerWolverineHost.StartAsync(
            database,
            _tenant,
            options =>
            {
                options.Discovery.IncludeType(typeof(CreateCustomerHandler));
                options.Services.AddSingleton<ISensitiveDataProtector, NoProtector>();
                options.Services.AddScoped<SqlServerCustomerDuplicateDetector>();
                options.Services.AddScoped<ICustomerDuplicateDetector>(sp => new StoppingDetector(
                    sp.GetRequiredService<SqlServerCustomerDuplicateDetector>(),
                    _barrier,
                    _steps
                ));
                options.Services.AddScoped<SqlCustomerCreationLock>();
                options.Services.AddScoped<ICustomerCreationLock>(sp => new RecordingLock(
                    sp.GetRequiredService<SqlCustomerCreationLock>(),
                    _steps
                ));
                options.Services.AddScoped<ICustomerImportRepository, CustomerImportRepository>();
                options.Services.AddSingleton<ICustomerImportCloudStorageClient, EmptyFile>();
                options.Services.AddSingleton<ICustomerImportReaderFactory>(new FixedRows(rows ?? []));
                options.Services.AddSingleton<ICatalogResolver, NoCatalogs>();
                options.Services.AddSingleton<ITransactionalUnitRunner, CustomerTransactionalUnitRunner>();
            },
            interceptors
        );

    private Task<Result<CustomerResponse>> CreateAsync(IHost host, string email) =>
        host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CustomerResponse>>(
                new CreateCustomerCommand(
                    _tenant,
                    _user,
                    CustomerKind.Individual,
                    "Race",
                    null,
                    "Client",
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
                    Language.En
                )
            );

    private static Task RunImportAsync(IHost host, Guid attemptId) =>
        RunCustomerImportHandler.Handle(
            new RunCustomerImportMessage(attemptId),
            host.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RunCustomerImportMessage>.Instance,
            CancellationToken.None
        );

    private async Task<Guid> SeedAttemptAsync(DuplicateStrategy strategy = DuplicateStrategy.Skip)
    {
        var attempt = CustomerImportAttempt
            .Create(_tenant, _user, $"cus2-{Guid.NewGuid():N}", strategy, ImportSourceKind.Csv, "race.csv")
            .Value;
        await using var db = database.NewContext(_tenant);
        db.CustomerImportAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }

    private async Task<int> CountAsync(string email) =>
        Convert.ToInt32(
            await database.ScalarAsync(
                "SELECT COUNT(*) FROM Customers WHERE TenantId = @tenant AND PrimaryEmailNormalized = @email",
                ("@tenant", _tenant),
                ("@email", email)
            )
        );

    private static string NewEmail() => $"cus2-{Guid.NewGuid():N}@example.test";

    private static ImportCustomerRow Row(int number, string email) =>
        new()
        {
            RowNumber = number,
            Kind = "Individual",
            FirstName = "Race",
            LastName = $"Client{number}",
            Email = email,
        };

    private sealed class RaceBarrier
    {
        private readonly Barrier _barrier = new(2);
        private volatile bool _disabled;

        public void Disable() => _disabled = true;

        // Vence a los 3 s: es lo que pasa cuando el otro se quedó esperando el candado.
        public void Wait()
        {
            if (!_disabled)
                _barrier.SignalAndWait(TimeSpan.FromSeconds(3));
        }
    }

    private sealed class StoppingDetector(
        SqlServerCustomerDuplicateDetector inner,
        RaceBarrier barrier,
        ConcurrentQueue<string> steps
    ) : ICustomerDuplicateDetector
    {
        public async Task<IReadOnlyList<DuplicateMatch>> FindDuplicatesAsync(
            Guid tenantId,
            IReadOnlyList<ImportCustomerRow> chunk,
            CancellationToken ct
        )
        {
            var matches = await inner.FindDuplicatesAsync(tenantId, chunk, ct);
            steps.Enqueue("detect");
            barrier.Wait();
            return matches;
        }

        public async Task<DuplicateMatch?> FindDuplicateAsync(
            Guid tenantId,
            CustomerDuplicateCandidate candidate,
            Guid? excludeCustomerId,
            CancellationToken ct
        )
        {
            var match = await inner.FindDuplicateAsync(tenantId, candidate, excludeCustomerId, ct);
            barrier.Wait();
            return match;
        }
    }

    private sealed class RecordingLock(SqlCustomerCreationLock inner, ConcurrentQueue<string> steps)
        : ICustomerCreationLock
    {
        public Task AcquireAsync(Guid tenantId, string normalizedEmail, CancellationToken ct)
        {
            steps.Enqueue($"lock:{normalizedEmail}");
            return inner.AcquireAsync(tenantId, normalizedEmail, ct);
        }
    }

    private sealed class EmptyFile : ICustomerImportCloudStorageClient
    {
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

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success<byte[]>([]));

        public Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class FixedRows(IReadOnlyList<ImportCustomerRow> rows)
        : ICustomerImportReaderFactory,
            ICustomerImportReader
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

    private sealed class NoCatalogs : ICatalogResolver
    {
        public Task<Guid?> ResolveOccupationIdAsync(string occupationName, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);

        public Task<Guid?> ResolvePrincipalBusinessActivityIdAsync(string naicsCode, CancellationToken ct) =>
            Task.FromResult<Guid?>(null);
    }

    private sealed class NoProtector : ISensitiveDataProtector
    {
        public short CurrentKeyVersion => 1;

        public byte[] Protect(string plainText) => [];

        public string Unprotect(byte[] cipher) => string.Empty;

        public string ComputeBlindIndex(string plainText, Guid tenantId) => string.Empty;

        public IReadOnlyCollection<string> ComputeBlindIndexCandidates(string plainText, Guid tenantId) => [];
    }
}
