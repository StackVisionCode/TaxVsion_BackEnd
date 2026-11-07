using System.Collections.Concurrent;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Scheduling;
using Wolverine.Tracking;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-10 contra SQL real, en base temporal: nunca toca datos de la flota. El borrado y su aviso se confirman
/// juntos. Las solicitudes llevan firmante y campo, que es lo que el borrado necesita cargado.
/// </summary>
public sealed class PurgeSchedulerIntegrationTests(PurgeHostFixture host) : IClassFixture<PurgeHostFixture>
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact(Timeout = 120_000)]
    public async Task PurgedRequest_PublishesPurgedEventInSameUnit()
    {
        var purged = await SeedCanceledAsync();
        var other = await SeedCanceledAsync();

        var sent = await PurgeAsync(purged, other);

        Assert.False(await ExistsAsync(purged));
        Assert.False(await ExistsAsync(other));
        Assert.Equal(new[] { purged, other }.Order(), sent.Select(evt => evt.SignatureRequestId).Order());
        Assert.All(sent, evt => Assert.Equal(_tenantId, evt.TenantId));
    }

    [Fact(Timeout = 120_000)]
    public async Task FailedDelete_KeepsTheRequestAndPublishesNothing_TheOthersGoOn()
    {
        var failing = await SeedCanceledAsync();
        var other = await SeedCanceledAsync();
        host.Failures.Requests.TryAdd(failing, 0);

        var sent = await PurgeAsync(failing, other);

        Assert.True(await ExistsAsync(failing));
        Assert.False(await ExistsAsync(other));
        Assert.Equal(other, Assert.Single(sent).SignatureRequestId);
    }

    // Candidata al listar, pero la oficina la retuvo antes de llegar a su unidad.
    [Fact(Timeout = 120_000)]
    public async Task LegalHoldAfterListing_IsNotPurged()
    {
        var held = await SeedCanceledAsync();
        await using (var db = host.Database.NewContext(_tenantId))
        {
            await db
                .SignatureRequests.IgnoreQueryFilters()
                .Where(r => r.Id == held)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.LegalHold, true));
        }

        var sent = await PurgeAsync(held);

        Assert.True(await ExistsAsync(held));
        Assert.Empty(sent);
    }

    private async Task<IReadOnlyList<SignatureRequestPurgedIntegrationEvent>> PurgeAsync(params Guid[] requestIds)
    {
        var job = new PurgeScheduler(
            host.Factory.Services,
            Options.Create(new PurgeSchedulerOptions { Enabled = true }),
            NullLogger<PurgeScheduler>.Instance
        );

        var session = await host
            .Factory.Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(60))
            .DoNotAssertOnExceptionsDetected()
            .ExecuteAndWaitAsync(_ => job.PurgeAsync(_tenantId, requestIds, DateTime.UtcNow, CancellationToken.None));

        return session
            .Sent.MessagesOf<SignatureRequestPurgedIntegrationEvent>()
            .Where(evt => evt.TenantId == _tenantId)
            .ToList();
    }

    private async Task<bool> ExistsAsync(Guid requestId)
    {
        await using var db = host.Database.NewContext(_tenantId);
        return await db.SignatureRequests.IgnoreQueryFilters().AnyAsync(r => r.Id == requestId);
    }

    private async Task<Guid> SeedCanceledAsync()
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Old", null, "Fiscal", 72, false, false, false)
            .Value;
        var document = request.AddDocument(limits, Guid.NewGuid(), "Return").Value;
        var signer = request
            .AddSigner(limits, SignerEmail.Create("s@example.test").Value, SignerFullName.Create("Signer").Value, null)
            .Value;
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
        Assert.True(request.Send(DateTime.UtcNow.AddYears(-9)).IsSuccess);
        Assert.True(request.Cancel(DateTime.UtcNow.AddYears(-9)).IsSuccess);

        await using var db = host.Database.NewContext(_tenantId);
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }
}

/// <summary>Host real de Signature sobre una base temporal, con fallos inyectados al borrar.</summary>
public sealed class PurgeHostFixture : IAsyncLifetime
{
    private HostFactory? _factory;

    internal SignatureTemporaryDatabase Database { get; } = new();
    internal PurgeFailureInterceptor Failures { get; } = new();
    internal SignatureApiFactory Factory => _factory!;

    public async Task InitializeAsync()
    {
        await Database.CreateAsync();
        _factory = new HostFactory(Database.ConnectionString, Failures);
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await Database.DropAsync();
    }

    private sealed class HostFactory(string connectionString, PurgeFailureInterceptor failures) : SignatureApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.ConfigureTestServices(services =>
                services.ConfigureDbContext<SignatureDbContext>(options => options.AddInterceptors(failures))
            );
        }
    }
}

/// <summary>Falla el guardado que borra una solicitud marcada.</summary>
internal sealed class PurgeFailureInterceptor : SaveChangesInterceptor
{
    public ConcurrentDictionary<Guid, byte> Requests { get; } = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        var deleting = eventData
            .Context!.ChangeTracker.Entries<SignatureRequest>()
            .Where(entry => entry.State == EntityState.Deleted)
            .Select(entry => entry.Entity.Id);
        if (deleting.Any(Requests.ContainsKey))
            throw new InvalidOperationException("Injected failure while deleting.");
        return ValueTask.FromResult(result);
    }
}
