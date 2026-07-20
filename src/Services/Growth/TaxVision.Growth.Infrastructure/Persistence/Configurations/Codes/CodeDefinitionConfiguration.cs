using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

public sealed class CodeDefinitionConfiguration : IEntityTypeConfiguration<CodeDefinition>
{
    public void Configure(EntityTypeBuilder<CodeDefinition> builder)
    {
        builder.ToTable(
            "CodeDefinitions",
            GrowthSchemas.Codes,
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CodeDefinitions_Period",
                    "[ExpiresAtUtc] IS NULL OR [ExpiresAtUtc] > [StartsAtUtc]"
                );
                table.HasCheckConstraint(
                    "CK_CodeDefinitions_Counters",
                    "[ActiveReservations] >= 0 AND [CommittedRedemptions] >= 0"
                );
            }
        );
        builder.HasKey(definition => definition.Id);
        builder
            .HasAlternateKey(definition => new { definition.Id, definition.TenantId })
            .HasName("AK_CodeDefinitions_Id_TenantId");
        builder.Ignore(definition => definition.DomainEvents);

        builder.Property(definition => definition.TenantId).IsRequired();
        builder
            .Property(definition => definition.OwnerScope)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(definition => definition.Name).HasMaxLength(200).IsRequired();
        builder.Property(definition => definition.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder
            .Property(definition => definition.CodeHash)
            .HasConversion(hash => hash.Value, value => CodeTokenHash.Create(value).Value)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder
            .Property(definition => definition.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(definition => definition.StartsAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(definition => definition.ExpiresAtUtc).HasColumnType("datetime2(7)");
        builder.Property(definition => definition.CreatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(definition => definition.UpdatedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.Property(definition => definition.RowVersion).IsRowVersion();

        builder.OwnsOne(
            definition => definition.Display,
            display => display.ConfigureDisplay("Code")
        );
        builder.Navigation(definition => definition.Display).IsRequired();

        builder
            .HasMany(definition => definition.RuleVersions)
            .WithOne()
            .HasForeignKey(rule => new { rule.CodeDefinitionId, rule.TenantId })
            .HasPrincipalKey(definition => new { definition.Id, definition.TenantId })
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasMany(definition => definition.Scopes)
            .WithOne()
            .HasForeignKey(scope => new { scope.CodeDefinitionId, scope.TenantId })
            .HasPrincipalKey(definition => new { definition.Id, definition.TenantId })
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasIndex(definition => new { definition.TenantScopeId, definition.CodeHash })
            .IsUnique()
            .HasDatabaseName("UX_CodeDefinitions_TenantScopeId_CodeHash");
        builder
            .HasIndex(definition => new { definition.TenantId, definition.Status })
            .HasDatabaseName("IX_CodeDefinitions_TenantId_Status");
    }
}
