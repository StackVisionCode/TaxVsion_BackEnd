using BuildingBlocks.Common;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Common;

/// <summary>
/// 2026-11-15, producción: al desplegar, varios servicios corrieron su reconciliación antes de que Auth y Tenant
/// respondieran y no volvieron a intentar en 12 horas.
/// </summary>
public sealed class ReconciliationRetryTests
{
    [Fact]
    public void A_pass_that_could_not_finish_retries_in_minutes_not_hours()
    {
        var interval = TimeSpan.FromHours(12);

        Assert.Equal(TimeSpan.FromMinutes(2), ReconciliationRetry.NextDelay(completed: false, interval));
        Assert.Equal(interval, ReconciliationRetry.NextDelay(completed: true, interval));
    }

    [Fact]
    public void A_short_interval_is_never_stretched_by_the_retry()
    {
        var interval = TimeSpan.FromMinutes(1);

        Assert.Equal(interval, ReconciliationRetry.NextDelay(completed: false, interval));
    }

    // Los jobs de respaldo que en producción no llegaron a Auth, Tenant o Customer al arrancar.
    [Theory]
    [InlineData("src/BuildingBlocks/TenantTimeZones/TenantTimeZoneReconciliationJob.cs")]
    [InlineData("src/BuildingBlocks/CustomerVisibility/CustomerAssignmentReconciliationJob.cs")]
    [InlineData("src/Services/Calendar/TaxVision.Calendar.Infrastructure/Jobs/TenantCustomerFullReconciliationJob.cs")]
    [InlineData("src/Services/Tasks/TaxVision.Tasks.Infrastructure/Jobs/TenantCustomerFullReconciliationJob.cs")]
    [InlineData("src/Services/Notes/TaxVision.Notes.Infrastructure/Jobs/TenantCustomerFullReconciliationJob.cs")]
    [InlineData(
        "src/Services/Mobile/TaxVision.Mobile.Infrastructure/RateLimiting/TenantEntitlementsReconciliationJob.cs"
    )]
    public void Startup_reconciliation_jobs_retry_soon_after_a_failed_pass(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath));

        Assert.Contains("ReconciliationRetry.NextDelay(", source);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TaxVision.slnx")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("TaxVision.slnx");
    }
}
