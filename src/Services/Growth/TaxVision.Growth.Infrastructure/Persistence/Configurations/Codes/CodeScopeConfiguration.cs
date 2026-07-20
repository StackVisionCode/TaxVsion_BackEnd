using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Growth.Infrastructure.Persistence.Configurations.Codes;

public sealed class CodeScopeConfiguration : IEntityTypeConfiguration<CodeScope>
{
    public void Configure(EntityTypeBuilder<CodeScope> builder)
    {
        builder.ToTable("CodeScopes", GrowthSchemas.Codes);
        builder.HasKey(scope => scope.Id);

        builder.Property(scope => scope.TenantId).IsRequired();
        builder.Property(scope => scope.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(scope => scope.ScopeId).HasMaxLength(200).IsRequired();
        builder.Property(scope => scope.Mode).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder
            .HasIndex(scope => new
            {
                scope.CodeDefinitionId,
                scope.Type,
                scope.ScopeId,
                scope.Mode,
            })
            .IsUnique()
            .HasDatabaseName("UX_CodeScopes_CodeDefinition_Target_Mode");
    }
}
