using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaxVision.Signature.Domain.Analytics;

namespace TaxVision.Signature.Infrastructure.Persistence.Configurations;

public sealed class SignatureAnalyticsSealedRequestConfiguration
    : IEntityTypeConfiguration<SignatureAnalyticsSealedRequest>
{
    public void Configure(EntityTypeBuilder<SignatureAnalyticsSealedRequest> builder)
    {
        builder.ToTable("SignatureAnalyticsSealedRequests");
        builder.HasKey(mark => mark.Id);

        builder.Property(mark => mark.TenantId).IsRequired();
        builder.Property(mark => mark.SignatureRequestId).IsRequired();
        builder.Property(mark => mark.CountedAtUtc).IsRequired();

        // Una sola suma por solicitud: un re-sellado concurrente choca aquí y el reintento ya la encuentra.
        builder.HasIndex(mark => new { mark.TenantId, mark.SignatureRequestId }).IsUnique();
    }
}
