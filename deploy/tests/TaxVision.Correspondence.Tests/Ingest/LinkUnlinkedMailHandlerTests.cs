using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Ingest;

public sealed class LinkUnlinkedMailHandlerTests
{
    private const string Address = "nuevo@example.com";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly FakeIncomingEmailRepository _emails = new();
    private readonly FakeEmailThreadRepository _threads = new();

    private async Task<EmailThread> SeedThreadAsync(string? providerThreadId = null, Guid? customerId = null)
    {
        var thread = EmailThread
            .NewFromMessage(_tenantId, customerId, "Subject", providerThreadId, DateTime.UtcNow.AddDays(-1))
            .Value;
        await _threads.AddAsync(thread);
        return thread;
    }

    private async Task<IncomingEmail> SeedEmailAsync(EmailThread thread, string from)
    {
        var email = IncomingEmail
            .Create(
                _tenantId,
                null,
                thread.Id,
                Guid.NewGuid(),
                "gmail",
                Guid.NewGuid().ToString(),
                EmailAddress.Create(from).Value,
                null,
                "Subject",
                "",
                DateTime.UtcNow.AddHours(-1),
                false,
                0
            )
            .Value;
        await _emails.AddAsync(email);
        return email;
    }

    private Task HandleAsync() =>
        LinkUnlinkedMailHandler.Handle(
            new LinkUnlinkedMailCommand(_tenantId, _customerId, Address),
            _emails,
            _threads,
            new FakeUnitOfWork(),
            NullLogger<LinkUnlinkedMailCommand>.Instance,
            CancellationToken.None
        );

    [Fact]
    public async Task CustomerCreatedWithAddress_LinksPendingMessages()
    {
        var first = await SeedThreadAsync();
        var second = await SeedThreadAsync();
        var one = await SeedEmailAsync(first, Address);
        var two = await SeedEmailAsync(second, Address);

        await HandleAsync();
        await HandleAsync();

        Assert.Equal(_customerId, one.CustomerId);
        Assert.Equal(_customerId, two.CustomerId);
        Assert.Equal(_customerId, first.CustomerId);
        Assert.Equal(_customerId, second.CustomerId);
        Assert.Equal(2, _threads.All.Count);
    }

    [Fact]
    public async Task ThreadSharedWithAnotherSender_OnlyMovesThisAddress()
    {
        var shared = await SeedThreadAsync("pt-1");
        var mine = await SeedEmailAsync(shared, Address);
        var other = await SeedEmailAsync(shared, "other@example.com");

        await HandleAsync();

        Assert.Null(shared.CustomerId);
        Assert.Null(other.CustomerId);
        Assert.Equal(shared.Id, other.EmailThreadId);
        Assert.Equal(1, shared.MessageCount);

        var created = _threads.All.Single(t => t.CustomerId == _customerId);
        Assert.Equal(created.Id, mine.EmailThreadId);
        Assert.Equal(_customerId, mine.CustomerId);
        Assert.Equal(1, created.MessageCount);
        Assert.Equal("pt-1", created.ProviderThreadId);
    }

    [Fact]
    public async Task CustomerAlreadyHasTheProviderThread_MovesIntoIt()
    {
        var existing = await SeedThreadAsync("pt-1", _customerId);
        var unlinked = await SeedThreadAsync("pt-1");
        var email = await SeedEmailAsync(unlinked, Address);

        await HandleAsync();

        Assert.Equal(existing.Id, email.EmailThreadId);
        Assert.Null(unlinked.CustomerId);
        Assert.Equal(0, unlinked.MessageCount);
    }

    [Fact]
    public async Task NoPendingMail_DoesNothing()
    {
        await HandleAsync();

        Assert.Empty(_threads.All);
    }
}
