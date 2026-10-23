using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Consents;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Commands;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.Tests.ClientForms;

public sealed class ExternalFormAttachmentHandlerTests
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid UserId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid ParticipantId = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Initiate_registers_pending_attachment_before_returning_presigned_post()
    {
        var fixture = Fixture();

        var result = await InitiateExternalFormAttachmentUploadHandler.Handle(
            new InitiateExternalFormAttachmentUploadCommand(
                fixture.Context,
                "fd_file001",
                null,
                "return.pdf",
                "application/pdf",
                1024,
                Now
            ),
            fixture.Requests,
            fixture.Versions,
            fixture.Engine,
            new PlainAnswersProtector(),
            new FormConsentGate(new MemoryConsentNoticeRepository(), new MemoryFormConsentRecordRepository()),
            fixture.Uploads,
            fixture.Options,
            fixture.UnitOfWork,
            default
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("FormParticipant", fixture.Uploads.LastRequest!.OwnerType);
        Assert.Equal(ParticipantId, fixture.Uploads.LastRequest.OwnerId);
        Assert.Equal(fixture.Uploads.FileId, result.Value.FileId);
        Assert.Equal(fixture.Uploads.UploadUrl, result.Value.PresignedPost.Url);
        var attachment = Assert.Single(fixture.Request.Submission!.Attachments);
        Assert.Equal(FormAttachmentStatus.Pending, attachment.Status);
        Assert.Equal(fixture.Uploads.FileId, attachment.FileId);
        Assert.Equal(1, fixture.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Initiate_rejects_oversized_file_before_calling_cloud_storage()
    {
        var fixture = Fixture();

        var result = await InitiateExternalFormAttachmentUploadHandler.Handle(
            new InitiateExternalFormAttachmentUploadCommand(
                fixture.Context,
                "fd_file001",
                null,
                "return.pdf",
                "application/pdf",
                2_000_000,
                Now
            ),
            fixture.Requests,
            fixture.Versions,
            fixture.Engine,
            new PlainAnswersProtector(),
            new FormConsentGate(new MemoryConsentNoticeRepository(), new MemoryFormConsentRecordRepository()),
            fixture.Uploads,
            fixture.Options,
            fixture.UnitOfWork,
            default
        );

        Assert.True(result.IsFailure);
        Assert.Null(fixture.Uploads.LastRequest);
        Assert.Equal(0, fixture.UnitOfWork.Saves);
    }

    [Fact]
    public async Task Complete_rejects_file_not_registered_in_bound_request()
    {
        var fixture = Fixture();

        var result = await CompleteExternalFormAttachmentUploadHandler.Handle(
            new CompleteExternalFormAttachmentUploadCommand(fixture.Context, Guid.NewGuid()),
            fixture.Requests,
            fixture.Uploads,
            default
        );

        Assert.True(result.IsFailure);
        Assert.False(fixture.Uploads.Completed);
    }

    private static FixtureState Fixture()
    {
        var version = Version();
        var request = FormRequest
            .Create(
                TenantId,
                FormParticipantRef.External(ParticipantId),
                version,
                null,
                null,
                null,
                null,
                Now.AddDays(7),
                null,
                null,
                FormRequestOrigin.Directed,
                null,
                UserId,
                UserId,
                Now
            )
            .Value;
        var limits = new DataCollectionLimits
        {
            MaxFileBytes = 1_000_000,
            MaxFilesPerField = 2,
            MaxFilesPerSubmission = 4,
            MaxSubmissionTotalBytes = 2_000_000,
        };
        return new FixtureState(
            request,
            ParticipantContext.External(TenantId, ParticipantId, request.Id, Guid.NewGuid()),
            new ExternalAttachmentRequestRepository(request),
            new MemoryFormTemplateVersionRepository(version),
            new FormEngine(limits),
            new RecordingUploadClient(),
            Options.Create(new ClientFormsOptions { Enabled = true, Limits = limits }),
            new RecordingUnitOfWork()
        );
    }

    private static FormTemplateVersion Version() =>
        FormTemplateVersion.Create(
            TenantId,
            Guid.NewGuid(),
            1,
            "Tax organizer",
            FormPurpose.DocumentRequest,
            FormPresentation.SinglePage,
            ClientFormsTestDocuments.Definition(
                """
                {"schemaVersion":1,"title":"Files","purpose":"documentRequest","presentation":"singlePage","settings":{"showProgress":true},"pages":[{"id":"pg_abcdefgh","title":"Files","description":null,"visibleWhen":null,"style":null,"sections":[{"id":"sc_abcdefgh","title":"Documents","description":null,"visibleWhen":null,"repeat":null,"style":null,"components":[{"id":"fd_file001","type":"file","key":null,"label":"Return","help":null,"required":true,"requiredWhen":null,"visibleWhen":null,"dataClass":"financial","reportable":false,"prefill":null,"style":null,"props":{"allowedTypes":["application/pdf"],"maxFiles":2,"maxFileBytes":1000000}}]}]}],"mappings":[],"actions":[]}
                """
            ),
            ClientFormsTestDocuments.Hash(new string('a', 64)),
            1,
            UserId,
            Now
        );

    private sealed record FixtureState(
        FormRequest Request,
        ParticipantContext Context,
        ExternalAttachmentRequestRepository Requests,
        MemoryFormTemplateVersionRepository Versions,
        IFormEngine Engine,
        RecordingUploadClient Uploads,
        IOptions<ClientFormsOptions> Options,
        RecordingUnitOfWork UnitOfWork
    );

    private sealed class RecordingUploadClient : IFormExternalUploadClient
    {
        internal Guid FileId { get; } = Guid.Parse("40000000-0000-0000-0000-000000000001");
        internal Uri UploadUrl { get; } = new("https://storage.test/upload");
        internal ExternalFormUploadRequest? LastRequest { get; private set; }
        internal bool Completed { get; private set; }

        public Task<Result<ExternalFormUploadResponse>> InitiateAsync(
            ExternalFormUploadRequest request,
            CancellationToken ct = default
        )
        {
            LastRequest = request;
            return Task.FromResult(
                Result.Success(
                    new ExternalFormUploadResponse(
                        FileId,
                        UploadUrl,
                        new Dictionary<string, string> { ["key"] = "value" },
                        Now.AddMinutes(10)
                    )
                )
            );
        }

        public Task<Result> CompleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default)
        {
            Completed = true;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class PlainAnswersProtector : IAnswersProtector
    {
        public ProtectedAnswers Protect(string json) => new(json, 1);

        public string Unprotect(ProtectedAnswers value) => value.Cipher;
    }

    private sealed class ExternalAttachmentRequestRepository(FormRequest request) : IFormRequestRepository
    {
        public void Add(FormRequest item) { }

        public Task<Result<FormRequest>> GetForCommandAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                tenantId == request.TenantId && requestId == request.Id
                    ? Result.Success(request)
                    : Result.Failure<FormRequest>(FormRequestErrors.NotFound)
            );

        public Task<Result<FormRequest>> GetForReviewAsync(
            Guid tenantId,
            Guid requestId,
            CancellationToken ct = default
        ) => GetForCommandAsync(tenantId, requestId, ct);

        public Task<bool> HasOpenRequestAsync(
            Guid tenantId,
            Guid customerId,
            Guid templateId,
            Guid? exceptRequestId = null,
            CancellationToken ct = default
        ) => Task.FromResult(false);

        public Task<bool> HasOpenExternalRequestAsync(
            Guid tenantId,
            Guid participantId,
            Guid templateId,
            CancellationToken ct = default
        ) => Task.FromResult(false);

        public Task<IReadOnlyList<FormRequest>> ListOpenByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

        public Task<IReadOnlyList<FormRequest>> ListByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            bool onlyOpen,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);

        public Task<IReadOnlyList<FormRequest>> ListByAttachmentFileIdAsync(
            Guid fileId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<FormRequest>>([]);
    }
}
