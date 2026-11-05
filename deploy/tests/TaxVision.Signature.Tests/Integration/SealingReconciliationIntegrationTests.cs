using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
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
/// SIG-2 contra el SQL Server real y el host de Signature (sin transportes externos): una pasada del job vuelve a
/// pedir el sellado de la completada sin sellar y deja fuera la que ya se dio por fallida. El corte por fecha
/// deja fuera las solicitudes reales de la base local. Tenant propio y limpieza por TenantId.
/// </summary>
public sealed class SealingReconciliationIntegrationTests(SignatureApiFactory factory)
    : IClassFixture<SignatureApiFactory>,
        IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.SignatureRequests.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task CompletedWithUnsealedDocs_RepublishesReadyForSealing_Once()
    {
        var completedAt = DateTime.UtcNow.AddMinutes(-20);
        var pending = await SeedCompletedAsync(completedAt, sealingFailed: false);
        var failed = await SeedCompletedAsync(completedAt, sealingFailed: true);
        var job = new SealingReconciliationScheduler(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            factory.Services.GetRequiredService<IHostApplicationLifetime>(),
            Options.Create(
                new SealingReconciliationOptions
                {
                    Grace = TimeSpan.FromMinutes(10),
                    CompletedAfterUtc = DateTime.UtcNow.AddHours(-1),
                }
            ),
            TimeProvider.System,
            NullLogger<SealingReconciliationScheduler>.Instance
        );

        var session = await factory
            .Services.GetRequiredService<IHost>()
            .TrackActivity()
            .Timeout(TimeSpan.FromSeconds(60))
            .ExecuteAndWaitAsync(_ => job.RunOnceAsync(CancellationToken.None));

        var ready = session
            .Sent.MessagesOf<SignatureDocumentsReadyForSealingIntegrationEvent>()
            .Where(evt => evt.TenantId == _tenantId)
            .ToList();
        var single = Assert.Single(ready);
        Assert.Equal(pending.Id, single.SignatureRequestId);
        Assert.Equal(pending.Documents.Select(d => d.Id), single.DocumentIds);
        Assert.DoesNotContain(ready, evt => evt.SignatureRequestId == failed.Id);
    }

    private async Task<SignatureRequest> SeedCompletedAsync(DateTime completedAtUtc, bool sealingFailed)
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Unsealed", null, "Fiscal", 72, false, false, false)
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
        Assert.True(request.Send(completedAtUtc.AddMinutes(-5)).IsSuccess);
        Assert.True(request.MarkSignerSigned(signer.Id, completedAtUtc, "127.0.0.1", "tests").IsSuccess);
        if (sealingFailed)
            request.MarkSealingFailed(completedAtUtc.AddMinutes(1));

        await using var db = NewContext();
        db.SignatureRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    private SignatureDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<SignatureDbContext>()
                .UseSqlServer(SignatureTestDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenantId)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
