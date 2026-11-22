using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TimeZones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files.Queries;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using static TaxVision.CloudStorage.Tests.Integration.Performance.DashboardPerformance;

namespace TaxVision.CloudStorage.Tests.Integration.Performance;

/// <summary>
/// Volumen del dashboard en CloudStorage: 200 000 archivos en un tenant de prueba y los de ruido, en la base temporal
/// BB-10. Mide <c>GET /storage/files/summary</c> con la oficina completa y con un empleado de 500 clientes asignados
/// de 20 000 (sus ids se leen de la proyección, como el resolver), y <c>GET /storage/usage?breakdown=true</c>, que es
/// dato de oficina sin recorte.
/// </summary>
[Trait("Category", "Performance")]
public sealed class CloudStorageDashboardPerformanceTests : IAsyncLifetime
{
    private const int Files = 200_000;
    private const int Customers = 20_000;
    private const int Employees = 40;
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private static readonly ResolvedTimeZone Zone = ResolvedTimeZone.Resolve("America/New_York");
    private static readonly ChecksumSha256 Checksum = ChecksumSha256.Create(new string('a', 64)).Value;
    private static readonly Guid[] Tenants = [.. AllTenants];
    private static readonly Guid Employee = Id("a5e5", 1, 0);
    private static readonly string ConnectionString = CloudStorageIntegrationDatabase.ConnectionString;
    private static readonly string[] OwnTables = ["[dbo].[Files]", "[dbo].[CustomerAssignmentProjections]"];

    public async Task InitializeAsync()
    {
        var template = await TemplateAsync();
        for (var t = 1; t <= Tenants.Length; t++)
            await SeedTenantAsync(t, template, DivisorOf(t));
        await UpdateStatisticsAsync(ConnectionString, OwnTables);
    }

    public async Task DisposeAsync()
    {
        await DeleteAsync(ConnectionString, Tenants, OwnTables);
        Assert.All((await CountAsync(ConnectionString, Tenants, OwnTables)).Values, count => Assert.Equal(0, count));
        Assert.All(
            (await CountAsync(FleetConnectionString(), Tenants, OwnTables)).Values,
            count => Assert.Equal(0, count)
        );
    }

    [Fact]
    public async Task Files_summary_and_usage_breakdown_with_volume()
    {
        Assert.Equal(Files + 1, (await CountAsync(ConnectionString, [Tenants[0]], OwnTables[0]))[OwnTables[0]]);

        foreach (var (scope, canViewAll) in new[] { ("oficina", true), ("empleado 500/20 000", false) })
            await MeasureAsync(
                "CloudStorage",
                "GET /storage/files/summary?days=14",
                scope,
                ConnectionString,
                async capture =>
                {
                    await using var db = NewContext(capture);
                    var actor = new StorageActorScope(false, null) { TenantId = Tenants[0] };
                    if (!canViewAll)
                        actor = actor with
                        {
                            BypassAssignmentFilter = false,
                            AssignedCustomerIds = await new CustomerAssignmentProjectionStore(
                                db
                            ).GetAssignedCustomerIdsAsync(Tenants[0], Employee),
                            AssignedUserId = Employee,
                        };
                    var result = await GetFileSummaryHandler.Handle(
                        new GetFileSummaryQuery(Tenants[0], actor, FileSummaryLimits.DefaultDays),
                        new FileSummaryReader(db),
                        new FixedZone(),
                        new FixedClock(),
                        default
                    );
                    Assert.True(result.IsSuccess);
                    Assert.NotEmpty(result.Value.Latest);
                }
            );

        await MeasureAsync(
            "CloudStorage",
            "GET /storage/usage?breakdown=true",
            "oficina (sin recorte por diseño)",
            ConnectionString,
            async capture =>
            {
                await using var db = NewContext(capture);
                var breakdown = await new StorageBreakdownReader(db).ReadAsync(Tenants[0], default);
                Assert.True(breakdown.Trash.Count > 0);
            }
        );
    }

