using System.Collections.Concurrent;
using System.Reflection;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;
using TaxVision.Signature.Infrastructure.Scheduling;
using Wolverine;
using Wolverine.Tracking;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-15 contra SQL real y el host de Signature, en base temporal: dos réplicas del mismo job en paralelo. La
/// segunda lista antes de que confirme la primera y sigue después: es el orden que la <c>RowVersion</c> no detiene,
/// porque carga la fila ya confirmada. El candado en memoria tiene la semántica de SET NX del de Redis, sin chocar
/// con las claves de la flota local.
/// </summary>
public sealed class SchedulerSingleReplicaIntegrationTests(SchedulerHostFixture host)
    : IClassFixture<SchedulerHostFixture>
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact(Timeout = 120_000)]
    public async Task Reminder_TwoReplicasInParallel_ProcessesEachRequestOnce()
    {
        var requestId = await SeedInProgressAsync(sentHoursAgo: 100, expirationHours: 168);

        var session = await RunBothAsync(
            () => NewReminderJob().RunOnceAsync(CancellationToken.None),
            () => NewReminderJob().RunOnceAsync(CancellationToken.None)
        );

        // El daño primero: un aviso de más al firmante. El listado solo dice que la réplica no entró.
        Assert.Single(
            session.Sent.MessagesOf<SignatureRequestReminderDueIntegrationEvent>(),
            evt => evt.SignatureRequestId == requestId
        );
        await using var db = host.Database.NewContext(_tenantId);
        var sent = await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r => r.Id == requestId)
            .Select(r => r.RemindersSent)
            .SingleAsync();
        Assert.Equal(1, sent);
        Assert.Equal(1, host.Gate.Listed);
    }

    [Fact(Timeout = 120_000)]
    public async Task Expiration_TwoReplicasInParallel_ProcessesEachRequestOnce()
    {
        var requestId = await SeedInProgressAsync(sentHoursAgo: 100, expirationHours: 72);

        var session = await RunBothAsync(
            () => NewExpirationJob().RunOnceAsync(CancellationToken.None),
            () => NewExpirationJob().RunOnceAsync(CancellationToken.None)
        );

        Assert.Single(
            session.Sent.MessagesOf<SignatureRequestExpiredIntegrationEvent>(),
            evt => evt.SignatureRequestId == requestId
        );
        Assert.Equal(1, host.Gate.Listed);
    }

    [Fact(Timeout = 120_000)]
    public async Task ReadyReconciliation_TwoReplicasInParallel_ListsOnce()
    {
        await SeedStrandedDraftAsync();

        await RunBothAsync(
            () => NewReadyJob().RunOnceAsync(CancellationToken.None),
            () => NewReadyJob().RunOnceAsync(CancellationToken.None)
        );

        Assert.Equal(1, host.Gate.Listed);
    }

    private ReminderScheduler NewReminderJob() => new(host.Factory.Services, NullLogger<ReminderScheduler>.Instance);

    private ExpirationScheduler NewExpirationJob() =>
        new(host.Factory.Services, NullLogger<ExpirationScheduler>.Instance);

    private ReadyReconciliationScheduler NewReadyJob() =>
        new(host.Factory.Services, NullLogger<ReadyReconciliationScheduler>.Instance);

    private async Task<ITrackedSession> RunBothAsync(Func<Task> first, Func<Task> second)
    {
        host.Gate.Reset();
        return await host
            .Factory.Services.GetRequiredService<IHost>()
            .TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .Timeout(TimeSpan.FromSeconds(60))
            .ExecuteAndWaitAsync(
                (Func<IMessageContext, Task>)(
                    async _ =>
                    {
                        var firstRun = first();
                        await host.Gate.FirstListed.WaitAsync(TimeSpan.FromSeconds(20));
                        var secondRun = second();
                        await firstRun;
                        host.Gate.FirstFinished();
                        await secondRun;
                    }
                )
            );
    }

    private async Task<Guid> SeedInProgressAsync(int sentHoursAgo, int expirationHours)
    {
        var request = NewRequest(expirationHours, out _);
        Assert.True(request.Send(DateTime.UtcNow.AddHours(-sentHoursAgo)).IsSuccess);
        await using var db = host.Database.NewContext(_tenantId);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task SeedStrandedDraftAsync()
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Stranded", null, "Fiscal", 72, false, false, false)
            .Value;
        request.AddDocument(limits, Guid.NewGuid(), "Return");
        await using var db = host.Database.NewContext(_tenantId);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r => r.Id == request.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.CreatedAtUtc, DateTime.UtcNow.AddHours(-1)));
    }

    private SignatureRequest NewRequest(int expirationHours, out Guid signerId)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Job", null, "Fiscal", expirationHours, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Return").Value;
        var signer = request
            .AddSigner(
                limits,
                SignerEmail.Create("s@example.test").Value,
                SignerFullName.Create("Signer One").Value,
                null
            )
            .Value;
        signerId = signer.Id;
        Assert.True(
            request
                .PlaceField(
                    TestSignatureLimits.MaxFieldsPerSigner,
                    signer.Id,
                    document.Id,
                    SignatureFieldKind.Signature,
                    FieldPosition.Create(1, 0.1, 0.1, 0.2, 0.05).Value,
                    null,
                    true
                )
                .IsSuccess
        );
        Assert.True(
            request
                .AttachVerifiedDocument(
                    document.Id,
                    VerifiedDocument.Create(DocumentHash.Create(new string('a', 64)).Value, 5).Value
                )
                .IsSuccess
        );
        return request;
    }
}

