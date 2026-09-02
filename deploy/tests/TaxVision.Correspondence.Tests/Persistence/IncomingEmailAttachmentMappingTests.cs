using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;

namespace TaxVision.Correspondence.Tests.Persistence;

/// <summary>
/// El <c>attachmentId</c> de Gmail es un token opaco largo (&gt;200 chars) y sin tope documentado.
/// Un cap fijo en <c>ProviderAttachmentId</c> truncaba el INSERT y hacía rollback de TODO el correo
/// entrante ("String or binary data would be truncated") — el correo con adjunto nunca aparecía.
/// El proveedor InMemory ignora los anchos, así que esto se fija a nivel de metadata del modelo:
/// la columna debe quedar sin MaxLength (nvarchar(max)).
/// </summary>
public sealed class IncomingEmailAttachmentMappingTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public void ProviderAttachmentId_is_unbounded_so_long_gmail_ids_do_not_truncate()
    {
        using var db = new CorrespondenceDbContext(
            new DbContextOptionsBuilder<CorrespondenceDbContext>()
                .UseInMemoryDatabase(nameof(IncomingEmailAttachmentMappingTests))
                .Options,
            new FakeTenantContext()
        );

        var property = db
            .Model.FindEntityType(typeof(IncomingEmailAttachment))!
            .FindProperty(nameof(IncomingEmailAttachment.ProviderAttachmentId))!;

        Assert.Null(property.GetMaxLength());
        Assert.False(property.IsNullable);
    }
}
