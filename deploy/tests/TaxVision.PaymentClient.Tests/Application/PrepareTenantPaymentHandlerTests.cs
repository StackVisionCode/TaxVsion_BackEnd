using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.PrepareTenantPayment;
using TaxVision.PaymentClient.Domain.Audit;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>CaracterizaciÃ³n de PCL-11: reglas de los modos Connect y Direct antes de extraerlos del handler.</summary>
public sealed class PrepareTenantPaymentHandlerTests
{
    private const long AmountCents = 2_500;
    private const string AccountId = "acct_prepare";

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly PaymentClientDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

    public PrepareTenantPaymentHandlerTests() => _db = InMemoryPaymentClient.CreateDb(_dbName);

    [Fact]
    public async Task Connect_WithValidFee_PlansTheSplit()
    {
        await SeedConnectAsync(canCharge: true);

        var result = await PrepareAsync(fee: 300, feeReference: "fee-1");

        Assert.True(result.Value.ReadyToCharge);
        var payment = await StoredAsync();
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("pm_1", payment.ChargeMethodReference);
        Assert.Equal(2_200, payment.SplitPayment!.TenantAmountCents);
        Assert.Equal(300, payment.SplitPayment.PlatformFeeAmountCents);
        Assert.Equal("fee-1", payment.SplitPayment.PlatformFeeReference);
    }

    [Fact]
    public async Task Connect_WithZeroFee_PlansTheWholeAmountForTheTenant()
    {
        await SeedConnectAsync(canCharge: true);

        var result = await PrepareAsync(fee: 0);

        Assert.True(result.Value.ReadyToCharge);
        var payment = await StoredAsync();
        Assert.Equal(AmountCents, payment.SplitPayment!.TenantAmountCents);
        Assert.Equal(0, payment.SplitPayment.PlatformFeeAmountCents);
    }

    [Fact]
    public async Task Connect_WithoutAccount_FailsBeforeCallingTheProvider()
    {
        await SeedConfigAsync(TenantPaymentMode.Connect);

        await AssertClosedAsync(await PrepareAsync(fee: 300), "Connect.NotChargeReady");
    }

