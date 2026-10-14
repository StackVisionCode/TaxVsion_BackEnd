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

        var result = folder.Rename(ValidName("Nuevo"), null);

        Assert.True(result.IsSuccess);
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

        var result = folder.Reparent(newParentId, "/Clientes/Oficina A");

        Assert.True(result.IsSuccess);
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

    private static Folder CreateSystemFolder(string category = "sys.signatures", string name = "Signed Documents") =>
        Folder
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                OwnerType.Customer,
                Guid.NewGuid(),
                null,
                ValidName(name),
                null,
                Guid.NewGuid(),
                DateTime.UtcNow,
                FolderCategory.Create(category).Value
            )
            .Value;

    private static Folder CreateUserFolder(string name = "Tax Year 2025") =>
        Folder
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                OwnerType.Customer,
                Guid.NewGuid(),
                null,
                ValidName(name),
                null,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;

    [Theory]
    [InlineData("sys.signatures")]
    [InlineData("sys.email")]
    [InlineData("sys.documents")]
    public void IsSystemFolder_is_true_when_category_starts_with_sys(string category)
    {
        var folder = CreateSystemFolder(category);

        Assert.True(folder.IsSystemFolder);
    }

    [Fact]
    public void IsSystemFolder_is_false_for_folders_without_category()
    {
        var folder = CreateUserFolder();

        Assert.False(folder.IsSystemFolder);
    }

    [Fact]
    public void Rename_rejects_a_system_folder()
    {
        var folder = CreateSystemFolder();

        var result = folder.Rename(ValidName("Hijacked"), null);

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.SystemFolderProtected, result.Error);
        Assert.Equal("Signed Documents", folder.Name); // sin cambios
    }

    [Fact]
    public void Reparent_rejects_a_system_folder()
    {
        var folder = CreateSystemFolder();
        var originalPath = folder.RelativePath;

        var result = folder.Reparent(Guid.NewGuid(), "/Attacker");

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.SystemFolderProtected, result.Error);
        Assert.Equal(originalPath, folder.RelativePath);
    }

    [Fact]
    public void SoftDelete_rejects_a_system_folder()
    {
        var folder = CreateSystemFolder();

        var result = folder.SoftDelete(Guid.NewGuid(), DateTime.UtcNow, TimeSpan.FromDays(30));

        Assert.True(result.IsFailure);
        Assert.Equal(FolderErrors.SystemFolderProtected, result.Error);
        Assert.False(folder.IsDeleted);
    }

    [Fact]
    public void Rename_allows_a_user_folder()
    {
        var folder = CreateUserFolder();

        var result = folder.Rename(ValidName("Tax Year 2026"), null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Tax Year 2026", folder.Name);
    }

    [Fact]
    public void Reparent_allows_a_user_folder()
    {
        var folder = CreateUserFolder();

        var result = folder.Reparent(Guid.NewGuid(), "/Clientes/Oficina A");

        Assert.True(result.IsSuccess);
        Assert.Equal("/Clientes/Oficina A/Tax Year 2025", folder.RelativePath);
    }

    [Fact]
    public void SoftDelete_allows_a_user_folder()
    {
        var folder = CreateUserFolder();

        var result = folder.SoftDelete(folder.Id, DateTime.UtcNow, TimeSpan.FromDays(30));

        Assert.True(result.IsSuccess);
        Assert.True(folder.IsDeleted);
    }
}
