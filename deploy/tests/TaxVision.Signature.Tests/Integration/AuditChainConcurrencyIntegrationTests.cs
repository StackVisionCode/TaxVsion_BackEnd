using BuildingBlocks.Security;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Audit;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Infrastructure.Audit;
using TaxVision.Signature.Infrastructure.Persistence;
using TaxVision.Signature.Infrastructure.Persistence.Repositories;

namespace TaxVision.Signature.Tests.Integration;

/// <summary>
/// SIG-4 contra el SQL Server real: varios appends en paralelo a la cadena de la misma solicitud, cada uno en
/// su transacción y sin reintentos, se ordenan en vez de chocar por el número de secuencia.
/// Tenant propio y limpieza por TenantId.
/// </summary>
public sealed class AuditChainConcurrencyIntegrationTests : IAsyncLifetime
{
    private const int ParallelAppends = 6;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _requestId = Guid.NewGuid();
    private readonly PlainSecretProtector _protector = new();

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        db.TenantSignatureSettings.Add(
            TenantSignatureSettings.CreateForNewTenant(_tenantId, _protector.Protect("audit-secret-for-tests")).Value
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.SignatureAuditEvents.IgnoreQueryFilters().Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.TenantSignatureSettings.IgnoreQueryFilters().Where(s => s.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task SixParallelAppendsSameRequest_AllPersistWithContiguousSequence()
    {
        var start = new TaskCompletionSource();
        var appends = Enumerable
            .Range(1, ParallelAppends)
            .Select(i => AppendInOwnTransactionAsync(i, start.Task))
            .ToList();
        start.SetResult();
        await Task.WhenAll(appends);

        await using var db = NewContext();
        var events = await new SignatureAuditRepository(db).ListAsync(_tenantId, _requestId);
        Assert.Equal(Enumerable.Range(1, ParallelAppends).Select(i => (long)i), events.Select(e => e.Sequence));
        var verification = await new HmacAuditChainVerifier(
            new TenantSignatureSettingsRepository(db),
            _protector
        ).VerifyAsync(_tenantId, _requestId, events);
        Assert.True(verification.IsIntact);
    }

    private async Task AppendInOwnTransactionAsync(int index, Task start)
    {
        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await start;
        var appender = NewAppender(db);
        var result = await appender.AppendAsync(
            _tenantId,
            _requestId,
            SignatureAuditEventKind.SignerViewed,
            DateTime.UtcNow,
            new { Index = index }
        );
        Assert.True(result.IsSuccess);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private HmacAuditChainAppender NewAppender(SignatureDbContext db) =>
        new(
            new SignatureAuditRepository(db),
            new TenantSignatureSettingsRepository(db),
            _protector,
            new SqlAuditChainLock(db)
        );

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

    // Sin cifrado real: la prueba es de orden, no de criptografía del secreto.
    private sealed class PlainSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;

        public bool TryUnprotect(string? protectedValue, out string plaintext, out SecretUnprotectFailure failure)
        {
            plaintext = protectedValue ?? string.Empty;
            failure = default;
            return protectedValue is not null;
        }
    }
}
