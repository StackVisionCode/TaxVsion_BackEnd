using System.Net;
using BuildingBlocks.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Campaigns.Application.Runs.Abstractions;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Infrastructure.Wallet;

namespace TaxVision.Campaigns.Tests.Infrastructure;

/// <summary>Un 4xx de liquidar es terminal, salvo 401, 403, 408, el 409 de concurrencia del Wallet y el 429.</summary>
public sealed class WalletSpendClientTests
{
    private sealed class FixedToken : IServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("token");
    }

    private sealed class FixedStatus(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private static WalletSpendClient Client(HttpStatusCode status) =>
        new(
            new HttpClient(new FixedStatus(status)) { BaseAddress = new Uri("http://wallet/") },
            new FixedToken(),
            NullLogger<WalletSpendClient>.Instance
        );

    private static Task SettleAsync(HttpStatusCode status) =>
        Client(status).SettleAsync(Guid.NewGuid(), WalletReferenceTypes.CampaignRun, Guid.NewGuid(), 1);

    [Fact]
    public async Task Settle_Conflict_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SettleAsync(HttpStatusCode.Conflict));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Settle_TransientStatus_Throws(HttpStatusCode status)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SettleAsync(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Settle_TerminalStatus_IsNoOp(HttpStatusCode status)
    {
        await SettleAsync(status);
    }

    [Fact]
    public async Task Reserve_Conflict_BlocksTheRun()
    {
        var result = await Client(HttpStatusCode.Conflict)
            .ReserveAsync(
                Guid.NewGuid(),
                WalletReferenceTypes.CampaignRun,
                Guid.NewGuid(),
                new WalletUnitCounts(0, 1, 0, 0),
                "token"
            );

        Assert.False(result.Reachable);
    }
}
