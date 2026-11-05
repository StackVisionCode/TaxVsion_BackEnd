using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Payables.ResolvePayable;
using TaxVision.PaymentClient.Domain.Payables;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>
/// Contra el SQL Server real: dos aperturas simultáneas de la URL estable dejan un solo link Active.
/// </summary>
public sealed class PayableResolverConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("PAYMENTCLIENT_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("7c2a4f19-3e6b-4d02-8a91-paymentclient1")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set PAYMENTCLIENT_DB_CONNECTION or the PaymentClient user-secrets to run this test."
        );

    private PayableReference _payable = null!;

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private PaymentClientDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<PaymentClientDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        _payable = PayableReference
            .Create(
                _tenantId,
                PaymentPurposeKind.InvoicePayment,
                $"inv-{Guid.NewGuid():N}",
                Money.Create(15000, "USD").Value,
                DateTime.UtcNow
            )
            .Value;
        db.PayableReferences.Add(_payable);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.PaymentLinks.IgnoreQueryFilters().Where(l => l.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.PayableReferences.IgnoreQueryFilters().Where(p => p.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TwoResolvesInParallel_MintOneActiveLink()
    {
        var barrier = new Barrier(2);

        var tokens = await Task.WhenAll(ResolveWithRetryAsync(barrier), ResolveWithRetryAsync(barrier));

        await using var db = NewContext();
        var active = await db
            .PaymentLinks.IgnoreQueryFilters()
            .CountAsync(l => l.TenantId == _tenantId && l.Status == PaymentLinkStatus.Active);

        Assert.Equal(1, active);
        Assert.Equal(tokens[0], tokens[1]);
    }

    /// <summary>Primer intento sincronizado con el otro; si pierde la carrera, reintenta con contexto nuevo.</summary>
    private async Task<string> ResolveWithRetryAsync(Barrier barrier)
    {
        try
        {
            await using var db = NewContext();
            return await ResolveAsync(db, new BarrierLinks(new PaymentLinkRepository(db), barrier));
        }
        catch (DbUpdateException)
        {
            await using var retry = NewContext();
            return await ResolveAsync(retry, new PaymentLinkRepository(retry));
        }
    }

    private async Task<string> ResolveAsync(PaymentClientDbContext db, IPaymentLinkRepository links)
    {
        var result = await ResolvePayableHandler.Handle(
            new ResolvePayableCommand(_payable.Reference),
            new PayableReferenceRepository(db),
            links,
            new TenantPaymentRepository(db),
            new TenantRegistry(db),
            db,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value.CheckoutToken;
    }

    /// <summary>Hace que los dos resolvers lean "no hay link" antes de que cualquiera guarde.</summary>
    private sealed class BarrierLinks(IPaymentLinkRepository inner, Barrier barrier) : IPaymentLinkRepository
    {
        public async Task<PaymentLink?> GetActiveByExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            CancellationToken ct = default
        )
        {
            var link = await inner.GetActiveByExternalReferenceAsync(tenantId, externalReferenceId, ct);
            await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(10)), ct);
            return link;
        }

        public Task<PaymentLink?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
            inner.GetByIdAsync(id, tenantId, ct);

        public Task<PaymentLink?> GetByTokenAsync(string token, CancellationToken ct = default) =>
            inner.GetByTokenAsync(token, ct);

        public Task<PaymentLink?> GetByRelatedTenantPaymentIdAsync(Guid id, CancellationToken ct = default) =>
            inner.GetByRelatedTenantPaymentIdAsync(id, ct);

        public Task<bool> AnyUsedForExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            DateTime? usedSinceUtc = null,
            CancellationToken ct = default
        ) => inner.AnyUsedForExternalReferenceAsync(tenantId, externalReferenceId, usedSinceUtc, ct);

        public Task<IReadOnlyList<PaymentLink>> SearchByTenantAsync(
            Guid tenantId,
            PaymentLinkStatus? status,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => inner.SearchByTenantAsync(tenantId, status, page, pageSize, ct);

        public Task<IReadOnlyList<PaymentLink>> GetActiveExpiredBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => inner.GetActiveExpiredBeforeAsync(cutoffUtc, batchSize, ct);

        public Task AddAsync(PaymentLink link, CancellationToken ct = default) => inner.AddAsync(link, ct);
    }
}
