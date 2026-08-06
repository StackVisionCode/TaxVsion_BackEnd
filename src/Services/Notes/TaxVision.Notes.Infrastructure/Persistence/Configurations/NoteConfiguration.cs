using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.ValueObjects;

namespace TaxVision.Notes.Infrastructure.Persistence.Configurations;

public sealed class NoteConfiguration : IEntityTypeConfiguration<Note>
{
    public void Configure(EntityTypeBuilder<Note> builder)
    {
        builder.ToTable("Notes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.CreatedByUserId).IsRequired();

        builder.Property(n => n.Visibility).HasConversion<int>().IsRequired();
        builder.Property(n => n.IsPinned).IsRequired();
        builder.Property(n => n.Status).HasConversion<int>().IsRequired();
        builder.Property(n => n.CreatedAtUtc).IsRequired();
        builder.Property(n => n.UpdatedAtUtc).IsRequired();

        // NoteContent — owned VO requerido, columnas propias (01_Modelo_De_Dominio.md §6).
        builder.OwnsOne(
            n => n.Content,
            content =>
            {
                content.Property(c => c.Html).HasColumnName("ContentHtml").HasColumnType("nvarchar(max)").IsRequired();
                content
                    .Property(c => c.PlainTextPreview)
                    .HasColumnName("ContentPreview")
                    .HasMaxLength(NoteContent.PreviewLength)
                    .IsRequired();
            }
        );
        builder.Navigation(n => n.Content).IsRequired();

        // NoteReference — owned VO requerido (siempre existe, aunque TargetType pueda ser None).
        builder.OwnsOne(
            n => n.Reference,
            reference =>
            {
                reference.Property(r => r.TargetType).HasColumnName("TargetType").HasConversion<int>().IsRequired();
                reference.Property(r => r.TargetId).HasColumnName("TargetId");

                // EF Core no soporta un índice compuesto que cruce columnas del owner (TenantId) y
                // del owned type inline vía la sintaxis "Navigation.Property" en HasIndex (probado:
                // falla en design-time con "no corresponding CLR property or field" — EF la trata
                // como un nombre de shadow property literal, no como una ruta anidada). Se indexa
                // (TargetType, TargetId) solo; el filtro global fail-closed por TenantId (siempre
                // aplicado) sigue acotando el resultado antes de tocar este índice.
                reference
                    .HasIndex(r => new { r.TargetType, r.TargetId })
                    .HasDatabaseName("IX_Notes_TargetType_TargetId");
            }
        );
        builder.Navigation(n => n.Reference).IsRequired();

        // NoteColor — owned VO opcional (columna ColorKind nullable).
        builder.OwnsOne(
            n => n.Color,
            color =>
            {
                color.Property(c => c.Kind).HasColumnName("ColorKind").HasConversion<int>();
            }
        );

        builder.HasMany(n => n.Attachments).WithOne().HasForeignKey(a => a.NoteId).OnDelete(DeleteBehavior.Cascade);

        // Guardrail 9: Attachments es una propiedad computada de solo lectura sobre el backing
        // field _attachments — se mapea la navegación vía el field (no se ignora), o EF la
        // auto-descubriría por convención y rompería migraciones.
        builder.Metadata.FindNavigation(nameof(Note.Attachments))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        // Índices de 01_Modelo_De_Dominio.md §6. El índice (TargetType, TargetId) queda dentro del
        // bloque OwnsOne(n => n.Reference) de arriba — ver el comentario ahí sobre por qué no cruza
        // TenantId (limitación real de EF Core con owned types inline, no un descuido).
        builder
            .HasIndex(n => new { n.TenantId, n.CreatedByUserId })
            .HasDatabaseName("IX_Notes_TenantId_CreatedByUserId");
        builder.HasIndex(n => new { n.TenantId, n.Status }).HasDatabaseName("IX_Notes_TenantId_Status");
    }
}
