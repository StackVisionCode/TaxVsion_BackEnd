using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Infrastructure.Persistence;
using TaxVision.Campaigns.Infrastructure.Persistence.Repositories;
using static TaxVision.Campaigns.Tests.Integration.Performance.DashboardPerformance;

namespace TaxVision.Campaigns.Tests.Integration.Performance;

/// <summary>
/// Volumen del dashboard en Campaigns: 5 000 runs de 50 campañas y 100 agendados en un tenant de prueba y los de
/// ruido, en la base temporal BB-10. Mide <c>GET /campaigns/runs/recent?take=5</c>, que es dato de oficina: el
/// endpoint no recorta por asignación.
/// </summary>
[Trait("Category", "Performance")]
public sealed class CampaignsDashboardPerformanceTests : IAsyncLifetime
{
    private const int Runs = 5_000;
    private const int CampaignCount = 50;
    private const int Schedules = 100;
    private static readonly DateTime Now = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid[] Tenants = [.. AllTenants];
    private static readonly Guid Actor = Id("a5e5", 1, 0);
    private static readonly string ConnectionString = CampaignsTestDatabase.ConnectionString;

    private static readonly string[] OwnTables =
    [
        "[dbo].[CampaignRecipients]",
        "[dbo].[CampaignRuns]",
        "[dbo].[CampaignSchedules]",
        "[dbo].[Campaigns]",
    ];

    public async Task InitializeAsync()
    {
        var (campaign, run, schedule) = await TemplatesAsync();
        for (var t = 1; t <= Tenants.Length; t++)
            await SeedTenantAsync(t, campaign, run, schedule, DivisorOf(t));
        await UpdateStatisticsAsync(ConnectionString, OwnTables);
    }

    public async Task DisposeAsync()
    {
        await DeleteAsync(ConnectionString, Tenants, OwnTables);
        Assert.All((await CountAsync(ConnectionString, Tenants, OwnTables)).Values, count => Assert.Equal(0, count));
        Assert.All(
            (await CountAsync(CampaignsTestDatabase.ServerConnectionString, Tenants, OwnTables)).Values,
            count => Assert.Equal(0, count)
        );
    }

    [Fact]
    public async Task Recent_runs_with_volume()
    {
        Assert.Equal(Runs + 1, (await CountAsync(ConnectionString, [Tenants[0]], OwnTables[1]))[OwnTables[1]]);

        await MeasureAsync(
            "Campaigns",
            "GET /campaigns/runs/recent?take=5",
            "oficina (sin recorte por diseño)",
            ConnectionString,
            async capture =>
            {
                await using var db = CampaignsTestDatabase.NewContext(Guid.Empty, capture);
                var recent = await new CampaignRunRepository(db).ListRecentAsync(Tenants[0], 5, Now);
                Assert.Equal(5, recent.Items.Count);
            }
        );
    }

    // Campaña, run y agendado reales: las copias heredan columnas válidas.
    private static async Task<(Guid Campaign, Guid Run, Guid Schedule)> TemplatesAsync()
    {
        var campaign = Campaign.Create(Tenants[0], Actor, "Template", CampaignChannel.Email, "body", "subj").Value;
        var run = CampaignRun
            .Start(
                Tenants[0],
                campaign.Id,
                Actor,
                "Manual",
                [new RunRecipientDraft("c", CampaignChannel.Email, "a@example.test", null)]
            )
            .Value;
        var schedule = CampaignSchedule.CreateOneTime(Tenants[0], campaign.Id, Now.AddDays(3), []).Value;
        await using var db = CampaignsTestDatabase.NewContext(Tenants[0]);
        db.Campaigns.Add(campaign);
        db.CampaignRuns.Add(run);
        db.CampaignSchedules.Add(schedule);
        await db.SaveChangesAsync();
        db.Entry(run).Property(r => r.CreatedAtUtc).CurrentValue = Now.AddYears(-1);
        await db.SaveChangesAsync();
        return (campaign.Id, run.Id, schedule.Id);
    }

    private static async Task SeedTenantAsync(int t, Guid campaign, Guid run, Guid schedule, int divisor)
    {
        var tenant = $"'{Tenants[t - 1]}'";
        await CloneAsync(
            ConnectionString,
            "[dbo].[Campaigns]",
            $"[Id] = '{campaign}'",
            CampaignCount,
            new Dictionary<string, string>
            {
                ["Id"] = SqlId("ca00", t, "n"),
                ["TenantId"] = tenant,
                ["Name"] = "CONCAT(N'Campaign ', n)",
            }
        );
        // Un run por hora hacia atrás, repartidos entre las campañas.
        await CloneAsync(
            ConnectionString,
            "[dbo].[CampaignRuns]",
            $"[Id] = '{run}'",
            Runs / divisor,
            new Dictionary<string, string>
            {
                ["Id"] = SqlId("ca01", t, "n"),
                ["TenantId"] = tenant,
                ["CampaignId"] = SqlId("ca00", t, $"n % {CampaignCount}"),
                ["CreatedAtUtc"] = "DATEADD(HOUR, -n, CAST('2026-11-21T14:00:00' AS datetime2))",
            }
        );
        // La mitad de los agendados ya disparó; la otra mitad dispara en las próximas horas.
        await CloneAsync(
            ConnectionString,
            "[dbo].[CampaignSchedules]",
            $"[Id] = '{schedule}'",
            Schedules,
            new Dictionary<string, string>
            {
                ["Id"] = SqlId("ca02", t, "n"),
                ["TenantId"] = tenant,
                ["CampaignId"] = SqlId("ca00", t, $"n % {CampaignCount}"),
                ["NextFireAtUtc"] = "DATEADD(HOUR, n - 50, CAST('2026-11-21T15:00:00' AS datetime2))",
            }
        );
    }
}
