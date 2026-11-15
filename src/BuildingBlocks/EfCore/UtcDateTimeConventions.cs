using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BuildingBlocks.EfCore;

/// <summary>
/// Convención opcional: SQL Server devuelve los <c>datetime2</c> con <c>Kind=Unspecified</c> y la respuesta JSON sale
/// sin "Z". Con <see cref="UseUtcDateTimes"/> cada <c>DateTime</c> se lee como UTC; una fecha civil guardada como
/// <c>DateTime</c> se marca con <see cref="IsCivilDateTime{TProperty}"/> y queda como está.
/// </summary>
public static class UtcDateTimeConventions
{
    public const string CivilAnnotation = "TaxVision:CivilDateTime";

    private static readonly ValueConverter<DateTime, DateTime> Utc = new(
        v => v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)
    );

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtc = new(
        v => v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v
    );

    public static PropertyBuilder<TProperty> IsCivilDateTime<TProperty>(this PropertyBuilder<TProperty> property) =>
        property.HasAnnotation(CivilAnnotation, true);

    /// <summary>Se llama al final de <c>OnModelCreating</c>; no toca las propiedades que ya tienen su conversor.</summary>
    public static ModelBuilder UseUtcDateTimes(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
        {
            if (property.GetValueConverter() is not null || property.FindAnnotation(CivilAnnotation) is not null)
                continue;
            if (property.ClrType == typeof(DateTime))
                property.SetValueConverter(Utc);
            else if (property.ClrType == typeof(DateTime?))
                property.SetValueConverter(NullableUtc);
        }
        return modelBuilder;
    }

    /// <summary>Para pruebas de arquitectura: <c>Entidad.Propiedad</c> de cada <c>DateTime</c> que no se lee como UTC.</summary>
    public static IReadOnlyList<string> DateTimesNotReadAsUtc(IModel model) =>
        model
            .GetEntityTypes()
            .SelectMany(entity => entity.GetProperties().Select(property => (entity, property)))
            .Where(x => x.property.ClrType == typeof(DateTime) || x.property.ClrType == typeof(DateTime?))
            .Where(x => x.property.FindAnnotation(CivilAnnotation) is null && !ReadsAsUtc(x.property))
            .Select(x => $"{x.entity.ClrType.Name}.{x.property.Name}")
            .Order(StringComparer.Ordinal)
            .ToList();

    private static bool ReadsAsUtc(IReadOnlyProperty property)
    {
        var converter = property.GetValueConverter();
        if (converter is null)
            return false;
        // Un conversor a otro tipo (ticks, texto) es una decisión propia de esa columna.
        if (converter.ProviderClrType != typeof(DateTime) && converter.ProviderClrType != typeof(DateTime?))
            return true;
        var read = converter.ConvertFromProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
        return read is DateTime { Kind: DateTimeKind.Utc };
    }
}
