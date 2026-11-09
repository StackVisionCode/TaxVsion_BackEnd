using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Reposición de CST-5: fija los contadores desde Files con la misma regla que QuotaFootprint.</summary>
public sealed class RecalculateTenantQuotaHandlerTests
{
    private const string Sha256 = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FakeFileObjectRepository _files = new();
    private readonly FakeStorageLimitRepository _limits = new();
    private readonly FakeStorageAuditRepository _audit = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    [Fact]
    public async Task Handle_SetsCountersFromFilesAndAudits()
    {
        var limit = TenantStorageLimit.Create(_tenantId, "starter", 10_000, 10_000);
        limit.Reserve(900);
        limit.Commit(700);
        _limits.Seed(limit);
        _files.Seed(Available(100));
        _files.Seed(Pending(20));
        _files.Seed(Infected(50));
        var deleted = Available(30);
        deleted.SoftDelete(DateTime.UtcNow, TimeSpan.FromDays(30));
        _files.Seed(deleted);

        var result = await HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(130, limit.UsedBytes);
        Assert.Equal(20, limit.ReservedBytes);
        Assert.Equal(700, result.Value.UsedBytesBefore);
        Assert.Equal(200, result.Value.ReservedBytesBefore);
        Assert.Equal("quota.recalculated", Assert.Single(_audit.Logs).Action);
        Assert.Equal(1, _unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Handle_RunTwice_LeavesTheSameCounters()
    {
        var limit = TenantStorageLimit.Create(_tenantId, "starter", 10_000, 10_000);
        _limits.Seed(limit);
        _files.Seed(Available(100));

        await HandleAsync();
        await HandleAsync();

        Assert.Equal(100, limit.UsedBytes);
        Assert.Equal(0, limit.ReservedBytes);
    }

    [Fact]
    public async Task Handle_WithoutQuotaRow_ReturnsNotProvisioned()
    {
        var result = await HandleAsync();

        Assert.Equal(QuotaErrors.NotProvisioned, result.Error);
        Assert.Equal(0, _unitOfWork.SaveChangesCallCount);
    }

    private Task<BuildingBlocks.Results.Result<RecalculateTenantQuotaResponse>> HandleAsync() =>
        RecalculateTenantQuotaHandler.Handle(
            new RecalculateTenantQuotaCommand(_tenantId, Guid.NewGuid()),
            _limits,
            _files,
            _audit,
            new FakeSystemClock(DateTime.UtcNow),
            _unitOfWork,
            CancellationToken.None
        );

    private FileObject Pending(long size)
    {
        var now = DateTime.UtcNow;
        var key = ObjectKey.Create($"tenants/{_tenantId:N}/tenant/documents/2026/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                _tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                "doc.pdf",
                "application/pdf",
                size,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
    }

    private FileObject Available(long size)
    {
        var file = Pending(size);
        file.MarkPendingScan(DateTime.UtcNow);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(Sha256).Value, "application/pdf", DateTime.UtcNow);
        return file;
    }

    private FileObject Infected(long size)
    {
        var file = Pending(size);
        file.MarkPendingScan(DateTime.UtcNow);
        file.MarkScanning();
        file.MarkInfected("Eicar", "application/pdf", DateTime.UtcNow);
        return file;
    }
}
