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

    [Fact]
    public void MuchasIpsAnonimas_NoBajanLaMediaDeLosTenants()
    {
        // Si las IPs anónimas (muchas y pequeñas) entraran en la media, dos tenants parejos quedarían
        // muy por encima de ella y ambos se sheddearían.
        var tracker = new TenantConsumptionTracker(60);
        for (var i = 0; i < 50; i++)
        {
            tracker.RecordRequest("tenant-a");
            tracker.RecordRequest("tenant-b");
        }
        for (var ip = 0; ip < 40; ip++)
            tracker.RecordRequest(
                TenantConsumptionTracker.AnonymousKeyFor(System.Net.IPAddress.Parse($"198.51.100.{ip}"))
            );

        Assert.Equal(1.0, tracker.GetSnapshot("tenant-a").ExcessOverFairShare, 3);
        Assert.Equal(2, tracker.GetSnapshot("tenant-a").ActiveTenantCount);
    }

    [Fact]
    public void UnaIpAnonimaAbusiva_SeComparaSoloContraOtrosAnonimos()
    {
        var tracker = new TenantConsumptionTracker(60);
        var abusiva = TenantConsumptionTracker.AnonymousKeyFor(System.Net.IPAddress.Parse("203.0.113.7"));
        for (var i = 0; i < 30; i++)
            tracker.RecordRequest(abusiva);
        foreach (var ip in new[] { "203.0.113.1", "203.0.113.2" })
            tracker.RecordRequest(TenantConsumptionTracker.AnonymousKeyFor(System.Net.IPAddress.Parse(ip)));
        for (var i = 0; i < 1000; i++)
            tracker.RecordRequest("tenant-grande");

        // Media anónima = 32/3 ≈ 10,67 → la abusiva va a ~2,8x, sin importar el volumen de los tenants.
        Assert.Equal(30 / (32.0 / 3), tracker.GetSnapshot(abusiva).ExcessOverFairShare, 3);
    }

    [Theory]
    [InlineData("203.0.113.9", "anon:203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "anon:203.0.113.9")] // IPv4 mapeada a IPv6 → misma clave
    [InlineData(null, "anon:unknown")]
    public void AnonymousKeyFor_NormalizaLaIp(string? ip, string expected)
    {
        var address = ip is null ? null : System.Net.IPAddress.Parse(ip);

        Assert.Equal(expected, TenantConsumptionTracker.AnonymousKeyFor(address));
    }
}
