using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Ingest;

public sealed class RepairCrossCustomerThreadsHandlerTests
{
    private static async Task SeedEmailAsync(
        FakeIncomingEmailRepository incomingEmails,
        Guid tenantId,
        Guid customerId,
        Guid threadId,
        DateTime receivedAtUtc
    ) =>
        await incomingEmails.AddAsync(
            IncomingEmail
                .Create(
                    tenantId,
                    customerId,
                    threadId,
                    Guid.NewGuid(),
                    "gmail",
                    Guid.NewGuid().ToString(),
                    EmailAddress.Create("sender@example.com").Value,
                    null,
                    "Subject",
                    "",
                    receivedAtUtc,
                    false,
                    0
                )
                .Value
        );

    private static Task<RepairCrossCustomerThreadsResult> RunAsync(
        Guid tenantId,
        FakeIncomingEmailRepository incomingEmails,
        FakeEmailThreadRepository threads
    ) =>
        RepairCrossCustomerThreadsHandler.Handle(
            new RepairCrossCustomerThreadsCommand(tenantId),
            incomingEmails,
            threads,
            new FakeUnitOfWork(),
            NullLogger<RepairCrossCustomerThreadsCommand>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task Repair_MovesForeignEmailsAndIsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        var customerA = Guid.NewGuid();
        var customerB = Guid.NewGuid();
        var threads = new FakeEmailThreadRepository();
        var incomingEmails = new FakeIncomingEmailRepository { Threads = threads };
        var t0 = DateTime.UtcNow.AddDays(-4);
        var threadA = EmailThread.NewFromMessage(tenantId, customerA, "Subject", "T1", t0).Value;
        threadA.AppendMessage(t0.AddDays(1));
        threadA.AppendMessage(t0.AddDays(2));
        threadA.AppendMessage(t0.AddDays(3));
        await threads.AddAsync(threadA);
        for (var i = 0; i < 3; i++)
            await SeedEmailAsync(incomingEmails, tenantId, customerA, threadA.Id, t0.AddDays(i));
        await SeedEmailAsync(incomingEmails, tenantId, customerB, threadA.Id, t0.AddDays(3));

        var first = await RunAsync(tenantId, incomingEmails, threads);
        var second = await RunAsync(tenantId, incomingEmails, threads);

        Assert.Equal(new RepairCrossCustomerThreadsResult(1, 1), first);
        Assert.Equal(new RepairCrossCustomerThreadsResult(0, 0), second);
        Assert.Equal(2, threads.All.Count);
        var threadB = Assert.Single(threads.All, t => t.CustomerId == customerB);
        Assert.Equal("T1", threadB.ProviderThreadId);
        Assert.Equal(1, threadB.MessageCount);
        Assert.Equal(t0.AddDays(3), threadB.LastMessageAtUtc);
        Assert.Equal(threadB.Id, Assert.Single(incomingEmails.All, e => e.CustomerId == customerB).EmailThreadId);
        Assert.Equal(3, threadA.MessageCount);
        Assert.Equal(t0.AddDays(2), threadA.LastMessageAtUtc);
    }
}
