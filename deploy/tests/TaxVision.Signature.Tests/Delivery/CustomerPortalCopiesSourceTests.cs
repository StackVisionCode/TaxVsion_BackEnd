using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Delivery;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Tests.Delivery;

/// <summary>La copia por cliente se declara en CloudStorage: la de un sellado como <c>sealed</c>, la de un certificado como <c>certificate</c>.</summary>
public sealed class CustomerPortalCopiesSourceTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Luis = Guid.NewGuid();

    [Theory]
    [InlineData(CertificateGenerationMode.PerDocument)]
    [InlineData(CertificateGenerationMode.SingleForRequest)]
    public async Task Each_copy_asks_for_the_role_of_its_source(CertificateGenerationMode mode)
    {
        var kit = new DeliveryTestKit(
            sendCertificate: true,
            certificateMode: mode,
            firstSignerCustomerId: Ana,
            secondSignerCustomerId: Luis
        ).Seal();
        var sealedFiles = kit.Request.Documents.Select(document => document.SealedFileId!.Value).ToHashSet();

        await Copier(kit).CopyAvailableAsync(kit.Request, null, CancellationToken.None);

        Assert.NotEmpty(kit.Storage.CopySources);
        foreach (var (sourceFileId, customerId, source) in kit.Storage.CopySources)
        {
            Assert.Equal(kit.Request.Id, source.RequestId);
            Assert.Equal(customerId, source.RelatedCustomerId);
            Assert.Equal(sealedFiles.Contains(sourceFileId) ? "sealed" : "certificate", source.Role);
        }
        Assert.Contains(kit.Storage.CopySources, copy => copy.Source.Role == "certificate");
    }

    private static CustomerPortalCopier Copier(DeliveryTestKit kit) =>
        new(kit.Storage, kit.Files, new FreeLock(), NullLogger<CustomerPortalCopier>.Instance);

    private sealed class FreeLock : IDistributedLock
    {
        public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<ILockHandle>(new Handle(key));
    }

    private sealed class Handle(string key) : ILockHandle
    {
        public bool IsAcquired => true;
        public string Key => key;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
