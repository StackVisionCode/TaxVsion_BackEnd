using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Referrals.Application.Attributions.FindAttributionByReferee;

namespace TaxVision.Growth.Tests.Application;

public sealed class FindAttributionByRefereeHandlerTests
{
    [Fact]
    public async Task A_referee_without_an_attribution_yields_null()
    {
        var result = await FindAttributionByRefereeHandler.Handle(
            new FindAttributionByRefereeQuery(Guid.NewGuid(), Guid.NewGuid()),
            new InMemoryReferralAttributionRepository(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_empty_owner_or_referee_is_rejected(bool emptyOwner)
    {
        var id = Guid.NewGuid();
        var query = emptyOwner ? new FindAttributionByRefereeQuery(Guid.Empty, id) : new FindAttributionByRefereeQuery(id, Guid.Empty);

        var result = await FindAttributionByRefereeHandler.Handle(query, new InMemoryReferralAttributionRepository(), default);

        Assert.True(result.IsFailure);
        Assert.Equal("ReferralAttribution.InvalidReferee", result.Error.Code);
    }
}
