using BuildingBlocks.Persistence;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Payables.ResolvePayable;
using TaxVision.PaymentClient.Domain.Payables;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

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

    private static PayableReference CreatePayable(long amountCents = 12500L) =>
        PayableReference
            .Create(
                TenantId,
                PaymentPurposeKind.InvoicePayment,
                InvoiceId,
                Money.Create(amountCents, "USD").Value,
                DateTime.UtcNow
            )
            .Value;

    private static PaymentLink CreateLink(long amountCents) =>
        PaymentLink
            .Create(
                TenantId,
                null,
                Money.Create(amountCents, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, InvoiceId).Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public async Task Resolve_WhenPayableAmountChanged_ReplacesActiveLink()
    {
        // Con BIL-1 el monto baja tras cada pago parcial: el link vigente no puede seguir cobrando el viejo.
        var payable = CreatePayable(15000L);
        var old = CreateLink(15000L);
        payable.UpdateAmount(Money.Create(5000L, "USD").Value, DateTime.UtcNow);
        var links = new FakeLinks { Active = old };

        var result = await Resolve(new FakePayables(payable), links);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(old.Token.Value, result.Value.CheckoutToken);
        Assert.Equal(5000L, links.LastAdded!.Amount.AmountCents);
        Assert.Equal(PaymentLinkStatus.Expired, old.Status);
    }

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

    [Fact]
    public async Task Resolve_WhenLatestLinkExpiredWithAttemptRequiresAction_ReturnsPaymentInProgress()
    {
        // El link venció con un 3DS en curso: acuñar otro haría pagar dos veces al contribuyente.
        var links = new FakeLinks();
        var payments = new FakePayments { HasAttemptInProgress = true };

        var result = await Resolve(new FakePayables(CreatePayable()), links, payments);

        Assert.True(result.IsFailure);
        Assert.Equal("Payable.PaymentInProgress", result.Error.Code);
        Assert.Equal(0, links.Added);
    }

    [Fact]
    public async Task Resolve_WhenSucceededPaymentAfterLastAmountChange_ReturnsAlreadyPaid()
    {
        // InvoicePaid de Billing aún no llegó, pero el saldo vigente ya se cobró: no se acuña otro link.
        var changedAt = DateTime.UtcNow.AddMinutes(-10);
        var payable = CreatePayable(15000L);
        payable.UpdateAmount(Money.Create(5000L, "USD").Value, changedAt);
        var links = new FakeLinks();

        var result = await Resolve(
            new FakePayables(payable),
            links,
            await PaymentsWithSucceededAsync(paidAtUtc: changedAt.AddMinutes(5))
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Payable.AlreadyPaid", result.Error.Code);
        Assert.Equal(0, links.Added);
    }

    [Fact]
    public async Task Resolve_WhenSucceededPaymentBeforeLastAmountChange_MintsLink()
    {
        // Pago parcial y después Billing bajó el saldo: queda algo por cobrar.
        var changedAt = DateTime.UtcNow.AddMinutes(-10);
        var payable = CreatePayable(15000L);
        payable.UpdateAmount(Money.Create(5000L, "USD").Value, changedAt);
        var links = new FakeLinks();

        var result = await Resolve(
            new FakePayables(payable),
            links,
            await PaymentsWithSucceededAsync(paidAtUtc: changedAt.AddMinutes(-5))
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, links.Added);
        Assert.Equal(5000L, links.LastAdded!.Amount.AmountCents);
    }

    [Fact]
    public async Task Resolve_WhenAmountUpdatedAtNull_DoesNotShortcutAlreadyPaid()
    {
        // Payable sin cambio de monto registrado (anterior al deploy): se cobra el saldo de Billing como hoy.
        var payable = CreatePayable(15000L);
        Assert.Null(payable.AmountUpdatedAtUtc);
        var links = new FakeLinks();

        var result = await Resolve(
            new FakePayables(payable),
            links,
            await PaymentsWithSucceededAsync(paidAtUtc: DateTime.UtcNow)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, links.Added);
    }

    [Fact]
    public async Task Resolve_WhenPreviousLinkUsedAndBalanceDue_MintsNewLink()
    {
        // INV-2026-00002 respondió Payable.AlreadyPaid al volver a PartiallyPaid: el link Used es del pago
        // anterior, y Billing mandó después un saldo nuevo.
        var payable = CreatePayable(300000L);
        payable.Settle(DateTime.UtcNow.AddHours(-1));
        payable.Reopen(Money.Create(150000L, "USD").Value, DateTime.UtcNow.AddMinutes(-5));
        var links = new FakeLinks { HasUsedLink = true, UsedLinkAtUtc = DateTime.UtcNow.AddHours(-1) };

        var result = await Resolve(new FakePayables(payable), links);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, links.Added);
        Assert.Equal(150000L, links.LastAdded!.Amount.AmountCents);
    }

    [Fact]
    public async Task Resolve_WhenLinkUsedAfterLastBalance_StillAlreadyPaid()
    {
        // El link se usó después del último saldo: ese pago ya cubrió lo que se cobra hoy.
        var payable = CreatePayable(15000L);
        payable.UpdateAmount(Money.Create(5000L, "USD").Value, DateTime.UtcNow.AddHours(-1));
        var links = new FakeLinks { HasUsedLink = true, UsedLinkAtUtc = DateTime.UtcNow.AddMinutes(-5) };

        var result = await Resolve(new FakePayables(payable), links);

        Assert.Equal("Payable.AlreadyPaid", result.Error.Code);
        Assert.Equal(0, links.Added);
    }

    [Fact]
    public async Task Resolve_ReopenedWithPaymentInProgress_IsInProgress()
    {
        var payable = CreatePayable(300000L);
        payable.Settle(DateTime.UtcNow.AddHours(-1));
        payable.Reopen(Money.Create(150000L, "USD").Value, DateTime.UtcNow.AddMinutes(-5));
        var links = new FakeLinks { HasUsedLink = true, UsedLinkAtUtc = DateTime.UtcNow.AddHours(-1) };

        var result = await Resolve(new FakePayables(payable), links, new FakePayments { HasAttemptInProgress = true });

        Assert.Equal("Payable.PaymentInProgress", result.Error.Code);
        Assert.Equal(0, links.Added);
    }

    // Repositorio real en memoria con un pago Succeeded de la factura.
    private static async Task<ITenantPaymentRepository> PaymentsWithSucceededAsync(DateTime paidAtUtc)
    {
        var db = InMemoryPaymentClient.CreateDb();
        var payment = TenantPayment
            .Create(
                TenantId,
                IdempotencyKey.Create($"paid-{Guid.NewGuid():N}").Value,
                Money.Create(5000L, "USD").Value,
                null,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, InvoiceId).Value,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                paidAtUtc.AddMinutes(-1)
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, $"pi_{Guid.NewGuid():N}").Value,
            null,
            null,
            Guid.Empty,
            paidAtUtc.AddMinutes(-1)
        );
        payment.MarkSucceeded(paidAtUtc, Guid.Empty);
        db.TenantPayments.Add(payment);
        await db.SaveChangesAsync();
        return new TenantPaymentRepository(db);
    }

    private static Task<BuildingBlocks.Results.Result<ResolvePayableResponse>> Resolve(
        FakePayables payables,
        FakeLinks links,
        ITenantPaymentRepository? payments = null
    ) =>
        ResolvePayableHandler.Handle(
            new ResolvePayableCommand(Reference),
            payables,
            links,
            payments ?? new FakePayments(),
            new FakeTenants(),
            new NoOpUnitOfWork(),
            CancellationToken.None
        );

    internal sealed class FakePayments : ITenantPaymentRepository
    {
        public bool HasAttemptInProgress { get; init; }

        public Task<bool> AnyInProgressForReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            CancellationToken ct = default
        ) => Task.FromResult(HasAttemptInProgress);

        public Task<bool> AnySucceededForReferenceSinceAsync(
            Guid tenantId,
            string externalReferenceId,
            DateTime sinceUtc,
            CancellationToken ct = default
        ) => Task.FromResult(false);

        public Task<IReadOnlyList<TenantPayment>> GetByIdsAcrossTenantsAsync(
            IReadOnlyCollection<Guid> tenantPaymentIds,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<TaxVision.PaymentClient.Domain.TenantPayments.TenantPayment?> GetByIdAsync(
            Guid tenantPaymentId,
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<TaxVision.PaymentClient.Domain.TenantPayments.TenantPayment?> GetByIdempotencyKeyAsync(
            Guid tenantId,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<TaxVision.PaymentClient.Domain.TenantPayments.TenantPayment?> GetByExternalReferenceAsync(
            Guid tenantId,
            PaymentProviderCode code,
            string providerChargeReference,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<AwaitingProviderPayment>> GetAwaitingProviderConfirmationAsync(
            DateTime stuckBeforeUtc,
            DateTime createdAfterUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<TaxVision.PaymentClient.Domain.TenantPayments.TenantPayment>> GetDueForRetryAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<IReadOnlyList<TaxVision.PaymentClient.Domain.TenantPayments.TenantPayment>> SearchAdminAsync(
            Guid? tenantId,
            TaxVision.PaymentClient.Domain.TenantPayments.PaymentStatus? status,
            DateTime? from,
            DateTime? to,
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task<bool> AnyForProviderAsync(Guid tenantId, PaymentProviderCode code, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<IReadOnlySet<PaymentProviderCode>> GetConfiguredProvidersWithPaymentsAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => throw new NotImplementedException();

        public Task AddAsync(
            TaxVision.PaymentClient.Domain.TenantPayments.TenantPayment payment,
            CancellationToken ct = default
        ) => throw new NotImplementedException();
    }

    internal sealed class FakePayables(PayableReference? payable) : IPayableReferenceRepository
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

    internal sealed class FakeLinks : IPaymentLinkRepository
    {
        public bool HasUsedLink { get; init; }

        /// <summary>Cuándo se usó ese link. Sin valor: se trata como recién usado.</summary>
        public DateTime? UsedLinkAtUtc { get; init; }
        public PaymentLink? Active { get; init; }
        public int Added { get; private set; }
        public PaymentLink? LastAdded { get; private set; }

        public Task<bool> AnyUsedForExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            DateTime? usedSinceUtc = null,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                HasUsedLink && (usedSinceUtc is null || (UsedLinkAtUtc ?? DateTime.MaxValue) > usedSinceUtc)
            );

        public Task<PaymentLink?> GetActiveByExternalReferenceAsync(
            Guid tenantId,
            string externalReferenceId,
            CancellationToken ct = default
        ) => Task.FromResult(Active);

        public Task AddAsync(PaymentLink link, CancellationToken ct = default)
        {
            Added++;
            LastAdded = link;
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

    internal sealed class FakeTenants : ITenantRegistry
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

    internal sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
