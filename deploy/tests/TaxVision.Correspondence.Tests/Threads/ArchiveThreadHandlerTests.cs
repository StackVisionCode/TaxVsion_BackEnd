using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Tests.Ingest;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Threads;

public sealed class ArchiveThreadHandlerTests
{
    [Fact]
    public async Task Handle_WithAnActiveThread_ArchivesItAndPersists()
    {
        var tenantId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, Guid.NewGuid(), "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);
        var unitOfWork = new FakeUnitOfWork();

        var result = await ArchiveThreadHandler.Handle(
            new ArchiveThreadCommand(tenantId, thread.Id),
            emailThreads,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailThreadStatus.Archived, thread.Status);
        Assert.NotNull(thread.ArchivedAtUtc);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    /// <summary>
    /// Matches EmailThread.Archive()'s own idempotency contract (Fase 3, see EmailThreadTests):
    /// calling it twice is a no-op success that does not move ArchivedAtUtc. The handler doesn't
    /// special-case "already archived" — it just calls Archive() and saves again.
    /// </summary>
    [Fact]
    public async Task Handle_CalledTwice_IsIdempotentAndKeepsTheOriginalArchivedAtUtc()
    {
        var tenantId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, Guid.NewGuid(), "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);
        var unitOfWork = new FakeUnitOfWork();

        var firstResult = await ArchiveThreadHandler.Handle(
            new ArchiveThreadCommand(tenantId, thread.Id),
            emailThreads,
            unitOfWork,
            CancellationToken.None
        );
        var firstArchivedAt = thread.ArchivedAtUtc;

        await Task.Delay(10);

        var secondResult = await ArchiveThreadHandler.Handle(
            new ArchiveThreadCommand(tenantId, thread.Id),
            emailThreads,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);
        Assert.Equal(EmailThreadStatus.Archived, thread.Status);
        Assert.Equal(firstArchivedAt, thread.ArchivedAtUtc);
        Assert.Equal(2, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithUnknownThread_ReturnsNotFound()
    {
        var emailThreads = new FakeEmailThreadRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await ArchiveThreadHandler.Handle(
            new ArchiveThreadCommand(Guid.NewGuid(), Guid.NewGuid()),
            emailThreads,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailThread.NotFound", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_WithThreadFromAnotherTenant_ReturnsNotFound()
    {
        var thread = EmailThread.NewFromMessage(Guid.NewGuid(), Guid.NewGuid(), "Subject", null, DateTime.UtcNow).Value;
        var emailThreads = new FakeEmailThreadRepository();
        await emailThreads.AddAsync(thread);
        var unitOfWork = new FakeUnitOfWork();

        var result = await ArchiveThreadHandler.Handle(
            new ArchiveThreadCommand(Guid.NewGuid(), thread.Id),
            emailThreads,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("EmailThread.NotFound", result.Error.Code);
        Assert.Equal(EmailThreadStatus.Active, thread.Status);
    }
}
