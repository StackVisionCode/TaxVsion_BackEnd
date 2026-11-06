using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.RegisterTenantPaymentWebhook;
using TaxVision.PaymentClient.Domain.TenantPaymentConfigs;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Persistence;
using TaxVision.PaymentClient.Infrastructure.Persistence.Repositories;
using TaxVision.PaymentClient.Infrastructure.Scheduling;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>La revisión de pendientes rescata solo los registros perdidos (más viejos que todo el ciclo de
/// reintentos), los re-encola una sola vez y con la función apagada los da por fallidos.</summary>
public sealed class PendingWebhookSetupSweepJobTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    // 3 × 60 s + 15 min = 18 min.
    private static readonly PaymentWebhookOptions Enabled = new()
    {
        PublicBaseUrl = "https://api.example.test",
        MaxAttempts = 3,
        RetryDelaySeconds = 60,
        PendingMarginMinutes = 15,
    };

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly PaymentClientDbContext _db;
    private readonly OutboxRecording _outbox = new();
    private List<object> _published => _outbox.Flushed;

    public PendingWebhookSetupSweepJobTests() => _db = RecordingPaymentClientOutbox.CreateDb(_databaseName, _root);

    [Fact]
    public void The_threshold_is_the_whole_retry_cycle_plus_the_margin()
    {
        Assert.Equal(TimeSpan.FromMinutes(18), Enabled.StalePendingAfter);
    }

    [Fact]
    public async Task A_lost_pending_setup_is_re_queued_once_and_its_mark_renewed()
    {
        var lost = await SeedPendingAsync(requestedAtUtc: NowUtc.AddMinutes(-19));

        var summary = await SweepAsync(Enabled, NowUtc);

        Assert.Equal(new PendingWebhookSetupSweepSummary(1, 0), summary);
        var command = Assert.IsType<RegisterTenantPaymentWebhookCommand>(Assert.Single(_published));
        Assert.Equal(new RegisterTenantPaymentWebhookCommand(lost.TenantId, PaymentProviderCode.Stripe), command);
        var saved = await ReloadAsync(lost.Id);
        Assert.Equal(WebhookSetupStatus.Pending, saved.WebhookSetup);
        Assert.Equal(NowUtc, saved.WebhookSetupRequestedAtUtc);

        // La marca renovada evita un segundo disparo en la vuelta siguiente.
        var again = await SweepAsync(Enabled, NowUtc.AddMinutes(10));
        Assert.Equal(new PendingWebhookSetupSweepSummary(0, 0), again);
        Assert.Single(_published);
    }

    [Fact]
    public async Task A_pending_setup_still_inside_the_retry_cycle_is_left_alone()
    {
        var inFlight = await SeedPendingAsync(requestedAtUtc: NowUtc.AddMinutes(-17));

        var summary = await SweepAsync(Enabled, NowUtc);

        Assert.Equal(new PendingWebhookSetupSweepSummary(0, 0), summary);
        Assert.Empty(_published);
        Assert.Equal(NowUtc.AddMinutes(-17), (await ReloadAsync(inFlight.Id)).WebhookSetupRequestedAtUtc);
    }

    [Fact]
    public async Task With_the_feature_off_a_lost_pending_setup_becomes_Failed()
    {
        var lost = await SeedPendingAsync(requestedAtUtc: NowUtc.AddHours(-2));
        var off = new PaymentWebhookOptions
        {
            MaxAttempts = 3,
            RetryDelaySeconds = 60,
            PendingMarginMinutes = 15,
        };

        var summary = await SweepAsync(off, NowUtc);

        Assert.Equal(new PendingWebhookSetupSweepSummary(0, 1), summary);
        Assert.Empty(_published);
        Assert.Equal(WebhookSetupStatus.Failed, (await ReloadAsync(lost.Id)).WebhookSetup);
    }

    [Fact]
    public async Task Only_pending_setups_are_swept()
    {
        var done = await SeedPendingAsync(requestedAtUtc: NowUtc.AddHours(-2));
        done.CompleteAutomaticWebhookSetup(EncryptedSecret.Create("enc:whsec").Value, "we_1", NowUtc.AddHours(-2));
        await _db.SaveChangesAsync();

        var summary = await SweepAsync(Enabled, NowUtc);

        Assert.Equal(new PendingWebhookSetupSweepSummary(0, 0), summary);
        Assert.Equal(WebhookSetupStatus.Automatic, (await ReloadAsync(done.Id)).WebhookSetup);
    }

    // Cada pendiente se procesa en su unidad (BB-3): la marca y el comando salen juntos por el outbox.
    private Task<PendingWebhookSetupSweepSummary> SweepAsync(PaymentWebhookOptions settings, DateTime nowUtc) =>
        PendingWebhookSetupSweepJob.SweepAsync(
            new TenantPaymentConfigRepository(_db),
            RecordingPaymentClientOutbox.InMemoryUnits(_databaseName, _root, _outbox),
            settings,
            NullLogger.Instance,
            nowUtc,
            CancellationToken.None
        );

    private async Task<TenantPaymentConfig> SeedPendingAsync(DateTime requestedAtUtc)
    {
        var config = TenantPaymentConfig
            .Create(
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                TenantPaymentMode.DirectApiKeys,
                "pk_live_1",
                StatementDescriptor.Create("ACME TAX SVC").Value,
                requestedAtUtc
            )
            .Value;
        config.UpdateSecrets(EncryptedSecret.Create("enc:sk").Value, null, Guid.Empty, requestedAtUtc);
        config.BeginAutomaticWebhookSetup(requestedAtUtc);
        _db.TenantPaymentConfigs.Add(config);
        await _db.SaveChangesAsync();
        return config;
    }

    private async Task<TenantPaymentConfig> ReloadAsync(Guid configId)
    {
        await using var fresh = RecordingPaymentClientOutbox.CreateDb(_databaseName, _root);
        return await fresh.TenantPaymentConfigs.IgnoreQueryFilters().SingleAsync(c => c.Id == configId);
    }
}
