using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Sealing;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// Backfill: Signature vuelve a declarar en CloudStorage los sellados, certificados y copias por cliente de las
/// solicitudes completadas, y el original a firmar de todas. <c>dryRun</c> solo cuenta; repetirlo publica lo mismo.
/// </summary>
public sealed class RedeclareFileSourcesTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Luis = Guid.NewGuid();

    [Fact]
    public async Task Dry_run_counts_without_publishing()
    {
        var kit = new DeliveryTestKit(sendCertificate: true, firstSignerCustomerId: Ana).Seal();
        var publisher = new RecordingPublisher();

        var report = await RunAsync(kit.Request, publisher, dryRun: true);

        Assert.True(report.DryRun);
        Assert.Equal(1, report.RequestsScanned);
        Assert.Equal(6, report.FilesDeclared);
        Assert.Empty(publisher.Pages);
    }

    [Fact]
    public async Task A_single_customer_request_declares_sealed_and_certificates_with_the_customer()
    {
        var kit = new DeliveryTestKit(sendCertificate: true, firstSignerCustomerId: Ana).Seal();
        var publisher = new RecordingPublisher();

        var report = await RunAsync(kit.Request, publisher, dryRun: false);

        var declared = publisher.Pages.SelectMany(page => page).ToList();
        Assert.Equal(6, report.FilesDeclared);
        Assert.Equal(6, declared.Count);
        Assert.All(
            declared,
            evt =>
            {
                Assert.Equal(kit.Request.TenantId, evt.TenantId);
                Assert.Equal("signature", evt.SourceService);
                Assert.Equal("signature_request", evt.SourceResourceType);
                Assert.Equal(kit.Request.Id, evt.SourceResourceId);
                Assert.Equal(Ana, evt.RelatedCustomerId);
            }
        );
        foreach (var document in kit.Request.Documents)
        {
            Assert.Contains(declared, evt => evt.FileId == document.SealedFileId && evt.SourceRole == "sealed");
            Assert.Contains(
                declared,
                evt => evt.FileId == document.CertificateFileId && evt.SourceRole == "certificate"
            );
            Assert.Contains(declared, evt => evt.FileId == document.OriginalFileId && evt.SourceRole == "original");
        }
    }

    [Fact]
    public async Task Several_customers_declare_masters_and_each_customer_copy()
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
        var publisher = new RecordingPublisher();

        await RunAsync(kit.Request, publisher, dryRun: false);

        var declared = publisher.Pages.SelectMany(page => page).ToList();
        Assert.Equal(7, declared.Count);
        Assert.All(
            kit.Request.Documents,
            document =>
                Assert.Contains(
                    declared,
                    evt =>
                        evt.FileId == document.OriginalFileId
                        && evt.SourceRole == "original"
                        && evt.RelatedCustomerId is null
                )
        );
        Assert.Contains(
            declared,
            evt => evt.FileId == federal && evt.SourceRole == "sealed_master" && evt.RelatedCustomerId is null
        );
        Assert.Contains(
            declared,
            evt => evt.FileId == certificate && evt.SourceRole == "certificate_master" && evt.RelatedCustomerId is null
        );
        Assert.Contains(
            declared,
            evt => evt.FileId == federalCopy && evt.SourceRole == "sealed" && evt.RelatedCustomerId == Ana
        );
        Assert.Contains(
            declared,
            evt => evt.FileId == certificateCopy && evt.SourceRole == "certificate" && evt.RelatedCustomerId == Luis
        );
    }

    [Fact]
    public async Task Running_it_again_publishes_the_same_declarations()
    {
        var kit = new DeliveryTestKit(sendCertificate: true, firstSignerCustomerId: Ana).Seal();
        var first = new RecordingPublisher();
        var second = new RecordingPublisher();

        await RunAsync(kit.Request, first, dryRun: false);
        await RunAsync(kit.Request, second, dryRun: false);

        Assert.Equal(
            first.Pages.SelectMany(page => page).Select(Key).Order(),
            second.Pages.SelectMany(page => page).Select(Key).Order()
        );
    }

    [Fact]
    public async Task A_request_not_completed_only_declares_its_original()
    {
        var draft = SignatureRequest
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
        var fileId = Guid.NewGuid();
        Assert.True(draft.AddDocument(TestSignatureLimits.Default, fileId, "Federal return").IsSuccess);
        var publisher = new RecordingPublisher();

        var report = await RunAsync(draft, publisher, dryRun: false);

        var declared = Assert.Single(publisher.Pages.SelectMany(page => page));
        Assert.Equal(1, report.RequestsScanned);
        Assert.Equal(fileId, declared.FileId);
        Assert.Equal("original", declared.SourceRole);
        Assert.Equal(draft.Id, declared.SourceResourceId);
    }

    [Fact]
    public async Task The_base_pdf_of_a_template_is_not_declared_as_original()
    {
        var draft = SignatureRequest
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
        var templateBase = Guid.NewGuid();
        var own = Guid.NewGuid();
        Assert.True(draft.AddDocument(TestSignatureLimits.Default, templateBase, "Letter").IsSuccess);
        Assert.True(draft.AddDocument(TestSignatureLimits.Default, own, "Annex").IsSuccess);
        var publisher = new RecordingPublisher();

        var report = await RunAsync(
            draft,
            publisher,
            dryRun: false,
            new TaxVision.Signature.Tests.Requests.TemplateFiles(templateBase)
        );

        var declared = Assert.Single(publisher.Pages.SelectMany(page => page));
        Assert.Equal(own, declared.FileId);
        Assert.Equal(1, report.FilesDeclared);
    }

    private static string Key(FileSourceDeclaredIntegrationEvent evt) =>
        $"{evt.FileId}|{evt.SourceRole}|{evt.RelatedCustomerId}";

    private static async Task<RedeclaredFileSourcesReport> RunAsync(
        SignatureRequest request,
        RecordingPublisher publisher,
        bool dryRun,
        TaxVision.Signature.Tests.Requests.TemplateFiles? templates = null
    )
    {
        var result = await RedeclareFileSourcesHandler.Handle(
            new RedeclareFileSourcesCommand(request.TenantId, dryRun),
            new SingleRequestRepository(request),
            new RequestOriginals(request),
            templates ?? TaxVision.Signature.Tests.Requests.TemplateFiles.None,
            publisher,
            new FixedCorrelation(),
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class RequestOriginals(SignatureRequest request) : ISignatureOriginalFileQuery
    {
        public Task<IReadOnlyList<RequestOriginalFiles>> ListAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RequestOriginalFiles>>(
                request.TenantId == tenantId
                    ?
                    [
                        new RequestOriginalFiles(
                            request.Id,
                            [.. request.Documents.Select(document => document.OriginalFileId)],
                            [.. request.Signers.Select(signer => signer.MappedCustomerId)]
                        ),
                    ]
                    : []
            );

        public Task<IReadOnlyList<SignatureRequest>> ListOwningFilesAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> fileIds,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class RecordingPublisher : IFileSourceDeclarationPublisher
    {
        public List<IReadOnlyList<FileSourceDeclaredIntegrationEvent>> Pages { get; } = [];

        public Task<Result> PublishAsync(
            IReadOnlyList<FileSourceDeclaredIntegrationEvent> page,
            CancellationToken ct = default
        )
        {
            Pages.Add(page);
            return Task.FromResult(Result.Success());
        }
    }
}
