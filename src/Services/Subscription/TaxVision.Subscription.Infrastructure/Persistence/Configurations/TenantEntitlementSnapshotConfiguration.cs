using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Infrastructure.Persistence.Configurations;

public sealed class TenantEntitlementSnapshotConfiguration : IEntityTypeConfiguration<TenantEntitlementSnapshot>
{
    public void Configure(EntityTypeBuilder<TenantEntitlementSnapshot> builder)
    {
        builder.ToTable("TenantEntitlementSnapshots");
        builder.HasKey(snapshot => snapshot.TenantId);

        builder.Property(snapshot => snapshot.RevisionNumber).IsRequired();
        builder.Property(snapshot => snapshot.ComputedAtUtc).IsRequired();
        builder.Property(snapshot => snapshot.PlanCode).HasMaxLength(50).IsRequired();
        builder.Property(snapshot => snapshot.PlanVersionId).IsRequired();
        builder.Property(snapshot => snapshot.SubscriptionStatus).HasMaxLength(30).IsRequired();
        builder.Property(snapshot => snapshot.SeatCount).IsRequired();
        builder.Property(snapshot => snapshot.AvailableSeatCount).IsRequired();

        // La propiedad pública Entries es de solo lectura (expone el backing field); se
        // ignora aquí para que EF no intente descubrirla como navegación por convención y
        // se mapea explícitamente el campo _entries más abajo.
        builder.Ignore(snapshot => snapshot.Entries);

        // Las entries se serializan como JSON (ver diseño §35.2 "SnapshotJson") en lugar de
        // modelarse como colección de entidades EF: no tienen identidad propia -- el
        // snapshot entero se reemplaza en cada recálculo, nunca se editan entries sueltas.
        var entriesConverter = new ValueConverter<List<EntitlementEntry>, string>(
            entries => Serialize(entries),
            json => Deserialize(json));
        var entriesComparer = new ValueComparer<List<EntitlementEntry>>(
            (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
            list => list.Aggregate(0, (hash, entry) => HashCode.Combine(hash, entry.Key.Value, entry.Value)),
            list => list.ToList());

        builder.Property<List<EntitlementEntry>>("_entries")
            .HasColumnName("EntriesJson")
            .HasColumnType("nvarchar(max)")
            .HasConversion(entriesConverter)
            .Metadata.SetValueComparer(entriesComparer);
    }

    private sealed record EntryDto(string Key, string ValueType, string Value, string Status, string Source, DateTime? ExpiresAtUtc);

    private static string Serialize(List<EntitlementEntry> entries) =>
        JsonSerializer.Serialize(entries.Select(ToDto).ToList());

    private static List<EntitlementEntry> Deserialize(string json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<EntryDto>>(json)!.Select(FromDto).ToList();

    private static EntryDto ToDto(EntitlementEntry entry) =>
        new(entry.Key.Value, entry.ValueType.ToString(), entry.Value, entry.Status.ToString(), entry.Source.ToString(), entry.ExpiresAtUtc);

    private static EntitlementEntry FromDto(EntryDto dto) =>
        new(
            EntitlementKey.Create(dto.Key).Value,
            Enum.Parse<EntitlementValueType>(dto.ValueType),
            dto.Value,
            Enum.Parse<EntitlementStatus>(dto.Status),
            Enum.Parse<EntitlementSource>(dto.Source),
            dto.ExpiresAtUtc);
}
