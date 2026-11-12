using BuildingBlocks.Common;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Definitions.Admin;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;

namespace TaxVision.Growth.Tests.Application;

/// <summary>Las lecturas de administración de giftcards validan paginación, estado y existencia antes de responder.</summary>
public sealed class CodeAdminQueryHandlerTests
{
    private sealed class FakeReadModel : ICodeAdminReadModel
    {
        public int Calls { get; private set; }
        public bool DefinitionExists { get; init; } = true;

        public Task<PagedResult<CodeDefinitionDetailsResponse>> ListPlatformDefinitionsAsync(
            int page,
            int size,
            string? status,
            CancellationToken ct = default
        )
        {
            Calls++;
            return Task.FromResult(new PagedResult<CodeDefinitionDetailsResponse>([], page, size, 0));
        }

        public Task<PagedResult<CodeRedemptionRow>?> ListRedemptionsAsync(
            Guid codeDefinitionId,
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            Calls++;
            return Task.FromResult<PagedResult<CodeRedemptionRow>?>(
                DefinitionExists ? new PagedResult<CodeRedemptionRow>([], page, size, 0) : null
            );
        }
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_paging_is_rejected_without_querying(int page, int size)
    {
        var readModel = new FakeReadModel();

        var list = await CodeAdminQueryHandler.Handle(new ListPlatformCodesQuery(page, size), readModel, default);
        var redemptions = await CodeAdminQueryHandler.Handle(
            new ListCodeRedemptionsQuery(Guid.NewGuid(), page, size),
            readModel,
            default
        );

        Assert.Equal("Codes.Admin.InvalidPaging", list.Error.Code);
        Assert.Equal("Codes.Admin.InvalidPaging", redemptions.Error.Code);
        Assert.Equal(0, readModel.Calls);
    }

    [Fact]
    public async Task An_unknown_status_filter_is_rejected()
    {
        var result = await CodeAdminQueryHandler.Handle(
            new ListPlatformCodesQuery(1, 20, "Bogus"),
            new FakeReadModel(),
            default
        );

        Assert.Equal("Codes.ListCodes.InvalidStatus", result.Error.Code);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("active")]
    [InlineData(null)]
    public async Task A_known_or_empty_status_filter_lists(string? status)
    {
        var result = await CodeAdminQueryHandler.Handle(
            new ListPlatformCodesQuery(1, 20, status),
            new FakeReadModel(),
            default
        );

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Redemptions_of_a_missing_gift_card_are_not_found()
    {
        var result = await CodeAdminQueryHandler.Handle(
            new ListCodeRedemptionsQuery(Guid.NewGuid()),
            new FakeReadModel { DefinitionExists = false },
            default
        );

        Assert.Equal("Codes.ListRedemptions.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task An_empty_definition_id_is_rejected()
    {
        var result = await CodeAdminQueryHandler.Handle(
            new ListCodeRedemptionsQuery(Guid.Empty),
            new FakeReadModel(),
            default
        );

        Assert.Equal("Codes.ListRedemptions.InvalidDefinition", result.Error.Code);
    }
}
