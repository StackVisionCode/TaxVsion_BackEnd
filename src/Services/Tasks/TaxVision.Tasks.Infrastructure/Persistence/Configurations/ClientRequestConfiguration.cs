using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Domain.ClientRequests;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

public sealed class ClientRequestConfiguration : IEntityTypeConfiguration<ClientRequest>
{
    public void Configure(EntityTypeBuilder<ClientRequest> builder)
    {
        builder.ToTable("ClientRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.CustomerId).IsRequired();
        builder.Property(r => r.TaskId);
        builder.Property(r => r.Title).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Details).HasMaxLength(4000);
        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.DueAtUtc);
        builder.Property(r => r.RequestedByUserId).IsRequired();
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.SubmittedAtUtc);
        builder.Property(r => r.ResolvedAtUtc);
        builder.Property(r => r.ResolvedByUserId);
        builder.Property(r => r.ResolutionNote).HasMaxLength(1000);

        builder.Ignore(r => r.IsOpen);
        builder.Ignore(r => r.DomainEvents);

        // El preparador cierra el pedido mientras el cliente sube otro documento: misma fila, dos
        // manos.
        builder.Property(r => r.RowVersion).IsRowVersion();

        builder
            .HasMany(r => r.Documents)
            .WithOne()
            .HasForeignKey(d => d.ClientRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .Metadata.FindNavigation(nameof(ClientRequest.Documents))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // La consulta del portal: lo del cliente, lo abierto primero.
        builder
            .HasIndex(r => new
            {
                r.TenantId,
                r.CustomerId,
                r.Status,
            })
            .HasDatabaseName("IX_ClientRequests_TenantId_CustomerId_Status");

        // La del staff: que le falta al encargo.
        builder.HasIndex(r => new { r.TenantId, r.TaskId }).HasDatabaseName("IX_ClientRequests_TenantId_TaskId");
    }
}

public sealed class ClientRequestDocumentConfiguration : IEntityTypeConfiguration<ClientRequestDocument>
{
    public void Configure(EntityTypeBuilder<ClientRequestDocument> builder)
    {
        builder.ToTable("ClientRequestDocuments");
        builder.HasKey(d => d.Id);

        // El Guid lo genera el dominio: sin esto EF intenta un UPDATE en vez de un INSERT.
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.ClientRequestId).IsRequired();
        builder.Property(d => d.FileId).IsRequired();
        builder.Property(d => d.DisplayName).HasMaxLength(260).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(160);
        builder.Property(d => d.SizeBytes).IsRequired();
        builder.Property(d => d.Status).HasConversion<int>().IsRequired();
        builder.Property(d => d.RejectionReason).HasMaxLength(200);
        builder.Property(d => d.UploadedAtUtc).IsRequired();
        builder.Property(d => d.ResolvedAtUtc);

        builder.Ignore(d => d.IsActive);

        // Mismo archivo una vez por pedido mientras siga vivo; tras un borrado en origen se puede
        // volver a subir.
        builder
            .HasIndex(d => new { d.ClientRequestId, d.FileId })
            .IsUnique()
            .HasFilter($"[Status] <> {(int)Domain.Tasks.AttachmentStatus.Detached}")
            .HasDatabaseName("UX_ClientRequestDocuments_RequestId_FileId_Active");

        // El consumer del escaneo llega con un FileId y nada mas.
        builder.HasIndex(d => d.FileId).HasDatabaseName("IX_ClientRequestDocuments_FileId");
    }
}
