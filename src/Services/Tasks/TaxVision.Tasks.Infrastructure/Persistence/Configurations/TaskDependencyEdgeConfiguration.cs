using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Tasks.Infrastructure.Persistence.ReadModels;

namespace TaxVision.Tasks.Infrastructure.Persistence.Configurations;

public sealed class TaskDependencyEdgeConfiguration : IEntityTypeConfiguration<TaskDependencyEdge>
{
    // Sin tabla ni vista propia: las filas siempre vienen del CTE, así que ninguna migración lo toca.
    public void Configure(EntityTypeBuilder<TaskDependencyEdge> builder)
    {
        builder.HasNoKey();
        builder.ToView(null);
    }
}
