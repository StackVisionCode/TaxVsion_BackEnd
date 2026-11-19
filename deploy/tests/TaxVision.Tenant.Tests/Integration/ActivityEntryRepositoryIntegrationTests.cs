using System.Data.Common;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Infrastructure.Persistence;
using TaxVision.Tenant.Infrastructure.Team;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Lectura del historial contra SQL Server (base temporal de la corrida), dentro de una transacción que se revierte:
/// ABAC por asignación, privadas, facturas, rango semiabierto, orden, paginación, aislamiento por oficina e índice
/// único por evento.
/// </summary>
public sealed class ActivityEntryRepositoryIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Base = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _member = Guid.NewGuid();
    private readonly Guid _viewer = Guid.NewGuid();
    private readonly Guid _assignedCustomer = Guid.NewGuid();
    private readonly Guid _otherCustomer = Guid.NewGuid();

    private SqlConnection _connection = default!;
    private DbTransaction _transaction = default!;

    public async Task InitializeAsync()
    {
        _connection = new SqlConnection(TenantIntegrationDatabase.ConnectionString);
        await _connection.OpenAsync();
        _transaction = await _connection.BeginTransactionAsync();
    }

    public async Task DisposeAsync()
    {
        await _transaction.RollbackAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Without_customers_view_all_only_entries_without_customer_or_of_assigned_customers_are_read()
    {
        var none = await SeedAsync(ActivityVerbs.TaskReopened, Base, customerId: null);
        var assigned = await SeedAsync(ActivityVerbs.TaskCompleted, Base.AddMinutes(1), _assignedCustomer);
        await SeedAsync(ActivityVerbs.TaskCompleted, Base.AddMinutes(2), _otherCustomer);
        await AssignAsync(_assignedCustomer, _viewer);
        await AssignAsync(_otherCustomer, _member);

        var restricted = await ListAsync(Filter() with { AssignedCustomersOf = _viewer });
        var all = await ListAsync(Filter());

        Assert.Equal([assigned, none], restricted.Items.Select(e => e.Id));
        Assert.Equal(2, restricted.TotalCount);
        Assert.Equal(3, all.TotalCount);
    }

    [Fact]
    public async Task Private_and_restricted_entries_need_their_flags()
    {
        var team = await SeedAsync(ActivityVerbs.TaskCreated, Base, null);
        var note = await SeedAsync(ActivityVerbs.NoteCreated, Base.AddMinutes(1), null, ActivityVisibility.Private);
        var invoice = await SeedAsync(
            ActivityVerbs.InvoiceIssued,
            Base.AddMinutes(2),
            null,
            ActivityVisibility.Restricted
        );

        var plain = await ListAsync(Filter() with { IncludePrivate = false, IncludeRestricted = false });
        var withPrivate = await ListAsync(Filter() with { IncludePrivate = true, IncludeRestricted = false });
        var withInvoices = await ListAsync(Filter() with { IncludePrivate = false, IncludeRestricted = true });

        Assert.Equal([team], plain.Items.Select(e => e.Id));
        Assert.Equal([note, team], withPrivate.Items.Select(e => e.Id));
        Assert.Equal([invoice, team], withInvoices.Items.Select(e => e.Id));
    }

    [Fact]
    public async Task The_range_is_half_open_and_verbs_filter()
    {
        var atFrom = await SeedAsync(ActivityVerbs.TaskCreated, Base, null);
        var inside = await SeedAsync(ActivityVerbs.NoteCreated, Base.AddHours(1), null);
        await SeedAsync(ActivityVerbs.TaskCreated, Base.AddHours(2), null);
        await SeedAsync(ActivityVerbs.TaskCreated, Base.AddTicks(-1), null);

        var range = await ListAsync(Filter() with { FromUtc = Base, ToUtc = Base.AddHours(2) });
        var notes = await ListAsync(Filter() with { Verbs = [ActivityVerbs.NoteCreated] });

        Assert.Equal([inside, atFrom], range.Items.Select(e => e.Id));
        Assert.Equal([inside], notes.Items.Select(e => e.Id));
    }

    [Fact]
    public async Task Pages_are_most_recent_first_with_the_total()
    {
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
            ids.Add(await SeedAsync(ActivityVerbs.TaskCreated, Base.AddMinutes(i), null));
        ids.Reverse();

        var first = await ListAsync(Filter() with { Page = 1, Size = 2 });
        var third = await ListAsync(Filter() with { Page = 3, Size = 2 });

        Assert.Equal(ids.Take(2), first.Items.Select(e => e.Id));
        Assert.Equal(ids.Skip(4), third.Items.Select(e => e.Id));
        Assert.Equal(5, first.TotalCount);
    }

    [Fact]
    public async Task Another_office_or_another_member_is_never_read()
    {
        await SeedAsync(ActivityVerbs.TaskCreated, Base, null, tenantId: Guid.NewGuid());
        await SeedAsync(ActivityVerbs.TaskCreated, Base, null, actor: Guid.NewGuid());
        var mine = await SeedAsync(ActivityVerbs.TaskCreated, Base, null);

        var page = await ListAsync(Filter());

        Assert.Equal([mine], page.Items.Select(e => e.Id));
    }

    [Fact]
    public async Task Exists_finds_the_event_only_in_its_office_and_a_second_insert_hits_the_unique_index()
    {
        var eventId = Guid.NewGuid();
        await SeedAsync(ActivityVerbs.TaskCreated, Base, null, eventId: eventId);

        await using var db = NewContext();
        var repository = new ActivityEntryRepository(db);
        Assert.True(await repository.ExistsAsync(_tenantId, eventId));
        Assert.False(await repository.ExistsAsync(Guid.NewGuid(), eventId));

        repository.Add(
            Entry(ActivityVerbs.TaskCreated, Base, null, ActivityVisibility.Team, _tenantId, _member, eventId)
        );
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => db.SaveChangesAsync());
        Assert.Equal("Persistence.UniqueConstraint", conflict.Code);
    }

    private ActivityFeedFilter Filter() => new(_tenantId, _member, null, null, [], null, true, true, 1, 100);

    private async Task<BuildingBlocks.Common.PagedResult<ActivityEntry>> ListAsync(ActivityFeedFilter filter)
    {
        await using var db = NewContext();
        return await new ActivityEntryRepository(db).ListAsync(filter);
    }

    private async Task<Guid> SeedAsync(
        string verb,
        DateTime occurredAt,
        Guid? customerId,
        ActivityVisibility visibility = ActivityVisibility.Team,
        Guid? tenantId = null,
        Guid? actor = null,
        Guid? eventId = null
    )
    {
        var entry = Entry(verb, occurredAt, customerId, visibility, tenantId ?? _tenantId, actor ?? _member, eventId);
        await using var db = NewContext();
        new ActivityEntryRepository(db).Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private static ActivityEntry Entry(
        string verb,
        DateTime occurredAt,
        Guid? customerId,
        ActivityVisibility visibility,
        Guid tenantId,
        Guid actor,
        Guid? eventId
    ) =>
        ActivityEntry
            .Create(
                new ActivityEntryDraft(
                    tenantId,
                    eventId ?? Guid.NewGuid(),
                    actor,
                    verb,
                    Guid.NewGuid(),
                    customerId,
                    visibility,
                    occurredAt
                )
            )
            .Value;

    private async Task AssignAsync(Guid customerId, Guid userId)
    {
        await using var db = NewContext();
        await new CustomerAssignmentProjectionStore(db).ReplaceAsync(_tenantId, customerId, [userId], Base);
        await db.SaveChangesAsync();
    }

    private TenantDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlServer(_connection).Options;
        var db = new TenantDbContext(options, new NoTenantContext());
        db.Database.UseTransaction(_transaction);
        return db;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
