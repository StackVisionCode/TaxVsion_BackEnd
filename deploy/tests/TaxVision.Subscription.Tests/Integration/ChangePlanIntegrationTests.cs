using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using TaxVision.Subscription.Tests.TestDoubles;
using Wolverine;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// SUB-7: durante la llamada a PaymentApp no hay transacción abierta sobre la suscripción. La pedida del upgrade
/// se confirma antes (el cobro no cambia de orden: primero el request, después la sesión).
/// </summary>
public sealed class ChangePlanIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();
    private readonly string _schema = SubscriptionWolverineHost.NewSchema("sub7p");
    private readonly RowWritingCheckoutClient _client;
    private IHost _host = null!;

    public ChangePlanIntegrationTests() => _client = new RowWritingCheckoutClient(_db.TenantId);

    public async Task InitializeAsync() =>
        _host = await SubscriptionWolverineHost.StartAsync(
            _schema,
            options =>
            {
                foreach (var handler in ChangePlanHandlers())
                    options.Discovery.IncludeType(handler);
                options.Services.AddSingleton<IPlanChangeCheckoutPaymentClient>(_client);
                options.Services.AddSingleton<ITenantUserCountClient>(FakeTenantUserCountClient.WithActive(1));
                options.Services.AddSingleton<ISubscriptionAuditLogWriter>(new FakeSubscriptionAuditLogWriter());
                options.Services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
            }
        );

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await using (var connection = new SqlConnection(SubscriptionIntegrationDb.Connection))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM PlanChangeRequests WHERE TenantId = @tenant";
            command.Parameters.AddWithValue("@tenant", _db.TenantId);
            await command.ExecuteNonQueryAsync();
        }
        await _db.CleanupAsync();
        await SubscriptionWolverineHost.DropSchemaAsync(_schema);
    }

    [Fact(Timeout = 120_000)]
    public async Task ProviderCallDoesNotHoldSubscriptionRowLock()
    {
        var targetPlanCode = await SeedSubscriptionOnCheapestPlanAsync();

        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<ChangePlanResult>>(
                new ChangePlanCommand(
                    _db.TenantId,
                    targetPlanCode,
                    null,
                    Guid.NewGuid(),
                    "owner@acme.test",
                    "https://app/ok",
                    "https://app/cancel"
                )
            );

        Assert.Null(_client.ConcurrentUpdateError);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(_client.SessionUrl, result.Value.CheckoutUrl);

        await using var db = _db.NewContext();
        var subscription = await new TenantSubscriptionRepository(db).GetByTenantIdAsync(_db.TenantId);
        var request = Assert.Single(subscription!.PlanChangeRequests);
        Assert.Equal(PlanChangeRequestStatus.AwaitingPayment, request.Status);
        Assert.Equal(_client.SessionUrl, request.CheckoutUrl);
    }

    // Suscripción sobre el plan publicado más barato, y el más caro como destino del upgrade.
    private async Task<string> SeedSubscriptionOnCheapestPlanAsync()
    {
        await using var db = _db.NewContext();
        var priced = (await new PlanRepository(db).GetAllAsync())
            .Select(plan => (Plan: plan, Version: plan.GetPublishedVersion()))
            .Where(item =>
                item.Version is not null && item.Version.SupportedBillingCycles.Contains(BillingCycle.Monthly)
            )
            .Select(item =>
                (
                    item.Plan,
                    item.Version,
                    Price: PlanPricing.ResolveBaseSubscriptionPrice(item.Version!, BillingCycle.Monthly)
                )
            )
            .Where(item => item.Price is { AmountCents: > 0 })
            .OrderBy(item => item.Price!.Value.AmountCents)
            .ToList();
        Assert.True(priced[0].Price!.Value.AmountCents < priced[^1].Price!.Value.AmountCents);

        var nowUtc = DateTime.UtcNow;
        var subscription = TenantSubscription
            .ActivateImmediately(
                _db.TenantId,
                priced[0].Plan,
                priced[0].Version!,
                BillingCycle.Monthly,
                nowUtc.AddDays(-1),
                nowUtc.AddDays(29),
                Guid.Empty,
                nowUtc
            )
            .Value;
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return priced[^1].Plan.Code.Value;
    }

    private static IEnumerable<Type> ChangePlanHandlers() =>
        typeof(ChangePlanCommand)
            .Assembly.GetTypes()
            .Where(type =>
                type.Name
                    is "ChangePlanHandler"
                        or "RequestPlanChangeHandler"
                        or "AttachUpgradeCheckoutHandler"
                        or "ReleaseUpgradeWithoutCheckoutHandler"
            );

    /// <summary>Durante la llamada, otro escritor actualiza la misma fila con un límite de 2 segundos.</summary>
    private sealed class RowWritingCheckoutClient(Guid tenantId) : IPlanChangeCheckoutPaymentClient
    {
        public string SessionUrl => "https://pay.example/upgrade";
        public Exception? ConcurrentUpdateError { get; private set; }

        public async Task<Result<PlanChangeCheckoutClientResult>> CreateCheckoutAsync(
            PlanChangeCheckoutClientRequest request,
            CancellationToken ct = default
        )
        {
            try
            {
                await using var connection = new SqlConnection(SubscriptionIntegrationDb.Connection);
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 2;
                command.CommandText =
                    "UPDATE TenantSubscriptions SET UpdatedAtUtc = SYSUTCDATETIME() WHERE TenantId = @tenant";
                command.Parameters.AddWithValue("@tenant", tenantId);
                await command.ExecuteNonQueryAsync(ct);
            }
            catch (SqlException ex)
            {
                ConcurrentUpdateError = ex;
            }

            return Result.Success(
                new PlanChangeCheckoutClientResult(Guid.NewGuid(), SessionUrl, "cs_up", DateTime.UtcNow.AddHours(24))
            );
        }
    }
}
