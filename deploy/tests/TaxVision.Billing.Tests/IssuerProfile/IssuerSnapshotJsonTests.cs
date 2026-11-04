using TaxVision.Billing.Domain.ValueObjects;
using TaxVision.Billing.Infrastructure.Persistence.Configurations;
using Xunit;

namespace TaxVision.Billing.Tests.IssuerProfile;

/// <summary>El snapshot del emisor vive como JSON en cada factura: el formato de F0 no puede romper la
/// lectura de las facturas ya emitidas ni ensuciar el JSON con propiedades calculadas.</summary>
public sealed class IssuerSnapshotJsonTests
{
    private static readonly JsonValueConverter<IssuerSnapshot> Converter = new();

    [Fact]
    public void An_old_snapshot_without_contact_keys_still_loads()
    {
        const string legacy =
            """{"name":"Old office","address":{"line1":"—","line2":null,"city":"","state":"","zip":"","country":"US"},"logoFileId":null}""";

        var snapshot = (IssuerSnapshot)Converter.ConvertFromProvider(legacy)!;

        Assert.Equal("Old office", snapshot.Name);
        Assert.Null(snapshot.Phone);
        Assert.Null(snapshot.TaxId);
        Assert.Null(snapshot.Address.ToSingleLine());
    }

    [Fact]
    public void A_new_snapshot_round_trips_without_computed_members()
    {
        var snapshot = new IssuerSnapshot(
            "Office",
            new Address("1 Main", "Suite 2", "Austin", "TX", "78701", "US"),
            "(512) 555-0112",
            "a@b.com",
            "https://b.com",
            null,
            "12-3456789"
        );

        var json = (string)Converter.ConvertToProvider(snapshot)!;
        var back = (IssuerSnapshot)Converter.ConvertFromProvider(json)!;

        Assert.Equal(snapshot, back);
        Assert.DoesNotContain("isBlank", json, StringComparison.OrdinalIgnoreCase);
    }
}
