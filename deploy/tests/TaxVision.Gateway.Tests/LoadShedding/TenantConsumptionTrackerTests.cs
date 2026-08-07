using TaxVision.Gateway.LoadShedding;
using Xunit;

namespace TaxVision.Gateway.Tests.LoadShedding;

/// <summary>
/// El tracker dejó de rankear (GW-14: el top-N era el bug) y ahora solo aporta los tres números con
/// los que <see cref="LoadShedder"/> calcula el exceso sobre la parte justa.
/// </summary>
public sealed class TenantConsumptionTrackerTests
{
    [Fact]
    public void GetSnapshot_DevuelveTotalTenantsActivosYConsumoDelTenant()
    {
        var tracker = new TenantConsumptionTracker(60);

        for (var i = 0; i < 5; i++)
            tracker.RecordRequest("tenant-a");
        for (var i = 0; i < 2; i++)
            tracker.RecordRequest("tenant-b");
        tracker.RecordRequest("tenant-c");

        var snapshot = tracker.GetSnapshot("tenant-a");

        Assert.Equal(8, snapshot.TotalRequests);
        Assert.Equal(3, snapshot.ActiveTenantCount);
        Assert.Equal(5, snapshot.TenantRequests);
    }

    [Fact]
    public void ExcessOverFairShare_EsLaRazonContraLaMedia()
    {
        var tracker = new TenantConsumptionTracker(60);

        for (var i = 0; i < 30; i++)
            tracker.RecordRequest("abusivo");
        for (var i = 0; i < 10; i++)
            tracker.RecordRequest("modesto");

        // Media = 40/2 = 20 → el abusivo va a 1.5x y el modesto a 0.5x.
        Assert.Equal(1.5, tracker.GetSnapshot("abusivo").ExcessOverFairShare, 3);
        Assert.Equal(0.5, tracker.GetSnapshot("modesto").ExcessOverFairShare, 3);
    }

    [Fact]
    public void ConConsumoIdentico_NadieSuperaLaMedia()
    {
        var tracker = new TenantConsumptionTracker(60);
        foreach (var tenant in new[] { "a", "b", "c", "d" })
        {
            for (var i = 0; i < 25; i++)
                tracker.RecordRequest(tenant);
        }

        // La propiedad que elimina GW-14 por construcción: da 1.0 con 4 tenants o con 400.
        Assert.Equal(1.0, tracker.GetSnapshot("a").ExcessOverFairShare, 3);
    }

    [Fact]
    public void SinTrafico_NoHayExcesoQueMedir()
    {
        var snapshot = new TenantConsumptionTracker(60).GetSnapshot("tenant-a");

        Assert.Equal(0, snapshot.TotalRequests);
        Assert.Equal(0, snapshot.ActiveTenantCount);
        Assert.Equal(0, snapshot.ExcessOverFairShare);
    }

    [Fact]
    public void UnTenantDesconocido_NoInventaConsumo()
    {
        var tracker = new TenantConsumptionTracker(60);
        tracker.RecordRequest("tenant-a");

        var snapshot = tracker.GetSnapshot("tenant-inexistente");

        Assert.Equal(1, snapshot.TotalRequests);
        Assert.Equal(0, snapshot.TenantRequests);
    }
}
