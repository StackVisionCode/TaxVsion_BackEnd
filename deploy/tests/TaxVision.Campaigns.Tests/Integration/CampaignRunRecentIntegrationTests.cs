using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaxVision.Campaigns.Domain.Campaigns;
using TaxVision.Campaigns.Domain.Runs;
using TaxVision.Campaigns.Domain.Scheduling;
using TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

namespace TaxVision.Campaigns.Tests.Integration;

/// <summary>
/// Runs recientes del dashboard en SQL Server: los últimos de la oficina con el nombre de su campaña, sin leer
/// destinatarios, y el próximo disparo Active (general y por campaña).
/// </summary>
public sealed class CampaignRunRecentIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private Campaign _spring = default!;
    private Campaign _fall = default!;
    private readonly List<CampaignRun> _runs = [];

    public async Task InitializeAsync()
    {
        _spring = Campaign.Create(_tenantId, _actor, "Spring promo", CampaignChannel.Email, "body", "subj").Value;
        _fall = Campaign.Create(_tenantId, _actor, "Fall promo", CampaignChannel.Email, "body", "subj").Value;

        await using (var db = CampaignsTestDatabase.NewContext(_tenantId))
        {
            db.Campaigns.AddRange(_spring, _fall);
            // 7 runs alternando campaña, uno por hora; cada uno con un destinatario.
            for (var i = 0; i < 7; i++)
            {
                var run = Run(_tenantId, i % 2 == 0 ? _spring.Id : _fall.Id);
                db.CampaignRuns.Add(run);
                db.Entry(run).Property(r => r.CreatedAtUtc).CurrentValue = Now.AddHours(-7 + i);
                _runs.Add(run);
            }
            db.CampaignSchedules.Add(OneTime(_tenantId, _spring.Id, Now.AddDays(2)));
            var paused = OneTime(_tenantId, _fall.Id, Now.AddDays(1));
            Assert.True(paused.Pause().IsSuccess);
            db.CampaignSchedules.Add(paused);
            db.CampaignSchedules.Add(OneTime(_tenantId, _fall.Id, Now.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        // Otra oficina con un disparo más cercano: no debe colarse.
        await using var other = CampaignsTestDatabase.NewContext(_otherTenant);
        other.CampaignSchedules.Add(OneTime(_otherTenant, Guid.NewGuid(), Now.AddHours(1)));
        other.CampaignRuns.Add(Run(_otherTenant, Guid.NewGuid()));
        await other.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await CampaignsTestDatabase.DeleteTenantAsync(_tenantId);
        await CampaignsTestDatabase.DeleteTenantAsync(_otherTenant);
    }

    [Fact]
    public async Task ListRecent_ReturnsLatestRunsWithoutRecipients_AndNextActiveFire()
    {
        var commands = new CommandCapture();
        await using var db = CampaignsTestDatabase.NewContext(Guid.Empty, commands);

        var recent = await new CampaignRunRepository(db).ListRecentAsync(_tenantId, 5, Now);

        Assert.Equal(_runs.AsEnumerable().Reverse().Take(5).Select(r => r.Id), recent.Items.Select(i => i.RunId));
        var latest = recent.Items[0];
        Assert.Equal("Spring promo", latest.CampaignName);
        Assert.Equal(nameof(CampaignStatus.Draft), latest.CampaignStatus);
        Assert.Equal(1, latest.RecipientCount);
        Assert.Equal(Now.AddHours(-1), latest.CreatedAtUtc);
        Assert.Equal(Now.AddDays(2), latest.CampaignNextFireAtUtc);
        Assert.Null(recent.Items[1].CampaignNextFireAtUtc); // Fall: solo pausado y vencido
        Assert.Equal(Now.AddDays(2), recent.NextFireAtUtc);
        Assert.DoesNotContain(commands.Sql, s => s.Contains("CampaignRecipients"));
    }

    private CampaignRun Run(Guid tenantId, Guid campaignId) =>
        CampaignRun
            .Start(
                tenantId,
                campaignId,
                _actor,
                "Manual",
                [new RunRecipientDraft("c", CampaignChannel.Email, "a@example.test", null)]
            )
            .Value;

    private static CampaignSchedule OneTime(Guid tenantId, Guid campaignId, DateTime runAtUtc) =>
        CampaignSchedule.CreateOneTime(tenantId, campaignId, runAtUtc, []).Value;

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
