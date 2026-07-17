using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>Fase C2 — arbol navegable de carpetas: creacion, rename, reparent, y el value object FolderName.</summary>
public sealed class FolderDomainTests
{
    private static FolderName ValidName(string value = "Recibos") => FolderName.Create(value).Value;

    [Fact]
    public void Create_at_root_composes_a_leading_slash_path()
    {
        var tenantId = Guid.NewGuid();
        var result = Folder.Create(
            Guid.NewGuid(),
            tenantId,
            OwnerType.Tenant,
            null,
            null,
            ValidName("Clientes"),
            null,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("/Clientes", result.Value.RelativePath);
        Assert.Null(result.Value.ParentFolderId);
    }

    [Fact]
    public void Create_under_a_parent_appends_to_the_parent_s_path()
    {
        var result = Folder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OwnerType.Tenant,
            null,
            Guid.NewGuid(),
            ValidName("Recibos"),
            "/Clientes/Oficina A",
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("/Clientes/Oficina A/Recibos", result.Value.RelativePath);
    }

    [Fact]
    public void Create_requires_an_owner_id_for_non_tenant_owner_types()
    {
        var result = Folder.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            OwnerType.Customer,
            null,
            null,
            ValidName(),
            null,
            Guid.NewGuid(),
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.OwnerRequired, result.Error);
    }

    [Fact]
    public void Rename_recomposes_the_path_using_the_new_name()
    {
        var folder = Folder
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                OwnerType.Tenant,
                null,
                null,
                ValidName("Viejo"),
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        folder.Rename(ValidName("Nuevo"), null);

        Assert.Equal("Nuevo", folder.Name);
        Assert.Equal("/Nuevo", folder.RelativePath);
    }

    [Fact]
    public void Reparent_recomposes_the_path_under_the_new_parent()
    {
        var folder = Folder
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                OwnerType.Tenant,
                null,
                null,
                ValidName("Recibos"),
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var newParentId = Guid.NewGuid();

        folder.Reparent(newParentId, "/Clientes/Oficina A");

        Assert.Equal(newParentId, folder.ParentFolderId);
        Assert.Equal("/Clientes/Oficina A/Recibos", folder.RelativePath);
    }

    [Fact]
    public void RebasePath_only_touches_the_materialized_path()
    {
        var folder = Folder
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                OwnerType.Tenant,
                null,
                Guid.NewGuid(),
                ValidName("Recibos"),
                "/A",
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

        folder.RebasePath("/B/Recibos");

        Assert.Equal("/B/Recibos", folder.RelativePath);
        Assert.Equal("Recibos", folder.Name); // el nombre propio no cambia
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Con/Barra")]
    public void FolderName_rejects_empty_or_slash_containing_values(string? value)
    {
        var result = FolderName.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.InvalidName, result.Error);
    }

    [Fact]
    public void FolderName_trims_surrounding_whitespace()
    {
        var result = FolderName.Create("  Recibos  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("Recibos", result.Value.Value);
    }
}
