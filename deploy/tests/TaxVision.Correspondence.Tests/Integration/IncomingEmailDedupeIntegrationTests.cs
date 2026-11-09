using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// Contra el SQL Server real: sin InternetMessageId, el índice filtrado por (TenantId, AccountId,
/// ProviderMessageId) frena la segunda copia de dos ingestas concurrentes del mismo mensaje.
/// </summary>
public sealed class IncomingEmailDedupeIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _accountId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.IncomingEmails.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private IncomingEmail NewEmail(string providerMessageId, string? internetMessageId) =>
        IncomingEmail
            .Create(
                _tenantId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                _accountId,
                "gmail",
                providerMessageId,
                EmailAddress.Create("sender@cor09.test").Value,
                null,
                "Subject",
                "",
                DateTime.UtcNow,
                false,
                0,
                internetMessageId
            )
            .Value;

    private async Task InsertAsync(IncomingEmail email)
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        db.IncomingEmails.Add(email);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task NoInternetMessageId_SameProviderMessage_SecondInsertFails()
    {
        await InsertAsync(NewEmail("pm-dup", null));

        // El DbContext traduce la violación del índice único a ConflictException.
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => InsertAsync(NewEmail("pm-dup", null)));
        Assert.IsType<DbUpdateException>(conflict.InnerException);

        // Con InternetMessageId distinto y no nulo, el mismo ProviderMessageId no choca.
        await InsertAsync(NewEmail("pm-with-id", "<a@cor09.test>"));
        await InsertAsync(NewEmail("pm-with-id", "<b@cor09.test>"));
    }
}
