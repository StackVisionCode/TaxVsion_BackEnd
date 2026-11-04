using BuildingBlocks.Persistence;
using TaxVision.Signature.Application.Settings.Commands.UpdateSettings;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Application;

/// <summary>
/// El PUT de ajustes ya no cambia los años de retención (los fija Cumplimiento); sigue cambiando la purga,
/// que es opt-in y solo si el plan la permite.
/// </summary>
public sealed class UpdateSignatureSettingsHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static SignatureLimits Plan(bool purgeAllowed) =>
        SignatureLimits.Create(
            TenantSignatureLimits.FallbackMaxDocumentsPerRequest,
            TenantSignatureLimits.FallbackMaxSignersPerRequest,
            TenantSignatureLimits.FallbackMaxPdfBytes,
            TenantSignatureLimits.FallbackMaxPagesPerDocument,
            TenantSignatureLimits.FallbackMaxTokenHours,
            3,
            purgeAllowed,
            VerificationChannel.Email | VerificationChannel.Sms
        );

    private static UpdateSignatureSettingsCommand Command(TenantSignatureSettings current, bool allowPurge) =>
        new(
            Tenant,
            Guid.NewGuid(),
            current.AllowedVerificationChannels,
            current.DefaultVerificationChannel,
            current.DefaultTokenExpirationHoursValue,
            current.RemindersEnabledByDefault,
            current.DefaultReminderIntervalHoursValue,
            current.GenerateCertificateByDefault,
            current.AllowEmployeeOwnSignature,
            current.DocumentLimits.MaxPdfBytes,
            current.DocumentLimits.MaxPagesPerDocument,
            allowPurge
        );

    private static async Task<BuildingBlocks.Results.Result> HandleAsync(
        TenantSignatureSettings settings,
        bool allowPurge,
        bool planAllowsPurge = true
    ) =>
        await UpdateSignatureSettingsHandler.Handle(
            Command(settings, allowPurge),
            new FixedSettings(settings),
            new FixedSignatureLimitsResolver(settings.EffectiveWithin(Plan(planAllowsPurge))),
            new NoOpUnitOfWork(),
            new CapturingBus(),
            new FixedCorrelation(),
            CancellationToken.None
        );

    [Fact]
    public async Task Saving_the_settings_does_not_change_the_retention_years()
    {
        var settings = TenantSignatureSettings.CreateForNewTenant(Tenant, "audit-secret").Value;
        settings.ReplaceRetentionPolicy(RetentionPolicy.Default().WithYears(9).Value);
        settings.ApplyOfficeRetention(12, DateTime.UtcNow);

        var result = await HandleAsync(settings, allowPurge: false);

        Assert.True(result.IsSuccess);
        Assert.Equal(9, settings.Retention.RetentionYears);
        Assert.Equal(12, settings.OfficeRetentionYears);
    }

    [Fact]
    public async Task Purge_can_be_turned_on_when_the_plan_allows_it_and_off_again()
    {
        var settings = TenantSignatureSettings.CreateForNewTenant(Tenant, "audit-secret").Value;
        Assert.False(settings.Retention.AllowPurge);

        await HandleAsync(settings, allowPurge: true);
        Assert.True(settings.Retention.AllowPurge);

        await HandleAsync(settings, allowPurge: false);
        Assert.False(settings.Retention.AllowPurge);
    }

    [Fact]
    public async Task Purge_cannot_be_turned_on_when_the_plan_does_not_allow_it()
    {
        var settings = TenantSignatureSettings.CreateForNewTenant(Tenant, "audit-secret").Value;

        var result = await HandleAsync(settings, allowPurge: true, planAllowsPurge: false);

        Assert.Equal("Signature.Settings.PurgeNotInPlan", result.Error.Code);
        Assert.False(settings.Retention.AllowPurge);
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