    // Un archivo real disponible: las copias heredan columnas válidas.
    private static async Task<Guid> TemplateAsync()
    {
        var id = Guid.NewGuid();
        var file = FileObject
            .Register(
                id,
                Tenants[0],
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                ObjectKey.Create($"tenants/{Tenants[0]:N}/Tenant/files/2025/{id:N}.bin").Value,
                "template.pdf",
                "application/pdf",
                1000,
                Employee,
                Now.AddDays(-400),
                Now.AddDays(-400).AddHours(1)
            )
            .Value;
        file.MarkPendingScan(Now.AddDays(-400));
        file.MarkScanning();
        file.MarkAvailable(Checksum, "application/pdf", Now.AddDays(-400));
        await using var db = NewContext(null, Tenants[0]);
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task SeedTenantAsync(int t, Guid template, int divisor)
    {
        var tenant = $"'{Tenants[t - 1]}'";
        var customer = SqlId("c057", t, $"n % {Customers}");
        // Por n % 20: 1 en revisión, 1 en la papelera, 1 sin subir y 17 disponibles. 1 de cada 4 es de la oficina y
        // el resto de un cliente; 2 de cada 5 fuera de las carpetas de documentos y 1 de cada 7 con origen declarado.
        // Un alta cada 2 minutos hacia atrás: unas 10 000 en la ventana de 14 días.
        await CloneAsync(
            ConnectionString,
            "[dbo].[Files]",
            $"[Id] = '{template}'",
            Files / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = SqlId("f11e", t, "n"),
                ["TenantId"] = tenant,
                ["ObjectKey"] = "CONCAT(N'perf/', n)",
                ["OriginalName"] =
                    "CONCAT(N'file', n, CASE n % 6 WHEN 0 THEN N'.pdf' WHEN 1 THEN N'.jpg' WHEN 2 THEN N'.mp4' WHEN 3 THEN N'.docx' WHEN 4 THEN N'.png' ELSE N'.zip' END)",
                ["Status"] =
                    "CASE n % 20 WHEN 0 THEN N'PendingReview' WHEN 1 THEN N'SoftDeleted' WHEN 2 THEN N'PendingUpload' ELSE N'Available' END",
                ["StatusBeforeSoftDelete"] = "CASE WHEN n % 20 = 1 THEN N'Available' END",
                ["OwnerType"] = "CASE WHEN n % 4 = 0 THEN N'Tenant' ELSE N'Customer' END",
                ["OwnerId"] = $"CASE WHEN n % 4 = 0 THEN NULL ELSE {customer} END",
                ["RelatedCustomerId"] = $"CASE WHEN n % 4 = 0 THEN NULL ELSE {customer} END",
                ["FolderType"] =
                    "CASE n % 5 WHEN 0 THEN N'Documents' WHEN 1 THEN N'Receipts' WHEN 2 THEN N'Invoices' WHEN 3 THEN N'Tasks' ELSE N'EmailIncoming' END",
                ["SourceResourceType"] = "CASE WHEN n % 7 = 0 THEN N'Note' END",
                ["SizeBytes"] = "1000 + n % 100000",
                ["CreatedAtUtc"] = "DATEADD(MINUTE, -2 * n, CAST('2026-11-21T14:00:00' AS datetime2))",
            }
        );
        await InsertAsync(
            ConnectionString,
            "[dbo].[CustomerAssignmentProjections]",
            Customers / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = "NEWID()",
                ["TenantId"] = tenant,
                ["CustomerId"] = SqlId("c057", t, "n"),
                ["UserId"] = SqlId("a5e5", t, $"(n / 50) % {Employees}"),
                ["Version"] = "SYSUTCDATETIME()",
            }
        );
    }

    private static string FleetConnectionString() =>
        Environment.GetEnvironmentVariable("CLOUDSTORAGE_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("taxvision-cloudstorage-development")
            .Build()
            .GetConnectionString("Default")!;

    private static CloudStorageDbContext NewContext(CommandCapture? capture, Guid tenant = default)
    {
        var options = new DbContextOptionsBuilder<CloudStorageDbContext>().UseSqlServer(ConnectionString);
        if (capture is not null)
            options.AddInterceptors(capture);
        return new CloudStorageDbContext(options.Options, new FixedTenant(tenant));
    }

    private sealed class FixedZone : IOfficeTimeZone
    {
        public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) => Task.FromResult(Zone);
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class FixedTenant(Guid tenant) : ITenantContext
    {
        public Guid TenantId => tenant;
        public bool HasTenant => tenant != Guid.Empty;

        public void SetTenant(Guid id) { }
    }
}
