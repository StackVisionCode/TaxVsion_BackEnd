using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Signature.Application.Requests.Commands.AddDocument;
using TaxVision.Signature.Application.Requests.Commands.RemoveDocument;
using TaxVision.Signature.Application.Requests.Commands.ReplaceDocumentFile;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Tests.Application;
using TaxVision.Signature.Tests.Application.Offboarding;
using TaxVision.Signature.Tests.Delivery;

namespace TaxVision.Signature.Tests.Requests;

/// <summary>
/// El original a firmar se declara en CloudStorage al fijarlo en la solicitud: rol <c>original</c>, con el cliente
/// firmante solo si es exactamente uno. El que se reemplaza o se quita pasa a <c>original_superseded</c>. El PDF
/// base de una plantilla nunca se declara.
/// </summary>
public sealed class AddDocumentDeclaresSourceTests
{
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Luis = Guid.NewGuid();

    [Fact]
    public async Task Adding_a_document_declares_its_original_with_the_single_customer()
    {
        var request = NewDraft(Ana);
        request.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "State return");
        var fileId = Guid.NewGuid();
        var bus = new CapturingBus();

        var result = await AddAsync(request, fileId, bus, TemplateFiles.None);

        Assert.True(result.IsSuccess);
        var declared = Assert.Single(bus.Messages.OfType<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(fileId, declared.FileId);
        Assert.Equal(request.TenantId, declared.TenantId);
        Assert.Equal("signature", declared.SourceService);
        Assert.Equal("signature_request", declared.SourceResourceType);
        Assert.Equal(request.Id, declared.SourceResourceId);
        Assert.Equal("original", declared.SourceRole);
        Assert.Equal(Ana, declared.RelatedCustomerId);
    }

    [Fact]
    public async Task A_refused_document_declares_nothing()
    {
        var request = NewDraft(Ana);
        var existing = Guid.NewGuid();
        request.AddDocument(TestSignatureLimits.Default, existing, "Federal return");
        var bus = new CapturingBus();

        var result = await AddAsync(request, existing, bus, TemplateFiles.None);

        Assert.True(result.IsFailure);
        Assert.Empty(bus.Messages.OfType<FileSourceDeclaredIntegrationEvent>());
    }

    [Fact]
    public async Task The_base_pdf_of_a_template_is_not_declared()
    {
        var request = NewDraft(Ana);
        var templateBase = Guid.NewGuid();
        var bus = new CapturingBus();

        var result = await AddAsync(request, templateBase, bus, new TemplateFiles(templateBase));

        Assert.True(result.IsSuccess);
        Assert.Empty(bus.Messages.OfType<FileSourceDeclaredIntegrationEvent>());
    }

    [Fact]
    public async Task Replacing_the_file_declares_the_new_original_and_supersedes_the_old_one()
    {
        var request = NewDraft(Ana, Luis);
        var oldFileId = Guid.NewGuid();
        request.AddDocument(TestSignatureLimits.Default, oldFileId, "Federal return");
        var newFileId = Guid.NewGuid();
        var bus = new CapturingBus();

        var result = await ReplaceAsync(request, newFileId, bus, TemplateFiles.None);

        Assert.True(result.IsSuccess);
        var declared = bus.Messages.OfType<FileSourceDeclaredIntegrationEvent>().ToList();
        Assert.Equal(2, declared.Count);
        Assert.Contains(
            declared,
            evt => evt.FileId == newFileId && evt.SourceRole == "original" && evt.RelatedCustomerId is null
        );
        Assert.Contains(
            declared,
            evt =>
                evt.FileId == oldFileId && evt.SourceRole == "original_superseded" && evt.SourceResourceId == request.Id
        );
    }

    [Fact]
    public async Task Replacing_a_template_base_pdf_only_declares_the_new_file()
    {
        var request = NewDraft(Ana);
        var templateBase = Guid.NewGuid();
        request.AddDocument(TestSignatureLimits.Default, templateBase, "Engagement letter");
        var newFileId = Guid.NewGuid();
        var bus = new CapturingBus();

        await ReplaceAsync(request, newFileId, bus, new TemplateFiles(templateBase));

        var declared = Assert.Single(bus.Messages.OfType<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(newFileId, declared.FileId);
        Assert.Equal("original", declared.SourceRole);
    }

    [Fact]
    public async Task Removing_a_document_supersedes_its_original()
    {
        var request = NewDraft(Ana);
        var fileId = Guid.NewGuid();
        request.AddDocument(TestSignatureLimits.Default, fileId, "Federal return");
        request.AddDocument(TestSignatureLimits.Default, Guid.NewGuid(), "State return");
        var bus = new CapturingBus();

        var result = await RemoveDocumentHandler.Handle(
            new RemoveDocumentCommand(request.TenantId, request.Id, request.Documents[0].Id),
            new SingleRequestRepository(request),
            new NoOpUnitOfWork(),
            new RecordingListCache(),
            bus,
            TemplateFiles.None,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var declared = Assert.Single(bus.Messages.OfType<FileSourceDeclaredIntegrationEvent>());
        Assert.Equal(fileId, declared.FileId);
        Assert.Equal("original_superseded", declared.SourceRole);
        Assert.Equal(Ana, declared.RelatedCustomerId);
    }

    private static Task<BuildingBlocks.Results.Result<TaxVision.Signature.Application.Requests.SignatureRequestDocumentResponse>> AddAsync(
        SignatureRequest request,
        Guid fileId,
        CapturingBus bus,
        TemplateFiles templates
    ) =>
        AddDocumentHandler.Handle(
            new AddDocumentCommand(request.TenantId, request.Id, fileId, "Federal return", null),
            new SingleRequestRepository(request),
            AcceptingDocumentVerifier.Instance,
            new FixedSignatureLimitsResolver(),
            new NoOpUnitOfWork(),
            new RecordingListCache(),
            bus,
            templates,
            CancellationToken.None
        );

    private static Task<BuildingBlocks.Results.Result<ReplaceDocumentFileResponse>> ReplaceAsync(
        SignatureRequest request,
        Guid newFileId,
        CapturingBus bus,
        TemplateFiles templates
    ) =>
        ReplaceDocumentFileHandler.Handle(
            new ReplaceDocumentFileCommand(request.TenantId, request.Id, request.Documents[0].Id, newFileId, null),
            new SingleRequestRepository(request),
            AcceptingDocumentVerifier.Instance,
            new NoOpUnitOfWork(),
            new RecordingListCache(),
            bus,
            new FixedCorrelation(),
            templates,
            CancellationToken.None
        );

    private static SignatureRequest NewDraft(params Guid[] customers)
    {
        var request = SignatureRequest
            .CreateDraft(
                TestSignatureLimits.Default,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "2025 filing",
                null,
                "Fiscal",
                72,
                false,
                false,
                false
            )
            .Value;
        for (var i = 0; i < customers.Length; i++)
            Assert.True(
                request
                    .AddSigner(
                        TestSignatureLimits.Default,
                        SignerEmail.Create($"signer{i}@example.com").Value,
                        SignerFullName.Create($"Signer {i}").Value,
                        customers[i]
                    )
                    .IsSuccess
            );
        return request;
    }
}
