using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Legal;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Legal;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fase L1.3 — RegisterDmcaTakedownHandler, SubmitDmcaCounterNoticeHandler y ReinstateDmcaFileHandler.</summary>
public sealed class DmcaHandlerTests
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

    private static RegisterDmcaTakedownCommand TakedownCommand(Guid tenantId, Guid fileId) =>
        new(
            tenantId,
            Guid.NewGuid(),
            fileId,
            "Acme Rights Holder",
            "legal@acme.example",
            "A photograph of the Golden Gate Bridge",
            "The exact same photograph re-uploaded without permission",
            SwornStatementAccepted: true,
            Audit()
        );

    [Fact]
    public async Task RegisterDmcaTakedown_on_an_available_file_blocks_it_holds_it_and_publishes_the_event()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var notices = new FakeDmcaNoticeRepository();
        var audit = new FakeStorageAuditRepository();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await RegisterDmcaTakedownHandler.Handle(
            TakedownCommand(tenantId, file.Id),
            files,
            notices,
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FileStatus.BlockedByPolicy, file.Status);
        Assert.True(file.IsLegalHeld);
        Assert.Single(audit.Logs, log => log.Action == "dmca.takedown.registered");
        Assert.Single(
            bus.Published.OfType<BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FileBlockedByDmcaTakedownIntegrationEvent>()
        );
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task RegisterDmcaTakedown_on_a_missing_file_fails_with_NotFound()
    {
        var result = await RegisterDmcaTakedownHandler.Handle(
            TakedownCommand(Guid.NewGuid(), Guid.NewGuid()),
            new FakeFileObjectRepository(),
            new FakeDmcaNoticeRepository(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task RegisterDmcaTakedown_when_an_active_notice_already_exists_for_the_file_fails()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var existing = DmcaNotice
            .Register(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                "Someone Else",
                ClaimantEmail.Create("other@example.com").Value,
                "desc",
                "desc",
                true,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var notices = new FakeDmcaNoticeRepository();
        notices.Seed(existing);

        var result = await RegisterDmcaTakedownHandler.Handle(
            TakedownCommand(tenantId, file.Id),
            files,
            notices,
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(DmcaErrors.ActiveNoticeAlreadyExists, result.Error);
        Assert.Equal(FileStatus.Available, file.Status); // no se toco el archivo
    }

    [Fact]
    public async Task RegisterDmcaTakedown_with_an_invalid_claimant_email_fails()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        var files = new FakeFileObjectRepository();
        files.Seed(file);

        var command = TakedownCommand(tenantId, file.Id) with { ClaimantEmail = "not-an-email" };
        var result = await RegisterDmcaTakedownHandler.Handle(
            command,
            files,
            new FakeDmcaNoticeRepository(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(DmcaErrors.InvalidClaimantEmail, result.Error);
    }

    [Fact]
    public async Task SubmitDmcaCounterNotice_on_an_open_notice_succeeds()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        file.BlockForTakedown(DateTime.UtcNow);
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var notice = DmcaNotice
            .Register(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                "Acme Rights Holder",
                ClaimantEmail.Create("legal@acme.example").Value,
                "desc",
                "desc",
                true,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var notices = new FakeDmcaNoticeRepository();
        notices.Seed(notice);
        var audit = new FakeStorageAuditRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await SubmitDmcaCounterNoticeHandler.Handle(
            new SubmitDmcaCounterNoticeCommand(
                tenantId,
                Guid.NewGuid(),
                notice.Id,
                "This is my own original work.",
                new StorageActorScope(false, null),
                Audit()
            ),
            notices,
            files,
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(DmcaNoticeStatus.CounterNoticeSubmitted, notice.Status);
        Assert.Single(audit.Logs, log => log.Action == "dmca.counter_notice.submitted");
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task SubmitDmcaCounterNotice_on_a_missing_notice_fails_with_NotFound()
    {
        var result = await SubmitDmcaCounterNoticeHandler.Handle(
            new SubmitDmcaCounterNoticeCommand(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "text",
                new StorageActorScope(false, null),
                Audit()
            ),
            new FakeDmcaNoticeRepository(),
            new FakeFileObjectRepository(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(DmcaErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task SubmitDmcaCounterNotice_from_a_customer_portal_scope_that_cannot_access_the_file_fails()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId); // OwnerType.Tenant, no accesible por un scope de customer portal
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var notice = DmcaNotice
            .Register(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                "Acme Rights Holder",
                ClaimantEmail.Create("legal@acme.example").Value,
                "desc",
                "desc",
                true,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var notices = new FakeDmcaNoticeRepository();
        notices.Seed(notice);

        var result = await SubmitDmcaCounterNoticeHandler.Handle(
            new SubmitDmcaCounterNoticeCommand(
                tenantId,
                Guid.NewGuid(),
                notice.Id,
                "text",
                new StorageActorScope(true, Guid.NewGuid()),
                Audit()
            ),
            notices,
            files,
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.NotFound, result.Error);
        Assert.Equal(DmcaNoticeStatus.Received, notice.Status); // no se toco el expediente
    }

    [Fact]
    public async Task ReinstateDmcaFile_on_an_open_notice_unblocks_the_file_lifts_the_hold_and_publishes_the_event()
    {
        var tenantId = Guid.NewGuid();
        var file = AvailableFile(tenantId);
        file.BlockForTakedown(DateTime.UtcNow);
        file.PlaceLegalHold();
        var files = new FakeFileObjectRepository();
        files.Seed(file);
        var notice = DmcaNotice
            .Register(
                Guid.NewGuid(),
                tenantId,
                file.Id,
                "Acme Rights Holder",
                ClaimantEmail.Create("legal@acme.example").Value,
                "desc",
                "desc",
                true,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        var notices = new FakeDmcaNoticeRepository();
        notices.Seed(notice);
        var audit = new FakeStorageAuditRepository();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReinstateDmcaFileHandler.Handle(
            new ReinstateDmcaFileCommand(tenantId, Guid.NewGuid(), notice.Id, "Claim withdrawn", Audit()),
            notices,
            files,
            audit,
            new FakeSystemClock(DateTime.UtcNow),
            unitOfWork,
            bus,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(DmcaNoticeStatus.Reinstated, notice.Status);
        Assert.Equal(FileStatus.Available, file.Status);
        Assert.False(file.IsLegalHeld);
        Assert.Single(audit.Logs, log => log.Action == "dmca.file.reinstated");
        Assert.Single(
            bus.Published.OfType<BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FileReinstatedFromTakedownIntegrationEvent>()
        );
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task ReinstateDmcaFile_on_a_missing_notice_fails_with_NotFound()
    {
        var result = await ReinstateDmcaFileHandler.Handle(
            new ReinstateDmcaFileCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Audit()),
            new FakeDmcaNoticeRepository(),
            new FakeFileObjectRepository(),
            new FakeStorageAuditRepository(),
            new FakeSystemClock(DateTime.UtcNow),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal(DmcaErrors.NotFound, result.Error);
    }
}
