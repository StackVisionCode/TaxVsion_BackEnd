using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files.LegalHold;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fase L1.2 — SetLegalHoldHandler y LiftLegalHoldHandler.</summary>
public sealed class LegalHoldHandlerTests
{
    private static FileObject AvailableFile(Guid tenantId)
    {
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

    private static RequestAuditContext Audit() => new(null, null, "corr-1");

    [Fact]
    public async Task SetLegalHold_on_an_existing_file_succeeds_and_audits_the_reason()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetLegalHoldHandler.Handle(
            new SetLegalHoldCommand(tenantId, Guid.NewGuid(), file.Id, "litigation-2026-001", Audit()),
            files,
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(file.IsLegalHeld);
        Assert.Single(audit.Logs, log => log.Action == "file.legal_hold_set" && log.Details == "litigation-2026-001");
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task SetLegalHold_on_a_missing_file_fails_with_NotFound()
    {
        var result = await SetLegalHoldHandler.Handle(
            new SetLegalHoldCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "reason", Audit()),
            new FakeFileObjectRepository(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task SetLegalHold_on_an_already_held_file_fails_without_auditing_or_saving()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        file.PlaceLegalHold();
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetLegalHoldHandler.Handle(
            new SetLegalHoldCommand(tenantId, Guid.NewGuid(), file.Id, "reason", Audit()),
            files,
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.AlreadyLegalHeld, result.Error);
        Assert.Empty(audit.Logs);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task LiftLegalHold_on_a_held_file_succeeds_and_audits_the_reason()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        file.PlaceLegalHold();
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await LiftLegalHoldHandler.Handle(
            new LiftLegalHoldCommand(tenantId, Guid.NewGuid(), file.Id, "litigation-closed", Audit()),
            files,
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(file.IsLegalHeld);
        Assert.Single(audit.Logs, log => log.Action == "file.legal_hold_unset" && log.Details == "litigation-closed");
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task LiftLegalHold_on_a_missing_file_fails_with_NotFound()
    {
        var result = await LiftLegalHoldHandler.Handle(
            new LiftLegalHoldCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "reason", Audit()),
            new FakeFileObjectRepository(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task LiftLegalHold_on_a_file_that_is_not_held_fails_without_auditing_or_saving()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await LiftLegalHoldHandler.Handle(
            new LiftLegalHoldCommand(tenantId, Guid.NewGuid(), file.Id, "reason", Audit()),
            files,
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotLegalHeld, result.Error);
        Assert.Empty(audit.Logs);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
