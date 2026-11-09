using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Integration;

public sealed class UnmatchedIncomingEmailPersistenceIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.UnmatchedIncomingEmails.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task IngestRejected_RoundTripsWithNewColumns()
    {
        var accountId = Guid.NewGuid();
        var row = UnmatchedIncomingEmail
            .CreateRejected(
                _tenantId,
                "sender@example.test",
                "",
                "pm-int-1",
                DateTime.UtcNow,
                UnmatchedReason.IngestRejected,
                "IncomingEmail.AccountIdRequired",
                accountId,
                "gmail",
                "<int@x>"
            )
            .Value;
        await using (var db = CorrespondenceSqlServer.NewContext(_tenantId))
        {
            db.UnmatchedIncomingEmails.Add(row);
            await db.SaveChangesAsync();
        }

        await using var read = CorrespondenceSqlServer.NewContext(_tenantId);
        var saved = await read.UnmatchedIncomingEmails.AsNoTracking().SingleAsync(x => x.Id == row.Id);
        Assert.Equal(UnmatchedReason.IngestRejected, saved.Reason);
        Assert.Equal("IncomingEmail.AccountIdRequired", saved.RejectionCode);
        Assert.Equal(accountId, saved.AccountId);
        Assert.Equal("gmail", saved.ProviderCode);
        Assert.Equal("<int@x>", saved.InternetMessageId);
        Assert.Equal(EmailThread.NoSubject, saved.Subject);
    }
}
