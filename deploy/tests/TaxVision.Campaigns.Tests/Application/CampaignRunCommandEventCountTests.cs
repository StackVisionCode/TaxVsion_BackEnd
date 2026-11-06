using System.Reflection;
using BuildingBlocks.Domain;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Application.Runs.Commands;
using TaxVision.Campaigns.Application.Runs.Consumers;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Tests.Fakes;

namespace TaxVision.Campaigns.Tests.Application;

/// <summary>
/// Un aviso por transición: suma lo publicado a mano y lo devuelto por los handlers de eventos de dominio. Si
/// quedan los dos caminos, el aviso sale dos veces. Ningún evento de dominio sale por el bus.
/// </summary>
public sealed class CampaignRunCommandEventCountTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();

    [Fact]
    public async Task StartRun_EmitsOneStartedAndOneDispatchPerUnit()
    {
        var fixture = new Fixture();

        await fixture.RunAsync(typeof(StartCampaignRunHandler), fixture.StartCommand("a@x.com", "b@x.com"));

        var emitted = fixture.Emitted();
        var started = Assert.Single(emitted.OfType<CampaignRunStartedIntegrationEvent>());
        Assert.Equal(2, started.RecipientCount);
        Assert.Equal("Manual", started.TriggeredBy);
        Assert.Equal(fixture.Campaign.Id, started.CampaignId);
        Assert.Equal(2, emitted.OfType<CampaignDispatchRequestedIntegrationEvent>().Count());
        Assert.Empty(emitted.OfType<CampaignRunCompletedIntegrationEvent>());
    }

    /// <summary>Todas las unidades Skipped: cierra al materializar; el arranque se anuncia antes que el cierre.</summary>
    [Fact]
    public async Task StartRun_AllSkipped_EmitsStartedThenCompleted()
    {
        var fixture = new Fixture();

        await fixture.RunAsync(typeof(StartCampaignRunHandler), fixture.StartCommand(new string?[] { null }));

        var emitted = fixture.Emitted();
        var started = Assert.Single(emitted.OfType<CampaignRunStartedIntegrationEvent>());
        var completed = Assert.Single(emitted.OfType<CampaignRunCompletedIntegrationEvent>());
        Assert.Equal(nameof(CampaignRunStatus.Completed), completed.TerminalStatus);
        Assert.Equal(1, completed.Skipped);
        Assert.True(IndexOf(emitted, started) < IndexOf(emitted, completed));
        Assert.Empty(emitted.OfType<CampaignDispatchRequestedIntegrationEvent>());
    }

    /// <summary>El Wallet rechaza: no se envió nada, así que no se anuncia ni arranque ni cierre.</summary>
    [Fact]
    public async Task StartRun_WalletRejects_EmitsNothing()
    {
        var fixture = new Fixture { Wallet = new RejectingWallet() };

        await fixture.RunAsync(typeof(StartCampaignRunHandler), fixture.StartCommand("a@x.com"), expectSuccess: false);

        Assert.Equal(CampaignRunStatus.Rejected, Assert.Single(fixture.Runs.Runs).Status);
        Assert.Empty(fixture.Emitted());
    }

    [Fact]
    public async Task ApplyDispatchResult_LastUnit_EmitsExactlyOneRunCompleted()
    {
        var fixture = new Fixture();
        var run = fixture.Dispatched("a@x.com", "b@x.com");
        var (first, second) = (run.Recipients.First(), run.Recipients.Last());

        await fixture.RunAsync(typeof(ApplyDispatchResultConsumer), Result(run, first, "Delivered"));
        Assert.Empty(fixture.Emitted().OfType<CampaignRunCompletedIntegrationEvent>());

        await fixture.RunAsync(typeof(ApplyDispatchResultConsumer), Result(run, second, "Failed"));
        var completed = Assert.Single(fixture.Emitted().OfType<CampaignRunCompletedIntegrationEvent>());

        Assert.Equal(run.Id, completed.RunId);
        Assert.Equal(nameof(CampaignRunStatus.PartiallyFailed), completed.TerminalStatus);
        Assert.Equal(1, completed.Delivered);
        Assert.Equal(1, completed.Failed);
    }

    /// <summary>D-CMP-4: la unidad resuelta después del cierre recalcula el run pero no vuelve a anunciarlo.</summary>
    [Fact]
    public async Task ApplyDispatchResult_AfterClosure_EmitsNothingNew()
    {
        var fixture = new Fixture();
        var run = fixture.Dispatched("a@x.com");
        var unit = run.Recipients.Single();
        await fixture.RunAsync(typeof(ApplyDispatchResultConsumer), Result(run, unit, "Unknown"));
        Assert.Single(fixture.Emitted().OfType<CampaignRunCompletedIntegrationEvent>());

        await fixture.RunAsync(typeof(ApplyDispatchResultConsumer), Result(run, unit, "Delivered"));

        Assert.Empty(fixture.Emitted());
        Assert.Equal(CampaignRunStatus.Completed, run.Status);
    }

    [Fact]
    public async Task Sweep_StaleRun_EmitsExactlyOneRunCompleted()
    {
        var fixture = new Fixture();
        var run = fixture.Dispatched("a@x.com");

        await fixture.RunAsync(typeof(SweepStaleCampaignRunHandler), new SweepStaleCampaignRunCommand(Tenant, run.Id));

        var completed = Assert.Single(fixture.Emitted().OfType<CampaignRunCompletedIntegrationEvent>());
        Assert.Equal(1, completed.Unknown);
    }

    [Fact]
    public async Task EmailReport_ClosingTheRun_EmitsExactlyOneRunCompleted()
    {
        var fixture = new Fixture();
        var run = fixture.Dispatched("a@x.com");

        await fixture.RunAsync(
            typeof(CampaignEmailDeliveredConsumer),
            new PostmasterEmailDeliverySucceededIntegrationEvent
            {
                TenantId = Tenant,
                CorrelationId = "test",
                NotificationLogId = Guid.NewGuid(),
                DispatchAttemptId = Guid.NewGuid(),
                SentMessageId = Guid.NewGuid(),
                EventAtUtc = DateTime.UtcNow,
                CampaignId = run.Recipients.Single().Id,
                ProviderMessageId = "prov-1",
            }
        );

        Assert.Single(fixture.Emitted().OfType<CampaignRunCompletedIntegrationEvent>());
    }

    [Fact]
    public async Task SmsReport_ClosingTheRun_EmitsExactlyOneRunCompleted()
    {
        var fixture = new Fixture();
        var run = fixture.Dispatched("a@x.com");

        await fixture.RunAsync(
            typeof(CampaignSmsFailedConsumer),
            new SmsMessageFailedIntegrationEvent
            {
                TenantId = Tenant,
                CorrelationId = "test",
                MessageId = Guid.NewGuid(),
                CustomerId = Guid.NewGuid(),
                SourceContext = $"campaign:{run.Recipients.Single().DispatchId}",
                FailureCode = "rejected",
            }
        );

        Assert.Single(fixture.Emitted().OfType<CampaignRunCompletedIntegrationEvent>());
    }

    private static int IndexOf(IReadOnlyList<object> messages, object message) => messages.ToList().IndexOf(message);

    private static CampaignDispatchResultIntegrationEvent Result(
        CampaignRun run,
        CampaignRecipient unit,
        string outcome
    ) =>
        new()
        {
            TenantId = Tenant,
            CorrelationId = "test",
            CampaignId = run.CampaignId,
            RunId = run.Id,
            DispatchId = unit.DispatchId,
            Outcome = outcome,
        };

    private sealed class Fixture
    {
        public Fixture()
        {
            Campaign = Campaign.Create(Tenant, Actor, "Newsletter", CampaignChannel.Email, "body", "subj").Value;
            Campaigns.Campaigns.Add(Campaign);
        }

        public Campaign Campaign { get; }

        public FakeCampaignRepository Campaigns { get; } = new();

        public FakeCampaignRunRepository Runs { get; } = new();

        public FakeMessageBus Bus { get; } = new();

        public IWalletSpendClient Wallet { get; init; } = new FakeWalletSpendClient();

        public StartCampaignRunCommand StartCommand(params string?[] emails) =>
            new(
                Tenant,
                Campaign.Id,
                Actor,
                emails.Select(e => new StartRunRecipient(Guid.NewGuid().ToString("N"), e, null)).ToList()
            );

        /// <summary>Un run con sus unidades despachadas, ya guardado: sus eventos ya salieron.</summary>
        public CampaignRun Dispatched(params string[] emails)
        {
            var run = CampaignRun
                .Start(
                    Tenant,
                    Campaign.Id,
                    Actor,
                    "Manual",
                    emails
                        .Select(e => new RunRecipientDraft(
                            Guid.NewGuid().ToString("N"),
                            CampaignChannel.Email,
                            e,
                            null
                        ))
                        .ToList()
                )
                .Value;
            foreach (var recipient in run.Recipients.ToList())
                run.MarkDispatched(recipient.Id);
            run.ClearDomainEvents();
            Runs.Runs.Add(run);
            return run;
        }

        /// <summary>Lo emitido desde la última lectura: publicado a mano más lo devuelto por los handlers de dominio.</summary>
        public IReadOnlyList<object> Emitted()
        {
            Assert.Empty(Bus.Published.OfType<IDomainEvent>());
            List<object> emitted = [.. Bus.Published, .. CampaignDomainEventReplay.Dispatch(Runs.Runs)];
            Bus.Published.Clear();
            return emitted;
        }

        public async Task RunAsync(Type handler, object command, bool expectSuccess = true)
        {
            var services = new List<object>
            {
                Campaigns,
                Runs,
                Bus,
                Wallet,
                new FakeSenderProfileRepository(),
                new FakeUnitOfWork(),
                new FakeCorrelationContext(),
            };
            var method = handler
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Handle" && m.GetParameters()[0].ParameterType == command.GetType());
            var args = method
                .GetParameters()
                .Select(p =>
                    p.ParameterType == command.GetType() ? command
                    : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                    : IsLogger(p.ParameterType)
                        ? Activator.CreateInstance(
                            typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments())
                        )
                    : services.First(s => p.ParameterType.IsInstanceOfType(s))
                )
                .ToArray();

            var task = (Task)method.Invoke(null, args)!;
            await task;
            if (expectSuccess && task.GetType().GetProperty("Result")?.GetValue(task) is Result result)
                Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        private static bool IsLogger(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>);
    }

    private sealed class RejectingWallet : IWalletSpendClient
    {
        public Task<WalletReserveResult> ReserveAsync(
            Guid tenantId,
            string referenceType,
            Guid referenceId,
            WalletUnitCounts units,
            string? callerBearerToken,
            CancellationToken ct = default
        ) => Task.FromResult(new WalletReserveResult(false, true, 100, 0, 0, "USD"));

        public Task SettleAsync(
            Guid tenantId,
            string referenceType,
            Guid referenceId,
            int consumedUnits,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }
}
