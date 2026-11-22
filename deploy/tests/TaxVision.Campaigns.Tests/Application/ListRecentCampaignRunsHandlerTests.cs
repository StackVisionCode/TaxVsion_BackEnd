using TaxVision.Campaigns.Application.Runs.Queries;
using TaxVision.Campaigns.Tests.Fakes;
using Xunit;

namespace TaxVision.Campaigns.Tests.Application;

/// <summary>Runs recientes del dashboard: el tope de filas lo pone el handler.</summary>
public sealed class ListRecentCampaignRunsHandlerTests
{
    [Theory]
    [InlineData(0, 5)]
    [InlineData(-3, 5)]
    [InlineData(3, 3)]
    [InlineData(20, 20)]
    [InlineData(500, 20)]
    public async Task Handle_ClampsTake(int requested, int expected)
    {
        var runs = new FakeCampaignRunRepository();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await ListRecentCampaignRunsHandler.Handle(
            new ListRecentCampaignRunsQuery(tenantId, requested, now),
            runs,
            CancellationToken.None
        );

        Assert.Equal((tenantId, expected, now), runs.LastRecentRequest);
    }
}
