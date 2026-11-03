using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Consumers;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Tests.Application;

public sealed class ImportFileScanResultConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("2b7e1f4a-9c3d-4e8f-a1b2-3c4d5e6f7a80");
    private static readonly Guid UserId = Guid.NewGuid();

    [Theory]
    [InlineData("EmptyFile", "Uploaded file is empty.")]
    [InlineData("UploadPolicy", "Uploaded file type or size is not allowed by the storage policy.")]
    [InlineData("StorageQuota.Exceeded", "Storage quota is full; the uploaded file could not be saved.")]
    [InlineData("InvalidOwnerOrFolder", "Uploaded file could not be saved to storage.")]
    public async Task Rejected_import_file_fails_the_queued_attempt_and_announces_it(string code, string expected)
    {
        var attempt = NewAttempt();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        await RejectAsync(Rejected(attempt.Id) with { Reason = code }, new InMemoryImports(attempt), bus, unitOfWork);

        Assert.Equal(ImportStatus.Failed, attempt.Status);
        Assert.Equal(expected, attempt.FailureReason);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal(0, unitOfWork.PublishedAtFirstSave);
        var failed = Assert.IsType<CustomerImportFailedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal(attempt.Id, failed.ImportJobId);
        Assert.Equal(UserId, failed.CreatedByUserId);
        Assert.Equal(TenantId, failed.TenantId);
        Assert.Equal(expected, failed.Reason);
    }

    [Fact]
    public async Task Rejection_while_canceling_confirms_the_cancel_without_a_failure()
    {
        var attempt = NewAttempt();
        attempt.RequestCancel(UserId);
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        await RejectAsync(Rejected(attempt.Id), new InMemoryImports(attempt), bus, unitOfWork);

        Assert.Equal(ImportStatus.Canceled, attempt.Status);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Repeated_rejection_is_idempotent()
    {
        var attempt = NewAttempt();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);
        var repo = new InMemoryImports(attempt);

        for (var i = 0; i < 2; i++)
            await RejectAsync(Rejected(attempt.Id), repo, bus, unitOfWork);

        Assert.Equal(ImportStatus.Failed, attempt.Status);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Single(bus.Published);
    }

    [Theory]
    [InlineData("signature", "Imports")]
    [InlineData("customer", "Avatars")]
    [InlineData("customer", null)]
    public async Task Rejections_of_other_files_are_ignored(string service, string? folderType)
    {
        var attempt = NewAttempt();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        await RejectAsync(
            Rejected(attempt.Id) with
            {
                RequestingService = service,
                FolderType = folderType,
            },
            new InMemoryImports(attempt),
            bus,
            unitOfWork
        );

        Assert.Equal(ImportStatus.Queued, attempt.Status);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Unknown_file_id_is_a_no_op()
    {
        var attempt = NewAttempt();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        await RejectAsync(Rejected(Guid.NewGuid()), new InMemoryImports(attempt), bus, unitOfWork);

        Assert.Equal(ImportStatus.Queued, attempt.Status);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Rejection_from_another_tenant_is_ignored()
    {
        var attempt = NewAttempt();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        await RejectAsync(
            Rejected(attempt.Id) with
            {
                TenantId = Guid.NewGuid(),
            },
            new InMemoryImports(attempt),
            bus,
            unitOfWork
        );

        Assert.Equal(ImportStatus.Queued, attempt.Status);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Attempt_that_already_started_is_not_failed()
    {
        var attempt = NewAttempt();
        attempt.Start();
        var bus = new FakeMessageBus();
        var unitOfWork = new RecordingUnitOfWork(bus);

        await RejectAsync(Rejected(attempt.Id), new InMemoryImports(attempt), bus, unitOfWork);

        Assert.Equal(ImportStatus.Validating, attempt.Status);
        Assert.Equal(0, unitOfWork.Saves);
        Assert.Empty(bus.Published);
    }

    private static CustomerImportAttempt NewAttempt() =>
        CustomerImportAttempt
            .Create(TenantId, UserId, $"key-{Guid.NewGuid():N}", DuplicateStrategy.Skip, ImportSourceKind.Csv, "c.csv")
            .Value;

    private static FileSaveRejectedIntegrationEvent Rejected(Guid fileId) =>
        new()
        {
            TenantId = TenantId,
            FileId = fileId,
            RequestingService = "customer",
            OwnerType = "Tenant",
            OwnerId = null,
            FolderType = "Imports",
            Reason = "EmptyFile",
            CorrelationId = "corr-import",
        };

    private static Task RejectAsync(
        FileSaveRejectedIntegrationEvent msg,
        ICustomerImportRepository repo,
        FakeMessageBus bus,
        RecordingUnitOfWork unitOfWork
    ) =>
        ImportFileScanResultConsumer.Handle(
            msg,
            repo,
            unitOfWork,
            bus,
            new FixedCorrelationContext(),
            NullLogger<CustomerImportAttempt>.Instance,
            CancellationToken.None
        );

    private sealed class InMemoryImports(params CustomerImportAttempt[] attempts) : ICustomerImportRepository
    {
        public Task<CustomerImportAttempt?> GetByIdAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(attempts.FirstOrDefault(a => a.Id == id));

        public Task<CustomerImportAttempt?> GetByIdempotencyKeyAsync(
            Guid tenantId,
            string idempotencyKey,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<int> CountActiveByTenantAsync(Guid tenantId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task AddAsync(CustomerImportAttempt attempt, CancellationToken ct) =>
            throw new NotImplementedException();
    }
}
