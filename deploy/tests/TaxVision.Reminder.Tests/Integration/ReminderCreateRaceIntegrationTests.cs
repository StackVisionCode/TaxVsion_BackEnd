using System.Reflection;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Application.Reminders.Commands;
using TaxVision.Reminder.Domain.ValueObjects;
using TaxVision.Reminder.Infrastructure.Persistence;
using TaxVision.Reminder.Infrastructure.Persistence.Repositories;
using TaxVision.Reminder.Tests.Reminders;

namespace TaxVision.Reminder.Tests.Integration;

/// <summary>
/// REM-4 y BB-12 con SQL real, en una base temporal propia. El middleware de EF de Wolverine vuelve a llamar
/// <c>SaveChangesAsync</c> al terminar el handler: la entidad perdedora no puede seguir <c>Added</c>.
/// </summary>
public sealed class ReminderCreateRaceIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "remint_";

    private readonly string _connectionString = new SqlConnectionStringBuilder(ReminderTestDatabase.ConnectionString)
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task ConcurrentCreateSameKey_LoserReturnsWinner_AndSecondSaveDoesNotThrow()
    {
        const string key = "race:key";
        var winner = Build(key);
        await using (var first = NewContext())
        {
            first.Reminders.Add(winner);
            await first.SaveChangesAsync();
        }

        await using var loser = NewContext();
        var repository = BlindFirstLookupProxy.Wrap(new ReminderRepository(loser));

        var result = await CreateReminderHandler.Handle(
            new CreateReminderCommand(
                _tenantId,
                _userId,
                "Call the client",
                null,
                ReminderCategory.General,
                null,
                DateTime.UtcNow.AddHours(1),
                null,
                null,
                "UTC",
                key
            ),
            repository,
            new RecordingScheduler(),
            loser,
            new RecordingReminderMetrics(),
            NullLogger<ReminderAggregate>.Instance,
            CancellationToken.None
        );

        Assert.Equal(winner.Id, result.Value.Id);
        await loser.SaveChangesAsync();
        await using var read = NewContext();
        Assert.Equal(1, await read.Reminders.IgnoreQueryFilters().CountAsync(r => r.TenantId == _tenantId));
    }

    [Fact]
    public async Task TwoEdits_FromSameRead_SecondGetsConflict()
    {
        var reminder = Build("edit:key");
        await using (var seed = NewContext())
        {
            seed.Reminders.Add(reminder);
            await seed.SaveChangesAsync();
        }

        await using var winnerDb = NewContext();
        await using var loserDb = NewContext();
        var winner = await winnerDb.Reminders.IgnoreQueryFilters().SingleAsync(r => r.Id == reminder.Id);
        var loser = await loserDb.Reminders.IgnoreQueryFilters().SingleAsync(r => r.Id == reminder.Id);
        winner.ChangeSubject(ReminderSubject.Create("First", null).Value);
        loser.ChangeSubject(ReminderSubject.Create("Second", null).Value);

        await winnerDb.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => loserDb.SaveChangesAsync());

        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);
    }

    private ReminderAggregate Build(string key)
    {
        var nowUtc = DateTime.UtcNow;
        return ReminderAggregate
            .Create(
                _tenantId,
                _userId,
                ReminderSubject.Create("Call the client", null).Value,
                ReminderTarget.Create(ReminderCategory.General, null).Value,
                ReminderSchedule.Absolute(nowUtc.AddHours(1), nowUtc).Value,
                ReminderTimeZone.Create("UTC").Value,
                RequestKey.Create(key).Value,
                nowUtc
            )
            .Value;
    }

    private ReminderDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<ReminderDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenantContext()
        );

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE name LIKE N'{Prefix}%' AND create_date < DATEADD(HOUR, -1, GETDATE());
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}

/// <summary>El primer lookup no ve al ganador, como si los dos hubieran leído antes de que alguien guardara.</summary>
public class BlindFirstLookupProxy : DispatchProxy
{
    private IReminderRepository _inner = default!;
    private bool _firstLookupDone;

    public static IReminderRepository Wrap(IReminderRepository inner)
    {
        var proxy = Create<IReminderRepository, BlindFirstLookupProxy>();
        ((BlindFirstLookupProxy)(object)proxy)._inner = inner;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod!.Name == nameof(IReminderRepository.FindByRequestKeyAsync) && !_firstLookupDone)
        {
            _firstLookupDone = true;
            return Task.FromResult<ReminderAggregate?>(null);
        }

        return targetMethod.Invoke(_inner, args);
    }
}
