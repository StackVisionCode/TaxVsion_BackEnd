using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Ingest;

public sealed class RepairCrossCustomerThreadsHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly FakeEmailThreadRepository _threads = new();
    private readonly FakeIncomingEmailRepository _incomingEmails;
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly List<Guid> _unitsRun = [];
    private readonly DateTime _t0 = DateTime.UtcNow.AddDays(-4);

    public RepairCrossCustomerThreadsHandlerTests() =>
        _incomingEmails = new FakeIncomingEmailRepository { Threads = _threads };

    private async Task SeedEmailAsync(Guid customerId, Guid threadId, DateTime receivedAtUtc) =>
        await _incomingEmails.AddAsync(
            IncomingEmail
                .Create(
                    _tenantId,
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

    // Hilo de A con 3 correos de A y 1 de B.
    private async Task<EmailThread> SeedMixedThreadAsync(string providerThreadId)
    {
        var thread = EmailThread.NewFromMessage(_tenantId, _customerA, "Subject", providerThreadId, _t0).Value;
        thread.AppendMessage(_t0.AddDays(1));
        thread.AppendMessage(_t0.AddDays(2));
        thread.AppendMessage(_t0.AddDays(3));
        await _threads.AddAsync(thread);
        for (var i = 0; i < 3; i++)
            await SeedEmailAsync(_customerA, thread.Id, _t0.AddDays(i));
        await SeedEmailAsync(_customerB, thread.Id, _t0.AddDays(3));
        return thread;
    }

    // Cada unidad corre su handler, como bus.InvokeAsync con su propia transacción.
    private FakeMessageBus UnitBus(Guid? failingThreadId = null) =>
        new()
        {
            OnInvoke = async message =>
            {
                var unit = Assert.IsType<RepairCrossCustomerThreadCommand>(message);
                _unitsRun.Add(unit.ThreadId);
                if (unit.ThreadId == failingThreadId)
                    throw new InvalidOperationException("Simulated failure on this thread.");
                return await RepairCrossCustomerThreadHandler.Handle(
                    unit,
                    _incomingEmails,
                    _threads,
                    _unitOfWork,
                    NullLogger<RepairCrossCustomerThreadCommand>.Instance,
                    CancellationToken.None
                );
            },
        };

    private Task<RepairCrossCustomerThreadsReport> RunAsync(
        RepairCrossCustomerThreadsCommand command,
        FakeMessageBus? bus = null
    ) =>
        RepairCrossCustomerThreadsHandler.Handle(
            command,
            _incomingEmails,
            bus ?? UnitBus(),
            NullLogger<RepairCrossCustomerThreadsCommand>.Instance,
            CancellationToken.None
        );

    private static (int, int, int, int, int) Counts(RepairCrossCustomerThreadsReport report) =>
        (report.ThreadsFound, report.EmailsFound, report.ThreadsRepaired, report.EmailsMoved, report.ThreadsCreated);

    [Fact]
    public async Task Repair_MovesForeignEmailsAndIsIdempotent()
    {
        var threadA = await SeedMixedThreadAsync("T1");

        var first = await RunAsync(new RepairCrossCustomerThreadsCommand(_tenantId, DryRun: false));
        var second = await RunAsync(new RepairCrossCustomerThreadsCommand(_tenantId, DryRun: false));

        Assert.False(first.DryRun);
        Assert.Equal((1, 1, 1, 1, 1), Counts(first));
        Assert.Empty(first.FailedThreadIds);
        Assert.Equal((0, 0, 0, 0, 0), Counts(second));
        Assert.Equal(2, _threads.All.Count);
        var threadB = Assert.Single(_threads.All, t => t.CustomerId == _customerB);
        Assert.Equal("T1", threadB.ProviderThreadId);
        Assert.Equal(1, threadB.MessageCount);
        Assert.Equal(_t0.AddDays(3), threadB.LastMessageAtUtc);
        Assert.Equal(threadB.Id, Assert.Single(_incomingEmails.All, e => e.CustomerId == _customerB).EmailThreadId);
        Assert.Equal(3, threadA.MessageCount);
        Assert.Equal(_t0.AddDays(2), threadA.LastMessageAtUtc);
    }

    [Fact]
    public async Task DryRun_IsTheDefaultAndOnlyReports()
    {
        var threadA = await SeedMixedThreadAsync("T1");

        var command = new RepairCrossCustomerThreadsCommand(_tenantId);
        var report = await RunAsync(command);

        Assert.True(command.DryRun);
        Assert.True(report.DryRun);
        Assert.Equal((1, 1, 0, 0, 0), Counts(report));
        Assert.Empty(_unitsRun);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
        Assert.Single(_threads.All);
        Assert.All(_incomingEmails.All, e => Assert.Equal(threadA.Id, e.EmailThreadId));
    }

    [Fact]
    public async Task Repair_RunsOneUnitPerThreadAndAFailedThreadDoesNotStopTheRest()
    {
        var failing = await SeedMixedThreadAsync("T1");
        var repaired = await SeedMixedThreadAsync("T2");

        var report = await RunAsync(
            new RepairCrossCustomerThreadsCommand(_tenantId, DryRun: false),
            UnitBus(failingThreadId: failing.Id)
        );

        Assert.Equal(2, _unitsRun.Count);
        Assert.Equal((2, 2, 1, 1, 1), Counts(report));
        Assert.Equal([failing.Id], report.FailedThreadIds);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
        Assert.Equal(3, repaired.MessageCount);
        Assert.Equal(4, failing.MessageCount);
    }
}
