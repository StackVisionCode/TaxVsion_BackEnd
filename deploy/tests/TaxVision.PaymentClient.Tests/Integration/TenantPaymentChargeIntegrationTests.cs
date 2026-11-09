using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.PaymentClientIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.PrepareLinkRedemption;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ExecuteTenantPaymentCharge;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.PrepareTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ReconcileTenantPayment;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.RecordTenantPaymentCharge;
using TaxVision.PaymentClient.Application.TenantPayments.DomainEventHandlers;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.Tenants;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.PaymentClient.Tests.Integration;

/// <summary>
/// PCL-8 (patrón BB-8) contra SQL Server local, con Wolverine real: la fila existe antes de llamar al proveedor, un
/// fallo después de que el proveedor aceptó no reenvía el cobro y la reconciliación encuentra el cargo sin registro.
/// </summary>
public sealed class TenantPaymentChargeIntegrationTests : IAsyncLifetime
{
    private readonly string _schema = $"pcl8_{Guid.NewGuid():N}"[..13];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FailOnSucceededInterceptor _interceptor = new();
    private readonly IdempotentRecordingPaymentProvider _stripe = new(
        new(PaymentProviderCode.Stripe, "Stripe", "Publishable key", "Secret key", true, false)
    );
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await PaymentClientTestDatabase.ExecuteAsync($"EXEC('CREATE SCHEMA [{_schema}]')");
        await SeedAsync();
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await using (var db = PaymentClientTestDatabase.NewContext(_tenantId))
        {
            await db.AuditEntries.Where(e => e.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.Set<TenantPaymentAttempt>()
                .IgnoreQueryFilters()
                .Where(a => a.TenantId == _tenantId)
                .ExecuteDeleteAsync();
            await db.TenantPayments.IgnoreQueryFilters().Where(p => p.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.PaymentLinks.IgnoreQueryFilters().Where(l => l.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.TenantPaymentConfigs.IgnoreQueryFilters().Where(c => c.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.Tenants.IgnoreQueryFilters().Where(t => t.Id == _tenantId).ExecuteDeleteAsync();
        }

        await PaymentClientTestDatabase.DropSchemaAsync(_schema);
    }

    [Fact]
    public async Task PaymentIsCommittedBeforeProviderCall()
    {
        var visibleDuringCall = false;
        _stripe.OnCharge = async request =>
        {
            await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
            var id = Guid.Parse(request.Metadata["tenantPaymentId"]);
            visibleDuringCall = await db.TenantPayments.AnyAsync(p => p.Id == id && p.Status == PaymentStatus.Pending);
        };

        var result = await ChargeAsync("pcl8-visible");

        Assert.True(result.IsSuccess);
        Assert.True(visibleDuringCall, "La fila tiene que estar confirmada antes de llamar al proveedor.");
        Assert.Equal(PaymentStatus.Succeeded, await StatusOfAsync(result.Value));
    }

    [Fact]
    public async Task FailureAfterProviderAccepted_DoesNotResend()
    {
        _interceptor.FailNextSucceededSave = true;

        var first = await ChargeAsync("pcl8-same-key");
        var retry = await ChargeAsync("pcl8-same-key");

        // Una sola llamada: con intento hecho y sin resultado no se reenvía a ciegas.
        Assert.Equal(first.Value, retry.Value);
        Assert.Equal(1, _stripe.Calls);
        Assert.Equal(1, _stripe.DistinctChargedKeys);
        Assert.Equal(PaymentStatus.Pending, await StatusOfAsync(first.Value));
    }

    [Fact]
    public async Task ChargedButUnrecorded_IsRecoveredByReconciliation()
    {
        _interceptor.FailNextSucceededSave = true;
        var charged = await ChargeAsync("pcl8-recover");

        var reconciled = await ReconcileAsync(charged.Value);

        Assert.Equal(TenantPaymentReconciliation.Resolved, reconciled.Value);
        Assert.Equal(PaymentStatus.Succeeded, await StatusOfAsync(charged.Value));
        Assert.Equal(1, _stripe.Calls);
        Assert.True(
            await WaitUntilAsync(async () =>
                await CountOutgoingAsync(nameof(TenantPaymentSucceededIntegrationEvent)) == 1
            )
        );
    }

    [Fact]
    public async Task RedeemChargedButUnrecorded_BlocksNewAttemptUntilReconciled()
    {
        var link = await SeedLinkAsync();
        _interceptor.FailNextSucceededSave = true;

        var first = await RedeemAsync(link, "card-1");
        var second = await RedeemAsync(link, "card-2");

        Assert.Equal(PaymentLink.AttemptInProgressError.Code, first.Error.Code);
        Assert.Equal(PaymentLink.AttemptInProgressError.Code, second.Error.Code);
        Assert.Equal(1, _stripe.Calls);

        var paymentId = await AttemptOfAsync(link);
        await ReconcileAsync(paymentId);

        Assert.Equal(PaymentStatus.Succeeded, await StatusOfAsync(paymentId));
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        var stored = await db.PaymentLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id);
        Assert.Equal(PaymentLinkStatus.Used, stored.Status);
    }

    [Fact]
    public async Task PendingWithoutCharge_IsClosedAndTheLinkAcceptsANewAttempt()
    {
        var link = await SeedLinkAsync();
        var paymentId = await PrepareOnlyAsync(link);

        await ReconcileAsync(paymentId);

        Assert.Equal(PaymentStatus.Failed, await StatusOfAsync(paymentId));
        Assert.Equal(0, _stripe.Calls);

        var retried = await RedeemAsync(link, "card-2");
        Assert.True(retried.IsSuccess);
        Assert.Equal(1, _stripe.Calls);
    }

    [Fact]
    public async Task UnknownOutcome_StaysPendingWhenTheProviderCannotAnswer()
    {
        var link = await SeedLinkAsync();
        var paymentId = await PrepareOnlyAsync(link);
        _stripe.SearchUnavailable = true;

        var reconciled = await ReconcileAsync(paymentId);

        Assert.True(reconciled.IsFailure);
        Assert.Equal(PaymentStatus.Pending, await StatusOfAsync(paymentId));
    }

    private IMessageBus Bus => _host.Services.GetRequiredService<IMessageBus>();

    private Task<Result<Guid>> ChargeAsync(string idempotencyKey) =>
        Bus.InvokeAsync<Result<Guid>>(
            new ChargeTenantPaymentCommand(
                _tenantId,
                PaymentProviderCode.Stripe,
                2500,
                "USD",
                TaxpayerId: null,
                PaymentPurposeKind.InvoicePayment,
                PurposeExternalReferenceId: "inv-pcl8",
                PaymentMethodReference: "pm_card",
                ReceiptEmail: null,
                IdempotencyKey: $"{idempotencyKey}-{_tenantId:N}",
                ActorUserId: Guid.NewGuid()
            )
        );

    private Task<Result<RedeemPaymentLinkResponse>> RedeemAsync(PaymentLink link, string token) =>
        Bus.InvokeAsync<Result<RedeemPaymentLinkResponse>>(
            new RedeemPaymentLinkCommand(link.Token.Value, PaymentProviderCode.Stripe, token, ReceiptEmail: null)
        );

    private Task<Result<TenantPaymentReconciliation>> ReconcileAsync(Guid paymentId) =>
        Bus.InvokeAsync<Result<TenantPaymentReconciliation>>(new ReconcileTenantPaymentCommand(_tenantId, paymentId));

    // Caída entre el paso 1 y el 2: la intención quedó y el proveedor nunca se llamó.
    private async Task<Guid> PrepareOnlyAsync(PaymentLink link)
    {
        var plan = await Bus.InvokeAsync<Result<LinkRedemptionPlan>>(
            new PrepareLinkRedemptionCommand(
                new RedeemPaymentLinkCommand(link.Token.Value, PaymentProviderCode.Stripe, "card-1", ReceiptEmail: null)
            )
        );
        return plan.Value.TenantPaymentId;
    }

    private async Task<Guid> AttemptOfAsync(PaymentLink link)
    {
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        var stored = await db.PaymentLinks.AsNoTracking().SingleAsync(l => l.Id == link.Id);
        return stored.RelatedTenantPaymentId!.Value;
    }

    private async Task<PaymentStatus> StatusOfAsync(Guid paymentId)
    {
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        return await db.TenantPayments.Where(p => p.Id == paymentId).Select(p => p.Status).SingleAsync();
    }

    private Task<int> CountOutgoingAsync(string messageType) =>
        PaymentClientTestDatabase.CountAsync(
            $"SELECT COUNT(*) FROM [{_schema}].[wolverine_outgoing_envelopes] WHERE message_type LIKE '%{messageType}%'"
        );

    private async Task SeedAsync()
    {
        var nowUtc = DateTime.UtcNow;
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        db.Tenants.Add(
            Tenant
                .Register(_tenantId, "Charge Office", $"ch-{_tenantId:N}"[..20], TenantKind.Customer, "Etc/UTC", nowUtc)
                .Value
        );
        var config = TenantPaymentConfig
            .Create(
                _tenantId,
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_test_office",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                nowUtc
            )
            .Value;
        config.UpdateSecrets(
            EncryptedSecret.Create(new PrefixSecretProtector().Protect("sk_test_office")).Value,
            null,
            Guid.Empty,
            nowUtc
        );
        config.MarkActive(Guid.Empty, nowUtc);
        db.TenantPaymentConfigs.Add(config);
        await db.SaveChangesAsync();
    }

    private async Task<PaymentLink> SeedLinkAsync()
    {
        var nowUtc = DateTime.UtcNow;
        var link = PaymentLink
            .Create(
                _tenantId,
                null,
                Money.Create(2500, "USD").Value,
                PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-pcl8-link").Value,
                PaymentLinkToken.Generate(),
                TimeSpan.FromDays(7),
                Guid.Empty,
                nowUtc
            )
            .Value;
        await using var db = PaymentClientTestDatabase.NewContext(_tenantId);
        db.PaymentLinks.Add(link);
        await db.SaveChangesAsync();
        return link;
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "pcl8-charge-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(ChargeTenantPaymentHandler))
                    .IncludeType(typeof(PrepareTenantPaymentHandler))
                    .IncludeType(typeof(ExecuteTenantPaymentChargeHandler))
                    .IncludeType(typeof(RecordTenantPaymentChargeHandler))
                    .IncludeType(typeof(RedeemPaymentLinkHandler))
                    .IncludeType(typeof(PrepareLinkRedemptionHandler))
                    .IncludeType(typeof(ReconcileTenantPaymentHandler))
                    .IncludeType(typeof(TenantPaymentSucceededDomainEventHandler));
                options.PersistMessagesWithSqlServer(PaymentClientTestDatabase.ConnectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                options.Services.AddDbContext<PaymentClientDbContext>(db =>
                    db.UseSqlServer(PaymentClientTestDatabase.ConnectionString).AddInterceptors(_interceptor)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PaymentClientDbContext>());
                options.Services.AddSingleton<ITenantContext>(new PaymentClientTestDatabase.FixedTenant(_tenantId));
                options.Services.AddSingleton<ICorrelationContext, FixedCorrelationContext>();
                options.Services.AddScoped<ITenantPaymentRepository, TenantPaymentRepository>();
                options.Services.AddScoped<ITenantPaymentConfigRepository, TenantPaymentConfigRepository>();
                options.Services.AddScoped<ITenantConnectAccountRepository, TenantConnectAccountRepository>();
                options.Services.AddScoped<IPaymentLinkRepository, PaymentLinkRepository>();
                options.Services.AddScoped<IPaymentAuditLogWriter, PaymentAuditLogWriter>();
                options.Services.AddSingleton(InMemoryPaymentClient.FakeAdapters(_stripe));
                options.Services.AddSingleton<ISecretProtector, PrefixSecretProtector>();
                options.Services.AddSingleton<IPaymentClientMetrics, NoOpPaymentClientMetrics>();
                options.Services.Configure<PlatformStripeCredentials>(_ => { });
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, PaymentClientDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                options.PublishMessage<TenantPaymentSucceededIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<PaymentLinkUsedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(200);
        }
        return false;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Falla una vez después de guardar el éxito y antes de confirmar: el proveedor ya aceptó.
    private sealed class FailOnSucceededInterceptor : SaveChangesInterceptor
    {
        public bool FailNextSucceededSave { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            var hit =
                FailNextSucceededSave
                && eventData
                    .Context!.ChangeTracker.Entries<TenantPayment>()
                    .Any(e => e.Entity.Status == PaymentStatus.Succeeded);
            if (!hit)
                return ValueTask.FromResult(result);

            FailNextSucceededSave = false;
            return ValueTask.FromException<int>(new InvalidOperationException("Fallo forzado después de guardar."));
        }
    }
}
