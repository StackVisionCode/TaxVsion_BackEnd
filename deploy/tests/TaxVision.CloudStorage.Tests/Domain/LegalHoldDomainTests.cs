using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Domain;

/// <summary>Fase L1.2 — FileObject.PlaceLegalHold/LiftLegalHold y su interaccion con SoftDelete.</summary>
public sealed class LegalHoldDomainTests
{
    private static FileObject AvailableFile()
    {
        var tenantId = Guid.NewGuid();
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2025/{Guid.NewGuid():N}.pdf").Value;
        var file = FileObject
            .Register(
                Guid.NewGuid(),
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                key,
                "return.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                DateTime.UtcNow,
                DateTime.UtcNow.AddHours(24)
            )
            .Value;
        file.MarkPendingScan();
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", DateTime.UtcNow);
        return file;
    }

    [Fact]
    public void PlaceLegalHold_on_a_file_that_is_not_held_succeeds()
    {
        var file = AvailableFile();

        var result = file.PlaceLegalHold();

        Assert.True(result.IsSuccess);
        Assert.True(file.IsLegalHeld);
    }

    [Fact]
    public void PlaceLegalHold_on_an_already_held_file_fails()
    {
        var file = AvailableFile();
        file.PlaceLegalHold();

        var result = file.PlaceLegalHold();

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.AlreadyLegalHeld, result.Error);
    }

    [Fact]
    public void LiftLegalHold_on_a_held_file_succeeds()
    {
        var file = AvailableFile();
        file.PlaceLegalHold();

        var result = file.LiftLegalHold();

        Assert.True(result.IsSuccess);
        Assert.False(file.IsLegalHeld);
    }

    [Fact]
    public void LiftLegalHold_on_a_file_that_is_not_held_fails()
    {
        var file = AvailableFile();

        var result = file.LiftLegalHold();

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotLegalHeld, result.Error);
    }

    [Fact]
    public void SoftDelete_of_a_legal_held_file_fails_and_leaves_it_Available()
    {
        var file = AvailableFile();
        file.PlaceLegalHold();

        var result = file.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30));

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.LegalHold, result.Error);
        Assert.Equal(FileStatus.Available, file.Status);
    }
}
