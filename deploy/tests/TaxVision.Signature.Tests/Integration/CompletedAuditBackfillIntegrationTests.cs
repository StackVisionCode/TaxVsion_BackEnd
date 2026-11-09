using BuildingBlocks.Results;
using BuildingBlocks.Security;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Audit;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-17 (D-SIG-17) contra SQL Server y el pipeline de Wolverine: la línea <c>RequestCompleted</c> que falta se agrega
/// al final de la cadena, encadenada con el último hash, sin tocar las anteriores. Tenant propio y limpieza por TenantId.
/// </summary>
public sealed class CompletedAuditBackfillIntegrationTests(SignatureApiFactory factory)
    : IClassFixture<SignatureApiFactory>,
        IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        var protector = factory.Services.GetRequiredService<ISecretProtector>();
        await using var db = NewContext();
        db.TenantSignatureSettings.Add(
            TenantSignatureSettings
                .CreateForNewTenant(_tenantId, protector.Protect(Convert.ToBase64String(Guid.NewGuid().ToByteArray())))
                .Value
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.SignatureAuditEvents.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.SignatureRequests.IgnoreQueryFilters().Where(r => r.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.TenantSignatureSettings.IgnoreQueryFilters().Where(s => s.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task CompletedRequestWithoutLine_GetsLineAtTheEnd_AndChainVerifies()
    {
        var request = await SeedCompletedAsync();
        await AppendAsync(request.Id, SignatureAuditEventKind.DocumentSigned);
        await AppendAsync(request.Id, SignatureAuditEventKind.RequestSealed);
        var before = await ListLinesAsync(request.Id);

        var report = await BackfillAsync(dryRun: false);

        Assert.Equal(1, report.MissingLines);
        Assert.Equal(1, report.LinesAdded);
        var lines = await ListLinesAsync(request.Id);
        Assert.Equal(3, lines.Count);
        // Las anteriores no cambian.
        Assert.Equal(before.Select(l => l.ChainHash), lines.Take(2).Select(l => l.ChainHash));
        var added = lines[^1];
        Assert.Equal(SignatureAuditEventKind.RequestCompleted, added.Kind);
        Assert.Equal(3, added.Sequence);
        Assert.Equal(before[^1].ChainHash, added.PreviousChainHash);
        Assert.Equal(await CompletedAtAsync(request.Id), added.OccurredAtUtc);
        Assert.Contains("\"Backfilled\":true", added.PayloadJson, StringComparison.Ordinal);
        Assert.Contains(request.Documents[0].Id.ToString(), added.PayloadJson, StringComparison.OrdinalIgnoreCase);
        Assert.True((await VerifyAsync(request.Id, lines)).IsIntact);
    }

    [Fact]
    public async Task SecondRun_AddsNothing()
    {
        var request = await SeedCompletedAsync();
        await AppendAsync(request.Id, SignatureAuditEventKind.DocumentSigned);

        var first = await BackfillAsync(dryRun: false);
        var second = await BackfillAsync(dryRun: false);

        Assert.Equal(1, first.LinesAdded);
        Assert.Equal(0, second.MissingLines);
        Assert.Equal(0, second.LinesAdded);
        Assert.Single(await ListLinesAsync(request.Id), l => l.Kind == SignatureAuditEventKind.RequestCompleted);
    }

    [Fact]
    public async Task DryRun_ReportsTheRequest_WithoutWriting()
    {
        var request = await SeedCompletedAsync();
        await AppendAsync(request.Id, SignatureAuditEventKind.DocumentSigned);

        var report = await BackfillAsync(dryRun: true);

        Assert.True(report.DryRun);
        Assert.Equal(1, report.MissingLines);
        Assert.Equal(0, report.LinesAdded);
        var item = Assert.Single(report.Items);
        Assert.Equal(request.Id, item.SignatureRequestId);
        Assert.Equal(1, item.ExistingLines);
        Assert.Single(await ListLinesAsync(request.Id));
    }

    [Fact]
    public async Task ChainWithItsCompletedLine_IsNotChanged()
    {
        // Caso de octubre: RequestSealed antes de RequestCompleted. No falta nada, solo cambia el orden.
        var request = await SeedCompletedAsync();
        await AppendAsync(request.Id, SignatureAuditEventKind.RequestSealed);
        await AppendAsync(request.Id, SignatureAuditEventKind.RequestCompleted);
        var before = await ListLinesAsync(request.Id);

        var report = await BackfillAsync(dryRun: false);

        Assert.Equal(0, report.MissingLines);
        Assert.Equal(before.Select(l => l.ChainHash), (await ListLinesAsync(request.Id)).Select(l => l.ChainHash));
    }

    [Fact]
    public async Task EmptyChain_StartsFromGenesis_WithTheBackfilledLine()
    {
        var request = await SeedCompletedAsync();

        var report = await BackfillAsync(dryRun: false);

        Assert.Equal(1, report.EmptyChains);
        var lines = await ListLinesAsync(request.Id);
        var line = Assert.Single(lines);
        Assert.Equal(SignatureAuditEventKind.RequestCompleted, line.Kind);
        Assert.Equal(SignatureAuditEvent.GenesisMarker, line.PreviousChainHash);
        Assert.True((await VerifyAsync(request.Id, lines)).IsIntact);
    }

    [Fact]
    public async Task AppendInFlight_BackfillWaitsForTheLock_AndGoesAfterIt()
    {
        var request = await SeedCompletedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
        await using var inFlight = await db.Database.BeginTransactionAsync();
        Assert.True(
            (
                await scope
                    .ServiceProvider.GetRequiredService<IAuditChainAppender>()
                    .AppendAsync(
                        _tenantId,
                        request.Id,
                        SignatureAuditEventKind.DocumentSigned,
                        DateTime.UtcNow,
                        new { }
                    )
            ).IsSuccess
        );
        await db.SaveChangesAsync();

        var backfill = BackfillAsync(dryRun: false);
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(backfill.IsCompleted);
        await inFlight.CommitAsync();
        var report = await backfill;

        Assert.Equal(1, report.LinesAdded);
        var lines = await ListLinesAsync(request.Id);
        Assert.Equal(
            [SignatureAuditEventKind.DocumentSigned, SignatureAuditEventKind.RequestCompleted],
            lines.Select(l => l.Kind)
        );
        Assert.True((await VerifyAsync(request.Id, lines)).IsIntact);
    }

    private async Task<CompletedAuditBackfillReport> BackfillAsync(bool dryRun)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<CompletedAuditBackfillReport>>(
                new BackfillCompletedAuditLinesCommand(_tenantId, dryRun)
            );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task AppendAsync(Guid requestId, SignatureAuditEventKind kind)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var appended = await scope
            .ServiceProvider.GetRequiredService<IAuditChainAppender>()
            .AppendAsync(_tenantId, requestId, kind, DateTime.UtcNow, new { Kind = kind.ToString() });
        Assert.True(appended.IsSuccess);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task<IReadOnlyList<SignatureAuditEvent>> ListLinesAsync(Guid requestId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<ISignatureAuditRepository>()
            .ListAsync(_tenantId, requestId);
    }

    private async Task<AuditChainVerification> VerifyAsync(Guid requestId, IReadOnlyList<SignatureAuditEvent> lines)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IAuditChainVerifier>()
            .VerifyAsync(_tenantId, requestId, lines);
    }

    private async Task<DateTime?> CompletedAtAsync(Guid requestId)
    {
        await using var db = NewContext();
        return await db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r => r.Id == requestId)
            .Select(r => r.CompletedAtUtc)
            .SingleAsync();
    }

    private async Task<SignatureRequest> SeedCompletedAsync()
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Completed", null, "Fiscal", 72, false, false, false)
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
        var signedAt = DateTime.UtcNow.AddDays(-3);
        Assert.True(request.Send(signedAt.AddHours(-1)).IsSuccess);
        Assert.True(request.MarkSignerSigned(signer.Id, signedAt, "127.0.0.1", "tests").IsSuccess);
        Assert.Equal(SignatureRequestStatus.Completed, request.Status);

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
