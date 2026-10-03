using BuildingBlocks.Persistence;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Payables.ResolvePayable;
using TaxVision.PaymentClient.Domain.Payables;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>
/// El resolver de la URL estable — la que va en el PDF y vive años. Acuña un link de checkout nuevo
/// cuando no hay ninguno vigente, y ese es el punto delicado: sin guardas, una factura ya cobrada
/// vuelve a ser pagable cada vez que alguien abre el enlace del PDF. Camino de doble cobro, medido
/// en producción el 2026-10-03.
///
/// <para>Hay DOS guardas a propósito. La del payable liquidado la pone <c>InvoicePaidConsumer</c> y
/// cubre lo que se cobre de ahora en adelante. La del link en <c>Used</c> cubre lo ya cobrado antes
/// de que el consumer existiera, que no tiene evento que reemitir.</para>
/// </summary>
public sealed class ResolvePayableHandlerTests
{
    private const string Reference = "186PB7xr6_NdKu6OyeRrzeAigpRmB23vbTQHGPoKYyM";
    private static readonly Guid TenantId = Guid.NewGuid();
    private const string InvoiceId = "7f14aa68-3927-482a-8ad3-aab4ec989fa8";

    private static PayableReference CreatePayable() =>
        PayableReference
            .Create(
                TenantId,
                PaymentPurposeKind.InvoicePayment,
                InvoiceId,
                Money.Create(12500L, "USD").Value,
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task A_payable_with_no_live_link_mints_one()
    {
        // El caso para el que existe la creación perezosa: el QR de un PDF viejo no puede quedar muerto.
        var payables = new FakePayables(CreatePayable());
        var links = new FakeLinks();

        var result = await Resolve(payables, links);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.CheckoutToken));
        Assert.Equal(1, links.Added);
    }

    [Fact]
    public async Task A_settled_payable_is_refused_and_mints_nothing()
    {
        var payable = CreatePayable();
        payable.Settle(DateTime.UtcNow);
        var links = new FakeLinks();

        var result = await Resolve(new FakePayables(payable), links);

        Assert.True(result.IsFailure);
        Assert.Equal("Payable.AlreadyPaid", result.Error.Code);
        Assert.Equal(0, links.Added);
    }

    [Fact]
    public async Task A_payable_with_a_USED_link_is_refused_even_if_it_was_never_settled()
    {
        // La guarda que de verdad arregla el caso reportado: esas facturas se cobraron antes de que
        // existiera InvoicePaidConsumer, así que su payable nunca se liquidó. Sin esta guarda, el
        // enlace del PDF seguiría acuñando links nuevos para siempre.
        var payable = CreatePayable();
        Assert.False(payable.IsSettled);
        var links = new FakeLinks { HasUsedLink = true };

        var result = await Resolve(new FakePayables(payable), links);

        Assert.True(result.IsFailure);
        Assert.Equal("Payable.AlreadyPaid", result.Error.Code);
        Assert.Equal(0, links.Added);
    }

    [Fact]
    public async Task A_voided_payable_still_says_voided_not_paid()
    {
        // Anulada y pagada son dos cosas distintas y el cliente tiene que leer cuál de las dos es.
        var payable = CreatePayable();
        payable.Revoke(DateTime.UtcNow);

        var result = await Resolve(new FakePayables(payable), new FakeLinks());

        Assert.Equal("Payable.Revoked", result.Error.Code);
    }

    [Fact]
    public async Task An_unknown_reference_is_not_found()
    {
        var result = await Resolve(new FakePayables(null), new FakeLinks());

        Assert.Equal("Payable.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Settle_is_idempotent()
    {
        // El evento de Billing puede reentregarse.
        var payable = CreatePayable();
        var first = DateTime.UtcNow;
        payable.Settle(first);
        payable.Settle(first.AddHours(1));

        Assert.Equal(first, payable.SettledAtUtc);
    }

    private static Task<BuildingBlocks.Results.Result<ResolvePayableResponse>> Resolve(
        FakePayables payables,
        FakeLinks links
    ) =>
        ResolvePayableHandler.Handle(
            new ResolvePayableCommand(Reference),
            payables,
            links,
            new FakeTenants(),
            new NoOpUnitOfWork(),
            CancellationToken.None
        );

    private sealed class FakePayables(PayableReference? payable) : IPayableReferenceRepository
    {
        public Task<PayableReference?> GetByReferenceAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(payable);

        public Task<PayableReference?> GetByExternalReferenceAsync(
            Guid tenantId,
            PaymentPurposeKind kind,
            string externalReferenceId,
            CancellationToken ct = default
        ) => Task.FromResult(payable);

        public Task AddAsync(PayableReference p, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeLinks : IPaymentLinkRepository
    {
        public bool HasUsedLink { get; init; }
        public int Added { get; private set; }

        public Task<bool> AnyUsedForExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            CancellationToken ct = default
        ) => Task.FromResult(HasUsedLink);

        public Task<PaymentLink?> GetActiveByExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            CancellationToken ct = default
        ) => Task.FromResult<PaymentLink?>(null);

        public Task AddAsync(PaymentLink link, CancellationToken ct = default)
        {
            Added++;
            return Task.CompletedTask;
        }

        public Task<PaymentLink?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<PaymentLink?> GetByTokenAsync(string token, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<PaymentLink?> GetByRelatedTenantPaymentIdAsync(Guid id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<PaymentLink>> SearchByTenantAsync(
            Guid tenantId,
            PaymentLinkStatus? status,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<PaymentLink>> GetActiveExpiredBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    private sealed class FakeTenants : ITenantRegistry
    {
        public Task<TaxVision.PaymentClient.Domain.Tenants.Tenant?> GetByIdAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<TaxVision.PaymentClient.Domain.Tenants.Tenant?>(null);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            BuildingBlocks.Tenancy.TenantKind kind,
            string defaultTimeZoneId,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task UpdateStatusAsync(
            Guid tenantId,
            string status,
            bool isActive,
            DateTime nowUtc,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
