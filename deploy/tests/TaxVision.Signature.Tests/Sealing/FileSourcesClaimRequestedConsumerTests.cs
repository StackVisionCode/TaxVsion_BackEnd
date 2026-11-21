using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Tests.Delivery;
using TaxVision.Signature.Tests.Requests;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen: Signature declara solo los suyos, con los mismos valores
/// que el backfill manual (sellados, certificados, copias y originales), e ignora el resto.
/// </summary>
public sealed class FileSourcesClaimRequestedConsumerTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Luis = Guid.NewGuid();

    [Fact]
    public async Task Declares_only_the_claimed_files_with_the_values_of_the_backfill()
    {
        var kit = new DeliveryTestKit(sendCertificate: true, firstSignerCustomerId: Ana).Seal();
        var documents = kit.Request.Documents;
        Guid[] owned =
        [
            documents[0].SealedFileId!.Value,
            documents[1].CertificateFileId!.Value,
            documents[0].OriginalFileId,
        ];
        var bus = new CapturingBus();

        await HandleAsync(Claim(kit.Request.TenantId, [.. owned, Guid.NewGuid()]), new Files(kit.Request), bus);

        var declared = bus.Messages.Cast<FileSourceDeclaredIntegrationEvent>().ToList();
        Assert.Equal(3, declared.Count);
        Assert.Equal(await BackfillKeysAsync(kit.Request, owned), declared.Select(Key).Order());
        Assert.Contains(
            declared,
            evt => evt.FileId == owned[0] && evt.SourceRole == "sealed" && evt.RelatedCustomerId == Ana
        );
    }

    [Fact]
    public async Task Several_customers_declare_masters_and_copies_like_the_backfill()
    {
        var kit = new DeliveryTestKit(
            sendCertificate: true,
            certificateMode: CertificateGenerationMode.SingleForRequest,
            firstSignerCustomerId: Ana,
            secondSignerCustomerId: Luis
        ).Seal();
        var federal = kit.Request.Documents[0].SealedFileId!.Value;
        var certificate = kit.Request.CertificateFileId!.Value;
        var federalCopy = Guid.NewGuid();
        var certificateCopy = Guid.NewGuid();
        kit.Request.RecordCustomerCopy(federal, Ana, federalCopy, DateTime.UtcNow);
        kit.Request.RecordCustomerCopy(certificate, Luis, certificateCopy, DateTime.UtcNow);
        Guid[] owned = [federal, certificate, federalCopy, certificateCopy, kit.Request.Documents[1].OriginalFileId];
        var bus = new CapturingBus();

        await HandleAsync(Claim(kit.Request.TenantId, owned), new Files(kit.Request), bus);

        var declared = bus.Messages.Cast<FileSourceDeclaredIntegrationEvent>().ToList();
        Assert.Equal(await BackfillKeysAsync(kit.Request, owned), declared.Select(Key).Order());
        Assert.Contains(declared, evt => evt.FileId == federal && evt.SourceRole == "sealed_master");
        Assert.Contains(
            declared,
            evt => evt.FileId == certificateCopy && evt.SourceRole == "certificate" && evt.RelatedCustomerId == Luis
        );
    }

    [Fact]
    public async Task The_base_pdf_of_a_template_is_not_declared()
    {
        var draft = Draft();
        var templateBase = Guid.NewGuid();
        var own = Guid.NewGuid();
        Assert.True(draft.AddDocument(TestSignatureLimits.Default, templateBase, "Letter").IsSuccess);
        Assert.True(draft.AddDocument(TestSignatureLimits.Default, own, "Annex").IsSuccess);
        var bus = new CapturingBus();

        await HandleAsync(
            Claim(draft.TenantId, templateBase, own),
            new Files(draft),
            bus,
            new TemplateFiles(templateBase)
        );

        var declared = Assert.Single(bus.Messages.Cast<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(own, declared.FileId);
        Assert.Equal("original", declared.SourceRole);
        Assert.Equal(draft.Id, declared.SourceResourceId);
    }

    [Fact]
    public async Task Ids_that_are_not_its_own_publish_nothing()
    {
        var kit = new DeliveryTestKit(sendCertificate: true, firstSignerCustomerId: Ana).Seal();
        var bus = new CapturingBus();

        await HandleAsync(Claim(kit.Request.TenantId, Guid.NewGuid(), Guid.NewGuid()), new Files(kit.Request), bus);

        Assert.Empty(bus.Messages);
    }

    [Fact]
    public async Task Empty_list_or_empty_tenant_does_nothing()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Ana).Seal();
        var files = new Files(kit.Request);
        var bus = new CapturingBus();

        await HandleAsync(Claim(kit.Request.TenantId), files, bus);
        await HandleAsync(Claim(Guid.Empty, kit.Request.Documents[0].SealedFileId!.Value), files, bus);

        Assert.Empty(bus.Messages);
        Assert.Empty(files.Calls);
    }

    [Fact]
    public async Task Only_looks_in_the_tenant_of_the_claim()
    {
        var kit = new DeliveryTestKit(firstSignerCustomerId: Ana).Seal();
        var files = new Files(kit.Request);
        var bus = new CapturingBus();
        var otherTenant = Guid.NewGuid();

        await HandleAsync(Claim(otherTenant, kit.Request.Documents[0].SealedFileId!.Value), files, bus);

        Assert.Empty(bus.Messages);
        Assert.Equal(otherTenant, Assert.Single(files.Calls));
    }

    private static string Key(FileSourceDeclaredIntegrationEvent evt) =>
        $"{evt.TenantId}|{evt.FileId}|{evt.SourceService}|{evt.SourceResourceType}|{evt.SourceResourceId}|{evt.SourceRole}|{evt.RelatedCustomerId}";

    // Lo que el backfill manual declara para esos archivos.
    private static async Task<IEnumerable<string>> BackfillKeysAsync(
        SignatureRequest request,
        IReadOnlyCollection<Guid> fileIds
    )
    {
        var publisher = new RecordingPublisher();
        var result = await RedeclareFileSourcesHandler.Handle(
            new RedeclareFileSourcesCommand(request.TenantId, DryRun: false),
            new SingleRequestRepository(request),
            new Files(request),
            TemplateFiles.None,
            publisher,
            new FixedCorrelation(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return publisher.Declared.Where(evt => fileIds.Contains(evt.FileId)).Select(Key).Order().ToList();
    }

    private static SignatureRequest Draft() =>
        SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Draft",
                null,
                "Fiscal",
                72,
                false,
                false,
                false
            )
            .Value;

    private static FileSourcesClaimRequestedIntegrationEvent Claim(Guid tenantId, params Guid[] fileIds) =>
        new() { TenantId = tenantId, FileIds = fileIds };

    private static Task HandleAsync(
        FileSourcesClaimRequestedIntegrationEvent claim,
        Files files,
        CapturingBus bus,
        TemplateFiles? templates = null
    ) =>
        FileSourcesClaimRequestedConsumer.Handle(
            claim,
            files,
            templates ?? TemplateFiles.None,
            bus,
            new FixedCorrelation(),
            CancellationToken.None
        );

    // Simula las consultas: solo el tenant pedido y solicitudes con algún archivo en la lista.
    private sealed class Files(SignatureRequest request) : ISignatureOriginalFileQuery
    {
        public List<Guid> Calls { get; } = [];

        public Task<IReadOnlyList<RequestOriginalFiles>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RequestOriginalFiles>>(
                request.TenantId == tenantId ? [SignatureFileDeclarations.OriginalsOf(request)] : []
            );

        public Task<IReadOnlyList<SignatureRequest>> ListOwningFilesAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> fileIds,
            CancellationToken ct = default
        )
        {
            Calls.Add(tenantId);
            var files = request
                .Documents.SelectMany(d => new[] { d.OriginalFileId, d.SealedFileId, d.CertificateFileId })
                .Append(request.CertificateFileId)
                .Concat(request.CustomerCopies.Select(copy => (Guid?)copy.CopyFileId))
                .OfType<Guid>();
            var owns = request.TenantId == tenantId && files.Any(fileIds.Contains);
            return Task.FromResult<IReadOnlyList<SignatureRequest>>(owns ? [request] : []);
        }
    }

    private sealed class RecordingPublisher : IFileSourceDeclarationPublisher
    {
        public List<FileSourceDeclaredIntegrationEvent> Declared { get; } = [];

        public Task<Result> PublishAsync(
            IReadOnlyList<FileSourceDeclaredIntegrationEvent> page,
            CancellationToken ct = default
        )
        {
            Declared.AddRange(page);
            return Task.FromResult(Result.Success());
        }
    }
}
