using TaxVision.Correspondence.Application.Compose;
using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Compose;

public sealed class CreateDraftHandlerTests
{
    [Fact]
    public async Task Handle_WithValidIds_CreatesAndPersistsANewDraft_AndReturnsItsId()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateDraftHandler.Handle(
            new CreateDraftCommand(tenantId, customerId, accountId, Guid.NewGuid()),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value);

        var persisted = Assert.Single(drafts.All);
        Assert.Equal(result.Value, persisted.Id);
        Assert.Equal(tenantId, persisted.TenantId);
        Assert.Equal(customerId, persisted.CustomerId);
        Assert.Equal(accountId, persisted.AccountId);
        Assert.Equal(DraftStatus.Draft, persisted.Status);
        Assert.Null(persisted.ReplyContext);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithEmptyCustomerId_FailsAndDoesNotPersist()
    {
        var drafts = new FakeDraftRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateDraftHandler.Handle(
            new CreateDraftCommand(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Guid.NewGuid()),
            drafts,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Draft.CustomerIdRequired", result.Error.Code);
        Assert.Empty(drafts.All);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
