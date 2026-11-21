using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Files;
using TaxVision.Correspondence.Tests.Integration;

namespace TaxVision.Correspondence.Tests.Persistence;

// Contra SQL Server (base temporal): índice por archivo de los adjuntos entrantes y la rama de entrantes traducida.
public sealed class IncomingAttachmentFileIndexTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _mailbox = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenant);
        await db.IncomingEmails.IgnoreQueryFilters().Where(x => x.TenantId == _tenant).ExecuteDeleteAsync();
        await db.EmailThreads.IgnoreQueryFilters().Where(x => x.TenantId == _tenant).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task The_index_by_file_exists_and_skips_attachments_never_opened()
    {
        await using var connection = new SqlConnection(CorrespondenceSqlServer.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            SELECT i.has_filter, i.filter_definition
            FROM sys.indexes i
            WHERE i.object_id = OBJECT_ID(N'dbo.IncomingEmailAttachments')
              AND i.name = N'IX_IncomingEmailAttachments_TenantId_CloudStorageFileId'
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync(), "IX_IncomingEmailAttachments_TenantId_CloudStorageFileId is missing.");
        Assert.True(reader.GetBoolean(0));
        Assert.Contains("CloudStorageFileId", reader.GetString(1));
    }

    [Fact]
    public async Task A_downloaded_incoming_attachment_is_found_by_its_file()
    {
        var file = Guid.NewGuid();
        var thread = MailFileTestData.Thread(_tenant, Guid.NewGuid());
        var email = MailFileTestData.Incoming(thread, _mailbox, file);
        await using (var seed = CorrespondenceSqlServer.NewContext(_tenant))
        {
            seed.AddRange(thread, email);
            await seed.SaveChangesAsync();
        }

        await using var db = CorrespondenceSqlServer.NewContext(_tenant);
        var items = await new MailFileVisibilityReader(db).GetVisibleFilesAsync(
            _tenant,
            [file],
            [_mailbox],
            null,
            Guid.NewGuid(),
            false
        );

        var item = Assert.Single(items);
        Assert.Equal(thread.Id, item.ResourceId);
        Assert.False(item.CanDownload);
    }
}
