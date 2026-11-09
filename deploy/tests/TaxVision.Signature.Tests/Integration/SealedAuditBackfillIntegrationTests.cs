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
/// SIG-2 (D-SIG-2) contra el SQL Server real y el pipeline de Wolverine: el backfill agrega la línea
/// <c>RequestSealed</c> que le falta a un documento sellado, una sola vez, con el candado de SIG-4 dentro de la
/// transacción del handler. Una línea antigua sin DocumentId pero con el SealedFileId cuenta como presente.
/// Tenant propio y limpieza por TenantId.
/// </summary>
public sealed class SealedAuditBackfillIntegrationTests(SignatureApiFactory factory)
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
    public async Task SealedDocumentWithoutLine_GetsOneLine_AndSecondRunAddsNothing()
    {
        var missing = await SeedSealedAsync();
        var legacy = await SeedSealedAsync();
        await AppendLegacyLineAsync(legacy);

        var first = await BackfillAsync();
        var second = await BackfillAsync();

        Assert.Equal(1, first.MissingLines);
        Assert.Equal(1, first.LinesAdded);
        Assert.Equal(0, second.MissingLines);
        await using var scope = factory.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<ISignatureAuditRepository>();
        var lines = await audit.ListAsync(_tenantId, missing.Id);
        var line = Assert.Single(lines);
        Assert.Equal(SignatureAuditEventKind.RequestSealed, line.Kind);
        Assert.Contains(missing.Documents[0].Id.ToString(), line.PayloadJson, StringComparison.OrdinalIgnoreCase);
        var verification = await scope
            .ServiceProvider.GetRequiredService<IAuditChainVerifier>()
            .VerifyAsync(_tenantId, missing.Id, lines);
        Assert.True(verification.IsIntact);
    }

    private async Task<SealedAuditBackfillReport> BackfillAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope
            .ServiceProvider.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<SealedAuditBackfillReport>>(new BackfillSealedAuditLinesCommand(_tenantId, false));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    // Línea de antes del multi-documento: solo el archivo sellado, sin DocumentId.
    private async Task AppendLegacyLineAsync(SignatureRequest request)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SignatureDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var appended = await scope
            .ServiceProvider.GetRequiredService<IAuditChainAppender>()
            .AppendAsync(
                _tenantId,
                request.Id,
                SignatureAuditEventKind.RequestSealed,
                DateTime.UtcNow,
                new { SealedFileId = request.Documents[0].SealedFileId }
            );
        Assert.True(appended.IsSuccess);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private async Task<SignatureRequest> SeedSealedAsync()
    {
        var limits = TestSignatureLimits.Default;
        var request = SignatureRequest
            .CreateDraft(limits, _tenantId, Guid.NewGuid(), "Sealed", null, "Fiscal", 72, false, false, false)
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
        Assert.True(request.Send(DateTime.UtcNow).IsSuccess);
        Assert.True(request.MarkSignerSigned(signer.Id, DateTime.UtcNow, "127.0.0.1", "tests").IsSuccess);
        Assert.True(
            request
                .MarkDocumentSealed(
                    document.Id,
                    Guid.NewGuid(),
                    DocumentHash.Create(new string('b', 64)).Value,
                    DateTime.UtcNow
                )
                .IsSuccess
        );

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
