using BuildingBlocks.Tenancy;
using TaxVision.Codes.Application.Definitions.ExpireCode;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Growth.Tests.Application.Fakes;
using TaxVision.Growth.Tests.Domain;

namespace TaxVision.Growth.Tests.Application;

/// <summary>El job diario cierra un código vencido a la vez; uno que no venció queda igual.</summary>
public sealed class ExpireCodeHandlerTests
{
    [Fact]
    public async Task A_code_past_its_end_date_is_expired()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();

        var expired = await ExpireCodeHandler.Handle(
            new ExpireCodeCommand(PlatformTenant.Id, definition.Id),
            new InMemoryCodeDefinitionRepository(definition),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc.AddDays(31))),
            CancellationToken.None
        );

        Assert.True(expired.IsSuccess, expired.IsFailure ? expired.Error.Code : null);
        Assert.Equal(CodeDefinitionStatus.Expired, definition.Status);
    }

    [Fact]
    public async Task A_code_still_in_date_is_left_alone()
    {
        var definition = GrowthTestData.CreateActivePercentageCode();

        var expired = await ExpireCodeHandler.Handle(
            new ExpireCodeCommand(PlatformTenant.Id, definition.Id),
            new InMemoryCodeDefinitionRepository(definition),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.Equal("Codes.CodeDefinition.NotDueForExpiry", expired.Error.Code);
        Assert.Equal(CodeDefinitionStatus.Active, definition.Status);
    }

    [Fact]
    public async Task An_unknown_code_is_not_found()
    {
        var expired = await ExpireCodeHandler.Handle(
            new ExpireCodeCommand(PlatformTenant.Id, Guid.NewGuid()),
            new InMemoryCodeDefinitionRepository(),
            new FixedTimeProvider(new DateTimeOffset(GrowthTestData.NowUtc)),
            CancellationToken.None
        );

        Assert.Equal("Codes.ExpireCode.NotFound", expired.Error.Code);
    }
}
