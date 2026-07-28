using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TaxVision.Billing.Infrastructure.Persistence.Configurations;

/// <summary>Persiste un value object (record) como JSON en una columna nvarchar(max). Se usa para
/// los snapshots (CustomerSnapshot/IssuerSnapshot): records posicionales que STJ (de)serializa por
/// su ctor público, evitando el binding de owned-types anidados que EF no soporta para records.</summary>
public sealed class JsonValueConverter<T> : ValueConverter<T, string>
    where T : class
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public JsonValueConverter()
        : base(
            value => JsonSerializer.Serialize(value, Options),
            json => JsonSerializer.Deserialize<T>(json, Options)!
        ) { }
}
