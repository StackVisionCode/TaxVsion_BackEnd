using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Archivos de prueba para la carpeta Avatars (fotos de perfil) y un documento normal de contraste.</summary>
internal static class AvatarTestFiles
{
    public const string Sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public static FileObject Avatar(
        Guid tenantId,
        OwnerType ownerType,
        Guid ownerId,
        bool available = true,
        string detectedContentType = "image/webp",
        DateTime? createdAtUtc = null
    )
    {
        var now = createdAtUtc ?? DateTime.UtcNow;
        var fileId = Guid.NewGuid();
        var file = FileObject
            .Register(
                fileId,
                tenantId,
                ownerType,
                ownerId,
                FolderType.Avatars,
                null,
                ObjectKey
                    .Create($"tenants/{tenantId:N}/{ownerType.ToSegment()}/{ownerId:N}/avatars/{fileId:N}.webp")
                    .Value,
                "avatar.webp",
                "image/webp",
                2048,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        if (available)
            MakeAvailable(file, detectedContentType, now);
        return file;
    }

    public static FileObject Document(Guid tenantId, Guid customerId, DateTime? createdAtUtc = null)
    {
        var now = createdAtUtc ?? DateTime.UtcNow;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Customer,
                customerId,
                FolderType.Documents,
                2026,
                ObjectKey
                    .Create($"tenants/{tenantId:N}/customer/{customerId:N}/documents/2026/{Guid.NewGuid():N}.pdf")
                    .Value,
                "return.pdf",
                "application/pdf",
                1024,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        MakeAvailable(file, "application/pdf", now);
        return file;
    }

    private static void MakeAvailable(FileObject file, string detectedContentType, DateTime now)
    {
        Assert.True(file.MarkPendingScan().IsSuccess);
        Assert.True(file.MarkScanning().IsSuccess);
        Assert.True(file.MarkAvailable(ChecksumSha256.Create(Sha256).Value, detectedContentType, now).IsSuccess);
    }
}
