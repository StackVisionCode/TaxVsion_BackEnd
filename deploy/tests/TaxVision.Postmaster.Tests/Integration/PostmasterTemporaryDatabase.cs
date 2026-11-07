using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Persistence;

namespace TaxVision.Postmaster.Tests.Integration;

/// <summary>Base temporal propia, sin host: se borra al terminar, y la de una corrida abortada se borra al arrancar.</summary>
internal sealed class PostmasterTemporaryDatabase(string prefix) : IAsyncDisposable
{
    /// <summary>Las clases que la usan corren en serie: crear y borrar bases en paralelo da fallos de login.</summary>
    public const string Collection = "postmaster-temporary-database";

    public string ConnectionString { get; } =
        new SqlConnectionStringBuilder(BaseConnection)
        {
            InitialCatalog = $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)],
        }.ConnectionString;

    private static string BaseConnection =>
        Environment.GetEnvironmentVariable("POSTMASTER_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("44669d5c-4874-4994-affe-1ca09fec4a42")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set POSTMASTER_DB_CONNECTION or the Postmaster user-secrets.");

    public async Task CreateAsync()
    {
        await DropOrphansAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public PostmasterDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PostmasterDbContext>().UseSqlServer(ConnectionString).Options, new NoTenant());

    /// <summary>Correo ya aceptado por el proveedor, con un destinatario por dirección.</summary>
    public async Task<SentMessage> SeedSentAsync(DateTime sentAtUtc, params string[] recipients)
    {
        var message = SentMessage
            .Queue(
                Guid.NewGuid(),
                Guid.NewGuid().ToString("N"),
                "Welcome",
                "no-reply@example.test",
                EmailStream.Transactional,
                "system-smtp",
                null,
                null,
                null,
                null,
                null,
                sentAtUtc
            )
            .Value;
        foreach (var recipient in recipients)
            message.AddRecipient(recipient, RecipientType.To, null);
        message.MarkAsSending();
        message.MarkAsSent($"provider-{message.Id:N}", sentAtUtc);

        await using var db = NewContext();
        db.SentMessages.Add(message);
        await db.SaveChangesAsync();
        return message;
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    public async Task DropOrphansAsync()
    {
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE name LIKE N'{prefix}[_]%';
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}

[CollectionDefinition(PostmasterTemporaryDatabase.Collection)]
public sealed class PostmasterTemporaryDatabaseCollection;