    [Fact]
    public async Task Connect_WithAnAccountThatCannotCharge_Fails()
    {
        await SeedConnectAsync(canCharge: false);

        await AssertClosedAsync(await PrepareAsync(fee: 300), "Connect.NotChargeReady");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    [InlineData(AmountCents + 1)]
    public async Task Connect_WithInvalidFee_Fails(long? fee)
    {
        await SeedConnectAsync(canCharge: true);

        await AssertClosedAsync(await PrepareAsync(fee: fee), "TenantPayment.InvalidPlatformFee");
    }

    [Fact]
    public async Task Connect_WithoutPaymentMethod_FailsWithThePlanError()
    {
        await SeedConnectAsync(canCharge: true);

        await AssertClosedAsync(await PrepareAsync(fee: 300, method: " "), "TenantPayment.InvalidPaymentMethod");
    }

    [Fact]
    public async Task Direct_WithReadableSecret_PlansWithoutSplit()
    {
        await SeedDirectAsync("enc:sk_test");

        var result = await PrepareAsync();

        Assert.True(result.Value.ReadyToCharge);
        var payment = await StoredAsync();
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("pm_1", payment.ChargeMethodReference);
        Assert.Null(payment.SplitPayment);
    }

    [Fact]
    public async Task Direct_WithoutSecret_Fails()
    {
        await SeedDirectAsync("enc:sk_test", keepSecret: false);

        var result = await PrepareAsync();

        await AssertClosedAsync(result, "TenantPaymentConfig.SecretUnreadable");
        Assert.Equal("No provider secret is configured for this tenant.", (await StoredAsync()).FailureReason);
    }

    [Fact]
    public async Task Direct_WithUnreadableSecret_Fails()
    {
        await SeedDirectAsync("garbled");

        var result = await PrepareAsync();

        await AssertClosedAsync(result, "TenantPaymentConfig.SecretUnreadable");
        Assert.Equal("Stored provider secret could not be decrypted.", (await StoredAsync()).FailureReason);
    }

    [Fact]
    public async Task Direct_WithoutPaymentMethod_FailsWithThePlanError()
    {
        await SeedDirectAsync("enc:sk_test");

        await AssertClosedAsync(await PrepareAsync(method: " "), "TenantPayment.InvalidPaymentMethod");
    }

    [Fact]
    public async Task UnknownConfig_IsRejected_WithoutCreatingAPayment()
    {
        var result = await PrepareAsync();

        Assert.Equal("TenantPaymentConfig.NotFound", result.Error.Code);
        Assert.Empty(await _db.TenantPayments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task InactiveConfig_IsRejected_WithoutCreatingAPayment()
    {
        await SeedConfigAsync(TenantPaymentMode.Connect, active: false);

        var result = await PrepareAsync();

        Assert.Equal("TenantPaymentConfig.NotActive", result.Error.Code);
        Assert.Empty(await _db.TenantPayments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task SameKey_ReturnsTheSamePayment_WithoutCharging()
    {
        await SeedDirectAsync("enc:sk_test");
        var first = await PrepareAsync();

        var second = await PrepareAsync();

        Assert.Equal(first.Value.TenantPaymentId, second.Value.TenantPaymentId);
        Assert.False(second.Value.ReadyToCharge);
        Assert.Single(await _db.TenantPayments.IgnoreQueryFilters().ToListAsync());
    }

    private async Task AssertClosedAsync(Result<PreparedTenantPayment> result, string code)
    {
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ReadyToCharge);
        var payment = await StoredAsync();
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(code, payment.FailureCode);
        Assert.Null(payment.ChargeMethodReference);
        Assert.Null(payment.SplitPayment);
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        var audit = Assert.Single(await read.AuditEntries.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(PaymentAuditAction.TenantPaymentFailed, audit.Action);
        Assert.Equal(_actor, audit.ActorUserId);
    }

    private async Task<TenantPayment> StoredAsync()
    {
        await using var read = InMemoryPaymentClient.CreateDb(_dbName);
        return await read.TenantPayments.IgnoreQueryFilters().SingleAsync();
    }

    private async Task<Result<PreparedTenantPayment>> PrepareAsync(
        long? fee = null,
        string? feeReference = null,
        string method = "pm_1"
    )
    {
        await using var db = InMemoryPaymentClient.CreateDb(_dbName);
        return await PrepareTenantPaymentHandler.Handle(
            new PrepareTenantPaymentCommand(
                new ChargeTenantPaymentCommand(
                    _tenantId,
                    PaymentProviderCode.Stripe,
                    AmountCents,
                    "USD",
                    TaxpayerId: Guid.NewGuid(),
                    PaymentPurposeKind.InvoicePayment,
                    PurposeExternalReferenceId: "inv-1",
                    PaymentMethodReference: method,
                    ReceiptEmail: null,
                    IdempotencyKey: "prepare-key",
                    ActorUserId: _actor,
                    PlatformFeeAmountCents: fee,
                    PlatformFeeReference: feeReference
                )
            ),
            new TenantPaymentRepository(db),
            new TenantPaymentConfigRepository(db),
            new TenantConnectAccountRepository(db),
            new PrefixSecretProtector(),
            new PaymentAuditLogWriter(db),
            db,
            new NoOpPaymentClientMetrics(),
            new FixedCorrelationContext(),
            NullLogger<TenantPayment>.Instance,
            CancellationToken.None
        );
    }

    private async Task SeedConnectAsync(bool canCharge)
    {
        await SeedConfigAsync(TenantPaymentMode.Connect);
        var account = TenantConnectAccount
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                ConnectAccountType.Express,
                StripeConnectAccountId.Create(AccountId).Value,
                DateTime.UtcNow
            )
            .Value;
        Assert.True(account.UpdateFromWebhook(canCharge, canCharge, [], DateTime.UtcNow).IsSuccess);
        _db.TenantConnectAccounts.Add(account);
        await _db.SaveChangesAsync();
    }

    private async Task SeedDirectAsync(string cipherText, bool keepSecret = true)
    {
        var config = NewConfig(TenantPaymentMode.DirectApiKeys);
        config.UpdateSecrets(EncryptedSecret.Create(cipherText).Value, null, Guid.Empty, DateTime.UtcNow);
        config.MarkActive(Guid.Empty, DateTime.UtcNow);
        // El dominio no deja activarla sin secreto, pero el handler lo revisa igual.
        if (!keepSecret)
            typeof(TenantPaymentConfig).GetProperty(nameof(config.SecretKeyEncrypted))!.SetValue(config, null);
        _db.TenantPaymentConfigs.Add(config);
        await _db.SaveChangesAsync();
    }

    private async Task SeedConfigAsync(TenantPaymentMode mode, bool active = true)
    {
        var config = NewConfig(mode);
        if (active)
            config.MarkActiveViaConnect(Guid.Empty, DateTime.UtcNow);
        _db.TenantPaymentConfigs.Add(config);
        await _db.SaveChangesAsync();
    }

    private TenantPaymentConfig NewConfig(TenantPaymentMode mode) =>
        TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                mode,
                "pk_test",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                DateTime.UtcNow
            )
            .Value;
}
