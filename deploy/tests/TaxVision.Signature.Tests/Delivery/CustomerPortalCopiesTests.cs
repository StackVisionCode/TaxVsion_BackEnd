using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Delivery;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Tests.Delivery;

/// <summary>
/// Varios clientes firmantes: cada uno recibe en su portal copia de lo que firmó (y sus certificados); con
/// uno solo no se copia nada porque el original ya es suyo. Una copia por (archivo, cliente), nunca dos.
/// </summary>
public sealed class CustomerPortalCopiesTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Luis = Guid.NewGuid();

    [Fact]
    public void With_a_single_customer_signer_nothing_is_copied()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Ana).Seal();

        Assert.Empty(kit.Request.PendingCustomerCopies());
    }

    [Fact]
    public void Each_customer_gets_the_documents_they_signed_and_those_certificates()
    {
        var kit = new DeliveryTestKit(sendCertificate: true, firstSignerCustomerId: Ana, secondSignerCustomerId: Luis).Seal();
        var federal = kit.Request.Documents[0];
        var state = kit.Request.Documents[1];

        var pending = kit.Request.PendingCustomerCopies();

        Assert.Equal(4, pending.Count);
        Assert.Contains(new PendingCustomerCopy(federal.SealedFileId!.Value, Ana), pending);
        Assert.Contains(new PendingCustomerCopy(federal.CertificateFileId!.Value, Ana), pending);
        Assert.Contains(new PendingCustomerCopy(state.SealedFileId!.Value, Luis), pending);
        Assert.DoesNotContain(new PendingCustomerCopy(state.SealedFileId!.Value, Ana), pending);
    }

    [Fact]
    public void A_request_level_certificate_goes_to_every_customer()
    {
        var kit = new DeliveryTestKit(
            sendCertificate: true,
            certificateMode: CertificateGenerationMode.SingleForRequest,
            firstSignerCustomerId: Ana,
            secondSignerCustomerId: Luis
        ).Seal();
        var certificate = kit.Request.CertificateFileId!.Value;

        var pending = kit.Request.PendingCustomerCopies();

        Assert.Contains(new PendingCustomerCopy(certificate, Ana), pending);
        Assert.Contains(new PendingCustomerCopy(certificate, Luis), pending);
    }

    [Fact]
    public void Recording_a_copy_twice_keeps_one()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Ana, secondSignerCustomerId: Luis).Seal();
        var source = kit.Request.Documents[0].SealedFileId!.Value;

        kit.Request.RecordCustomerCopy(source, Ana, Guid.NewGuid(), DateTime.UtcNow);
        kit.Request.RecordCustomerCopy(source, Ana, Guid.NewGuid(), DateTime.UtcNow);

        Assert.Single(kit.Request.CustomerCopies);
        Assert.DoesNotContain(new PendingCustomerCopy(source, Ana), kit.Request.PendingCustomerCopies());
    }

    [Fact]
    public async Task The_copier_copies_available_files_once_and_a_second_pass_does_nothing()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Ana, secondSignerCustomerId: Luis).Seal();
        var copier = Copier(kit);

        var first = await copier.CopyAvailableAsync(kit.Request, null, CancellationToken.None);
        var second = await copier.CopyAvailableAsync(kit.Request, null, CancellationToken.None);

        Assert.Equal(2, first);
        Assert.Equal(0, second);
        Assert.Equal(2, kit.Storage.Copies.Count);
        Assert.Equal(2, kit.Request.CustomerCopies.Count);
    }

    [Fact]
    public async Task A_failed_copy_stays_pending_for_the_next_trigger()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Ana, secondSignerCustomerId: Luis).Seal();
        var failing = kit.Request.Documents[1].SealedFileId!.Value;
        kit.Storage.FailCopiesOf.Add(failing);

        var copied = await Copier(kit).CopyAvailableAsync(kit.Request, null, CancellationToken.None);

        Assert.Equal(1, copied);
        Assert.Contains(new PendingCustomerCopy(failing, Luis), kit.Request.PendingCustomerCopies());
    }

    [Fact]
    public async Task While_another_node_copies_the_trigger_retries_instead_of_skipping()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Ana, secondSignerCustomerId: Luis).Seal();
        var copier = new CustomerPortalCopier(kit.Storage, kit.Files, new HeldLock(), NullLogger<CustomerPortalCopier>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            copier.CopyAvailableAsync(kit.Request, null, CancellationToken.None)
        );
    }

    [Fact]
    public async Task The_backfill_dry_run_only_counts_what_is_missing()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Ana, secondSignerCustomerId: Luis).Seal();

        var report = await Backfill(kit, dryRun: true);

        Assert.True(report.DryRun);
        Assert.Equal(1, report.RequestsWithPendingCopies);
        Assert.Equal(2, report.CopiesStillPending);
        Assert.Equal(0, report.CopiesCreated);
        Assert.Empty(kit.Storage.Copies);
    }

    [Fact]
    public async Task The_backfill_creates_the_missing_copies_and_running_it_again_does_nothing()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Ana, secondSignerCustomerId: Luis).Seal();

        var first = await Backfill(kit, dryRun: false);
        var second = await Backfill(kit, dryRun: false);

        Assert.Equal(2, first.CopiesCreated);
        Assert.Equal(0, first.CopiesStillPending);
        Assert.Equal(0, second.RequestsWithPendingCopies);
        Assert.Equal(2, kit.Storage.Copies.Count);
    }

    private static async Task<PortalCopiesBackfillReport> Backfill(DeliveryTestKit kit, bool dryRun) =>
        (
            await BackfillCustomerPortalCopiesHandler.Handle(
                new BackfillCustomerPortalCopiesCommand(kit.Request.TenantId, dryRun),
                new SingleRequestRepository(kit.Request),
                Copier(kit),
                new NoOpUnitOfWork(),
                CancellationToken.None
            )
        ).Value;

    private static CustomerPortalCopier Copier(DeliveryTestKit kit) =>
        new(kit.Storage, kit.Files, new FreeLock(), NullLogger<CustomerPortalCopier>.Instance);

    private sealed class FreeLock : IDistributedLock
    {
        public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<ILockHandle>(new Handle(key, acquired: true));
    }

    private sealed class HeldLock : IDistributedLock
    {
        public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<ILockHandle>(new Handle(key, acquired: false));
    }

    private sealed class Handle(string key, bool acquired) : ILockHandle
    {
        public bool IsAcquired => acquired;
        public string Key => key;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
