using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using static TaxVision.Tasks.Tests.ClientForms.FormRequestFixtures;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class FormRequestParticipantMutationTests
{
    [Fact]
    public async Task Open_returns_the_row_version_written_by_the_save()
    {
        var version = Version();
        var request = CustomerRequest(version);
        var unitOfWork = new RowVersionBumpingUnitOfWork(request);

        var result = await OpenPortalFormRequestHandler.Handle(
            new OpenPortalFormRequestCommand(Portal(request), request.Id, Now),
            new SingleFormRequestRepository(request),
            new RecordingFormAuditLog(),
            unitOfWork,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.Saves);
        Assert.Equal(Convert.ToBase64String(request.RowVersion), result.Value.RowVersion);
    }

    [Fact]
    public async Task Register_returns_the_new_row_version_and_ignores_a_verdict_of_another_tenant()
    {
        var version = Version();
        var request = CustomerRequest(version);
        var fileId = Guid.NewGuid();
        var foreign = FormFileScanVerdict.Create(
            Guid.NewGuid(),
            fileId,
            FormFileVerdictStatus.Available,
            "Customer",
            CustomerId,
            "ClientForms",
            "application/pdf",
            10,
            "return.pdf",
            Now,
            Now
        );
        var unitOfWork = new RowVersionBumpingUnitOfWork(request);

        var result = await Register(
            request,
            version,
            fileId,
            new MemoryFormFileScanVerdictRepository(foreign),
            unitOfWork
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(FormAttachmentStatus.Pending, result.Value.Status);
        Assert.Equal(Convert.ToBase64String(request.RowVersion), result.Value.RowVersion);
    }

    [Fact]
    public async Task Register_applies_the_projected_verdict_of_the_same_tenant()
    {
        var version = Version();
        var request = CustomerRequest(version);
        var fileId = Guid.NewGuid();
        var own = FormFileScanVerdict.Create(
            TenantId,
            fileId,
            FormFileVerdictStatus.Available,
            "Customer",
            CustomerId,
            "ClientForms",
            "application/pdf",
            10,
            "return.pdf",
            Now,
            Now
        );

        var result = await Register(
            request,
            version,
            fileId,
            new MemoryFormFileScanVerdictRepository(own),
            new RowVersionBumpingUnitOfWork(request)
        );

        Assert.Equal(FormAttachmentStatus.Available, result.Value.Status);
    }

    [Fact]
    public async Task Detach_returns_the_new_row_version()
    {
        var version = Version();
        var request = CustomerRequest(version);
        var attachment = request
            .RegisterAttachment(
                new FormAnswerAddress(FileComponent, null),
                Guid.NewGuid(),
                "return.pdf",
                "application/pdf",
                10,
                2,
                20,
                null,
                Now
            )
            .Value;
        var unitOfWork = new RowVersionBumpingUnitOfWork(request);

        var result = await DetachPortalFormAttachmentHandler.Handle(
            new DetachPortalFormAttachmentCommand(Portal(request), request.Id, attachment.Id, Now),
            new SingleFormRequestRepository(request),
            unitOfWork,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(Convert.ToBase64String(request.RowVersion), result.Value.RowVersion);
    }

    [Fact]
    public async Task Capture_returns_the_new_row_version_without_the_plaintext()
    {
        var version = Version();
        var request = CustomerRequest(version);
        var unitOfWork = new RowVersionBumpingUnitOfWork(request);

        var result = await CapturePortalSensitiveAnswerHandler.Handle(
            new CapturePortalSensitiveAnswerCommand(
                Portal(request),
                request.Id,
                TaxIdComponent,
                null,
                FormTaxIdentifierKind.Ssn,
                "123-45-6789",
                Now
            ),
            new SingleFormRequestRepository(request),
            new MemoryFormTemplateVersionRepository(version),
            FormTemplateHandlerTestDoubles.Engine(),
            new PlainFormAnswersProtector(),
            new OpaqueSensitiveProtector(),
            ConsentGate(),
            new RecordingFormAuditLog(),
            unitOfWork,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("6789", result.Value.Last4);
        Assert.Equal(Convert.ToBase64String(request.RowVersion), result.Value.RowVersion);
    }

    [Fact]
    public async Task Draft_save_is_measured_as_ok_and_a_stale_row_version_as_conflict()
    {
        var version = Version();
        var request = CustomerRequest(version);
        var metrics = new RecordingClientFormsMetrics();
        const string json = "{\"schemaVersion\":1,\"values\":{}}";

        var saved = await SaveDraftAsync(request, version, json, request.RowVersion, metrics);
        var stale = await SaveDraftAsync(request, version, json, [9, 9, 9], metrics);

        Assert.True(saved.Value.Saved);
        Assert.Equal(FormRequestErrors.ConcurrencyConflict, stale.Error);
        Assert.Equal(["draft:ok", "draft:conflict"], metrics.Recorded);
    }

    private static Task<Result<FormDraftSaveOutcome>> SaveDraftAsync(
        FormRequest request,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion version,
        string json,
        byte[] rowVersion,
        RecordingClientFormsMetrics metrics
    ) =>
        SavePortalFormDraftHandler.Handle(
            new SavePortalFormDraftCommand(Portal(request), request.Id, json, rowVersion, Now),
            new SingleFormRequestRepository(request),
            new MemoryFormTemplateVersionRepository(version),
            FormTemplateHandlerTestDoubles.Engine(),
            new PlainFormAnswersProtector(),
            ConsentGate(),
            new RowVersionBumpingUnitOfWork(request),
            metrics,
            default
        );

    [Fact]
    public async Task Draft_keeps_the_server_last4_and_kind_instead_of_the_client_values()
    {
        var version = Version();
        var request = CustomerRequest(version);
        var answer = request
            .CaptureSensitiveAnswer(
                new FormAnswerAddress(TaxIdComponent, null),
                FormTaxIdentifierKind.Ssn,
                new ProtectedTaxIdentifier("protected", 1, "6789"),
                FormActor.PortalUser(UserId),
                Now
            )
            .Value;
        var json =
            "{\"schemaVersion\":1,\"values\":{\"fd_taxid001\":{\"sensitiveRef\":\""
            + answer.Id
            + "\",\"last4\":\"0000\",\"kind\":\"itin\"}}}";

        var result = await SavePortalFormDraftHandler.Handle(
            new SavePortalFormDraftCommand(Portal(request), request.Id, json, request.RowVersion, Now),
            new SingleFormRequestRepository(request),
            new MemoryFormTemplateVersionRepository(version),
            FormTemplateHandlerTestDoubles.Engine(),
            new PlainFormAnswersProtector(),
            ConsentGate(),
            new RowVersionBumpingUnitOfWork(request),
            new RecordingClientFormsMetrics(),
            default
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Saved);
        var stored = request.Submission!.DraftAnswers!.Cipher;
        Assert.Contains("\"last4\":\"6789\"", stored);
        Assert.Contains("\"kind\":\"ssn\"", stored);
        Assert.DoesNotContain("0000", stored);
    }

    private static Task<BuildingBlocks.Results.Result<FormAttachmentResponse>> Register(
        FormRequest request,
        TaxVision.Tasks.ClientForms.Domain.Versions.FormTemplateVersion version,
        Guid fileId,
        IFormFileScanVerdictRepository verdicts,
        RowVersionBumpingUnitOfWork unitOfWork
    ) =>
        RegisterPortalFormAttachmentHandler.Handle(
            new RegisterPortalFormAttachmentCommand(
                Portal(request),
                request.Id,
                FileComponent,
                null,
                fileId,
                "return.pdf",
                "application/pdf",
                10,
                Now
            ),
            new SingleFormRequestRepository(request),
            new MemoryFormTemplateVersionRepository(version),
            verdicts,
            FormTemplateHandlerTestDoubles.Engine(),
            new PlainFormAnswersProtector(),
            ConsentGate(),
            unitOfWork,
            default
        );

    private static FormConsentGate ConsentGate() =>
        new(new MemoryConsentNoticeRepository(), new MemoryFormConsentRecordRepository());

    private sealed class OpaqueSensitiveProtector : ISensitiveValueProtector
    {
        public ProtectedSensitiveValue Protect(string normalized) => new("opaque-cipher", 1);

        public string Unprotect(ProtectedSensitiveValue protectedValue) => throw new NotSupportedException();
    }
}