/// <summary>Host real de Signature sobre una base temporal, con el candado y el listado de candidatos controlados.</summary>
public sealed class SchedulerHostFixture : IAsyncLifetime
{
    private HostFactory? _factory;

    internal SignatureTemporaryDatabase Database { get; } = new();
    internal CandidateListGate Gate { get; } = new();
    internal SignatureApiFactory Factory => _factory!;

    public async Task InitializeAsync()
    {
        await Database.CreateAsync();
        _factory = new HostFactory(Database.ConnectionString, Gate);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await Database.DropAsync();
    }

    private sealed class HostFactory(string connectionString, CandidateListGate gate) : SignatureApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDistributedLock>();
                services.AddSingleton<IDistributedLock, InMemoryDistributedLock>();
                services.RemoveAll<ISigningTokenService>();
                services.AddSingleton<ISigningTokenService, FixedTokenService>();
                services.RemoveAll<ISignatureRequestRepository>();
                services.AddScoped(provider =>
                    GatedRepository.Wrap(
                        new SignatureRequestRepository(provider.GetRequiredService<SignatureDbContext>()),
                        gate
                    )
                );
            });
        }
    }
}

/// <summary>Ordena dos réplicas: la segunda, si llega a listar, sigue solo cuando la primera ya confirmó.</summary>
internal sealed class CandidateListGate
{
    private static readonly TimeSpan Overlap = TimeSpan.FromSeconds(1);

    private int _listed;
    private TaskCompletionSource _firstListed = NewSignal();
    private TaskCompletionSource _firstFinished = NewSignal();

    public int Listed => Volatile.Read(ref _listed);
    public Task FirstListed => _firstListed.Task;

    public void Reset()
    {
        Volatile.Write(ref _listed, 0);
        _firstListed = NewSignal();
        _firstFinished = NewSignal();
    }

    public void FirstFinished() => _firstFinished.TrySetResult();

    public async Task AfterListAsync()
    {
        if (Interlocked.Increment(ref _listed) == 1)
        {
            _firstListed.TrySetResult();
            // La primera réplica sostiene su pasada: la otra arranca con el candado ocupado.
            await Task.Delay(Overlap);
            return;
        }
        await _firstFinished.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Deja pasar todo y avisa al <see cref="CandidateListGate"/> después de listar candidatos de un job.</summary>
public class GatedRepository : DispatchProxy
{
    private static readonly string[] CandidateQueries =
    [
        nameof(ISignatureRequestRepository.ListReminderCandidatesAsync),
        nameof(ISignatureRequestRepository.ListExpiredCandidatesAsync),
        nameof(ISignatureRequestRepository.ListStrandedDraftsAsync),
    ];

    private ISignatureRequestRepository _inner = default!;
    private CandidateListGate _gate = default!;

    internal static ISignatureRequestRepository Wrap(ISignatureRequestRepository inner, CandidateListGate gate)
    {
        var proxy = Create<ISignatureRequestRepository, GatedRepository>();
        var gated = (GatedRepository)(object)proxy;
        gated._inner = inner;
        gated._gate = gate;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var result = targetMethod!.Invoke(_inner, args);
        if (!CandidateQueries.Contains(targetMethod.Name))
            return result;

        var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
        return typeof(GatedRepository)
            .GetMethod(nameof(ThenGateAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(resultType)
            .Invoke(this, [result]);
    }

    private async Task<T> ThenGateAsync<T>(Task<T> query)
    {
        var listed = await query;
        await _gate.AfterListAsync();
        return listed;
    }
}

/// <summary>SET NX en memoria: mismo contrato que <c>RedisDistributedLock</c>.</summary>
internal sealed class InMemoryDistributedLock : IDistributedLock
{
    private readonly ConcurrentDictionary<string, byte> _held = new();

    public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<ILockHandle>(new Handle(key, _held.TryAdd(key, 0), _held));

    private sealed class Handle(string key, bool acquired, ConcurrentDictionary<string, byte> held) : ILockHandle
    {
        public bool IsAcquired => acquired;
        public string Key => key;

        public ValueTask DisposeAsync()
        {
            if (acquired)
                held.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }
}
