using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Persistence;

public sealed class IncomingEmailRepositoryTests
{
    private static CorrespondenceDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static IncomingEmail NewIncomingEmail(Guid tenantId, Guid customerId, Guid emailThreadId) =>
        IncomingEmail
            .Create(
                tenantId,
                customerId,
                emailThreadId,
                Guid.NewGuid(),
                "gmail",
                "provider-msg-1",
                EmailAddress.Create("customer@example.com").Value,
                "The Customer",
                "Subject",
                "Snippet",
                DateTime.UtcNow,
                hasAttachments: false,
                attachmentCount: 0
            )
            .Value;

    [Fact]
    public async Task GetByIdAsync_returns_the_email_scoped_by_tenant()
    {
        await using var db = CreateContext();
        var repository = new IncomingEmailRepository(db);
        var tenantId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId, Guid.NewGuid(), Guid.NewGuid());
        await repository.AddAsync(email);
        await db.SaveChangesAsync();

        var found = await repository.GetByIdAsync(tenantId, email.Id);

        Assert.NotNull(found);
        Assert.Equal(email.Id, found!.Id);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_the_email_belongs_to_another_tenant()
    {
        await using var db = CreateContext();
        var repository = new IncomingEmailRepository(db);
        var email = NewIncomingEmail(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await repository.AddAsync(email);
        await db.SaveChangesAsync();

        var found = await repository.GetByIdAsync(Guid.NewGuid(), email.Id);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_when_the_id_does_not_exist()
    {
        await using var db = CreateContext();
        var repository = new IncomingEmailRepository(db);

        var found = await repository.GetByIdAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(found);
    }

    // ---------- ListByThreadAsync (Fase 9) ----------

    [Fact]
    public async Task ListByThreadAsync_WithFiveMessages_PaginatesInReceivedAtUtcAscendingOrder()
    {
        await using var db = CreateContext();
        var repository = new IncomingEmailRepository(db);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var emailThreadId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var emails = new List<IncomingEmail>();
        for (var i = 0; i < 5; i++)
        {
            var email = IncomingEmail
                .Create(
                    tenantId,
                    customerId,
                    emailThreadId,
                    Guid.NewGuid(),
                    "gmail",
                    $"provider-msg-{i}",
                    EmailAddress.Create("customer@example.com").Value,
                    "The Customer",
                    "Subject",
                    "Snippet",
                    now.AddMinutes(i),
                    hasAttachments: false,
                    attachmentCount: 0
                )
                .Value;
            emails.Add(email);
            await repository.AddAsync(email);
        }
        await db.SaveChangesAsync();

        var page1 = await repository.ListByThreadAsync(tenantId, emailThreadId, page: 1, size: 2);
        var page2 = await repository.ListByThreadAsync(tenantId, emailThreadId, page: 2, size: 2);
        var page3 = await repository.ListByThreadAsync(tenantId, emailThreadId, page: 3, size: 2);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal([emails[0].Id, emails[1].Id], page1.Items.Select(x => x.Id));
        Assert.Equal([emails[2].Id, emails[3].Id], page2.Items.Select(x => x.Id));
        Assert.Equal([emails[4].Id], page3.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task ListByThreadAsync_WithZeroOrNegativePageAndSize_ClampsToDefaults()
    {
        await using var db = CreateContext();
        var repository = new IncomingEmailRepository(db);
        var tenantId = Guid.NewGuid();
        var emailThreadId = Guid.NewGuid();
        var email = NewIncomingEmail(tenantId, Guid.NewGuid(), emailThreadId);
        await repository.AddAsync(email);
        await db.SaveChangesAsync();

        var result = await repository.ListByThreadAsync(tenantId, emailThreadId, page: 0, size: -5);

        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.Size);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task ListByThreadAsync_WithMessageFromAnotherThread_NeverReturnsIt()
    {
        await using var db = CreateContext();
        var repository = new IncomingEmailRepository(db);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ownThreadId = Guid.NewGuid();
        var ownEmail = NewIncomingEmail(tenantId, customerId, ownThreadId);
        var otherThreadEmail = NewIncomingEmail(tenantId, customerId, Guid.NewGuid());
        await repository.AddAsync(ownEmail);
        await repository.AddAsync(otherThreadEmail);
        await db.SaveChangesAsync();

        var result = await repository.ListByThreadAsync(tenantId, ownThreadId, page: 1, size: 20);

        var item = Assert.Single(result.Items);
        Assert.Equal(ownEmail.Id, item.Id);
    }

    // ---------- ListAllByThreadAsync (Fase 15) ----------

    [Fact]
    public async Task ListAllByThreadAsync_ReturnsAllMessagesUnpaginatedInReceivedAtUtcAscendingOrder()
    {
        await using var db = CreateContext();
        var repository = new IncomingEmailRepository(db);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var emailThreadId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var emails = new List<IncomingEmail>();
        for (var i = 0; i < 3; i++)
        {
            var email = IncomingEmail
                .Create(
                    tenantId,
                    customerId,
                    emailThreadId,
                    Guid.NewGuid(),
                    "gmail",
                    $"provider-msg-{i}",
                    EmailAddress.Create("customer@example.com").Value,
                    "The Customer",
                    "Subject",
                    "Snippet",
                    now.AddMinutes(2 - i),
                    hasAttachments: false,
                    attachmentCount: 0
                )
                .Value;
            emails.Add(email);
            await repository.AddAsync(email);
        }
        await db.SaveChangesAsync();

        var result = await repository.ListAllByThreadAsync(tenantId, emailThreadId);

        Assert.Equal([emails[2].Id, emails[1].Id, emails[0].Id], result.Select(x => x.Id));
    }

    [Fact]
    public async Task ListAllByThreadAsync_WithMessageFromAnotherThreadOrTenant_NeverReturnsIt()
    {
        await using var db = CreateContext();
        var repository = new IncomingEmailRepository(db);
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var ownThreadId = Guid.NewGuid();
        var ownEmail = NewIncomingEmail(tenantId, customerId, ownThreadId);
        var otherThreadEmail = NewIncomingEmail(tenantId, customerId, Guid.NewGuid());
        var otherTenantEmail = NewIncomingEmail(Guid.NewGuid(), customerId, ownThreadId);
        await repository.AddAsync(ownEmail);
        await repository.AddAsync(otherThreadEmail);
        await repository.AddAsync(otherTenantEmail);
        await db.SaveChangesAsync();

        var result = await repository.ListAllByThreadAsync(tenantId, ownThreadId);

        var item = Assert.Single(result);
        Assert.Equal(ownEmail.Id, item.Id);
    }
}
