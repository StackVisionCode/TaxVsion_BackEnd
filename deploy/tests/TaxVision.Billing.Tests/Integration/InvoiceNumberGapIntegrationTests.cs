using System.Globalization;
using BuildingBlocks.Messaging.BillingIntegrationEvents;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Billing.Application.Invoices.IssueInvoice;
using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Numbering;
using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence;
using Wolverine;
using Xunit;

namespace TaxVision.Billing.Tests.Integration;

/// <summary>
/// BIL-16 con host real y SQL real: la transacción de Wolverine guarda lo rastreado aunque el handler devuelva
/// Failure. Emitir una factura que ya no es borrador no puede gastar un número.
/// </summary>
[Collection(BillingTestDatabase.Collection)]
public sealed class InvoiceNumberGapIntegrationTests(BillingTestDatabase database) : IAsyncLifetime
{
    private const long NextNumber = 7;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _periodKey = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
    private WebApplicationFactory<Program> _factory = null!;
    private Guid _invoiceId;

    public async Task InitializeAsync()
    {
        _factory = new BillingApiFactory(database).WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.ConfigureWolverine(options =>
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate
                )
            )
        );
        _ = _factory.Services;

        var now = DateTime.UtcNow;
        var invoice = Invoice
            .CreateDraft(
                _tenantId,
                Guid.NewGuid(),
                new CustomerSnapshot(Guid.NewGuid(), "Client", null, null, null, null),
                "USD",
                [new DraftInvoiceLine("Tax prep", 1, 10_000, 0)],
                null,
                now
            )
            .Value;
        Assert.True(
            invoice
                .Issue(InvoiceNumber.Create($"INV-{_periodKey}-00006").Value, now, now.AddDays(30), Guid.NewGuid())
                .IsSuccess
        );
        _invoiceId = invoice.Id;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        db.Invoices.Add(invoice);
        db.InvoiceNumberSequences.Add(InvoiceNumberSequence.Start(_tenantId, _periodKey));
        await db.SaveChangesAsync();
        await db
            .InvoiceNumberSequences.IgnoreQueryFilters()
            .Where(sequence => sequence.TenantId == _tenantId)
            .ExecuteUpdateAsync(set => set.SetProperty(sequence => sequence.Next, NextNumber));
    }

    public async Task DisposeAsync()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == _tenantId).ExecuteDeleteAsync();
            await db
                .InvoiceNumberSequences.IgnoreQueryFilters()
                .Where(sequence => sequence.TenantId == _tenantId)
                .ExecuteDeleteAsync();
        }
        await _factory.DisposeAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task Issuing_an_invoice_that_is_not_a_draft_does_not_advance_the_sequence()
    {
        using var scope = _factory.Services.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        bus.TenantId = _tenantId.ToString();

        var result = await bus.InvokeAsync<Result<IssueInvoiceResult>>(
            new IssueInvoiceCommand(_tenantId, _invoiceId, Guid.NewGuid(), CanViewAll: true)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Billing.Invoice.NotDraft", result.Error.Code);
        Assert.Equal(NextNumber, await ReadNextAsync());
    }

    // BIL-17: el pedido de factura de onboarding con montos que no cuadran no puede gastar un número ONB-.
    [Fact(Timeout = 120_000)]
    public async Task Onboarding_invoice_that_cannot_be_created_does_not_advance_the_onboarding_sequence()
    {
        var onboardingPeriod = $"ONB-{_periodKey}";
        var before = await EnsureSequenceAsync(PlatformTenant.Id, onboardingPeriod, NextNumber);
        try
        {
            using var scope = _factory.Services.CreateScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            bus.TenantId = PlatformTenant.Id.ToString();

            // Neto distinto de bruto − descuento: CreateForOnboarding responde InvalidNet.
            await bus.InvokeAsync(
                new OnboardingInvoiceRequestedIntegrationEvent
                {
                    TenantId = PlatformTenant.Id,
                    OnboardingId = Guid.NewGuid(),
                    PlanId = Guid.NewGuid(),
                    PlanDescription = "Professional",
                    PayerName = "Payer",
                    PaymentId = Guid.NewGuid(),
                    GrossAmountCents = 10_000,
                    DiscountAmountCents = 0,
                    NetAmountCents = 5_000,
                    Currency = "USD",
                    SettlementType = "Paid",
                }
            );

            Assert.Equal(NextNumber, await ReadNextAsync(PlatformTenant.Id, onboardingPeriod));
        }
        finally
        {
            await RestoreSequenceAsync(PlatformTenant.Id, onboardingPeriod, before);
        }
    }

    private Task<long> ReadNextAsync() => ReadNextAsync(_tenantId, _periodKey);

    private async Task<long> ReadNextAsync(Guid tenantId, string periodKey)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT [Next] FROM billing.InvoiceNumberSequences WHERE TenantId = @tenant AND PeriodKey = @period";
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@period", periodKey);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    // Deja la secuencia en `next` y devuelve el valor anterior (null si no existía).
    private async Task<long?> EnsureSequenceAsync(Guid tenantId, string periodKey, long next)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var existing = await db
            .InvoiceNumberSequences.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.PeriodKey == periodKey)
            .Select(s => (long?)s.Next)
            .FirstOrDefaultAsync();
        if (existing is null)
        {
            db.InvoiceNumberSequences.Add(InvoiceNumberSequence.Start(tenantId, periodKey));
            await db.SaveChangesAsync();
        }
        await db
            .InvoiceNumberSequences.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.PeriodKey == periodKey)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.Next, next));
        return existing;
    }

    private async Task RestoreSequenceAsync(Guid tenantId, string periodKey, long? before)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var rows = db
            .InvoiceNumberSequences.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.PeriodKey == periodKey);
        if (before is { } previous)
            await rows.ExecuteUpdateAsync(set => set.SetProperty(s => s.Next, previous));
        else
            await rows.ExecuteDeleteAsync();
    }
}
