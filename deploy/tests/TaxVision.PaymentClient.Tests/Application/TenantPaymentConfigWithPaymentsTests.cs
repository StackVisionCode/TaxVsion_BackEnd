using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.DeleteTenantPaymentConfig;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;

namespace TaxVision.PaymentClient.Tests.Application;

/// <summary>Un proveedor con pagos no se borra (sus reembolsos y disputas siguen llegando por webhook y se
/// verifican con su secreto): 409 y se ofrece desactivar. La lista dice cuáles tienen pagos.</summary>
public sealed class TenantPaymentConfigWithPaymentsTests
{
    private static readonly DateTime NowUtc = DateTime.UtcNow;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PaymentClientDbContext _db = InMemoryPaymentClient.CreateDb();

    [Fact]
    public async Task Deleting_a_provider_with_payments_is_a_409_and_keeps_the_config()
    {
        await SeedConfigAsync(PaymentProviderCode.PayPal);
        await SeedPaymentAsync(PaymentProviderCode.PayPal);

        var result = await DeleteAsync(PaymentProviderCode.PayPal);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPaymentConfig.HasPayments", result.Error.Code);
        Assert.Equal("This provider has payments. Turn it off instead.", result.Error.Message);
        Assert.Equal(409, result.Error.ToHttpStatusCode());
        Assert.Single(await _db.TenantPaymentConfigs.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_provider_without_payments_removes_it_even_if_another_provider_has_payments()
    {
        await SeedConfigAsync(PaymentProviderCode.Stripe);
        await SeedConfigAsync(PaymentProviderCode.PayPal);
        await SeedPaymentAsync(PaymentProviderCode.Stripe);

        var result = await DeleteAsync(PaymentProviderCode.PayPal);

        Assert.True(result.IsSuccess);
        var remaining = Assert.Single(await _db.TenantPaymentConfigs.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(PaymentProviderCode.Stripe, remaining.ProviderCode);
    }

    [Fact]
    public async Task Payments_of_another_office_do_not_block_the_delete()
    {
        await SeedConfigAsync(PaymentProviderCode.PayPal);
        await SeedPaymentAsync(PaymentProviderCode.PayPal, tenantId: Guid.NewGuid());

        var result = await DeleteAsync(PaymentProviderCode.PayPal);

        Assert.True(result.IsSuccess);
        Assert.Empty(await _db.TenantPaymentConfigs.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task The_list_and_the_detail_say_which_providers_have_payments()
    {
        await SeedConfigAsync(PaymentProviderCode.Stripe);
        await SeedConfigAsync(PaymentProviderCode.PayPal);
        await SeedPaymentAsync(PaymentProviderCode.Stripe);
        await SeedPaymentAsync(PaymentProviderCode.Stripe);
        await SeedPaymentAsync(PaymentProviderCode.PayPal, tenantId: Guid.NewGuid());

        var list = await ListTenantPaymentConfigsHandler.Handle(
            new ListTenantPaymentConfigsQuery(_tenantId),
            new TenantPaymentConfigRepository(_db),
            new TenantPaymentRepository(_db),
            CancellationToken.None
        );
        var payPal = await GetTenantPaymentConfigHandler.Handle(
            new GetTenantPaymentConfigQuery(_tenantId, PaymentProviderCode.PayPal),
            new TenantPaymentConfigRepository(_db),
            new TenantPaymentRepository(_db),
            CancellationToken.None
        );

        Assert.True(list.IsSuccess);
        Assert.True(Assert.Single(list.Value, c => c.ProviderCode == "Stripe").HasPayments);
        Assert.False(Assert.Single(list.Value, c => c.ProviderCode == "PayPal").HasPayments);
        Assert.False(payPal.Value.HasPayments);
    }

    private Task<Result> DeleteAsync(PaymentProviderCode provider) =>
        DeleteTenantPaymentConfigHandler.Handle(
            new DeleteTenantPaymentConfigCommand(_tenantId, provider, Guid.NewGuid()),
            new TenantPaymentConfigRepository(_db),
            new TenantPaymentRepository(_db),
            new PaymentAuditLogWriter(_db),
            _db,
            new FixedCorrelationContext(),
            InMemoryPaymentClient.RealAdapters(),
            new PrefixSecretProtector(),
            NullLogger<TenantPaymentConfig>.Instance,
            CancellationToken.None
        );

    private async Task SeedConfigAsync(PaymentProviderCode provider)
    {
        _db.TenantPaymentConfigs.Add(
            TenantPaymentConfig
                .Create(
                    _tenantId,
                    provider,
                    TenantPaymentMode.DirectApiKeys,
                    "public-key",
                    StatementDescriptor.Create("ACME TAX SVC").Value,
                    NowUtc
                )
                .Value
        );
        await _db.SaveChangesAsync();
    }

    private async Task SeedPaymentAsync(PaymentProviderCode provider, Guid? tenantId = null)
    {
        _db.TenantPayments.Add(
            TenantPayment
                .Create(
                    tenantId ?? _tenantId,
                    IdempotencyKey.Create($"charge-{Guid.NewGuid():N}").Value,
                    Money.Create(4900, "USD").Value,
                    null,
                    PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-1").Value,
                    provider,
                    StatementDescriptor.Create("ACME TAX SVC").Value,
                    Guid.Empty,
                    NowUtc
                )
                .Value
        );
        await _db.SaveChangesAsync();
    }
}
