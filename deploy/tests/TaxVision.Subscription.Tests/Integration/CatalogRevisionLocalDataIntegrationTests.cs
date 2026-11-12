using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Subscription.Application.Catalog.Revisions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Catalog;
using TaxVision.Subscription.Infrastructure.Configuration;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Infrastructure.Scheduling;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit.Abstractions;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// F3 con datos reales, sin tocarlos: copia la base local de desarrollo a una base temporal, corre R1 y R2 con el
/// runner y comprueba que ninguna oficina cambia de términos ni pierde derechos, y que solo Enterprise queda a la
/// venta. La base local solo se lee.
/// </summary>
public sealed class CatalogRevisionLocalDataIntegrationTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Prefix = "subcopy_";

    private readonly string _sourceConnection =
        new ConfigurationBuilder()
            .AddUserSecrets("e4b1c9d2-7f3a-4a86-9b2d-subscription1")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set the Subscription Api user-secrets to run this check.");

    private string _copyConnection = default!;

    private static readonly HashSet<string> PlatformKeys = EntitlementKeyCatalog
        .PlatformDefaults.Select(seed => seed.Key)
        .ToHashSet(StringComparer.Ordinal);

    public async Task InitializeAsync()
    {
        _copyConnection = new SqlConnectionStringBuilder(_sourceConnection)
        {
            InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
        }.ConnectionString;
        await using (var db = Context(_copyConnection))
            await db.Database.EnsureCreatedAsync();
        await CopyAsync(new SqlConnectionStringBuilder(_sourceConnection).InitialCatalog);
    }

    public async Task DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var db = Context(_copyConnection);
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task R1_and_R2_keep_every_office_as_it_is_and_leave_only_enterprise_on_sale()
    {
        var before = await LoadAsync();
        Assert.NotEmpty(before.Subscriptions);

        await Runner().RunAsync(CatalogRevisionCatalog.All, CancellationToken.None);
        var after = await LoadAsync();

        // Solo Enterprise a la venta, con los términos nuevos.
        Assert.Equal(["enterprise"], after.Plans.Values.Where(p => p.IsOnSale).Select(p => p.Code.Value));
        var enterprise = CommercialTerms.Of(after.Plans["enterprise"].GetPublishedVersion()!);
        Assert.Equal(3, enterprise.SeatsMax);
        Assert.Equal(16_106_127_360, enterprise.StorageMaxBytes);

        var problems = new List<string>();
        foreach (var subscription in after.Subscriptions)
        {
            var old = before.Subscriptions.Single(s => s.TenantId == subscription.TenantId);
            var oldTerms = CommercialTerms.Of(Contracted(before, old));
            var contracted = Contracted(after, subscription);
            if (CommercialTerms.Of(contracted) != oldTerms)
                problems.Add($"{subscription.TenantId}: commercial terms changed");

            // Nadie queda en las ediciones que publicó R2: esas son solo para clientes nuevos.
            if (
                contracted.Id == after.Plans[subscription.PlanCode].GetPublishedVersion()!.Id
                && contracted.Id != before.Plans[subscription.PlanCode].GetPublishedVersion()!.Id
            )
                problems.Add($"{subscription.TenantId}: moved to the new R2 edition");

            var snapshot = before.Snapshots.SingleOrDefault(s => s.TenantId == subscription.TenantId);
            if (snapshot is null)
                continue;
            var expected = ContractedEntitlementPolicy
                .BaseEntries(contracted, after.Modules, SubscriptionAccess.GrantsAccess(subscription.Status))
                .Value;
            foreach (var entry in snapshot.Entries.Where(entry => entry.Source == EntitlementSource.Plan))
                if (!expected.TryGetValue(entry.Key.Value, out var now) || now.Value != entry.Value)
                    problems.Add(
                        $"{subscription.TenantId} {entry.Key.Value}: {entry.Value} -> {now?.Value ?? "(none)"}"
                    );
            var stored = snapshot.Entries.Select(entry => entry.Key.Value).ToHashSet(StringComparer.Ordinal);
            foreach (var key in expected.Keys.Where(key => !stored.Contains(key) && !PlatformKeys.Contains(key)))
                problems.Add($"{subscription.TenantId} {key}: (none) -> {expected[key].Value}");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // Idempotente: otra corrida no crea ediciones ni registros.
        await Runner().RunAsync(CatalogRevisionCatalog.All, CancellationToken.None);
        var again = await LoadAsync();
        Assert.Equal(after.Plans.Values.Sum(p => p.Versions.Count), again.Plans.Values.Sum(p => p.Versions.Count));
        await using var db = Context(_copyConnection);
        var rows = await db.CatalogRevisions.AsNoTracking().OrderBy(row => row.AppliedAtUtc).ToListAsync();
        Assert.Equal(CatalogRevisionCatalog.All.Count, rows.Count);
        foreach (var row in rows)
            output.WriteLine($"{row.RevisionId}: {row.Summary}");
        // F0: en local ninguna oficina tiene términos distintos entre ediciones.
        Assert.EndsWith("skipped 0", rows[0].Summary);
    }

    private static SubscriptionPlanVersion Contracted(Snapshot state, TenantSubscription subscription) =>
        state.Plans[subscription.PlanCode].Versions.Single(version => version.Id == subscription.PlanVersionId);

    private sealed record Snapshot(
        Dictionary<string, SubscriptionPlan> Plans,
        List<TenantSubscription> Subscriptions,
        List<TenantEntitlementSnapshot> Snapshots,
        List<TaxVision.Subscription.Domain.Catalog.Module> Modules
    );

    private async Task<Snapshot> LoadAsync()
    {
        await using var db = Context(_copyConnection);
        var plans = await db
            .Plans.AsNoTracking()
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.Features)
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.Entitlements)
            .Include(plan => plan.Versions)
                .ThenInclude(version => version.PriceTiers)
            .ToDictionaryAsync(plan => plan.Code.Value);
        var subscriptions = await db.Subscriptions.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var snapshots = await db.EntitlementSnapshots.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        var modules = await db.Modules.AsNoTracking().ToListAsync();
        return new Snapshot(plans, subscriptions, snapshots, modules);
    }

    // Copia fila por fila las tablas del modelo desde la base local (solo lectura) a la temporal.
    private async Task CopyAsync(string sourceDatabase)
    {
        await using var connection = new SqlConnection(_copyConnection);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (
            var list = new SqlCommand(
                "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id "
                    + "WHERE s.name = 'dbo' AND t.name <> '__EFMigrationsHistory'",
                connection
            )
        )
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));

        foreach (var table in tables)
            await ExecuteAsync(connection, $"ALTER TABLE [dbo].[{table}] NOCHECK CONSTRAINT ALL");

        // La base temporal trae las filas de HasData: se reemplazan por las de la base local.
        foreach (var table in tables)
            await ExecuteAsync(connection, $"DELETE FROM [dbo].[{table}]");

        foreach (var table in tables)
        {
            var columns = new List<string>();
            var hasIdentity = false;
            await using (
                var describe = new SqlCommand(
                    "SELECT c.name, c.is_identity FROM sys.columns c JOIN sys.types ty ON ty.user_type_id = c.user_type_id "
                        + "WHERE c.object_id = OBJECT_ID(@table) AND c.is_computed = 0 AND ty.name <> 'timestamp'",
                    connection
                )
            )
            {
                describe.Parameters.AddWithValue("@table", $"dbo.{table}");
                await using var reader = await describe.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add($"[{reader.GetString(0)}]");
                    hasIdentity |= reader.GetBoolean(1);
                }
            }

            var list = string.Join(", ", columns);
            var insert = $"INSERT INTO [dbo].[{table}] ({list}) SELECT {list} FROM [{sourceDatabase}].[dbo].[{table}]";
            await ExecuteAsync(
                connection,
                hasIdentity
                    ? $"SET IDENTITY_INSERT [dbo].[{table}] ON; {insert}; SET IDENTITY_INSERT [dbo].[{table}] OFF;"
                    : insert
            );
        }

        foreach (var table in tables)
            await ExecuteAsync(connection, $"ALTER TABLE [dbo].[{table}] WITH CHECK CHECK CONSTRAINT ALL");
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private CatalogRevisionRunner Runner()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext, NoTenant>();
        services.AddDbContext<SubscriptionDbContext>(options => options.UseSqlServer(_copyConnection));
        services.AddSubscriptionCatalogRevisions();
        services.AddSingleton<IDistributedLockFactory, FakeDistributedLockFactory>();
        services.Configure<SubscriptionOptions>(options => options.DefaultPlanCode = "enterprise");
        services.Configure<SubscriptionCatalogOptions>(_ => { });
        return services.BuildServiceProvider().GetRequiredService<CatalogRevisionRunner>();
    }

    private static SubscriptionDbContext Context(string connection) =>
        new(new DbContextOptionsBuilder<SubscriptionDbContext>().UseSqlServer(connection).Options, new NoTenant());

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
