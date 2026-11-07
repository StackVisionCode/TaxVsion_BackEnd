using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Audit;
using TaxVision.Connectors.Application.Messages;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Persistence.Repositories;
using TaxVision.Connectors.Tests.Messages;
using TaxVision.Connectors.Tests.Sync;
using Wolverine;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>
/// Cuerpo, adjunto y aviso de entrega se leen del proveedor sin una transacción abierta. Proveedor falso: nunca se
/// llama a Gmail ni a Graph.
/// </summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class MessageFetchTransactionIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("confetch");
    private readonly FakeEmailProviderClient _provider = new(ProviderCode.Gmail);

    public Task InitializeAsync() =>
        _host.StartAsync(
            services =>
            {
                services.AddScoped<IEmailProviderClientFactory>(_ => new FakeEmailProviderClientFactory(_provider));
                services.AddScoped<IProviderConnectionAuditLogRepository, ProviderConnectionAuditLogRepository>();
                services.AddSingleton<IMessageBodyRateLimiter, FakeMessageBodyRateLimiter>();
                services.AddSingleton<IAttachmentRateLimiter, FakeAttachmentRateLimiter>();
                services.AddSingleton<IDeliveryReportParser, StubDeliveryReportParser>();
            },
            options =>
            {
                options.Discovery.IncludeType(typeof(GetMessageBodyHandler));
                options.Discovery.IncludeType(typeof(GetMessageAttachmentHandler));
                options.Discovery.IncludeType(typeof(InspectDeliveryReportHandler));
            }
        );

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private sealed class StubDeliveryReportParser : IDeliveryReportParser
    {
        public DeliveryReport Parse(byte[] raw) => DeliveryReport.Empty;
    }

    private IMessageBus Bus => _host.Services.GetRequiredService<IMessageBus>();

    [Fact(Timeout = 120_000)]
    public async Task GetMessageBody_CallsTheProviderWithoutAnOpenTransaction()
    {
        var (tenantId, accountId) = await SeedAccountAsync();
        int? openDuringFetch = null;
        _provider.OnGetMessageBody = (_, _) =>
        {
            openDuringFetch = _host.Transactions.Open;
            return new MessageBody(10, "<p>x</p>", "x", new Dictionary<string, string>(), []);
        };

        var result = await Bus.InvokeAsync<Result<MessageBodyDto>>(new GetMessageBodyQuery(tenantId, accountId, "m-1"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, openDuringFetch);
    }

    [Fact(Timeout = 120_000)]
    public async Task GetMessageAttachment_CallsTheProviderWithoutAnOpenTransaction()
    {
        var (tenantId, accountId) = await SeedAccountAsync();
        int? openDuringFetch = null;
        _provider.OnGetMessage = (_, id) =>
        {
            openDuringFetch = _host.Transactions.Open;
            return MessageWithAttachment(id);
        };

        var result = await Bus.InvokeAsync<Result<MessageAttachmentDownload>>(
            new GetMessageAttachmentQuery(tenantId, accountId, "m-1", "att-1", "a.pdf", 3, null)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, openDuringFetch);
    }

    [Fact(Timeout = 120_000)]
    public async Task InspectDeliveryReport_CallsTheProviderWithoutAnOpenTransaction()
    {
        var (tenantId, accountId) = await SeedAccountAsync();
        int? openDuringFetch = null;
        _provider.OnGetRawMessage = _ =>
        {
            openDuringFetch = _host.Transactions.Open;
            return [1];
        };

        await Bus.InvokeAsync(new InspectDeliveryReport(accountId, tenantId, "m-1", DateTime.UtcNow, "corr-fetch"));

        Assert.Equal(0, openDuringFetch);
    }

    private static RawMessage MessageWithAttachment(string id) =>
        new(
            id,
            null,
            null,
            null,
            [],
            "customer@example.com",
            null,
            ["office@gmail.com"],
            [],
            [],
            "Subject",
            "Snippet",
            DateTime.UtcNow,
            [new RawMessageAttachment("att-1", "a.pdf", "application/pdf", 3)],
            AuthenticationSignals.Unknown
        );

    private async Task<(Guid TenantId, Guid AccountId)> SeedAccountAsync()
    {
        var now = DateTime.UtcNow;
        var tenantId = Guid.NewGuid();
        var account = TenantEmailAccount
            .Create(tenantId, $"{Guid.NewGuid():N}@example.test", ProviderCode.Gmail, Guid.NewGuid(), now)
            .Value;
        account.MarkConnected(now);
        account.Activate(now);
        await using var db = _host.NewContext();
        db.TenantEmailAccounts.Add(account);
        await db.SaveChangesAsync();
        return (tenantId, account.Id);
    }
}
