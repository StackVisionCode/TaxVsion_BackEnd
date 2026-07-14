using TaxVision.Signature.Domain.Analytics;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Tests.Domain;

public sealed class SignatureAnalyticsSnapshotTests
{
    [Fact]
    public void CreateEmpty_initializes_all_counters_to_zero()
    {
        var s = SignatureAnalyticsSnapshot.CreateEmpty(
            Guid.NewGuid(),
            new DateOnly(2026, 4, 15),
            SignatureCategory.Fiscal
        );

        Assert.Equal(0, s.RequestsCreated);
        Assert.Equal(0, s.RequestsSent);
        Assert.Equal(0, s.RequestsCompleted);
        Assert.Equal(0, s.RequestsSealed);
        Assert.Equal(0, s.SignersSigned);
        Assert.Equal(0, s.SignersRejected);
    }

    [Fact]
    public void Increment_updates_the_matching_counter_and_updated_timestamp()
    {
        var s = SignatureAnalyticsSnapshot.CreateEmpty(
            Guid.NewGuid(),
            new DateOnly(2026, 4, 15),
            SignatureCategory.Fiscal
        );
        var before = s.UpdatedAtUtc;

        Thread.Sleep(5);
        s.IncrementCreated();
        s.IncrementCreated();
        s.IncrementSent();
        s.IncrementSignersSigned();
        s.IncrementCompleted();
        s.IncrementSealed();
        s.IncrementSignersRejected();
        s.IncrementCanceled();
        s.IncrementExpired();

        Assert.Equal(2, s.RequestsCreated);
        Assert.Equal(1, s.RequestsSent);
        Assert.Equal(1, s.RequestsCompleted);
        Assert.Equal(1, s.RequestsSealed);
        Assert.Equal(1, s.SignersSigned);
        Assert.Equal(1, s.SignersRejected);
        Assert.Equal(1, s.RequestsCanceled);
        Assert.Equal(1, s.RequestsExpired);
        Assert.True(s.UpdatedAtUtc > before);
    }

    [Fact]
    public void CreateEmpty_binds_tenant_day_and_category()
    {
        var tenant = Guid.NewGuid();
        var day = new DateOnly(2026, 7, 9);

        var s = SignatureAnalyticsSnapshot.CreateEmpty(tenant, day, SignatureCategory.ConsentToDisclose);

        Assert.Equal(tenant, s.TenantId);
        Assert.Equal(day, s.Day);
        Assert.Equal(SignatureCategory.ConsentToDisclose, s.Category);
    }
}
